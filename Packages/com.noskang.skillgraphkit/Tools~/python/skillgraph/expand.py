"""Override expansion: a child bundle starts from its parent and adds ('+'), removes ('-') or overrides nodes and slots,
like a prefab variant. Value bundles inherit the same way (child rows replace parent rows by name)."""
from .core import NAME_RE
from .messages import text

NODE_FIELDS = ['Line', 'Event', 'EventValue', 'FromNode', 'Order', 'Priority', 'Module']


def chain(key, parents, rep, cfg, at):
    """Root-first list of keys from the root down to key, or None on a cycle or a missing parent."""
    seen, cur = [key], parents.get(key)
    while cur is not None:
        if cur in seen:
            rep.e('InheritanceCycle', at, chain=seen); return None
        if cur not in parents:
            rep.e('MissingParent', at, parent=cur); return None
        seen.append(cur)
        cur = parents.get(cur)
    if len(seen) > cfg.max_depth:
        rep.e('InheritanceDepth', at, depth=cfg.max_depth, layers=' > '.join(cfg['layers']), chain=list(reversed(seen)))
    return list(reversed(seen))


def expand_values(data, rep, cfg):
    parents = {r['Key']: r['ParentKey'] for r in data['ValueBundleData']}
    own = {k: {} for k in parents}
    for r in data['ValueData']:
        at = dict(sheet=r.get('_sheet'), row=r.get('_row'))
        if r['BundleKey'] not in own:
            rep.e('MissingReference', at, what=rep_text(rep, 'noun.valueBundle'), key=r['BundleKey']); continue
        name = r['Name']
        if not isinstance(name, str) or not NAME_RE.match(name):
            rep.e('ValueName', at, name=name); continue
        low = {n.lower(): n for n in own[r['BundleKey']]}
        if name.lower() in low and low[name.lower()] != name:
            rep.e('ValueNameCase', at, a=name, b=low[name.lower()])
        own[r['BundleKey']][name] = (r['Value'], r['BundleKey'])
    out = {}
    for k in parents:
        c = chain(k, parents, rep, cfg, dict(values=k))
        if c is None:
            continue
        merged = {}
        for b in c:
            merged.update(own.get(b, {}))
        out[k] = merged
    return out


def rep_text(rep, key):
    return text(rep.lang, key)


def expand_modules(data, rep, cfg):
    note = cfg['columns']['nodeNote']
    bundles = {r['Key']: r for r in data['ModuleBundleData']}
    parents = {k: r['ParentKey'] for k, r in bundles.items()}
    own_nodes = {k: {} for k in bundles}
    own_links = {k: {} for k in bundles}
    for r in data['ModuleNodeData']:
        if r['BundleKey'] not in bundles:
            rep.e('MissingReference', dict(sheet=r.get('_sheet'), row=r.get('_row')), what=rep_text(rep, 'noun.moduleBundle'), key=r['BundleKey']); continue
        own_nodes[r['BundleKey']][r['NodeId']] = r
    for r in data['ValueLinkData']:
        if r['BundleKey'] not in bundles:
            rep.e('MissingReference', dict(sheet=r.get('_sheet'), row=r.get('_row')), what=rep_text(rep, 'noun.moduleBundle'), key=r['BundleKey']); continue
        own_links[r['BundleKey']][(r['NodeId'], r['Slot'])] = r
    out = {}
    for k in bundles:
        c = chain(k, parents, rep, cfg, dict(bundle=k))
        if c is None:
            continue
        for b in c[1:]:
            if bundles[b]['Kind'] != bundles[c[0]]['Kind']:
                rep.e('KindMismatch', dict(bundle=b))
        nodes, links = {}, {}
        for depth, b in enumerate(c):
            for nid, r in own_nodes[b].items():
                at = dict(sheet=r.get('_sheet'), row=r.get('_row'), bundle=b, node=nid)
                mark = r.get('Override') or 'None'
                if depth == 0 or mark == '+':
                    if depth > 0 and nid in nodes:
                        rep.e('OverrideAddExists', at); continue
                    if depth == 0 and mark != 'None':
                        rep.e('OverrideOnRoot', at)
                    for f in ('Event', 'Module', 'Order'):
                        if r.get(f) is None:
                            rep.e('RequiredCell', at, 'RequiredField', field=f)
                    nodes[nid] = dict({f: r.get(f) for f in NODE_FIELDS}, Note=r.get(note), _from=b, _mark='+' if depth else '', _fields={})
                elif mark == '-':
                    if nid not in nodes:
                        rep.e('OverrideRemoveMissing', at); continue
                    del nodes[nid]
                    links = {kk: v for kk, v in links.items() if kk[0] != nid}
                else:
                    if nid not in nodes:
                        rep.e('OverrideTargetMissing', at); continue
                    for f in NODE_FIELDS:
                        if f in ('Line', 'Priority') and r.get(f) in ('Logic', 0):
                            continue  # a column default is not an override
                        if r.get(f) is not None:
                            if f in ('Module', 'Event') and r[f] != nodes[nid][f]:
                                rep.e('OverrideKindChange', at, field=f); continue
                            nodes[nid][f] = r[f]
                            nodes[nid]['_fields'][f] = b
            for (nid, slot), r in own_links[b].items():
                at = dict(sheet=r.get('_sheet'), row=r.get('_row'), bundle=b, node=nid, slot=slot)
                if nid not in nodes:
                    rep.e('LinkNodeMissing', at); continue
                if r['Source'] == 'Default':
                    if depth == 0:
                        rep.e('DefaultOnRoot', at)
                    links.pop((nid, slot), None)
                    continue
                links[(nid, slot)] = dict(source=r['Source'], value=r.get('Value'), _from=b)
        for nid, n in nodes.items():
            if n['FromNode'] is not None and n['FromNode'] not in nodes:
                rep.e('FromNodeRemoved', dict(bundle=k, node=nid), node=n['FromNode'])
        out[k] = dict(kind=bundles[k]['Kind'], chain=c, desc=bundles[k]['Desc'], nodes=nodes, links=links)
    return out


def ordered_nodes(b, line, d):
    ev = {e: i for i, e in enumerate(d.events)}
    items = [(nid, n) for nid, n in b['nodes'].items() if n['Line'] == line]
    return sorted(items, key=lambda x: (ev.get(x[1]['Event'], len(ev)), str(x[1]['EventValue']), x[1]['FromNode'] or 0,
                                        x[1]['Priority'] or 0, x[1]['Order'] or 0, x[0]))
