"""Graph editor backend: edit operations, the edit model, change list, Excel write-back, aux file and practice workspace.

The Unity window only draws. An edit session is "the workbooks as first read + a list of operations"; the window sends
that list here and gets a full model back. Undo/redo is just shortening or growing the list, so it lives in the window.
Excel is written only when the user presses Save:

  * only changed cells are written; a new row goes right under the last row of its bundle; deleted rows pull rows up
  * lock check (Excel owner file, open-for-write) and "changed since the editor read it" check first
  * temp file -> one previous copy in the backup folder -> replace -> read back and compare (restore the copy on mismatch)

Aux file (layout-free editor notes: collapsed bundles, named bands, node memos, combos, empty columns) is one JSON file
next to the workbooks and is never part of the exported data. The practice workspace is a copy of the product
workbooks in its own folder; keys created there use the reserved practice owner number.
"""
import collections
import copy
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

from .core import Dictionary, Report, SkillDataError, load_registry, sha_file
from .keyframes import KeyframeError, is_keyframe_ref, motion_keyframes
from .messages import text
from .pipeline import run_data
from .expand import expand_modules, expand_values
from .validate import owners as owner_rows, spawn_targets
from .workbook import lock_state, open_workbook, parse, read_table


class OpError(Exception):
    """An operation cannot be applied; the message is shown to the user."""


# ------------------------------------------------------------------ workspace and first read
class Workspace:
    def __init__(self, cfg, kind='product'):
        if kind not in ('product', 'practice'):
            raise OpError(text(cfg['language'], 'op.workspace', kind=kind))
        self.cfg, self.kind = cfg, kind
        self.folder = cfg.data_dir if kind == 'product' else cfg.practice_dir
        self.dictionary = cfg.dictionary_path
        self.files = cfg.documents(self.folder)
        self.aux = cfg.aux_path(self.folder)

    @property
    def practice(self):
        return self.kind == 'practice'

    def exists(self):
        required = [p for doc, p in self.files.items() if doc not in self.cfg['optionalDocuments']]
        return bool(required) and all(p is not None and Path(p).is_file() for p in required)

    def signature(self):
        out = {}
        for doc, p in self.files.items():
            if p and Path(p).is_file():
                s = Path(p).stat()
                out[doc] = (s.st_mtime_ns, s.st_size)
        if self.aux.is_file():
            s = self.aux.stat()
            out['aux'] = (s.st_mtime_ns, s.st_size)
        return out


class Loaded:
    """First read: rows per table in sheet order, each with its original Excel row. Edits work on a deep copy."""

    def __init__(self, ws):
        cfg = ws.cfg
        self.ws, self.cfg, self.lang = ws, cfg, cfg['language']
        self.dictionary = Dictionary(ws.dictionary, cfg)
        self.registry = load_registry(cfg, self.dictionary)
        self.report = Report(self.lang)
        self.tables, self.base = {}, {}
        if not ws.exists():
            raise OpError(text(self.lang, 'op.noWorkspacePractice' if ws.practice else 'op.noWorkspace', folder=ws.folder))
        for doc, path in ws.files.items():
            if path is None:
                continue
            self.base[doc] = sha_file(path)
            wb = open_workbook(path)
            try:
                for t in self.registry['tables']:
                    if t['document'] != doc:
                        continue
                    rows = read_table(wb, t, self.report, self.registry, cfg)
                    for r in rows:
                        r['_id'] = f"{t['json']}:{r['_row']}"
                    self.tables[t['json']] = dict(table=t, doc=doc, sheet=t['sheet'], rows=rows)
            finally:
                wb.close()
        for t in self.registry['tables']:
            self.tables.setdefault(t['json'], dict(table=t, doc=t['document'], sheet=t['sheet'], rows=[]))
        self.aux = load_aux(ws.aux, cfg)
        self.signature = ws.signature()


def load_aux(path, cfg):
    fmt = cfg['formats']['aux']
    if Path(path).is_file():
        aux = json.loads(Path(path).read_text(encoding='utf-8'))
        if aux.get('format') != fmt:
            raise OpError(text(cfg['language'], 'op.auxFormat', path=Path(path).name))
        aux.setdefault('bundles', {})
        aux.setdefault('combos', [])
        return aux
    return dict(format=fmt, bundles={}, combos=[])


# ------------------------------------------------------------------ edit state
class Edit:
    def __init__(self, loaded):
        self.loaded, self.cfg, self.lang = loaded, loaded.cfg, loaded.lang
        self.d = loaded.dictionary
        self.reg = loaded.registry
        self.tables = {k: dict(v, rows=[dict(r) for r in v['rows']]) for k, v in loaded.tables.items()}
        self.aux = copy.deepcopy(loaded.aux)
        self.new_count = 0
        self.cols = {k: {c['name']: c for c in v['table']['columns']} for k, v in self.tables.items()}

    def fail(self, _msg, **kw):
        raise OpError(text(self.lang, _msg, **kw))

    def rows(self, name):
        return self.tables[name]['rows']

    def find(self, name, **key):
        for r in self.rows(name):
            if all(r.get(k) == v for k, v in key.items()):
                return r
        return None

    def typed(self, name, col, value, where):
        """Check a value from the window against the column type (same rules as reading Excel)."""
        c = self.cols[name][col]
        if value is None or value == '':
            if c['required']:
                self.fail('op.notEmpty', where=where, col=col)
            return c['default']
        if isinstance(value, float) and value.is_integer() and (c['type'] == 'int' or c['type'].startswith('key:')):
            value = int(value)
        rep = Report(self.lang)
        v = parse(c, value, rep, dict(text=where), self.reg['enums'], self.cfg)
        if rep.errors:
            raise OpError(f'{rep.errors[0].message} ({where} {col})')
        return v

    def set(self, name, row, col, value):
        v = self.typed(name, col, value, f"{self.tables[name]['sheet']} {row['_row']}")
        if row.get(col) != v:
            row[col] = v

    def insert(self, name, values, group_col=None):
        """New row: right under the last row with the same group_col value (sheet end when there is none)."""
        t = self.tables[name]
        row = {c['name']: c['default'] for c in t['table']['columns']}
        for col, v in values.items():
            row[col] = self.typed(name, col, v, f"{t['sheet']} +")
        for c in t['table']['columns']:
            if c['required'] and row.get(c['name']) is None:
                self.fail('op.newRowRequired', sheet=t['sheet'], col=c['name'])
        self.new_count += 1
        row.update(_row=f'+{self.new_count}', _sheet=t['sheet'], _id=f'new:{name}:{self.new_count}', _new=True)
        rows = t['rows']
        at = len(rows)
        if group_col is not None:
            same = [i for i, r in enumerate(rows) if r.get(group_col) == row[group_col]]
            if same:
                at = same[-1] + 1
        rows.insert(at, row)
        return row

    def delete(self, name, row):
        self.tables[name]['rows'].remove(row)

    def data(self):
        return {k: v['rows'] for k, v in self.tables.items()}

    def expanded(self):
        rep = Report(self.lang)
        d = self.data()
        return expand_modules(d, rep, self.cfg), expand_values(d, rep, self.cfg)

    def bundle_row(self, key):
        r = self.find('ModuleBundleData', Key=key)
        if r is None:
            self.fail('op.noBundle', key=key)
        return r

    def _chain(self, table, key):
        parents = {r['Key']: r['ParentKey'] for r in self.rows(table)}
        out, cur = [key], parents.get(key)
        while cur is not None and cur not in out:
            out.append(cur)
            cur = parents.get(cur)
        return list(reversed(out))

    def chain(self, key):
        return self._chain('ModuleBundleData', key)

    def value_chain(self, key):
        return self._chain('ValueBundleData', key)

    def used_keys(self):
        keys = set()
        for o in self.cfg.owner_tables():
            keys |= {r['Key'] for r in self.rows(o['table']) if isinstance(r.get('Key'), int)}
        for name in ('ModuleBundleData', 'ValueBundleData', 'ModifierData'):
            keys |= {r['Key'] for r in self.rows(name) if isinstance(r.get('Key'), int)}
        return keys

    def new_key(self, band, owner, prefer=None, reserved=()):
        """Smallest free serial for this band and owner (prefer a given serial when free). Overflow bands follow."""
        cfg = self.cfg
        used = self.used_keys() | set(reserved)
        bands = [cfg.band(band)] + [cfg.band(b) for b in cfg['keys']['overflow'].get(band, [])]
        for b in bands:
            if prefer is not None and b == bands[0] and 1 <= prefer <= cfg.max_serial():
                k = cfg.make_key(b, int(owner), prefer)
                if k not in used:
                    return k
            for serial in range(1, cfg.max_serial() + 1):
                k = cfg.make_key(b, int(owner), serial)
                if k not in used:
                    return k
        self.fail('op.noSerial', band=bands[0], owner=owner)

    def node_ids(self, key):
        """Node ids used anywhere in the bundle family (bundles that share the same root)."""
        root = self.chain(key)[0]
        family = {r['Key'] for r in self.rows('ModuleBundleData') if self.chain(r['Key'])[0] == root}
        return {r['NodeId'] for r in self.rows('ModuleNodeData') if r['BundleKey'] in family}

    def next_node_id(self, key):
        starts = self.cfg['nodeIdStart']
        depth = len(self.chain(key)) - 1
        used = self.node_ids(key)
        n = starts[min(depth, len(starts) - 1)]
        while n in used:
            n += 1
        return n


