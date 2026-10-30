"""Read -> expand -> validate -> export. The editor backend runs the same path on edited rows."""
from pathlib import Path

from .core import Dictionary, Report, SkillDataError, canonical, load_registry, sha, sha_file
from .expand import expand_modules, expand_values, ordered_nodes
from .keyframes import is_keyframe_ref, keyframe_time, validate_keyframes
from .messages import text
from .validate import names_in, owners, validate_bundles, validate_owners
from .workbook import load

VERSION = '0.1.0'


def run(files, cfg, product=True, practice=False, dictionary_path=None):
    """files: document id -> path (None = optional document absent). Returns errors, warnings and, without errors, the exports."""
    rep = Report(cfg['language'])
    d = Dictionary(dictionary_path or cfg.dictionary_path, cfg)
    reg = load_registry(cfg, d)
    for doc, path in files.items():
        if path is None:
            if doc not in cfg['optionalDocuments']:
                rep.e('MissingDocument', dict(doc=doc), path=doc)
        elif not Path(path).is_file():
            rep.e('MissingDocument', dict(doc=doc), path=path)
    if rep.errors:
        return dict(errors=rep.errors, warnings=rep.warnings, report=rep, dictionary=d, registry=reg)
    data = load(files, rep, reg, cfg)
    return run_data(data, rep, d, reg, cfg, product, practice)


def run_data(data, rep, d, reg, cfg, product=True, practice=False):
    if product and not practice:
        po = cfg['practiceOwner']
        for t, rows in data.items():
            for row in rows:
                for col in ('Key', 'BundleKey'):
                    v = row.get(col)
                    if cfg.is_key(v) and cfg.key_owner(v) == po:
                        rep.e('PracticeKey', dict(sheet=row.get('_sheet', t), row=row.get('_row')), owner=po, key=v)
    vals = expand_values(data, rep, cfg)
    mods = expand_modules(data, rep, cfg)
    validate_bundles(mods, rep, d, cfg, product)
    validate_keyframes(data, mods, rep, d, cfg)
    reach, types = validate_owners(data, mods, vals, rep, d, cfg)
    ctx = dict(data=data, mods=mods, vals=vals, reach=reach, types=types, dictionary=d, registry=reg, config=cfg,
               report=rep, product=product, practice=practice)
    for check in cfg.plugins()['validate']:
        check(ctx)
    result = dict(errors=rep.errors, warnings=rep.warnings, report=rep, data=data, mods=mods, vals=vals, reach=reach,
                  types=types, dictionary=d, registry=reg)
    if not rep.errors:
        logic, pres, names = export(data, mods, vals, types, reg, d, cfg)
        hook = cfg.plugins()['export']
        if hook:
            hook(logic, pres, ctx)
        result.update(logic=logic, presentation=pres, names=names, logic_bytes=canonical(logic), presentation_bytes=canonical(pres))
    return result


