"""A minimal turn-based preset: different events, kind names, key layout, two layers and a whole-turn time grid.
Nothing in the core may assume real-time casts."""
import json
import shutil
import tempfile
import unittest
from pathlib import Path

import helpers  # noqa: F401  (puts the package on sys.path)
from skillgraph import demo
from skillgraph import editor as se
from skillgraph.config import Config
from skillgraph.core import Dictionary, load_registry
from skillgraph.pipeline import run

SETTINGS = {
    'language': 'en',
    'dataDir': 'Tables',
    'documents': {'Skills': 'Actions.xlsx', 'ModuleBundles': 'ActionGraphs.xlsx', 'Presentation': 'Visuals.xlsx'},
    'keys': {'layout': {'band': 3, 'owner': 1, 'serial': 3},
             'bands': {'skill': 510, 'buff': 511, 'moduleBundle': 520, 'valueBundle': 530, 'modifier': 540, 'presentation': 550}},
    'practiceOwner': 9,
    'layers': ['Class', 'Action'],
    'nodeIdStart': [1, 50],
    'bundleKinds': {'skill': 'Action', 'effect': 'Summon', 'buff': 'Status'},
    'owners': [{'table': 'SkillMetaData', 'kind': 'skill', 'band': 'skill', 'loc': 'ACTION.{key}', 'export': 'actions', 'exportColumns': []},
               {'table': 'BuffMetaData', 'kind': 'buff', 'band': 'buff', 'loc': 'STATUS.{key}', 'export': 'statuses', 'exportColumns': []}],
    'events': {'start': 'TurnStart', 'timed': ['AfterTurns'], 'exits': ['CheckPass', 'CheckFail']},
    'time': {'unit': ' turn', 'grid': 1},
    'modules': {'spawn': {'module': 'Summon', 'slot': 'BundleKey'}, 'status': {'module': 'ApplyStatus', 'slot': 'StatusKey'}},
}

DICTIONARY = dict(
    format='SkillGraph Module Dictionary 1',
    events=['TurnStart', 'AfterTurns', 'CheckPass', 'CheckFail', 'StatusApply', 'StatusExpire'],
    kindEvents={'Action': ['TurnStart', 'AfterTurns', 'CheckPass', 'CheckFail'], 'Summon': ['TurnStart'], 'Status': ['StatusApply', 'StatusExpire', 'AfterTurns']},
    presentationEvents=['TurnStart', 'AfterTurns', 'CheckPass', 'StatusApply'],
    subscriptionEvents=[], subjects=[], slotEnums={'STAT': ['Attack', 'Defense']},
    modules=[
        dict(name='Attack', line='Logic', group='core', events=['TurnStart', 'AfterTurns', 'CheckPass', 'CheckFail'], exits=[], random=False, implemented=True,
             slots=[['Power', 'int', '', 'balance', None, 'turn', None]]),
        dict(name='Roll', line='Logic', group='core', events=['TurnStart'], exits=['Pass', 'Fail'], random=True, implemented=True,
             slots=[['Chance', 'float', '%', 'balance', None, 'turn', None]]),
        dict(name='ApplyStatus', line='Logic', group='core', events=['TurnStart', 'CheckPass'], exits=[], random=False, implemented=True,
             slots=[['StatusKey', 'key:buff', '', 'structure', None, 'turn', None], ['Turns', 'int', 'turn', 'balance', None, 'turn', None]]),
        dict(name='StatModify', line='Logic', group='core', events=['StatusApply'], exits=[], random=False, implemented=True,
             slots=[['Stat', 'enum:STAT', '', 'structure', None, 'turn', None], ['Value', 'int', '', 'balance', None, 'turn', None]]),
        dict(name='PlayAnim', line='Presentation', group='core', events=['TurnStart', 'AfterTurns', 'CheckPass'], exits=[], random=False, implemented=True,
             slots=[['VisualKey', 'key:presentation', '', 'structure', None, 'turn', None]]),
    ])


