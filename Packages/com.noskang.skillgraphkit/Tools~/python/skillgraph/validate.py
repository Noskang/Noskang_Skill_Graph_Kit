"""Checks against the module dictionary (events, slots, sources) and between owners, bundles and values."""
from .core import NAME_RE, NUMERIC, is_num
from .keyframes import is_keyframe_ref
from .messages import text


def check_fixed(s, v, rep, at, d, cfg):
    kind = s[1]
    if kind.startswith('enum:'):
        if v not in d.slot_enums[kind[5:]]:
            rep.e('SlotEnum', at, value=v, allowed=', '.join(d.slot_enums[kind[5:]]))
    elif kind.startswith('list:enum:'):
        items = [x.strip() for x in str(v).split(',')]
        if any(x not in d.slot_enums[kind[10:]] for x in items):
            rep.e('SlotEnum', at, value=v, allowed=', '.join(d.slot_enums[kind[10:]]))
    elif kind.startswith('key:'):
        band = cfg.band(kind[4:])
        if not (is_num(v) and v == int(v) and cfg.is_key(int(v), band)):
            rep.e('SlotKeyBand', at, band=band, value=v)
    elif kind in NUMERIC:
        if not is_num(v):
            rep.e('SlotType', at, type=kind, value=v)
        elif kind == 'int' and v != int(v):
            rep.e('SlotType', at, type=kind, value=v)
    elif kind in ('string', 'bool'):
        if kind == 'string' and (not isinstance(v, str) or not v):
            rep.e('SlotType', at, type=kind, value=v)


def spawn_targets(b, cfg):
    sp = cfg['modules']['spawn']
    for (nid, slot), lk in b['links'].items():
        if slot == sp['slot'] and b['nodes'].get(nid, {}).get('Module') == sp['module']:
            yield nid, lk['value']