def export(data, mods, vals, types, reg, d, cfg):
    """Names become numbers: events, modules, enum values and value names are written as indexes."""
    names = sorted({n for b in mods.values() for n, _, _ in names_in(b, d)} | {m['ValueName'] for m in data.get('ModifierData', [])}
                   | {n for v in vals.values() for n in v})
    nid_of = {n: i for i, n in enumerate(names)}
    ev_index = {e: i for i, e in enumerate(d.events)}
    q = cfg.snap

    def fixed(s, v):
        if s[1].startswith('enum:'):
            return d.slot_enums[s[1][5:]].index(v)
        if s[1].startswith('list:enum:'):
            return [d.slot_enums[s[1][10:]].index(x.strip()) for x in str(v).split(',')]
        if s[1] == 'time':
            return q(v)
        if s[1] == 'int' or s[1].startswith('key:'):
            return int(v)
        return v

    def node_json(nid, n, b):
        links = []
        for (lnid, slot), lk in b['links'].items():
            if lnid != nid:
                continue
            index, s = d.slot(n['Module'], slot)
            links.append([index, 'ref', nid_of[lk['value']]] if lk['source'] == 'Ref' else [index, 'fixed', fixed(s, lk['value'])])
        links.sort(key=lambda x: x[0])
        ev = n['EventValue']
        if d.is_subscription(n):
            at = d.subjects.index(ev)
        else:
            if is_keyframe_ref(ev):
                ev = keyframe_time(b, ev, data, d, cfg)[0]
            at = ['ref', nid_of[ev]] if isinstance(ev, str) else (None if ev is None else q(ev))
        return {'id': nid, 'event': ev_index[n['Event']], 'at': at, 'from': n['FromNode'], 'priority': n['Priority'] or 0,
                'order': n['Order'], 'module': d.index[n['Module']], 'links': links}

    def bundles(line):
        out = []
        for k in sorted(mods):
            b = mods[k]
            nodes = [node_json(nid, n, b) for nid, n in ordered_nodes(b, line, d)]
            if nodes:
                out.append(dict(key=k, kind=reg['enums']['BUNDLE_KIND'].index(b['kind']), nodes=nodes))
        return out

    def values(k):
        return [[nid_of[n], q(v) if 'time' in types.get(n, ()) else v] for n, (v, _) in sorted(vals[k].items())]

    def owner_json(o, r):
        out = dict(key=r['Key'], bundle=r['ModuleBundleKey'], values=r['ValueBundleKey'])
        t = reg['by_json'][o['table']]
        for col in o.get('exportColumns', []):
            c = next(c for c in t['columns'] if c['name'] == col)
            v = r.get(col)
            out[col[0].lower() + col[1:]] = reg['enums'][c['type'].split(':')[1]].index(v) if c['type'].startswith('enum:') and v is not None else v
        return out

    all_owners = owners(data, cfg)
    logic = dict(format=cfg['formats']['logic'], dictionaryHash=d.hash, bundles=bundles('Logic'),
                 values=[dict(key=k, entries=values(k)) for k in sorted({r['ValueBundleKey'] for _, r in all_owners} & set(vals))],
                 modifiers=[dict(key=m['Key'], targets=m['TargetSkillKeys'], name=nid_of[m['ValueName']], op=reg['enums']['MODIFIER_OP'].index(m['Op']),
                                 value=m['Value'], priority=m['Priority'] or 0) for m in data.get('ModifierData', [])])
    for o in cfg.owner_tables():
        logic[o['export']] = [owner_json(o, r) for r in data.get(o['table'], [])]
    pres = dict(format=cfg['formats']['presentation'], dictionaryHash=d.hash, valueNames=names, bundles=bundles('Presentation'),
                metas=[dict(key=r['Key'], nameLoc=r.get('NameLoc') or '', descLoc=r.get('DescLoc') or '', icon=r.get('IconKey') or 0) for _, r in all_owners],
                resources=[dict(key=r['PresentationKey'], skin=r.get('Skin') or 0, clip=r.get('Clip') or '', effect=r.get('Effect') or '', sound=r.get('Sound') or '')
                           for r in sorted(data.get('PresentationResourceData', []), key=lambda r: (r['PresentationKey'], r.get('Skin') or 0))])
    return logic, pres, names


def build(cfg, data_dir=None):
    """Product build: raises SkillDataError on errors. Returns logic/presentation bytes and a manifest block."""
    files = cfg.documents(data_dir)
    r = run(files, cfg, True)
    if r['errors']:
        raise SkillDataError(text(cfg['language'], 'build.failed', count=len(r['errors'])) + ':\n' + '\n'.join(map(str, r['errors'][:40])))
    lb, pb = r['logic_bytes'], r['presentation_bytes']
    sources = sorted((Path(p).name, sha_file(p)) for p in files.values() if p)
    manifest = dict(toolVersion=VERSION, logicFile='skills.logic.' + sha(lb) + '.json', logicSha256=sha(lb),
                    presentationFile='skills.presentation.' + sha(pb) + '.json', presentationSha256=sha(pb),
                    dictionarySha256=r['dictionary'].hash, sourceFiles=[n for n, _ in sources], sourceSha256=[h for _, h in sources])
    return dict(logic=lb, presentation=pb, manifest=manifest, warnings=r['warnings'], result=r)