def rows():
    n, ref, fix = demo.node, demo.ref, demo.fix
    return {
        'SkillMetaData': [dict(Key=5101001, NameRef='Strike', NameLoc='ACTION.5101001', ModuleBundleKey=5201001, ValueBundleKey=5301001),
                          dict(Key=5101002, NameRef='Venom Strike', NameLoc='ACTION.5101002', ModuleBundleKey=5201002, ValueBundleKey=5301002)],
        'BuffMetaData': [dict(Key=5111001, NameRef='Weakened', NameLoc='STATUS.5111001', ModuleBundleKey=5201101, ValueBundleKey=5301101)],
        'ValueBundleData': [dict(Key=5301001, Desc='Strike'), dict(Key=5301002, ParentKey=5301001, Desc='Venom Strike'), dict(Key=5301101, Desc='Weakened')],
        'ValueData': [dict(BundleKey=5301001, Name='Strike.Power', Value=10), dict(BundleKey=5301002, Name='Venom.Chance', Value=0.5),
                      dict(BundleKey=5301002, Name='Venom.Turns', Value=3), dict(BundleKey=5301002, Name='Venom.Delay', Value=2),
                      dict(BundleKey=5301002, Name='Venom.Tick', Value=2), dict(BundleKey=5301101, Name='Weak.Attack', Value=-3)],
        'ModuleBundleData': [dict(Key=5201001, Kind='Action', Desc='Strike'), dict(Key=5201002, Kind='Action', ParentKey=5201001, Desc='Venom Strike'),
                             dict(Key=5201101, Kind='Status', Desc='Weakened')],
        'ModuleNodeData': [n(5201001, 1, 'TurnStart', 'Attack'), n(5201001, 2, 'TurnStart', 'PlayAnim', line='Presentation'),
                           n(5201002, 50, 'TurnStart', 'Roll', order=20, override='+'),
                           n(5201002, 51, 'CheckPass', 'ApplyStatus', frm=50, override='+'),
                           n(5201002, 52, 'AfterTurns', 'Attack', 'Venom.Delay', override='+'),
                           n(5201101, 1, 'StatusApply', 'StatModify')],
        'ValueLinkData': [ref(5201001, 1, 'Power', 'Strike.Power'), fix(5201001, 2, 'VisualKey', 5501001),
                          ref(5201002, 50, 'Chance', 'Venom.Chance'), fix(5201002, 51, 'StatusKey', 5111001), ref(5201002, 51, 'Turns', 'Venom.Turns'),
                          ref(5201002, 52, 'Power', 'Venom.Tick'),
                          fix(5201101, 1, 'Stat', 'Attack'), ref(5201101, 1, 'Value', 'Weak.Attack')],
        'PresentationResourceData': [dict(PresentationKey=5501001, Clip='strike', Desc='Strike animation')],
    }


class TurnBased:
    def __init__(self, edit=None):
        self.edit = edit

    def __enter__(self):
        self.dir = Path(tempfile.mkdtemp(prefix='skillgraph-turn-'))
        self.cfg = Config(SETTINGS, self.dir)
        self.cfg.dictionary_path.parent.mkdir(parents=True)
        self.cfg.dictionary_path.write_text(json.dumps(DICTIONARY), encoding='utf-8')
        data = rows()
        if self.edit:
            self.edit(data)
        reg = load_registry(self.cfg, Dictionary(self.cfg.dictionary_path, self.cfg))
        demo.write_workbooks(self.cfg, reg, data, self.cfg.data_dir)
        return self

    def __exit__(self, *a):
        shutil.rmtree(self.dir, ignore_errors=True)

    def run(self):
        return run(self.cfg.documents(), self.cfg, True)