# ------------------------------------------------------------------ operations
def op_set_link(e, o):
    """Set a slot's source and value. source None removes this bundle's row (falls back to the parent or default)."""
    b, nid, slot = o['bundle'], o['node'], o['slot']
    e.bundle_row(b)
    row = e.find('ValueLinkData', BundleKey=b, NodeId=nid, Slot=slot)
    src = o.get('source')
    if src is None:
        if row is not None:
            e.delete('ValueLinkData', row)
        return
    if src not in ('Ref', 'Fixed', 'Default'):
        e.fail('op.source', source=src)
    value = None if src == 'Default' else o.get('value')
    if row is None:
        e.insert('ValueLinkData', dict(BundleKey=b, NodeId=nid, Slot=slot, Source=src, Value=value), 'BundleKey')
    else:
        e.set('ValueLinkData', row, 'Source', src)
        e.set('ValueLinkData', row, 'Value', value)


def op_add_node(e, o):
    b = o['bundle']
    br = e.bundle_row(b)
    mods, _ = e.expanded()
    nid = o.get('node') or e.next_node_id(b)
    if nid in e.node_ids(b):
        e.fail('op.nodeIdUsed', node=nid)
    order = o.get('order')
    if order is None:
        same = [n['Order'] or 0 for n in mods.get(b, {}).get('nodes', {}).values()
                if n['Line'] == o.get('line', 'Logic') and n['Event'] == o['event'] and n['EventValue'] == o.get('eventValue') and n['FromNode'] == o.get('fromNode')]
        order = (max(same) if same else 0) + 10
    child = br['ParentKey'] is not None
    note = e.cfg['columns']['nodeNote']
    values = dict(BundleKey=b, NodeId=nid, Line=o.get('line', 'Logic'), Event=o['event'], EventValue=o.get('eventValue'),
                  FromNode=o.get('fromNode'), Order=order, Priority=o.get('priority') or None, Module=o['module'], Override='+' if child else None)
    if note in e.cols['ModuleNodeData']:
        values[note] = o.get('note')
    e.insert('ModuleNodeData', values, 'BundleKey')
    for lk in o.get('links', []):
        op_set_link(e, dict(bundle=b, node=nid, slot=lk['slot'], source=lk['source'], value=lk.get('value')))
    o['node'] = nid


def own_node_row(e, b, nid):
    return e.find('ModuleNodeData', BundleKey=b, NodeId=nid)


def op_remove_node(e, o):
    """A node this bundle defines (root or '+') loses its row; an inherited node gets a '-' row."""
    b, nid = o['bundle'], o['node']
    br = e.bundle_row(b)
    row = own_node_row(e, b, nid)
    for lk in [r for r in e.rows('ValueLinkData') if r['BundleKey'] == b and r['NodeId'] == nid]:
        e.delete('ValueLinkData', lk)
    if row is not None and (br['ParentKey'] is None or row['Override'] == '+'):
        e.delete('ModuleNodeData', row)
        return
    if row is not None:
        e.delete('ModuleNodeData', row)
    if br['ParentKey'] is None:
        e.fail('op.noNode', bundle=b, node=nid)
    e.insert('ModuleNodeData', dict(BundleKey=b, NodeId=nid, Override='-'), 'BundleKey')


NODE_COLUMNS = dict(event='Event', eventValue='EventValue', fromNode='FromNode', order='Order', priority='Priority', line='Line')


def op_move_node(e, o):
    """Move a node: event column, time, source node, order. Inherited nodes may only change time, order and priority."""
    b, nid = o['bundle'], o['node']
    br = e.bundle_row(b)
    row = own_node_row(e, b, nid)
    defined_here = row is not None and (br['ParentKey'] is None or row['Override'] == '+')
    fields = {k: o[k] for k in NODE_COLUMNS if k in o}
    if defined_here:
        for k, v in fields.items():
            e.set('ModuleNodeData', row, NODE_COLUMNS[k], v)
        return
    mods, _ = e.expanded()
    node = mods.get(b, {}).get('nodes', {}).get(nid)
    if node is None:
        e.fail('op.noNode', bundle=b, node=nid)
    if 'event' in fields and fields['event'] != node['Event']:
        e.fail('op.inheritedEvent', node=nid, owner=node['_from'])
    if 'line' in fields and fields['line'] != node['Line']:
        e.fail('op.inheritedLine', node=nid, owner=node['_from'])
    if 'fromNode' in fields and fields['fromNode'] != node['FromNode']:
        e.fail('op.inheritedFrom', node=nid, owner=node['_from'])
    if row is None:
        row = e.insert('ModuleNodeData', dict(BundleKey=b, NodeId=nid), 'BundleKey')
    for k in ('eventValue', 'order', 'priority'):
        if k in fields:
            e.set('ModuleNodeData', row, NODE_COLUMNS[k], fields[k])


