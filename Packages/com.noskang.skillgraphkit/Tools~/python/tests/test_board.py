"""Board keys: the key table, the Board source, board slots, checks, export and the three edit operations.
Data without board keys must give the same bytes as 0.4.0 (demo sample and the turn-based preset)."""
import copy
import json
import shutil
import tempfile
import unittest
from pathlib import Path

import openpyxl

from helpers import Temp, error_codes, find, sha as sha_path
from skillgraph import demo
from skillgraph import editor as se
from skillgraph.codes import LEVELS
from skillgraph.config import Config
from skillgraph.core import canonical, sha
from skillgraph.messages import MESSAGES
from skillgraph.pipeline import build, run
from skillgraph.views import graph_model
from test_turn_based import TurnBased

# Taken from 0.4.0 (55017d6) with demo_fingerprint / turn_fingerprint below, before any board code existed.
BASE_040 = {
    'demo': dict(logic='ae306cc8d8e8c565d458cec405ff148b81b2c310430165eadde42cbd642e2b7e',
                 presentation='9161997a645cc36f4e61353f3230442a0fdb6dbbcd39142837e46afdecabb45a',
                 dictionary='33ba2516312093e01153500f81aab66cf1edd4c4119807dba5a0c349213da19e',
                 display='93c42508fdd37ec1dddce33ff8f6f1127863c6e8991df775941cefb8a50b97b4',
                 save=dict(changes='34addf1d48b41266ee25ff9cac89376f461b9ff2e151af58760e55b59cf16225',
                           cells='2374db8700fa5bfb8803d3473f5bcdab872a71e7f1b70a61fab7562c674d5aeb',
                           aux='63817a6378833a42d9e0e773d2619afea587b4a7edf62adb53cf7eef117c6cbb')),
    'turn': dict(logic='48d6ea48e923d4e10d0e08e12e23120ca0364cdf230c4d0101042a1804525e13',
                 presentation='ba956da121e4b4236c59724f9bdcbf6e87a7ddc4157cabcfcdea92d35af74593',
                 save=dict(changes='ebe54e029637c9389282ad3d2b7203535d184c82d11db4d9944233c0f6d49c3d',
                           cells='001ce502d95323e99a0d963a82a7b95b78f7a326a9aba52ab87519b91da1779e', aux=None)),
}

SAVE_OPS = {
    'demo': [dict(op='setValue', values=2300201, name='Bolt.Damage', value=26),
             dict(op='setLink', bundle=2200101, node=11, slot='Count', source='Fixed', value=1),
             dict(op='setMemo', bundle=2200001, node=1, text='hi')],
    'turn': [dict(op='setValue', values=5301001, name='Strike.Power', value=11),
             dict(op='setLink', bundle=5201001, node=1, slot='Power', source='Fixed', value=12)],
}


def cells(folder):
    """Every cell value of every workbook in a folder (a saved file carries its save time, its cells do not)."""
    out = {}
    for p in sorted(Path(folder).glob('*.xlsx')):
        wb = openpyxl.load_workbook(p)
        out[p.name] = {ws.title: [list(r) for r in ws.iter_rows(values_only=True)] for ws in wb.worksheets}
        wb.close()
    return out


def saved(cfg, ops):
    """Apply ops and save; fingerprint what the save wrote: the change list, every cell and the aux file."""
    first = se.Loaded(se.Workspace(cfg))
    res = se.Backend(cfg).handle(dict(cmd='save', ops=ops, base=first.base, allowErrors=True))
    assert res['ok'], res
    aux = cfg.aux_path(cfg.data_dir)
    return dict(changes=sha(canonical(res['changes'])), cells=sha(canonical(cells(cfg.data_dir))), aux=sha_path(aux) if aux.is_file() else None)


def demo_fingerprint():
    """A fresh demo sample without board keys: logic / presentation exports, dictionary files and an edit save."""
    folder = Path(tempfile.mkdtemp(prefix='skillgraph-board-'))
    try:
        demo.make(folder)
        cfg = Config(json.loads((folder / 'skillgraph.json').read_text(encoding='utf-8')), folder)
        b = build(cfg)
        return dict(logic=sha(b['logic']), presentation=sha(b['presentation']), dictionary=sha_path(cfg.dictionary_path),
                    display=sha_path(cfg.generated() / cfg['files']['display']), save=saved(cfg, SAVE_OPS['demo']))
    finally:
        shutil.rmtree(folder, ignore_errors=True)


def turn_fingerprint():
    with TurnBased() as t:
        r = t.run()
        return dict(logic=sha(r['logic_bytes']), presentation=sha(r['presentation_bytes']), save=saved(t.cfg, SAVE_OPS['turn']))