class TurnBasedTests(unittest.TestCase):
    def test_clean_and_exported(self):
        with TurnBased() as t:
            r = t.run()
            self.assertEqual(([str(x) for x in r['errors']], [str(x) for x in r['warnings']]), ([], []))
            self.assertEqual([a['key'] for a in r['logic']['actions']], [5101001, 5101002], 'owner export names come from settings')
            venom = next(b for b in r['logic']['bundles'] if b['key'] == 5201002)
            delayed = next(n for n in venom['nodes'] if n['id'] == 52)
            self.assertEqual(delayed['at'], ['ref', r['names'].index('Venom.Delay')])
            self.assertEqual(r['mods'][5201002]['chain'], [5201001, 5201002])

    def test_whole_turn_grid(self):
        with TurnBased(lambda d: next(v for v in d['ValueData'] if v['Name'] == 'Venom.Delay').update(Value=1.5)) as t:
            r = t.run()
            self.assertEqual([x.code for x in r['warnings']], ['OffGrid'])
            self.assertIn('1 turn grid', r['warnings'][0].message)
            entries = dict(next(v for v in r['logic']['values'] if v['key'] == 5301002)['entries'])
            self.assertEqual(entries[r['names'].index('Venom.Delay')], 2, 'snapped to whole turns')

    def test_two_layers_only(self):
        def edit(d):
            d['ModuleBundleData'].append(dict(Key=5201003, Kind='Action', ParentKey=5201002, Desc='too deep'))
        with TurnBased(edit) as t:
            r = t.run()
            depth = next(x for x in r['errors'] if x.code == 'InheritanceDepth')
            self.assertIn('Class > Action', depth.message)

    def test_kind_names_and_events_from_dictionary(self):
        with TurnBased(lambda d: d['ModuleNodeData'].append(demo.node(5201101, 2, 'TurnStart', 'Attack'))) as t:
            r = t.run()
            self.assertIn('EventForKind', [x.code for x in r['errors']])
            self.assertIn('Status', next(x for x in r['errors'] if x.code == 'EventForKind').message)

    def test_tier_families_from_a_roster_table(self):
        # A class roster unlocks actions by rank per slot; the passive slot is not a tier family.
        roster = [dict(ClassKey=7, Slot='Main', Rank=2, ActionKey=5101002), dict(ClassKey=7, Slot='Main', Rank=1, ActionKey=5101001),
                  dict(ClassKey=7, Slot='Side', Rank=1, ActionKey=5101003), dict(ClassKey=7, Slot='Passive', Rank=1, ActionKey=5111001),
                  dict(ClassKey=8, Slot='Main', Rank=1, ActionKey=5101004)]
        tiers = dict(table='ClassRoster', owner='ActionKey', group='ClassKey', level='Rank', familyBy=['ClassKey', 'Slot'], exclude={'Slot': ['Passive']})
        cfg = Config(dict(SETTINGS, tiers=tiers))
        stages = se._stages({'ClassRoster': roster}, cfg)
        self.assertEqual(stages[5101001], dict(group=7, level=1, family=[5101001, 5101002]), 'owner column, ordered by rank')
        self.assertEqual(stages[5101003]['family'], [5101003], 'another slot of the same class is its own family')
        self.assertEqual(stages[5101004]['family'], [5101004])
        self.assertNotIn(5111001, stages, 'excluded rows make no family')
        plain = se._stages({'ClassRoster': roster}, Config(dict(SETTINGS, tiers=dict(table='ClassRoster', owner='ActionKey', group='ClassKey', level='Rank'))))
        self.assertEqual(plain[5101001]['family'], [5101001, 5101003, 5111001, 5101002], 'without familyBy the group column alone makes the family')

    def test_editor_uses_key_layout(self):
        with TurnBased() as t:
            m = se.Backend(t.cfg).handle(dict(cmd='model', ops=[dict(op='newSkill', kind='Action', ownerNumber=1, name='Guard', moduleParent=5201001),
                                                              dict(op='addNode', bundle=5201002, event='AfterTurns', eventValue='Venom.Delay', module='PlayAnim',
                                                                   line='Presentation', links=[dict(slot='VisualKey', source='Fixed', value=5501001)])]))['model']
            new, added = m['ops']
            self.assertEqual((new['key'], new['bundle'], new['values']), (5101003, 5201003, 5301003), 'band 3 + owner 1 + serial 3')
            self.assertEqual(added['node'], 53, 'second layer ids start at 50')
            self.assertEqual([i for i in m['issues'] if i['level'] == 'error'], [])
            self.assertEqual(m['settings']['time']['grid'], 1)


if __name__ == '__main__':
    unittest.main()