def validate_bundles(mods, rep, d, cfg, product):
    effect = cfg.kind('effect')
    spawned = {v for b in mods.values() for _, v in spawn_targets(b, cfg)}
    timed = d.timed_events
    exits = set(d.exit_events)
    balance = cfg['dictionary']['balanceKind']
    for k, b in mods.items():
        for nid, n in b['nodes'].items():
            at = dict(bundle=k, node=nid)
            mod = d.by_name.get(n['Module'])
            if mod is None:
                rep.e('UnknownModule', at, module=n['Module']); continue
            if product and not mod['implemented']:
                rep.e('UnimplementedModule', at, module=n['Module'])
            if mod['line'] != n['Line']:
                rep.e('LineMismatch', at, module=n['Module'], line=mod['line'])
            ev = n['Event']
            if ev not in d.kind_events.get(b['kind'], ()):
                rep.e('EventForKind', at, event=ev, kind=b['kind'])
            if ev not in mod['events']:
                rep.e('EventForModule', at, module=n['Module'], event=ev)
            if n['Line'] == 'Presentation' and ev not in d.presentation_events:
                rep.e('PresentationEvent', at, event=ev)
            if ev in timed and n['EventValue'] is None:
                rep.e('EventValueRequired', at, event=ev)
            if ev in d.subscription_events:
                if n['EventValue'] not in d.subjects:
                    rep.e('SubscriptionSubject', at, subjects=', '.join(d.subjects), value=n['EventValue'])
            elif ev not in timed and n['EventValue'] is not None:
                rep.e('EventValueUnexpected', at, event=ev)
            if ev in timed and is_num(n['EventValue']) and n['EventValue'] != 0:
                rep.w('FixedTiming', at, value=n['EventValue'])
            if ev in exits:
                src = b['nodes'].get(n['FromNode'])
                if src is None:
                    rep.e('FromNodeMissing', at, node=n['FromNode'])
                elif not d.by_name.get(src['Module'], {}).get('exits'):
                    rep.e('FromNodeNotCheck', at, module=src['Module'])
                elif src['Line'] == 'Presentation':
                    rep.e('PresentationToLogic', at)
            elif n['FromNode'] is not None:
                rep.e('FromNodeUnexpected', at)
        orders = {}
        for nid, n in sorted(b['nodes'].items()):
            key = (n['Line'], n['Event'], str(n['EventValue']), n['FromNode'], n['Priority'] or 0, n['Order'])
            if key in orders:
                rep.e('OrderTie', dict(bundle=k), a=orders[key], b=nid)
            orders[key] = nid
        for (nid, slot), lk in b['links'].items():
            at = dict(bundle=k, node=nid, slot=slot)
            n = b['nodes'][nid]
            if n['Module'] not in d.by_name:
                continue
            _, s = d.slot(n['Module'], slot)
            if s is None:
                rep.e('UnknownSlot', at, module=n['Module']); continue
            if lk['source'] == 'Ref':
                if not isinstance(lk['value'], str) or not NAME_RE.match(lk['value']):
                    rep.e('RefName', at, value=lk['value'])
                elif not (s[1] in NUMERIC or s[1].startswith('key:')):
                    rep.e('RefNotAllowed', at, type=s[1])
            else:
                check_fixed(s, lk['value'], rep, at, d, cfg)
                if s[3] == balance and lk['value'] not in (0, None):
                    rep.w('FixedBalance', at, value=lk['value'])
                if product and s[7] is not None and lk['value'] not in s[7]:
                    rep.e('UnsupportedValue', at, value=lk['value'], supported=', '.join(map(str, s[7])))
        for nid, n in b['nodes'].items():
            mod = d.by_name.get(n['Module'])
            if mod is None:
                continue
            for s in mod['slots']:
                if s[4] is None and (nid, s[0]) not in b['links']:
                    rep.e('RequiredSlot', dict(bundle=k, node=nid), module=n['Module'], slot=s[0])
                if product and s[7] is not None and (nid, s[0]) not in b['links'] and isinstance(s[4], str) and s[4] not in s[7]:
                    rep.e('UnsupportedValue', dict(bundle=k, node=nid, slot=s[0]), value=s[4], supported=', '.join(map(str, s[7])))
        logic = [n for n in b['nodes'].values() if n['Line'] == 'Logic']
        for rule in cfg['rules']['companions']:
            kind = cfg.kind(rule['kind']) if rule['kind'] in cfg['bundleKinds'] else rule['kind']
            if b['kind'] != kind or (rule.get('spawnedOnly') and kind == effect and k not in spawned):
                continue
            if any(n['Event'] == rule['event'] for n in logic) and not any(n['Module'] == rule['requires'] for n in logic):
                rep.e('CompanionMissing', dict(bundle=k), kind=kind, event=rule['event'], module=rule['requires'])


def names_in(bundle, d):
    """Value names a bundle reads: Ref slots and timed event values (not subjects, not keyframe references)."""
    out = []
    for (nid, slot), lk in bundle['links'].items():
        if lk['source'] == 'Ref' and isinstance(lk['value'], str):
            out.append((lk['value'], nid, slot))
    for nid, n in bundle['nodes'].items():
        if isinstance(n['EventValue'], str) and not d.is_subscription(n) and not is_keyframe_ref(n['EventValue']):
            out.append((n['EventValue'], nid, 'EventValue'))
    return out


def reachable(root, mods, rep, cfg, owner):
    """Bundles an owner runs: its own bundle plus every effect bundle spawned from there."""
    effect = cfg.kind('effect')
    seen, todo = [], [root]
    while todo:
        k = todo.pop(0)
        if k in seen:
            continue
        seen.append(k)
        for nid, tgt in spawn_targets(mods[k], cfg):
            at = dict(owner=owner, bundle=k, node=nid)
            if tgt not in mods:
                rep.e('MissingReference', at, what=text(rep.lang, 'noun.moduleBundle'), key=tgt); continue
            if mods[tgt]['kind'] != effect:
                rep.e('SpawnNotEffect', at, key=tgt, kind=effect)
            if tgt == k or tgt in seen:
                rep.e('SpawnCycle', dict(owner=owner, bundle=k), key=tgt); continue
            todo.append(tgt)
    return seen