def op_revert(e, o):
    """Drop what this bundle changed on a node (one slot when slot is given; otherwise override, slot and '-' rows)."""
    b, nid, slot = o['bundle'], o['node'], o.get('slot')
    if slot is not None:
        op_set_link(e, dict(bundle=b, node=nid, slot=slot, source=None))
        return
    row = own_node_row(e, b, nid)
    if row is not None and row['Override'] == '+':
        e.fail('op.revertAdded', node=nid)
    if row is not None:
        e.delete('ModuleNodeData', row)
    for lk in [r for r in e.rows('ValueLinkData') if r['BundleKey'] == b and r['NodeId'] == nid]:
        e.delete('ValueLinkData', lk)


def _anchor(e, name, key):
    rows = e.rows(name)
    same = [i for i, r in enumerate(rows) if r.get('BundleKey') == key]
    return same[-1] + 1 if same else len(rows)


def op_apply_to_parent(e, o):
    """Move this bundle's slot row (or node override) up to the parent bundle. Every owner of the parent is affected."""
    b, nid, slot = o['bundle'], o['node'], o.get('slot')
    br = e.bundle_row(b)
    parent = br['ParentKey']
    if parent is None:
        e.fail('op.noParent', bundle=b)
    if slot is not None:
        row = e.find('ValueLinkData', BundleKey=b, NodeId=nid, Slot=slot)
        if row is None:
            e.fail('op.slotNotHere', bundle=b, node=nid, slot=slot)
        op_set_link(e, dict(bundle=parent, node=nid, slot=slot, source=row['Source'], value=row['Value']))
        e.delete('ValueLinkData', row)
        return
    row = own_node_row(e, b, nid)
    if row is None:
        e.fail('op.nodeNotHere', bundle=b, node=nid)
    if row['Override'] == '+':
        prow = e.bundle_row(parent)
        row['BundleKey'] = parent
        row['Override'] = '+' if prow['ParentKey'] is not None else 'None'
        e.delete('ModuleNodeData', row)
        e.tables['ModuleNodeData']['rows'].insert(_anchor(e, 'ModuleNodeData', parent), row)
        for lk in [r for r in e.rows('ValueLinkData') if r['BundleKey'] == b and r['NodeId'] == nid]:
            op_set_link(e, dict(bundle=parent, node=nid, slot=lk['Slot'], source=lk['Source'], value=lk['Value']))
            e.delete('ValueLinkData', lk)
        return
    if row['Override'] == '-':
        e.fail('op.removedToParent')
    target = own_node_row(e, parent, nid)
    if target is None:
        target = e.insert('ModuleNodeData', dict(BundleKey=parent, NodeId=nid), 'BundleKey')
    for f in ('EventValue', 'Order', 'Priority'):
        if row.get(f) not in (None, 0):
            target[f] = row[f]
    e.delete('ModuleNodeData', row)


def _owner_of(e, key):
    if not key:
        e.fail('op.ownerUnknown')
    return e.cfg.key_owner(key)


def op_new_bundle(e, o):
    """New module bundle; with a parent it becomes a child layer of that bundle."""
    parent = o.get('parent')
    kind = o.get('kind') or (e.bundle_row(parent)['Kind'] if parent else None)
    if kind not in e.cfg.kinds:
        e.fail('op.kindRequired', kinds=', '.join(e.cfg.kinds))
    key = o.get('key') or e.new_key('moduleBundle', o['ownerNumber'] if o.get('ownerNumber') is not None else _owner_of(e, parent), o.get('serial'))
    if e.find('ModuleBundleData', Key=key):
        e.fail('op.bundleExists', key=key)
    e.insert('ModuleBundleData', dict(Key=key, Kind=kind, ParentKey=parent, Desc=o.get('desc') or text(e.lang, 'new.bundle', key=key)))
    o['key'] = key


def op_set_bundle(e, o):
    row = e.bundle_row(o['bundle'])
    if 'desc' in o:
        e.set('ModuleBundleData', row, 'Desc', o['desc'])
    if 'parent' in o:
        e.set('ModuleBundleData', row, 'ParentKey', o['parent'])


def op_set_value(e, o):
    """Set a value in one value bundle (per tier). value None removes this bundle's row (falls back to the parent)."""
    vb, name = o['values'], o['name']
    if e.find('ValueBundleData', Key=vb) is None:
        e.fail('op.noValues', key=vb)
    row = e.find('ValueData', BundleKey=vb, Name=name)
    if o.get('value') is None and 'desc' not in o:
        if row is not None:
            e.delete('ValueData', row)
        return
    if row is None:
        e.insert('ValueData', dict(BundleKey=vb, Name=name, Value=o['value'], Desc=o.get('desc')), 'BundleKey')
    else:
        if o.get('value') is not None:
            e.set('ValueData', row, 'Value', o['value'])
        if 'desc' in o:
            e.set('ValueData', row, 'Desc', o['desc'])


def op_apply_value_to_parent(e, o):
    vb, name = o['values'], o['name']
    br = e.find('ValueBundleData', Key=vb)
    if br is None or br['ParentKey'] is None:
        e.fail('op.valuesNoParent', key=vb)
    row = e.find('ValueData', BundleKey=vb, Name=name)
    if row is None:
        e.fail('op.valueNotHere', key=vb, name=name)
    op_set_value(e, dict(values=br['ParentKey'], name=name, value=row['Value']))
    e.delete('ValueData', row)


def op_new_values(e, o):
    parent = o.get('parent')
    key = o.get('key') or e.new_key('valueBundle', o['ownerNumber'] if o.get('ownerNumber') is not None else _owner_of(e, parent), o.get('serial'))
    if e.find('ValueBundleData', Key=key):
        e.fail('op.valuesExists', key=key)
    e.insert('ValueBundleData', dict(Key=key, ParentKey=parent, Desc=o.get('desc') or text(e.lang, 'new.values', key=key)))
    o['key'] = key


def _owner_table(e, kind):
    for t in e.cfg.owner_tables():
        if kind in (t['kindName'], t['kind']):
            return t
    e.fail('op.kindRequired', kinds=', '.join(t['kindName'] for t in e.cfg.owner_tables()))


def _owner_number(e, o, fallback):
    owner = o.get('ownerNumber')
    if owner is None and e.loaded.ws.practice:
        owner = e.cfg['practiceOwner']
    if owner is None:
        owner = _owner_of(e, fallback)
    return int(owner)


def _shared_serial(e, t, owner):
    cfg = e.cfg
    used = e.used_keys()
    bands = (t['band'], 'moduleBundle', 'valueBundle')
    serial = next((s for s in range(1, cfg.max_serial() + 1) if all(cfg.make_key(b, owner, s) not in used for b in bands)), None)
    if serial is None:
        e.fail('op.noSharedSerial', owner=owner)
    return [cfg.make_key(b, owner, serial) for b in bands]