# Test modules added to the demo dictionary: Tally writes a number key, Gate reads one by name and passes or fails.
TEST_MODULES = [
    dict(name='Tally', line='Logic', group='board', events=['CastStart', 'Hit'], exits=[], random=False, implemented=True,
         slots=[['Key', 'board:int,float', '', 'structure', None, 'hit', None], ['Amount', 'float', '', 'balance', 1, 'hit', None]]),
    dict(name='Gate', line='Logic', group='board', events=['CastStart'], exits=['Pass', 'Fail'], random=False, implemented=True,
         slots=[['Key', 'board:read,int,float', '', 'structure', None, 'cast', None], ['Than', 'float', '', 'structure', None, 'cast', None]]),
]


def link(bundle, nid, slot, name):
    return dict(BundleKey=bundle, NodeId=nid, Slot=slot, Source='Board', Value=name)


def board_rows(rows):
    """Combo.Count (Actor) counts hits of the base hit area, Slash checks it when the cast starts, Damage writes what it dealt."""
    rows['BoardKeyData'] = [dict(Name='Combo.Count', Type='int', Scope='Actor', Default=0, Desc='Hits in a row'),
                            dict(Name='Hit.Dealt', Type='float', Scope='Cast')]
    rows['ModuleNodeData'] += [demo.node(2200002, 4, 'Hit', 'Tally', order=20), demo.node(2200101, 14, 'CastStart', 'Gate', order=20, override='+')]
    rows['ValueLinkData'] += [link(2200002, 4, 'Key', 'Combo.Count'), link(2200002, 3, 'AmountTo', 'Hit.Dealt'),
                              link(2200101, 14, 'Key', 'Combo.Count'), demo.fix(2200101, 14, 'Than', 3)]


class Board(Temp):
    """The demo sample with board keys and the test modules (Damage gets an optional write slot AmountTo)."""

    def __init__(self, edit=None, settings=None, lang=None):
        def rows(r):
            board_rows(r)
            if edit:
                edit(r)
        super().__init__(rows, settings, lang)

    def __enter__(self):
        super().__enter__()
        p = self.cfg.dictionary_path
        d = json.loads(p.read_text(encoding='utf-8'))
        next(m for m in d['modules'] if m['name'] == 'Damage')['slots'].append(['AmountTo', 'board', '', 'structure', '', 'hit', None])
        d['modules'] += copy.deepcopy(TEST_MODULES)
        p.write_text(json.dumps(d), encoding='utf-8')
        return self

    def run(self):
        return run(self.cfg.documents(), self.cfg, True)


def logic_node(r, bundle, nid):
    b = next(b for b in r['logic']['bundles'] if b['key'] == bundle)
    return next(n for n in b['nodes'] if n['id'] == nid)


def issues(r, level='errors'):
    return sorted((x.code, x.at.get('bundle'), x.at.get('node')) for x in r[level])