def value_types(mods, d):
    """For each value name, the slot types that read it (time slots are snapped to the grid and grid-checked)."""
    out = {}
    for b in mods.values():
        for (nid, slot), lk in b['links'].items():
            n = b['nodes'][nid]
            if lk['source'] != 'Ref' or not isinstance(lk['value'], str) or n['Module'] not in d.by_name:
                continue
            _, s = d.slot(n['Module'], slot)
            if s is not None:
                out.setdefault(lk['value'], set()).add(s[1])
        for nid, n in b['nodes'].items():
            if isinstance(n['EventValue'], str) and not d.is_subscription(n) and not is_keyframe_ref(n['EventValue']):
                out.setdefault(n['EventValue'], set()).add('time')
    return out


def owners(data, cfg):
    """[(owner table entry, row)] for every owner row (skills, buffs, ...)."""
    out = []
    for o in cfg.owner_tables():
        out += [(o, r) for r in data.get(o['table'], [])]
    return out


def validate_owners(data, mods, vals, rep, d, cfg):
    used = {k: set() for k in vals}
    types = value_types(mods, d)
    reach = {}
    grid, unit = cfg['time']['grid'], cfg['time']['unit']
    for o, r in owners(data, cfg):
        at = dict(owner=r['Key'])
        mb, vb = r['ModuleBundleKey'], r['ValueBundleKey']
        if mb not in mods:
            rep.e('MissingReference', at, what=text(rep.lang, 'noun.moduleBundle'), key=mb); continue
        if mods[mb]['kind'] != o['kindName']:
            rep.e('MetaBundleKind', at, key=mb, kind=mods[mb]['kind'])
        if vb not in vals:
            rep.e('MissingReference', at, what=text(rep.lang, 'noun.valueBundle'), key=vb); continue
        reach[r['Key']] = reachable(mb, mods, rep, cfg, r['Key'])
        for b in reach[r['Key']]:
            for name, nid, slot in names_in(mods[b], d):
                bat = dict(owner=r['Key'], bundle=b, node=nid, slot=slot)
                if name not in vals[vb]:
                    rep.e('MissingValueName', bat, values=vb, name=name)
                    continue
                used[vb].add(name)
                value = vals[vb][name][0]
                vat = dict(owner=r['Key'], name=name)
                for t in types.get(name, ()):
                    if (t in NUMERIC or t.startswith('key:')) and not is_num(value):
                        rep.e('SlotType', vat, type=t, value=value)
                    elif t == 'int' and value != int(value):
                        rep.e('SlotType', vat, type=t, value=value)
                    elif t.startswith('key:') and not cfg.is_key(int(value), cfg.band(t[4:])):
                        rep.e('SlotKeyBand', vat, band=cfg.band(t[4:]), value=value)
        if grid:
            for name, (value, _) in vals[vb].items():
                if 'time' in types.get(name, ()) and is_num(value) and not cfg.on_grid(value):
                    rep.w('OffGrid', dict(owner=r['Key'], name=name), value=value, grid=grid, unit=unit)
            for b in reach[r['Key']]:
                for nid, n in mods[b]['nodes'].items():
                    if is_num(n['EventValue']) and n['Event'] in d.timed_events and not cfg.on_grid(n['EventValue']):
                        rep.w('OffGrid', dict(owner=r['Key'], bundle=b, node=nid), value=n['EventValue'], grid=grid, unit=unit)
    skills = {r['Key']: r for r in data.get('SkillMetaData', [])}
    for m in data.get('ModifierData', []):
        for sk in m['TargetSkillKeys'] or []:
            meta = skills.get(sk)
            if meta is None:
                rep.e('MissingReference', dict(modifier=m['Key']), what=text(rep.lang, 'noun.skill'), key=sk)
            elif m['ValueName'] not in vals.get(meta['ValueBundleKey'], {}):
                rep.e('MissingValueName', dict(modifier=m['Key']), values=meta['ValueBundleKey'], name=m['ValueName'])
            else:
                used[meta['ValueBundleKey']].add(m['ValueName'])
    owned = {r['ValueBundleKey'] for _, r in owners(data, cfg)}
    for vb, names in vals.items():
        if vb not in owned:
            continue
        for name in sorted(set(names) - used[vb]):
            rep.w('UnusedValue', dict(values=vb), name=name)
    return reach, types