def _seed_values(e, bundle, values):
    """Value names the new owner's bundle reads but its value chain lacks get a 0 row marked 'set this value'."""
    mods, vals = e.expanded()
    have = vals.get(values, {})
    need = []
    for (nid, slot), lk in mods.get(bundle, {}).get('links', {}).items():
        if lk['source'] == 'Ref' and isinstance(lk['value'], str) and lk['value'] not in have and lk['value'] not in need:
            need.append(lk['value'])
    for n in mods.get(bundle, {}).get('nodes', {}).values():
        v = n['EventValue']
        if isinstance(v, str) and not is_keyframe_ref(v) and n['Event'] in e.d.timed_events and v not in have and v not in need:
            need.append(v)
    for name in need:
        e.insert('ValueData', dict(BundleKey=values, Name=name, Value=0, Desc=text(e.lang, 'new.seed')), 'BundleKey')
    return need


def _loc(t, key):
    return t['loc'].format(key=key)


def op_new_skill(e, o):
    """New owner (skill or buff): owner row + module bundle + value bundle that share the same serial."""
    t = _owner_table(e, o.get('kind', e.cfg.kind('skill')))
    owner = _owner_number(e, o, o.get('moduleParent'))
    key, bkey, vkey = _shared_serial(e, t, owner)
    name = o.get('name') or text(e.lang, 'new.skill', key=key)
    e.insert('ModuleBundleData', dict(Key=bkey, Kind=t['kindName'], ParentKey=o.get('moduleParent'), Desc=o.get('desc') or name))
    e.insert('ValueBundleData', dict(Key=vkey, ParentKey=o.get('valueParent'), Desc=text(e.lang, 'new.valuesOf', name=name)))
    e.insert(t['table'], dict(Key=key, NameRef=name, NameLoc=_loc(t, key), ModuleBundleKey=bkey, ValueBundleKey=vkey))
    seeded = _seed_values(e, bkey, vkey)
    o.update(key=key, bundle=bkey, values=vkey, seeded=seeded)


def _copy_bundle(e, src, dst, mapping):
    sp = e.cfg['modules']['spawn']
    row = e.bundle_row(src)
    e.insert('ModuleBundleData', dict(Key=dst, Kind=row['Kind'], ParentKey=row['ParentKey'], Desc=text(e.lang, 'new.copy', name=row['Desc'])))
    cols = [c for c in e.cols['ModuleNodeData'] if c != 'BundleKey']
    for r in [r for r in e.rows('ModuleNodeData') if r['BundleKey'] == src]:
        e.insert('ModuleNodeData', {k: r.get(k) for k in cols} | dict(BundleKey=dst), 'BundleKey')
    for r in [r for r in e.rows('ValueLinkData') if r['BundleKey'] == src]:
        value = mapping.get(r['Value'], r['Value']) if r['Slot'] == sp['slot'] and r['Source'] == 'Fixed' else r['Value']
        e.insert('ValueLinkData', dict(BundleKey=dst, NodeId=r['NodeId'], Slot=r['Slot'], Source=r['Source'], Value=value), 'BundleKey')


def op_duplicate_skill(e, o):
    """Copy an owner under new keys. withEffects also copies the effect bundles it spawns and points the copies at them."""
    cfg = e.cfg
    src_key = o['owner']
    t = cfg.owner_table_of(src_key)
    src = e.find(t['table'], Key=src_key) if t else None
    if src is None:
        e.fail('op.noMeta', key=src_key)
    owner = _owner_number(e, o, src_key)
    key, bkey, vkey = _shared_serial(e, t, owner)
    mapping = {src['ModuleBundleKey']: bkey}
    if o.get('withEffects'):
        mods, _ = e.expanded()
        todo = [src['ModuleBundleKey']]
        while todo:
            k = todo.pop(0)
            for _, target in spawn_targets(mods.get(k, dict(links={}, nodes={})), cfg):
                if target not in mapping:
                    mapping[target] = None
                    todo.append(target)
        for k in list(mapping):
            if mapping[k] is None:
                mapping[k] = e.new_key('moduleBundle', owner, reserved={bkey} | {v for v in mapping.values() if v})
        for k, v in list(mapping.items()):
            if k != src['ModuleBundleKey']:
                _copy_bundle(e, k, v, mapping)
    _copy_bundle(e, src['ModuleBundleKey'], bkey, mapping)
    vrow = e.find('ValueBundleData', Key=src['ValueBundleKey'])
    name = o.get('name') or text(e.lang, 'new.copy', name=src['NameRef'])
    e.insert('ValueBundleData', dict(Key=vkey, ParentKey=vrow['ParentKey'], Desc=text(e.lang, 'new.valuesOf', name=name)))
    for r in [r for r in e.rows('ValueData') if r['BundleKey'] == src['ValueBundleKey']]:
        e.insert('ValueData', dict(BundleKey=vkey, Name=r['Name'], Value=r['Value'], Desc=r['Desc']), 'BundleKey')
    fields = {c: src.get(c) for c in e.cols[t['table']]}
    fields.update(Key=key, NameRef=name, ModuleBundleKey=bkey, ValueBundleKey=vkey, NameLoc=_loc(t, key))
    if 'DescLoc' in fields:
        fields['DescLoc'] = None
    e.insert(t['table'], fields)
    o.update(key=key, bundle=bkey, values=vkey, effects={str(k): v for k, v in mapping.items() if k != src['ModuleBundleKey']})


def op_set_meta(e, o):
    key = o['owner']
    t = e.cfg.owner_table_of(key)
    row = e.find(t['table'], Key=key) if t else None
    if row is None:
        e.fail('op.noMeta', key=key)
    fields = o.get('fields') or {k: v for k, v in o.items() if k in e.cols[t['table']]}
    for col, v in fields.items():
        if col in e.cols[t['table']] and col != 'Key':
            e.set(t['table'], row, col, v)


def op_insert_combo(e, o):
    """Insert a saved combo as ordinary node rows. Relative source nodes are mapped to the new ids."""
    combo = next((c for c in e.aux['combos'] if c['name'] == o['combo']), None)
    if combo is None:
        e.fail('op.noCombo', name=o['combo'])
    ids, added = {}, []
    for n in combo['nodes']:
        a = dict(bundle=o['bundle'], line=n.get('line', 'Logic'), event=o.get('event') or n['event'], eventValue=o.get('eventValue', n.get('eventValue')),
                 fromNode=ids.get(n.get('fromNode')) if n.get('fromNode') else o.get('fromNode'), module=n['module'], links=n.get('links', []), order=None)
        op_add_node(e, a)
        ids[n['id']] = a['node']
        added.append(a['node'])
    o['nodes'] = added