class BoardTests(unittest.TestCase):
    def test_data_without_keys_keeps_040_bytes(self):
        self.assertEqual(demo_fingerprint(), BASE_040['demo'], 'demo: logic / presentation exports, dictionary files, edit save')
        self.assertEqual(turn_fingerprint(), BASE_040['turn'], 'turn-based preset: exports and edit save')

    def test_key_sheet_is_optional(self):
        with Temp() as t:
            wb = openpyxl.load_workbook(t.cfg.data_dir / 'ModuleBundles.xlsx')
            self.assertNotIn('Board Keys', wb.sheetnames, 'no keys, no sheet')
            r = run(t.cfg.documents(), t.cfg, True)
            self.assertEqual(error_codes(r), [])
            self.assertNotIn('boardKeys', r['logic'])
            self.assertNotIn('boardKeys', r['presentation'])

    def test_export(self):
        with Board(lambda rows: rows['BoardKeyData'].append(dict(Name='Combo.Ready', Type='bool', Scope='Actor', Default=True))) as t:
            r = t.run()
            self.assertEqual((issues(r), issues(r, 'warnings')), ([], [('UnusedBoardKey', None, None)]))
            keys = [dict(name='Combo.Count', type='int', scope='Actor', default=0), dict(name='Combo.Ready', type='bool', scope='Actor', default=True),
                    dict(name='Hit.Dealt', type='float', scope='Cast', default=0.0)]
            self.assertEqual(r['logic']['boardKeys'], keys, 'name order; a blank default is 0, false or empty text')
            self.assertEqual(r['presentation']['boardKeys'], keys, 'same numbers in both files')
            self.assertEqual(logic_node(r, 2200002, 4)['links'], [[0, 'board', 0]], '[slot, "board", key number]')
            self.assertIn([2, 'board', 2], logic_node(r, 2200002, 3)['links'])
            self.assertEqual(logic_node(r, 2200101, 14)['links'], [[0, 'board', 0], [1, 'fixed', 3]])
            self.assertFalse({k['name'] for k in keys} & set(r['names']), 'board keys are not value names')
            view = graph_model(r, t.cfg, 'test')
            slot = next(s for b in view['bundles'] if b['key'] == 2200002 for n in b['nodes'] if n['id'] == 4 for s in n['slots'] if s['name'] == 'Key')
            self.assertEqual((slot['source'], slot['value']), ('board', 'Combo.Count'))

    def test_key_table_checks(self):
        def edit(rows):
            rows['BoardKeyData'] += [dict(Name='combo', Type='int', Scope='Actor'), dict(Name='Combo.Kind', Type='number', Scope='Actor'),
                                     dict(Name='Combo.Bad', Type='int', Scope='Actor', Default='x'), dict(Name='Combo.Far', Type='key:skill', Scope='Target')]
        with Board(edit) as t:
            r = t.run()
            got = sorted((x.code, x.message) for x in r['errors'])
            self.assertEqual([c for c, _ in got], ['BoardKeyName', 'BoardKeyType', 'BoardKeyType', 'BoardScopeMismatch'])
            self.assertTrue(any('default' in m for c, m in got if c == 'BoardKeyType'), got)
            self.assertIn('Cast, Actor', next(m for c, m in got if c == 'BoardScopeMismatch'))
            self.assertEqual(sorted(x.message.split()[-1] for x in r['warnings'] if x.code == 'UnusedBoardKey'),
                             ['Combo.Bad', 'Combo.Far', 'Combo.Kind', 'combo'])

    def test_link_checks(self):
        def edit(rows):
            rows['BoardKeyData'] += [dict(Name='Combo.Name', Type='text', Scope='Actor'), dict(Name='Combo.On', Type='bool', Scope='Actor')]
            rows['ModuleNodeData'] += [demo.node(2200002, n, 'Hit', 'Tally', order=10 * n) for n in (5, 6, 7, 8)]
            rows['ValueLinkData'] += [
                link(2200002, 2, 'Radius', 'Combo.Count'),     # float <- int: fine
                link(2200002, 2, 'Shape', 'Combo.Count'),      # enum slot: no
                link(2200002, 1, 'Duration', 'Combo.On'),      # time <- bool: no
                link(2200002, 5, 'Key', 'Combo.Name'),         # board:int,float <- text: no
                demo.fix(2200002, 6, 'Key', 'Combo.Count'),    # a board slot takes a key, not a fixed value
                demo.ref(2200002, 7, 'Key', 'Hit.Damage'),     # ... nor a value name
                link(2200002, 3, 'Amount', 'Combo.Missing'),   # not in the table
            ]                                                  # node 8: Key missing (required)
        with Board(edit) as t:
            got = {(c, n) for c, b, n in issues(t.run()) if b == 2200002}
            self.assertEqual(got, {('BoardTypeMismatch', 2), ('BoardTypeMismatch', 1), ('BoardTypeMismatch', 5), ('BoardWriteTarget', 6),
                                   ('BoardWriteTarget', 7), ('UnknownBoardKey', 3), ('RequiredSlot', 8)})

    def test_scope_kinds(self):
        with Board(settings={'board': {'scopes': {'Cast': {'kinds': ['skill', 'Buff']}}}}) as t:
            got = issues(t.run())
            self.assertEqual({(c, n) for c, _, n in got}, {('BoardScopeMismatch', 3)}, 'Hit.Dealt (Cast) is written by an effect bundle')
            self.assertIn(2200002, {b for _, b, _ in got})

    def test_boards_off(self):
        with Board(settings={'board': {'table': None}}) as t:
            self.assertEqual({c for c, _, _ in issues(t.run())}, {'UnknownBoardKey'})
            bk = se.Backend(t.cfg)
            self.assertNotIn('board', bk.handle(dict(cmd='model'))['model'])
            with self.assertRaisesRegex(se.OpError, 'board.table'):
                se.apply_ops(se.Loaded(se.Workspace(t.cfg)), [dict(op='addBoardKey', name='Combo.Count')])

    def test_model(self):
        with Board(settings={'board': {'scopes': {'Actor': {'label': {'en': 'Hero', 'ko': 'Hero (ko)'}}}}}) as t:
            m = se.Backend(t.cfg).handle(dict(cmd='model'))['model']
            b = m['board']
            self.assertEqual((b['table'], b['sheet'], b['types']), ('BoardKeyData', 'Board Keys', ['int', 'float', 'bool', 'text']))
            self.assertEqual([(s['name'], s['label'], s['kinds']) for s in b['scopes']], [('Cast', 'Cast', None), ('Actor', 'Hero', None)])
            combo = b['keys'][0]
            self.assertEqual((combo['name'], combo['type'], combo['scope'], combo['default'], combo['desc']), ('Combo.Count', 'int', 'Actor', 0, 'Hits in a row'))
            uses = {(u['bundle'], u['node'], u['slot'], u['writes']) for u in combo['uses']}
            self.assertIn((2200002, 4, 'Key', True), uses, 'Tally writes the key')
            self.assertIn((2200101, 14, 'Key', False), uses, 'Gate reads it by name')
            self.assertIn((2200112, 4, 'Key', True), uses, 'a child bundle inherits the node')
            area = next(x for x in m['bundles'] if x['key'] == 2200002)
            slot = next(s for n in area['nodes'] if n['id'] == 3 for s in n['slots'] if s['name'] == 'AmountTo')
            self.assertEqual((slot['source'], slot['value']), ('board', 'Hit.Dealt'))

    def test_edit_operations(self):
        with Temp() as t:  # the demo without keys: the first key creates the sheet
            bk = se.Backend(t.cfg)
            first = se.Loaded(se.Workspace(t.cfg))
            ops = [dict(op='addBoardKey', name='Combo.Count', type='int', scope='Actor', default=0, desc='Hits in a row'),
                   dict(op='setLink', bundle=2200002, node=2, slot='Radius', source='Board', value='Combo.Count')]
            m = bk.handle(dict(cmd='model', ops=ops))['model']
            self.assertEqual([i['code'] for i in m['issues'] if i['level'] == 'error'], [])
            self.assertEqual([(u['bundle'], u['node'], u['writes']) for u in m['board']['keys'][0]['uses']], [(2200002, 2, False)],
                             'the Ground Spike area sets its own Radius, so it does not inherit the link')
            res = bk.handle(dict(cmd='save', ops=ops, base=first.base))
            self.assertTrue(res['ok'], res)
            self.assertIn(('insert', 'Board Keys'), {(c['kind'], c['sheet']) for c in res['changes']})
            ws = openpyxl.load_workbook(t.cfg.data_dir / 'ModuleBundles.xlsx')['Board Keys']
            self.assertEqual((ws['A1'].value, [c.value for c in ws[4]][:5], [c.value for c in ws[8]][:5]),
                             ('BoardKeyData', ['Name', 'Type', 'Scope', 'Default', 'Desc'], ['Combo.Count', 'int', 'Actor', 0, 'Hits in a row']))
            r = run(t.cfg.documents(), t.cfg, True)
            self.assertEqual((error_codes(r), r['logic']['boardKeys']), ([], [dict(name='Combo.Count', type='int', scope='Actor', default=0)]))
            lo = se.Loaded(se.Workspace(t.cfg))
            e, _ = se.apply_ops(lo, [dict(op='setBoardKey', name='Combo.Count', newName='Combo.Hits', type='float', desc='')])
            self.assertEqual([(x['Name'], x['Type'], x['Desc']) for x in e.rows('BoardKeyData')], [('Combo.Hits', 'float', None)])
            self.assertEqual(find(e.data(), 'ValueLinkData', BundleKey=2200002, NodeId=2, Slot='Radius')['Value'], 'Combo.Hits', 'links follow the rename')
            with self.assertRaisesRegex(se.OpError, '2200002/2/Radius'):
                se.apply_ops(lo, [dict(op='removeBoardKey', name='Combo.Count')])
            e, _ = se.apply_ops(lo, [dict(op='setLink', bundle=2200002, node=2, slot='Radius', source='Ref', value='Hit.Radius'),
                                     dict(op='removeBoardKey', name='Combo.Count')])
            self.assertEqual(e.rows('BoardKeyData'), [])
            for bad, msg in ((dict(op='addBoardKey', name='Combo.Count'), 'already exists'), (dict(op='setBoardKey', name='Nope.Key'), 'does not exist'),
                             (dict(op='setLink', bundle=2200002, node=2, slot='Radius', source='Keyed', value='x'), 'Board')):
                with self.assertRaisesRegex(se.OpError, msg):
                    se.apply_ops(lo, [bad])

    def test_codes_have_messages(self):
        for code in ('UnknownBoardKey', 'BoardTypeMismatch', 'BoardScopeMismatch', 'BoardWriteTarget', 'BoardKeyName', 'BoardKeyType', 'UnusedBoardKey'):
            self.assertIn(code, LEVELS)
            self.assertIn(code, MESSAGES)


if __name__ == '__main__':
    unittest.main()
