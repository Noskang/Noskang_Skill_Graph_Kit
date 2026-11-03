"""Board keys: named values that nodes write and read while a skill runs.

Keys are rows of the board key table (settings board.table): Name (Object.Name), Type (int, float, bool, text or
key:<band role>), Scope (Cast = one cast with the effects it spawns, Actor = one character, kept by the game), Default.
A slot reads a key's value when its link source is Board. A slot of module type 'board' names a key instead: the node
writes that key ('board', 'board:<type>,<type>') or reads it by name ('board:read[,<type>,…]', e.g. a check). A board
slot takes only a Board link.
"""
from .core import NAME_RE, is_num

TYPES = ('int', 'float', 'bool', 'text')


def is_board_slot(kind):
    return kind == 'board' or kind.startswith('board:')


def board_slot(kind):
    """(writes, key types or None = any) of a board slot."""
    parts = [x.strip() for x in kind[6:].split(',') if x.strip()] if kind.startswith('board:') else []
    return 'read' not in parts, [p for p in parts if p != 'read'] or None


def board_keys(data, cfg, reg):
    """{name: row} of the board key table; empty when boards are off."""
    t = cfg.board_table(reg)
    return {r['Name']: r for r in data.get(t, []) if r.get('Name')} if t else {}


def type_ok(kind, cfg):
    if kind in TYPES:
        return True
    if isinstance(kind, str) and kind.startswith('key:'):
        try:
            cfg.band(kind[4:])
            return True
        except (KeyError, ValueError):
            return False
    return False


def default_ok(row, cfg):
    kind, v = row['Type'], row.get('Default')
    if v is None:
        return True
    if kind == 'bool':
        return isinstance(v, bool) or (is_num(v) and v in (0, 1)) or (isinstance(v, str) and v.lower() in ('true', 'false'))
    if kind == 'text':
        return isinstance(v, str)
    if not is_num(v):
        return False
    if kind.startswith('key:'):
        return v == int(v) and (v == 0 or cfg.is_key(int(v), cfg.band(kind[4:])))
    return kind == 'float' or v == int(v)


def default_value(row):
    """The value a scope starts with: the Default cell, or 0, false or empty text when it is blank."""
    kind, v = row['Type'], row.get('Default')
    if kind == 'bool':
        return v.lower() == 'true' if isinstance(v, str) else bool(v)
    if kind == 'text':
        return v or ''
    if kind == 'float':
        return float(v or 0)
    return int(v or 0)


def same_type(a, b, cfg):
    if a.startswith('key:') and b.startswith('key:'):
        return cfg.band(a[4:]) == cfg.band(b[4:])
    return a == b


def reads(slot, key, cfg):
    """Can a slot of this type read a key of this type? (enum and list slots stay fixed in the graph)"""
    if slot in ('float', 'time'):
        return key in ('int', 'float')
    if slot == 'string':
        return key == 'text'
    if slot in ('int', 'bool') or slot.startswith('key:'):
        return same_type(slot, key, cfg)
    return False


def key_json(row):
    return dict(name=row['Name'], type=row['Type'], scope=row['Scope'], default=default_value(row))


def usage(mods, d):
    """[(bundle, node, slot, key, writes)] for every Board link."""
    out = []
    for k in sorted(mods):
        b = mods[k]
        for (nid, slot), lk in sorted(b['links'].items()):
            if lk['source'] != 'Board' or b['nodes'][nid]['Module'] not in d.by_name:
                continue
            _, s = d.slot(b['nodes'][nid]['Module'], slot)
            if s is not None:
                out.append((k, nid, slot, lk['value'], is_board_slot(s[1]) and board_slot(s[1])[0]))
    return out


def validate_board(data, mods, rep, d, cfg, reg, unused=None):
    """Key table rows, then every Board link against the table. unused takes the UnusedBoardKey warnings (like UnusedValue)."""
    keys = board_keys(data, cfg, reg)
    scopes = cfg.board_scopes()
    for name, r in keys.items():
        at = dict(sheet=r.get('_sheet'), row=r.get('_row'))
        if not NAME_RE.match(name):
            rep.e('BoardKeyName', at, name=name)
        if not type_ok(r.get('Type'), cfg):
            rep.e('BoardKeyType', at, name=name, type=r.get('Type'))
        elif not default_ok(r, cfg):
            rep.e('BoardKeyType', at, 'BoardKeyType.default', name=name, type=r['Type'], value=r.get('Default'))
        if r.get('Scope') not in scopes:
            rep.e('BoardScopeMismatch', at, 'BoardScopeMismatch.unknown', name=name, scope=r.get('Scope'), allowed=', '.join(scopes))
    used = set()
    for k, nid, slot, name, _ in usage(mods, d):
        at = dict(bundle=k, node=nid, slot=slot)
        r = keys.get(name)
        if r is None:
            rep.e('UnknownBoardKey', at, name=name)
            continue
        used.add(name)
        kind = r.get('Type')
        if type_ok(kind, cfg):
            _, s = d.slot(mods[k]['nodes'][nid]['Module'], slot)
            if is_board_slot(s[1]):
                allowed = board_slot(s[1])[1]
                if allowed is not None and not any(same_type(a, kind, cfg) for a in allowed if type_ok(a, cfg)):
                    rep.e('BoardTypeMismatch', at, 'BoardTypeMismatch.slot', name=name, key=kind, allowed=', '.join(allowed))
            elif not reads(s[1], kind, cfg):
                rep.e('BoardTypeMismatch', at, name=name, key=kind, type=s[1])
        kinds = cfg.scope_kinds(r.get('Scope'))
        if kinds is not None and mods[k]['kind'] not in kinds:
            rep.e('BoardScopeMismatch', at, name=name, scope=r.get('Scope'), kind=mods[k]['kind'])
    unused_rep = rep if unused is None else unused
    for name in sorted(set(keys) - used):
        unused_rep.w('UnusedBoardKey', dict(sheet=keys[name].get('_sheet'), row=keys[name].get('_row')), name=name)
    return keys