def op_save_combo(e, o):
    mods, _ = e.expanded()
    b = mods.get(o['bundle'])
    if b is None:
        e.fail('op.noBundle', key=o['bundle'])
    nodes = []
    for nid in o['nodes']:
        n = b['nodes'][nid]
        links = [dict(slot=s, source=lk['source'], value=lk['value']) for (lnid, s), lk in sorted(b['links'].items()) if lnid == nid]
        nodes.append(dict(id=nid, line=n['Line'], event=n['Event'], eventValue=n['EventValue'],
                          fromNode=n['FromNode'] if n['FromNode'] in o['nodes'] else None, module=n['Module'], links=links))
    e.aux['combos'] = [c for c in e.aux['combos'] if c['name'] != o['name']] + [dict(name=o['name'], desc=o.get('desc', ''), nodes=nodes)]


def op_remove_combo(e, o):
    e.aux['combos'] = [c for c in e.aux['combos'] if c['name'] != o['name']]


def _aux_bundle(e, key):
    return e.aux['bundles'].setdefault(str(key), {})


def op_set_memo(e, o):
    memos = _aux_bundle(e, o['bundle']).setdefault('memos', {})
    if o.get('text'):
        memos[str(o['node'])] = o['text']
    else:
        memos.pop(str(o['node']), None)


def op_set_bands(e, o):
    _aux_bundle(e, o['bundle'])['bands'] = [dict(name=x['name'], **{'from': x['from'], 'to': x['to']}) for x in o.get('bands', [])]


def op_set_collapsed(e, o):
    _aux_bundle(e, o['bundle'])['collapsed'] = bool(o.get('collapsed'))


# ---- columns (event conditions). A column = nodes that share event, time and source node.
# An empty column has no Excel row yet, so it lives in the aux file until a node moves in.
def column_id(e, event, value, frm):
    """Same unit as the window's column key: timed and subscription columns include the value, exit columns the source."""
    if event in e.d.timed_events or event in e.d.subscription_events:
        return (event, value, None)
    if event in e.d.exit_events:
        return (event, None, frm)
    return (event, None, None)


def _column_spec(o, to=False):
    if to:
        return o['toEvent'], o.get('toEventValue'), o.get('toFromNode')
    return o['event'], o.get('eventValue'), o.get('fromNode')


def _column_nodes(e, b, cid, mods=None):
    mods = mods if mods is not None else e.expanded()[0]
    return sorted(nid for nid, n in mods.get(b, {}).get('nodes', {}).items() if column_id(e, n['Event'], n['EventValue'], n['FromNode']) == cid)


def _check_column(e, b, event, value, frm, mods):
    kind = e.bundle_row(b)['Kind']
    allowed = e.d.kind_events.get(kind, set())
    if event not in allowed:
        e.fail('op.columnEvent', event=event, kind=kind, allowed=', '.join(sorted(allowed, key=e.d.events.index)))
    if event in e.d.timed_events:
        if value is None or value == '':
            e.fail('op.columnTime')
        if isinstance(value, (int, float)) and value < 0:
            e.fail('op.columnNegative')
    if event in e.d.exit_events:
        src = mods.get(b, {}).get('nodes', {}).get(frm)
        if src is None:
            e.fail('op.columnFrom', node=frm, bundle=b)
        if not e.d.by_name.get(src['Module'], {}).get('exits'):
            e.fail('FromNodeNotCheck', module=src['Module'])


def _aux_columns(e, b):
    return _aux_bundle(e, b).setdefault('columns', [])


def _aux_column_index(e, b, cid):
    cols = e.aux['bundles'].get(str(b), {}).get('columns', [])
    return next((i for i, c in enumerate(cols) if column_id(e, c['event'], c.get('value'), c.get('from')) == cid), None)


def op_add_column(e, o):
    b = o['bundle']
    event, value, frm = _column_spec(o)
    mods, _ = e.expanded()
    _check_column(e, b, event, value, frm, mods)
    cid = column_id(e, event, value, frm)
    if _column_nodes(e, b, cid, mods) or any(_aux_column_index(e, k, cid) is not None for k in e.chain(b)):
        e.fail('op.columnExists')
    _aux_columns(e, b).append(dict(event=event, value=cid[1], **{'from': cid[2]}))


def op_remove_column(e, o):
    """Remove a column with all its nodes on this layer ('-' for inherited ones), and the exit columns those nodes raise."""
    b = o['bundle']
    todo, removed = [column_id(e, *_column_spec(o))], []
    while todo:
        cid = todo.pop()
        mods, _ = e.expanded()
        for nid in _column_nodes(e, b, cid, mods):
            op_remove_node(e, dict(bundle=b, node=nid))
            removed.append(nid)
            todo += [(x, None, nid) for x in e.d.exit_events]
        for k in e.chain(b):
            if k != b and _aux_column_index(e, k, cid) is not None:
                e.fail('op.columnParent', key=k)
        i = _aux_column_index(e, b, cid)
        if i is not None:
            del e.aux['bundles'][str(b)]['columns'][i]
    o['removed'] = removed


def op_set_column(e, o):
    """Change a column: every node in it moves. Inherited nodes may only change time on this layer."""
    b = o['bundle']
    mods, _ = e.expanded()
    cid = column_id(e, *_column_spec(o))
    to_event, to_value, to_from = _column_spec(o, True)
    _check_column(e, b, to_event, to_value, to_from, mods)
    to_cid = column_id(e, to_event, to_value, to_from)
    if to_cid == cid:
        return
    nodes = _column_nodes(e, b, cid, mods)
    if to_cid[0] != cid[0] or to_cid[2] != cid[2]:
        fixed = [f"#{nid}({mods[b]['nodes'][nid]['_from']})" for nid in nodes if mods[b]['nodes'][nid]['_from'] != b]
        if fixed:
            e.fail('op.columnInherited', nodes=', '.join(fixed))
    for nid in nodes:
        op_move_node(e, dict(bundle=b, node=nid, event=to_cid[0], eventValue=to_cid[1], fromNode=to_cid[2]))
    i = _aux_column_index(e, b, cid)
    if i is not None:
        e.aux['bundles'][str(b)]['columns'][i] = dict(event=to_cid[0], value=to_cid[1], **{'from': to_cid[2]})
    elif not nodes:
        e.fail('op.columnMissing', bundle=b)


def prune_aux_columns(e):
    """Empty columns that received nodes leave the aux file (the column now exists as Excel rows)."""
    if not any(v.get('columns') for v in e.aux['bundles'].values()):
        return
    mods, _ = e.expanded()
    for key, v in e.aux['bundles'].items():
        cols = v.get('columns')
        if not cols:
            continue
        live = {column_id(e, n['Event'], n['EventValue'], n['FromNode']) for n in mods.get(int(key), {}).get('nodes', {}).values()}
        v['columns'] = [c for c in cols if column_id(e, c['event'], c.get('value'), c.get('from')) not in live]
        if not v['columns']:
            del v['columns']


OPS = {'setLink': op_set_link, 'addNode': op_add_node, 'removeNode': op_remove_node, 'moveNode': op_move_node, 'revert': op_revert,
       'applyToParent': op_apply_to_parent, 'newBundle': op_new_bundle, 'setBundle': op_set_bundle, 'setValue': op_set_value,
       'applyValueToParent': op_apply_value_to_parent, 'newValues': op_new_values, 'newSkill': op_new_skill, 'duplicateSkill': op_duplicate_skill,
       'setMeta': op_set_meta, 'insertCombo': op_insert_combo, 'saveCombo': op_save_combo, 'removeCombo': op_remove_combo,
       'setMemo': op_set_memo, 'setBands': op_set_bands, 'setCollapsed': op_set_collapsed,
       'addColumn': op_add_column, 'removeColumn': op_remove_column, 'setColumn': op_set_column}
RESULT_FIELDS = ('key', 'node', 'bundle', 'values', 'nodes', 'effects', 'removed', 'seeded')


def apply_ops(loaded, ops):
    """Apply operations in order. A failure names the operation's position. Each operation gets its results (new ids)."""
    e = Edit(loaded)
    done = []
    for i, o in enumerate(ops or []):
        o = dict(o)
        f = OPS.get(o.get('op'))
        if f is None:
            e.fail('op.unknown', index=i + 1, op=o.get('op'))
        try:
            f(e, o)
        except OpError as error:
            raise OpError(text(e.lang, 'op.failed', index=i + 1, op=o['op'], error=error)) from None
        except KeyError as error:
            raise OpError(text(e.lang, 'op.missingField', index=i + 1, op=o['op'], field=error)) from None
        done.append(o)
    prune_aux_columns(e)
    return e, done


# ------------------------------------------------------------------ edit model (the JSON the window reads)
def run_edit(e):
    lo = e.loaded
    rep = Report(e.lang)
    rep.extend(lo.report)
    data = {k: list(v) for k, v in e.data().items()}
    return run_data(data, rep, lo.dictionary, lo.registry, e.cfg, True, lo.ws.practice)


def _slot_dict(s):
    return dict(name=s[0], type=s[1], unit=s[2], kind=s[3], default=s[4], timing=s[5], description=s[6], supported=s[7])


def _rel(cfg, p):
    p = Path(p)
    try:
        return p.resolve().relative_to(cfg.root).as_posix()
    except ValueError:
        return p.as_posix()


def _keyframes(b, data, d, cfg):
    out = []
    motion = cfg['keyframes']['motion']['module']
    for nid, n in sorted(b['nodes'].items()):
        if n['Module'] != motion:
            continue
        try:
            info = motion_keyframes(b, nid, data, d, cfg)
        except KeyframeError as error:
            out.append(dict(node=nid, error=text(cfg['language'], error.msg, **error.kw), keys=[]))
            continue
        out.append(dict(node=nid, clip=info['clip'], start=info['start'], speed=info['speed'], length=info['length'],
                        keys=[dict(name=name, frame=frame, rate=rate, seconds=sec) for name, frame, rate, sec in info['keys']]))
    return out


def _stages(data, cfg):
    """Tier families: owners that share a tier group, ordered by tier."""
    t = cfg['tiers']
    groups = {}
    for r in data.get(t['table'], []):
        g, level = r.get(t['group']), r.get(t['level'])
        if g and level is not None:
            groups.setdefault(g, []).append((level, r['Key']))
    out = {}
    for g, rows in groups.items():
        family = [k for _, k in sorted(rows)]
        for level, k in sorted(rows):
            out[k] = dict(group=g, level=level, family=family)
    return out


def _prefixes(bundle):
    """Object parts of the value names this bundle reads, most used first (to suggest names for new links)."""
    c = collections.Counter()
    for (nid, slot), lk in bundle['links'].items():
        if lk['source'] == 'Ref' and isinstance(lk['value'], str) and '.' in lk['value']:
            v = lk['value']
            c[v[:len(v) - len(slot) - 1] if v.endswith('.' + slot) else v.split('.')[0]] += 1
    return [p for p, _ in c.most_common()]


def build_model(e, r, done=()):
    lo, d, cfg = e.loaded, e.d, e.cfg
    data = e.data()
    status = cfg['modules']['status']
    model = dict(format=cfg['formats']['editModel'], language=cfg['language'],
                 workspace=dict(kind=lo.ws.kind, files={doc: _rel(cfg, p) for doc, p in lo.ws.files.items() if p}, base=lo.base,
                                aux=_rel(cfg, lo.ws.aux), practiceExists=Workspace(cfg, 'practice').exists()),
                 settings=dict(layers=cfg['layers'], time=cfg['time'], kinds=cfg['bundleKinds'], practiceOwner=cfg['practiceOwner'],
                               keyframeFunction=cfg['keyframes']['function'], balanceKind=cfg['dictionary']['balanceKind'],
                               modules=cfg['modules'], baseColumns=cfg['editor']['baseColumns'], keys=cfg['keys']['layout']),
                 dictionaryHash=d.hash, displayStale=d.display_stale,
                 dictionary=dict(events=d.events, kindEvents={k: sorted(v, key=d.events.index) for k, v in d.kind_events.items()},
                                 presentationEvents=sorted(d.presentation_events, key=d.events.index), subjects=d.subjects,
                                 timedEvents=sorted(d.timed_events, key=d.events.index), exitEvents=d.exit_events, startEvent=d.start_event,
                                 subscriptionEvents=sorted(d.subscription_events, key=d.events.index), slotEnums=d.slot_enums,
                                 drawn=cfg['presentationDrawn'],
                                 modules=[dict(name=m['name'], label=m.get('label', ''), line=m['line'], implemented=m['implemented'], events=m['events'],
                                               exits=m['exits'], note=m.get('note', ''), slots=[_slot_dict(s) for s in m['slots']]) for m in d.modules]),
                 display=d.display, issues=[i.to_dict() for i in r['errors'] + r['warnings']],
                 ops=[dict(op=o['op'], **{k: v for k, v in o.items() if k in RESULT_FIELDS}) for o in done],
                 owners=[], bundles=[], values=[], keyNames={}, resources=[], aux=e.aux)
    if 'mods' not in r:
        return model
    mods, vals, reach = r['mods'], r['vals'], r['reach'] or {}
    stage_of = _stages(data, cfg)
    used_by = collections.defaultdict(set)
    value_used_by = collections.defaultdict(set)
    for t, m in sorted(owner_rows(data, cfg), key=lambda x: x[1]['Key']):
        key = m['Key']
        bundles = reach.get(key, [])
        buffs = sorted({lk['value'] for b in bundles for (nid, s), lk in mods[b]['links'].items()
                        if s == status['slot'] and mods[b]['nodes'][nid]['Module'] == status['module'] and lk['source'] == 'Fixed'})
        for b in bundles:
            for c in mods[b]['chain']:
                used_by[c].add(key)
        for v in e.value_chain(m['ValueBundleKey']):
            value_used_by[v].add(key)
        model['owners'].append(dict(key=key, kind=t['kindName'], name=m['NameRef'], bundle=m['ModuleBundleKey'], values=m['ValueBundleKey'],
                                    bundles=list(bundles), buffs=buffs,
                                    chain=mods[m['ModuleBundleKey']]['chain'] if m['ModuleBundleKey'] in mods else [m['ModuleBundleKey']],
                                    valueChain=e.value_chain(m['ValueBundleKey']), stage=stage_of.get(key), note=m.get('Note') or ''))
    own_nodes = collections.defaultdict(dict)
    for row in data['ModuleNodeData']:
        own_nodes[row['BundleKey']][row['NodeId']] = row
    own_links = collections.defaultdict(dict)
    for row in data['ValueLinkData']:
        own_links[row['BundleKey']][(row['NodeId'], row['Slot'])] = row
    bundle_rows = {row['Key']: row for row in data['ModuleBundleData']}
    for k in sorted(mods):
        b = mods[k]
        parent = bundle_rows[k]['ParentKey']
        pnodes = mods[parent]['nodes'] if parent in mods else {}
        nodes = []
        for nid, n in sorted(b['nodes'].items()):
            slots = []
            mod = d.by_name.get(n['Module'])
            for s in (mod['slots'] if mod else []):
                lk = b['links'].get((nid, s[0]))
                own = own_links[k].get((nid, s[0]))
                entry = dict(name=s[0], source='default', value=s[4], definedIn=None, own=own is not None)
                if lk is not None:
                    entry.update(source='ref' if lk['source'] == 'Ref' else 'fixed', value=lk['value'], definedIn=lk['_from'])
                elif own is not None and own['Source'] == 'Default':
                    entry.update(definedIn=k)
                slots.append(entry)
            nodes.append(dict(id=nid, line=n['Line'], event=n['Event'], eventValue=n['EventValue'], fromNode=n['FromNode'], order=n['Order'],
                              priority=n['Priority'] or 0, module=n['Module'], note=n.get('Note') or '', definedIn=n['_from'],
                              added=bool(n['_mark']) and n['_from'] == k, fields=dict(n['_fields']), own=nid in own_nodes[k], slots=slots))
        removed = [dict(id=nid, module=n['Module'], event=n['Event'], line=n['Line']) for nid, n in sorted(pnodes.items()) if nid not in b['nodes']]
        model['bundles'].append(dict(key=k, kind=b['kind'], desc=b['desc'] or '', parent=parent, chain=b['chain'], nodes=nodes, removed=removed,
                                     usedBy=sorted(used_by.get(k, [])), prefixes=_prefixes(b), keyframes=_keyframes(b, data, d, cfg)))
    vrows = {row['Key']: row for row in data['ValueBundleData']}
    vdesc = {(row['BundleKey'], row['Name']): row.get('Desc') or '' for row in data['ValueData']}
    for k in sorted(vals):
        entries = [dict(name=n, value=v, definedIn=src, own=src == k, desc=vdesc.get((src, n), '')) for n, (v, src) in sorted(vals[k].items())]
        model['values'].append(dict(key=k, parent=vrows[k]['ParentKey'], desc=vrows[k]['Desc'] or '', chain=e.value_chain(k), entries=entries,
                                    usedBy=sorted(value_used_by.get(k, []))))
    names = model['keyNames']
    for _, m in owner_rows(data, cfg):
        names[str(m['Key'])] = m['NameRef']
    for table, col in (('ModuleBundleData', 'Desc'), ('ValueBundleData', 'Desc'), ('ModifierData', 'Desc')):
        for row in data.get(table, []):
            names[str(row['Key'])] = row.get(col) or ''
    for row in data.get('PresentationResourceData', []):
        if not row.get('Skin'):
            names[str(row['PresentationKey'])] = row.get('Desc') or ''
    model['resources'] = [dict(key=row['PresentationKey'], skin=row.get('Skin') or 0, clip=row.get('Clip') or '', effect=row.get('Effect') or '',
                               sound=row.get('Sound') or '', desc=row.get('Desc') or '') for row in data.get('PresentationResourceData', [])]
    return model


# ------------------------------------------------------------------ change list and save
def cell_value(c, v):
    """Typed value -> Excel cell value. Optional columns at their default stay blank (Override None, Priority 0)."""
    if v is None:
        return None
    if c['name'] == 'Override' and v == 'None':
        return None
    if c['name'] == 'Priority' and v == 0:
        return None
    if isinstance(v, list):
        return ','.join(str(x) for x in v)
    if v == '' and c['type'] in ('string', 'loc'):
        return '<empty>'
    return v


def _key(t, r):
    return ' / '.join(f"{k} {r.get(k)}" for k in t['table']['key'])


def changes(loaded, e):
    """Differences between the first read and the edit: changed cells, inserted rows (under which original row), deleted rows."""
    out = []
    for name, t in e.tables.items():
        cols = t['table']['columns']
        before = {r['_id']: r for r in loaded.tables[name]['rows']}
        after_ids = {r['_id'] for r in t['rows']}
        for r in loaded.tables[name]['rows']:
            if r['_id'] not in after_ids:
                out.append(dict(kind='delete', table=name, doc=t['doc'], sheet=t['sheet'], row=r['_row'], key=_key(t, r)))
        prev_original = None
        for r in t['rows']:
            if r.get('_new'):
                out.append(dict(kind='insert', table=name, doc=t['doc'], sheet=t['sheet'], after=prev_original, key=_key(t, r),
                                values={c['name']: cell_value(c, r.get(c['name'])) for c in cols}, id=r['_id']))
                continue
            prev_original = r['_row']
            old = before[r['_id']]
            for c in cols:
                if old.get(c['name']) != r.get(c['name']):
                    out.append(dict(kind='change', table=name, doc=t['doc'], sheet=t['sheet'], row=r['_row'], column=c['name'], col=cols.index(c) + 1,
                                    old=cell_value(c, old.get(c['name'])), new=cell_value(c, r.get(c['name'])), key=_key(t, r)))
    aux_changed = json.dumps(e.aux, sort_keys=True, ensure_ascii=False) != json.dumps(loaded.aux, sort_keys=True, ensure_ascii=False)
    return out, aux_changed


def check_save(loaded, e, base=None):
    items, aux_changed = changes(loaded, e)
    blocked = []
    for doc in sorted({c['doc'] for c in items}):
        path = loaded.ws.files[doc]
        expected = (base or loaded.base).get(doc)
        if Path(path).is_file() and expected and sha_file(path) != expected:
            blocked.append(text(e.lang, 'save.changed', file=Path(path).name))
        msg = lock_state(path, e.lang)
        if msg:
            blocked.append(msg)
    return items, aux_changed, blocked


def write_sheet(ws, t, items):
    """Write one sheet: cell changes by original row, then inserts and deletes from the bottom up (row numbers stay valid)."""
    cols = t['table']['columns']
    head = t['table']['headerRows']
    for c in [x for x in items if x['kind'] == 'change']:
        ws.cell(c['row'], c['col']).value = c['new']
    structural = collections.defaultdict(lambda: dict(insert=[], delete=False))
    for c in items:
        if c['kind'] == 'insert':
            structural[c['after'] if c['after'] is not None else head]['insert'].append(c)
        elif c['kind'] == 'delete':
            structural[c['row']]['delete'] = True
    for row in sorted(structural, reverse=True):
        s = structural[row]
        if s['insert']:
            n = len(s['insert'])
            if row < ws.max_row:
                ws.insert_rows(row + 1, n)
            style_row = row if row > head else (head + n + 1 if ws.max_row > head + n else None)
            for i, c in enumerate(s['insert']):
                for j, col in enumerate(cols, start=1):
                    cell = ws.cell(row + 1 + i, j)
                    cell.value = c['values'][col['name']]
                    if style_row:
                        src = ws.cell(style_row, j)
                        if src.has_style:
                            cell._style = copy.copy(src._style)
        if s['delete']:
            ws.delete_rows(row, 1)


def _backup_name(loaded, path):
    return loaded.cfg.backup_dir / (('practice_' if loaded.ws.practice else '') + Path(path).name)


def _plain(r, t):
    return tuple(r.get(c['name']) for c in t['table']['columns'])


def save(loaded, e, base=None):
    items, aux_changed, blocked = check_save(loaded, e, base)
    if blocked:
        return dict(ok=False, blocked=blocked, changes=items)
    cfg = loaded.cfg
    written, backups = [], []
    cfg.backup_dir.mkdir(parents=True, exist_ok=True)
    docs = sorted({c['doc'] for c in items})
    for doc in docs:
        path = Path(loaded.ws.files[doc])
        wb = open_workbook(path)
        try:
            for name, t in e.tables.items():
                mine = [c for c in items if c['table'] == name]
                if t['doc'] == doc and mine:
                    write_sheet(wb[t['sheet']], t, mine)
            fd, temp = tempfile.mkstemp(prefix='.' + path.stem + '.', suffix='.xlsx', dir=path.parent)
            os.close(fd)
            wb.save(temp)
        finally:
            wb.close()
        backup = _backup_name(loaded, path)
        shutil.copy2(path, backup)
        try:
            os.replace(temp, path)
        finally:
            if os.path.exists(temp):
                os.unlink(temp)
        written.append(_rel(cfg, path))
        backups.append(_rel(cfg, backup))
    if aux_changed:
        loaded.ws.aux.parent.mkdir(parents=True, exist_ok=True)
        raw = (json.dumps(e.aux, ensure_ascii=False, sort_keys=True, indent=1) + '\n').encode('utf-8')
        fd, temp = tempfile.mkstemp(prefix='.aux.', suffix='.json', dir=loaded.ws.aux.parent)
        with os.fdopen(fd, 'wb') as f:
            f.write(raw)
        os.replace(temp, loaded.ws.aux)
        written.append(_rel(cfg, loaded.ws.aux))
    again = Loaded(loaded.ws)
    mismatch = [t['sheet'] for name, t in e.tables.items()
                if [_plain(r, t) for r in t['rows']] != [_plain(r, t) for r in again.tables[name]['rows']]]
    if mismatch:
        for doc in docs:
            shutil.copy2(_backup_name(loaded, loaded.ws.files[doc]), loaded.ws.files[doc])
        return dict(ok=False, blocked=[text(e.lang, 'save.mismatch', sheets=', '.join(mismatch))], changes=items)
    return dict(ok=True, written=written, backups=backups, changes=items, base=again.base)


# ------------------------------------------------------------------ practice workspace
def practice(cfg, action):
    lang = cfg['language']
    if action not in ('create', 'reset'):
        raise OpError(text(lang, 'op.practiceAction'))
    target_ws = Workspace(cfg, 'practice')
    if action == 'create' and target_ws.exists():
        return dict(ok=True, created=False, folder=_rel(cfg, cfg.practice_dir))
    cfg.practice_dir.mkdir(parents=True, exist_ok=True)
    src = Workspace(cfg, 'product')
    copied = []
    for doc, p in src.files.items():
        if p is None:
            continue
        target = cfg.practice_dir / Path(p).name
        msg = lock_state(target, lang) if target.exists() else None
        if msg:
            raise OpError(msg)
        shutil.copy2(p, target)
        copied.append(Path(p).name)
    aux = cfg.aux_path(cfg.practice_dir)
    if src.aux.is_file():
        aux.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src.aux, aux)
    elif aux.is_file():
        aux.unlink()
    return dict(ok=True, created=True, folder=_rel(cfg, cfg.practice_dir), files=copied)


# ------------------------------------------------------------------ requests (serve and CLI)
class Backend:
    def __init__(self, cfg):
        self.cfg = cfg
        self.cache = {}

    def loaded(self, kind):
        ws = Workspace(self.cfg, kind)
        sig = ws.signature()
        hit = self.cache.get(kind)
        if hit is None or hit.signature != sig:
            hit = Loaded(ws)
            self.cache[kind] = hit
        return hit

    def handle(self, req):
        cmd = req.get('cmd')
        if cmd == 'ping':
            return dict(ok=True, format=self.cfg['formats']['editModel'])
        if cmd == 'practice':
            self.cache.pop('practice', None)
            return practice(self.cfg, req.get('action') or 'create')
        lo = self.loaded(req.get('workspace') or 'product')
        base = req.get('base')
        external = bool(base) and any(lo.base.get(doc) != h for doc, h in base.items())
        e, done = apply_ops(lo, req.get('ops'))
        if cmd == 'model':
            model = build_model(e, run_edit(e), done)
            items, aux_changed = changes(lo, e)
            model.update(externalChange=external, pending=dict(changes=len(items), aux=aux_changed))
            return dict(ok=True, model=model)
        if cmd == 'changes':
            items, aux_changed, blocked = check_save(lo, e, base)
            r = run_edit(e)
            return dict(ok=True, changes=items, aux=aux_changed, blocked=blocked, errors=len(r['errors']), warnings=len(r['warnings']))
        if cmd == 'save':
            r = run_edit(e)
            if r['errors'] and not req.get('allowErrors'):
                return dict(ok=False, blocked=[text(e.lang, 'save.errors', count=len(r['errors']))], changes=[])
            result = save(lo, e, base)
            self.cache.pop(lo.ws.kind, None)
            return result
        raise OpError(text(self.cfg['language'], 'op.unknownCmd', cmd=cmd))


def serve(cfg, stdin=None, stdout=None):
    """One JSON request per line in, one JSON response per line out. Ends when stdin closes."""
    backend = Backend(cfg)
    out = stdout or sys.stdout.buffer
    for line in (stdin or sys.stdin.buffer):
        line = line.strip()
        if not line:
            continue
        req = None
        try:
            req = json.loads(line.decode('utf-8') if isinstance(line, bytes) else line)
            res = backend.handle(req)
        except (OpError, SkillDataError, OSError, ValueError, KeyError) as error:
            res = dict(ok=False, error=str(error))
        res['id'] = req.get('id') if isinstance(req, dict) else None
        out.write((json.dumps(res, ensure_ascii=False, default=str, separators=(',', ':')) + '\n').encode('utf-8'))
        out.flush()
