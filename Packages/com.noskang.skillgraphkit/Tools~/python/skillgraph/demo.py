"""Made-up demo data: a small real-time action skill set built only from generic modules.

  python -X utf8 -m skillgraph.demo --out <folder> [--lang en|ko]

Writes skillgraph.json, the module dictionary and display files, one animation clip and the three workbooks.
The output is byte-for-byte reproducible (fixed workbook timestamps and zip entry times), so the committed sample
can be checked against a fresh build.

Owners: 00 = shared base layer, 01 = Warrior, 02 = Mage. Keys are band(3) + owner(2) + serial(2).
"""
import argparse
import io
import json
import re
import zipfile
from datetime import datetime
from pathlib import Path

import openpyxl

from .config import Config
from .core import Dictionary, load_registry, sha
from .workbook import write_header

FIXED_TIME = datetime(2026, 1, 1)
ZIP_TIME = (2026, 1, 1, 0, 0, 0)

SETTINGS = {
    'language': 'en',
    'dataDir': 'Data',
    'keyframes': {'animRoot': 'Animations'},
    'rules': {'companions': [{'kind': 'effect', 'event': 'Hit', 'requires': 'HitShape', 'spawnedOnly': True}]},
}

# ------------------------------------------------------------------ module dictionary (what C# declarations export)
EVENTS = ['CastStart', 'Moment', 'Create', 'Step', 'Alarm', 'Hit', 'LifeEnd', 'Destroy', 'Cancel', 'CheckPass', 'CheckFail',
          'StatusApply', 'StatusExpire', 'StatusRemove', 'OnHurt']
KIND_EVENTS = {
    'Skill': ['CastStart', 'Moment', 'Cancel', 'CheckPass', 'CheckFail'],
    'Effect': ['Create', 'Step', 'Alarm', 'Hit', 'LifeEnd', 'Destroy', 'CheckPass', 'CheckFail'],
    'Buff': ['StatusApply', 'StatusExpire', 'StatusRemove', 'Step', 'Alarm', 'CheckPass', 'CheckFail', 'OnHurt'],
}
PRESENTATION_EVENTS = ['CastStart', 'Moment', 'Create', 'Hit', 'Destroy', 'CheckPass', 'CheckFail', 'StatusApply', 'StatusExpire']
SLOT_ENUMS = {
    'ATTACH': ['Effect', 'Body', 'Ground'],
    'DAMAGE_KIND': ['Normal', 'Pierce'],
    'MOVE_MODE': ['Dash', 'Leap', 'Teleport'],
    'SHAPE': ['Circle', 'Box'],
    'STAT': ['Attack', 'Defense', 'MoveSpeed'],
    'STAT_OP': ['Add', 'Multiply'],
    'TARGET': ['Self', 'HitTarget', 'Allies'],
    'WHILE': ['Owner', 'Charging', 'Released'],
}
B, S = 'balance', 'structure'


def slot(name, kind_, unit, role, default, timing='cast', supported=None):
    return [name, kind_, unit, role, default, timing, supported]


MODULES = [
    # name, line, events, slots, exits, implemented
    ('CastLock', 'Logic', ['CastStart'], [slot('Duration', 'time', 's', B, None), slot('MoveLockUntil', 'time', 's', B, 0)], [], True),
    ('SpawnEffect', 'Logic', ['CastStart', 'Moment', 'Create', 'Alarm', 'Hit', 'LifeEnd', 'CheckPass', 'CheckFail'],
     [slot('BundleKey', 'key:moduleBundle', '', S, None), slot('Count', 'int', '', B, 1), slot('OffsetX', 'float', 'm', B, 0),
      slot('AngleStep', 'float', 'deg', B, 0)], [], True),
    ('SelfMove', 'Logic', ['CastStart', 'Moment'],
     [slot('Mode', 'enum:MOVE_MODE', '', S, 'Dash'), slot('Distance', 'float', 'm', B, None), slot('Duration', 'time', 's', B, None)], [], True),
    ('Charge', 'Logic', ['CastStart'], [slot('MaxTime', 'time', 's', B, None), slot('MinTime', 'time', 's', B, 0)], ['Pass', 'Fail'], True),
    ('ApplyStatus', 'Logic', ['CastStart', 'Moment', 'Create', 'Hit', 'CheckPass', 'CheckFail', 'StatusApply'],
     [slot('BuffKey', 'key:buff', '', S, None), slot('Target', 'enum:TARGET', '', S, 'HitTarget'), slot('Duration', 'time', 's', B, None)], [], True),
    ('StatModify', 'Logic', ['StatusApply'],
     [slot('Stat', 'enum:STAT', '', S, None), slot('Op', 'enum:STAT_OP', '', S, 'Add'), slot('Value', 'float', '', B, None)], [], True),
    ('SetLifetime', 'Logic', ['Create'], [slot('Duration', 'time', 's', B, None, 'spawn')], [], True),
    ('MoveProjectile', 'Logic', ['Create'], [slot('Speed', 'float', 'm/s', B, None, 'spawn'), slot('Range', 'float', 'm', B, 0, 'spawn')], [], True),
    ('HitShape', 'Logic', ['Create'],
     [slot('Shape', 'enum:SHAPE', '', S, 'Circle', 'spawn'), slot('Radius', 'float', 'm', B, 0, 'spawn'), slot('Width', 'float', 'm', B, 0, 'spawn'),
      slot('Height', 'float', 'm', B, 0, 'spawn')], [], True),
    ('HitWindow', 'Logic', ['Create'], [slot('From', 'time', 's', B, 0, 'spawn'), slot('Until', 'time', 's', B, None, 'spawn')], [], True),
    ('Damage', 'Logic', ['Hit'], [slot('Amount', 'float', '', B, None, 'hit'), slot('Kind', 'enum:DAMAGE_KIND', '', S, 'Normal', 'hit', ['Normal'])], [], True),
    ('Knockback', 'Logic', ['Hit'], [slot('Force', 'float', 'm', B, None, 'hit')], [], False),
    ('Destroy', 'Logic', ['Hit', 'Alarm', 'LifeEnd', 'CheckPass', 'CheckFail'], [], [], True),
    ('PlayMotion', 'Presentation', ['CastStart', 'Moment'],
     [slot('PresentationKey', 'key:presentation', '', S, None), slot('Speed', 'float', 'x', S, 1.0), slot('While', 'enum:WHILE', '', S, 'Owner'),
      slot('Until', 'time', 's', S, 0), slot('LoopFrom', 'time', 's', S, 0), slot('LoopPeriod', 'time', 's', S, 0)], [], True),
    ('PlaySound', 'Presentation', ['CastStart', 'Moment', 'Create', 'Hit', 'Destroy', 'CheckPass', 'CheckFail', 'StatusApply'],
     [slot('PresentationKey', 'key:presentation', '', S, None)], [], True),
    ('ShowVisual', 'Presentation', ['CastStart', 'Moment', 'Create', 'Hit', 'Destroy', 'StatusApply', 'StatusExpire'],
     [slot('PresentationKey', 'key:presentation', '', S, None), slot('Attach', 'enum:ATTACH', '', S, 'Effect')], [], True),
]


def dictionary_json(fmt='SkillGraph Module Dictionary 1'):
    return dict(format=fmt, events=EVENTS, kindEvents=KIND_EVENTS, presentationEvents=PRESENTATION_EVENTS,
                subscriptionEvents=['OnHurt'], subjects=['Self', 'Ally', 'Enemy', 'Any'], timedEvents=['Moment', 'Alarm'],
                exitEvents=['CheckPass', 'CheckFail'], startEvent='CastStart', slotEnums=SLOT_ENUMS,
                modules=[dict(name=n, line=line, group='common', events=ev, slots=slots, exits=exits, random=False, implemented=impl)
                         for n, line, ev, slots, exits, impl in MODULES])


def T(en, ko):
    return {'en': en, 'ko': ko}


FAMILIES = [
    dict(name='CastFlow', label=T('Cast & flow', '시전·흐름'), color='#4E6C8E', icon='◷'),
    dict(name='SpawnLife', label=T('Spawn & life', '생성·수명'), color='#A7862C', icon='✦'),
    dict(name='Move', label=T('Movement', '이동'), color='#3E8A57', icon='→'),
    dict(name='HitDamage', label=T('Hit & damage', '판정·피해'), color='#AE4646', icon='✕'),
    dict(name='StatusBuff', label=T('Status & buffs', '상태·버프'), color='#7552A8', icon='◆'),
    dict(name='Presentation', label=T('Presentation', '연출'), color='#C0742C', icon='▶'),
]
DISPLAY = {
    'CastLock': ('CastFlow', T('Cast lock', '시전 잠금'), T('Locks actions while casting', '시전 동안 다른 행동을 막음'),
                 {'Duration': (T('Duration', '길이'), T('How long the cast lasts', '시전이 이어지는 시간')),
                  'MoveLockUntil': (T('Move lock', '이동 잠금'), T('Movement is locked until this time', '이 시각까지 걷지 못함'))}),
    'SpawnEffect': ('SpawnLife', T('Spawn effect', '효과 생성'), T('Creates an effect object from another bundle', '다른 묶음의 효과 객체를 만듦'),
                    {'BundleKey': (T('Effect', '효과'), T('Effect bundle to create', '만들 효과 묶음')),
                     'Count': (T('Count', '개수'), T('How many to create', '만드는 개수')),
                     'OffsetX': (T('Forward offset', '앞 거리'), T('Distance in front of the caster', '시전자 앞쪽 거리')),
                     'AngleStep': (T('Angle step', '각도 간격'), T('Angle between copies', '여러 개일 때 사이 각도'))}),
    'SelfMove': ('Move', T('Self move', '자기 이동'), T('Moves the caster', '시전자를 움직임'),
                 {'Mode': (T('Mode', '방식'), T('How to move', '움직이는 방식')), 'Distance': (T('Distance', '거리'), T('Distance', '거리')),
                  'Duration': (T('Duration', '시간'), T('Time to cover the distance', '거리를 가는 시간'))}),
    'Charge': ('CastFlow', T('Charge', '충전'), T('Hold to charge; passes when held long enough', '누르고 모음. 충분히 모으면 통과'),
               {'MaxTime': (T('Max time', '최대'), T('Charging stops here', '이 시각에 충전이 끝남')),
                'MinTime': (T('Min time', '최소'), T('Shorter charges fail', '이보다 짧으면 실패'))}),
    'ApplyStatus': ('StatusBuff', T('Apply buff', '버프 걸기'), T('Applies a buff', '버프를 건다'),
                    {'BuffKey': (T('Buff', '버프'), T('Buff to apply', '걸 버프')), 'Target': (T('Target', '대상'), T('Who gets it', '받는 대상')),
                     'Duration': (T('Duration', '지속'), T('Buff duration', '버프 지속 시간'))}),
    'StatModify': ('StatusBuff', T('Stat modify', '능력치 변경'), T('Changes a stat while the buff lasts', '버프 동안 능력치를 바꿈'),
                   {'Stat': (T('Stat', '능력치'), T('Stat to change', '바꿀 능력치')), 'Op': (T('Operation', '연산'), T('Add or multiply', '더하기 또는 곱하기')),
                    'Value': (T('Value', '값'), T('Amount', '양'))}),
    'SetLifetime': ('SpawnLife', T('Lifetime', '수명'), T('Effect disappears after this time', '이 시간이 지나면 효과가 사라짐'),
                    {'Duration': (T('Duration', '수명'), T('Lifetime', '수명'))}),
    'MoveProjectile': ('Move', T('Projectile', '투사체'), T('Flies straight forward', '앞으로 곧게 날아감'),
                       {'Speed': (T('Speed', '속도'), T('Flight speed', '나는 속도')), 'Range': (T('Range', '사거리'), T('0 = until lifetime ends', '0이면 수명까지'))}),
    'HitShape': ('HitDamage', T('Hit shape', '판정 모양'), T('Area that can hit', '맞히는 영역'),
                 {'Shape': (T('Shape', '모양'), T('Circle or box', '원 또는 상자')), 'Radius': (T('Radius', '반지름'), T('Circle radius', '원 반지름')),
                  'Width': (T('Width', '너비'), T('Box width', '상자 너비')), 'Height': (T('Height', '높이'), T('Box height', '상자 높이'))}),
    'HitWindow': ('HitDamage', T('Active window', '켜짐 구간'), T('The hit shape only works inside this window', '이 구간에만 판정이 켜짐'),
                  {'From': (T('From', '시작'), T('Window start', '켜지는 시각')), 'Until': (T('Until', '끝'), T('Window end', '꺼지는 시각'))}),
    'Damage': ('HitDamage', T('Damage', '피해'), T('Deals damage on hit', '맞히면 피해를 줌'),
               {'Amount': (T('Amount', '피해량'), T('Damage dealt', '주는 피해')), 'Kind': (T('Kind', '종류'), T('Damage kind', '피해 종류'))}),
    'Knockback': ('HitDamage', T('Knockback', '밀치기'), T('Pushes the target (no runtime yet)', '대상을 밀어냄(실행기 없음)'),
                  {'Force': (T('Force', '힘'), T('Push distance', '미는 거리'))}),
    'Destroy': ('SpawnLife', T('Destroy', '소멸'), T('Removes the effect', '효과를 없앰'), {}),
    'PlayMotion': ('Presentation', T('Play motion', '모션 재생'), T('Plays a body animation', '몸 애니메이션을 재생'),
                   {'PresentationKey': (T('Motion', '모션'), T('Motion resource', '모션 리소스')), 'Speed': (T('Speed', '속도'), T('Playback speed', '재생 속도')),
                    'While': (T('While', '동안'), T('When it plays', '재생하는 동안')), 'Until': (T('Until', '끝'), T('0 = clip end', '0이면 클립 끝')),
                    'LoopFrom': (T('Loop from', '반복 시작'), T('Loop start', '반복 시작 시각')), 'LoopPeriod': (T('Loop period', '반복 주기'), T('0 = no loop', '0이면 반복 없음'))}),
    'PlaySound': ('Presentation', T('Play sound', '소리 재생'), T('Plays a sound', '소리를 냄'),
                  {'PresentationKey': (T('Sound', '소리'), T('Sound resource', '소리 리소스'))}),
    'ShowVisual': ('Presentation', T('Show visual', '그림 표시'), T('Shows a visual effect', '효과 그림을 보여 줌'),
                   {'PresentationKey': (T('Visual', '그림'), T('Visual resource', '그림 리소스')), 'Attach': (T('Attach', '붙는 곳'), T('Where it follows', '따라가는 대상'))}),
}


def display_json(dictionary_hash):
    modules = []
    for name, _, _, slots, _, _ in MODULES:
        family, label, note, texts = DISPLAY[name]
        entry = dict(name=name, label=label, family=family, note=note, slots=[[s[0], texts[s[0]][0], texts[s[0]][1]] for s in slots],
                     stages=[dict(name='Basic', advanced=False, slots=[s[0] for s in slots])], kind=None, choose=None, unused=[])
        if name == 'HitShape':
            entry['stages'] = [dict(name='Basic', advanced=False, slots=['Shape', 'Radius']), dict(name='Box', advanced=True, slots=['Width', 'Height'])]
            entry['kind'] = dict(slot='Shape', rules=[dict(values=['Circle'], events=[], slots=['Radius'], verified=True),
                                                      dict(values=['Box'], events=[], slots=['Width', 'Height'], verified=True)])
        if name == 'PlayMotion':
            entry['stages'] = [dict(name='Basic', advanced=False, slots=['PresentationKey', 'Speed']),
                               dict(name='Timing', advanced=True, slots=['While', 'Until', 'LoopFrom', 'LoopPeriod'])]
        modules.append(entry)
    events = {'CastStart': T('Cast start', '시전 시작'), 'Moment': T('At time', '시각'), 'Create': T('Created', '생성'), 'Step': T('Every tick', '매 박자'),
              'Alarm': T('Alarm', '알람'), 'Hit': T('Hit', '명중'), 'LifeEnd': T('Life end', '수명 끝'), 'Destroy': T('Destroyed', '소멸'),
              'Cancel': T('Cancelled', '취소'), 'CheckPass': T('Pass', '통과'), 'CheckFail': T('Fail', '실패'), 'StatusApply': T('Applied', '적용'),
              'StatusExpire': T('Expired', '만료'), 'StatusRemove': T('Removed', '제거'), 'OnHurt': T('When hurt', '맞을 때')}
    return dict(format='SkillGraph Module Display 1', dictionaryHash=dictionary_hash, families=FAMILIES, events=events, enumLabels={}, modules=modules)


# ------------------------------------------------------------------ workbook rows
def node(bundle, nid, event, module, value=None, order=10, line='Logic', frm=None, override=None, note=None):
    return dict(BundleKey=bundle, NodeId=nid, Line=line, Event=event, EventValue=value, FromNode=frm, Order=order, Module=module, Override=override, Note=note)


def remove(bundle, nid):
    return dict(BundleKey=bundle, NodeId=nid, Override='-')


def ref(bundle, nid, slot_, name):
    return dict(BundleKey=bundle, NodeId=nid, Slot=slot_, Source='Ref', Value=name)


def fix(bundle, nid, slot_, value):
    return dict(BundleKey=bundle, NodeId=nid, Slot=slot_, Source='Fixed', Value=value)


def skill(key, name, bundle, values, tier_group=None, tier=None, icon=None, note=None):
    return dict(Key=key, NameRef=name, NameLoc=f'SKILL.K{key}.NAME', ModuleBundleKey=bundle, ValueBundleKey=values, IconKey=icon,
                TierGroup=tier_group, Tier=tier, Note=note)


def demo_rows():
    P = 'Presentation'
    skills = [
        skill(2100101, 'Slash', 2200101, 2300101, note='Basic melee: the hit follows the swing keyframe'),
        skill(2100102, 'Ground Spike', 2200102, 2300102, note='Fixed hit area in front of the caster'),
        skill(2100103, 'War Cry', 2200103, 2300103, note='Buffs the caster'),
        skill(2100104, 'Dash', 2200104, 2300104, note='Self movement'),
        skill(2100201, 'Fire Bolt', 2200201, 2300201, 'FireBolt', 1, note='Projectile, tier 1'),
        skill(2100202, 'Fire Bolt II', 2200202, 2300202, 'FireBolt', 2, note='Tier 2 adds a second bolt'),
        skill(2100203, 'Fire Bolt III', 2200203, 2300203, 'FireBolt', 3, note='Tier 3 swaps the sound for a burst visual'),
        skill(2100204, 'Guard Field', 2200204, 2300204, note='Area with an active window that shields allies'),
        skill(2100205, 'Charge Beam', 2200205, 2300205, note='Charge, then branch on pass or fail'),
    ]
    buffs = [dict(Key=2110101, NameRef='Battle Cry', NameLoc='BUFF.K2110101.NAME', ModuleBundleKey=2200111, ValueBundleKey=2300111, Category='Buff'),
             dict(Key=2110201, NameRef='Guarded', NameLoc='BUFF.K2110201.NAME', ModuleBundleKey=2200221, ValueBundleKey=2300221, Category='Buff')]
    value_bundles = [
        (2300001, None, 'Shared cast values'),
        (2300101, 2300001, 'Slash'), (2300102, 2300001, 'Ground Spike'), (2300103, 2300001, 'War Cry'), (2300104, 2300001, 'Dash'),
        (2300111, None, 'Battle Cry buff'),
        (2300201, 2300001, 'Fire Bolt'), (2300202, 2300201, 'Fire Bolt II'), (2300203, 2300201, 'Fire Bolt III'),
        (2300204, 2300001, 'Guard Field'), (2300205, 2300001, 'Charge Beam'), (2300221, None, 'Guarded buff'),
    ]
    values = [
        (2300001, 'Cast.Duration', 0.5, 'Default cast time'),
        (2300101, 'Hit.Damage', 12, 'Slash damage'), (2300101, 'Hit.Radius', 1.2, 'Slash reach'), (2300101, 'Hit.Life', 0.1, 'Hit area lifetime'),
        (2300102, 'Cast.Duration', 0.7, 'Longer wind-up'), (2300102, 'Spike.Time', 0.4, 'When the spike rises'),
        (2300102, 'Spike.Offset', 2.5, 'Distance in front'), (2300102, 'Hit.Damage', 20, ''), (2300102, 'Hit.Life', 0.3, ''),
        (2300102, 'Spike.Width', 1.0, ''), (2300102, 'Spike.Height', 3.0, ''),
        (2300103, 'Cry.Duration', 6.0, 'Buff duration'),
        (2300104, 'Cast.Duration', 0.3, ''), (2300104, 'Dash.Distance', 4.0, ''), (2300104, 'Dash.Duration', 0.2, ''),
        (2300111, 'Cry.Attack', 0.2, '+20% attack'),
        (2300201, 'Bolt.Release', 0.3, 'Release time'), (2300201, 'Bolt.Damage', 25, ''), (2300201, 'Bolt.Speed', 14.0, ''),
        (2300201, 'Bolt.Life', 1.2, ''), (2300201, 'Bolt.Radius', 0.4, ''),
        (2300202, 'Bolt.Damage', 30, 'Tier 2 hits harder'), (2300202, 'Bolt.Release2', 0.45, 'Second bolt'),
        (2300202, 'Bolt.Spread', 8, 'Tier 2 overrides a slot of an inherited node'),
        (2300203, 'Bolt.Damage', 36, ''), (2300203, 'Bolt.Release2', 0.4, ''),
        (2300204, 'Field.Life', 4.0, ''), (2300204, 'Field.Radius', 3.0, ''), (2300204, 'Field.ActiveFrom', 0.5, 'Turns on after 0.5 s'),
        (2300204, 'Field.ActiveUntil', 3.5, ''), (2300204, 'Guard.Duration', 1.0, ''),
        (2300205, 'Beam.MaxCharge', 2.0, ''), (2300205, 'Beam.MinCharge', 0.8, ''), (2300205, 'Beam.Damage', 60, 'Charged'),
        (2300205, 'Beam.WeakDamage', 15, 'Released early'), (2300205, 'Beam.Speed', 30.0, ''), (2300205, 'Beam.Life', 0.5, ''),
        (2300205, 'Beam.Radius', 0.6, ''),
        (2300221, 'Guard.Defense', 10, 'Flat defense'),
    ]
    bundles = [
        (2200001, 'Skill', None, 'Base skill: cast lock'),
        (2200002, 'Effect', None, 'Base hit area: lifetime, shape, damage'),
        (2200003, 'Effect', None, 'Base projectile: flies, hits once, disappears'),
        (2200101, 'Skill', 2200001, 'Slash'), (2200102, 'Skill', 2200001, 'Ground Spike'), (2200112, 'Effect', 2200002, 'Ground Spike area'),
        (2200103, 'Skill', 2200001, 'War Cry'), (2200111, 'Buff', None, 'Battle Cry'), (2200104, 'Skill', 2200001, 'Dash'),
        (2200201, 'Skill', 2200001, 'Fire Bolt'), (2200211, 'Effect', 2200003, 'Fire bolt'),
        (2200202, 'Skill', 2200201, 'Fire Bolt II'), (2200203, 'Skill', 2200201, 'Fire Bolt III'),
        (2200204, 'Skill', 2200001, 'Guard Field'), (2200214, 'Effect', None, 'Guard field area'), (2200221, 'Buff', None, 'Guarded'),
        (2200205, 'Skill', 2200001, 'Charge Beam'), (2200215, 'Effect', 2200003, 'Charged beam'), (2200216, 'Effect', 2200215, 'Weak beam'),
    ]
    nodes = [
        node(2200001, 1, 'CastStart', 'CastLock'),
        node(2200002, 1, 'Create', 'SetLifetime'), node(2200002, 2, 'Create', 'HitShape', order=20), node(2200002, 3, 'Hit', 'Damage'),
        node(2200003, 1, 'Create', 'SetLifetime'), node(2200003, 2, 'Create', 'MoveProjectile', order=20), node(2200003, 3, 'Create', 'HitShape', order=30),
        node(2200003, 4, 'Hit', 'Damage'), node(2200003, 5, 'Hit', 'Destroy', order=20),
        # Slash: the hit spawns on the motion's Swing keyframe
        node(2200101, 10, 'CastStart', 'PlayMotion', line=P, override='+'),
        node(2200101, 11, 'Moment', 'SpawnEffect', '@10:Swing', override='+', note='Hit follows the animation'),
        node(2200101, 12, 'Moment', 'PlaySound', '@10:Swing', line=P, override='+'),
        # Ground Spike: fixed area at an offset, box shape override in the child effect
        node(2200102, 10, 'Moment', 'SpawnEffect', 'Spike.Time', override='+'),
        node(2200102, 11, 'Moment', 'ShowVisual', 'Spike.Time', line=P, override='+'),
        # War Cry
        node(2200103, 10, 'CastStart', 'ApplyStatus', order=20, override='+'), node(2200103, 11, 'CastStart', 'PlaySound', line=P, override='+'),
        node(2200111, 1, 'StatusApply', 'StatModify'), node(2200111, 2, 'StatusApply', 'ShowVisual', line=P),
        # Dash
        node(2200104, 10, 'CastStart', 'SelfMove', order=20, override='+'), node(2200104, 11, 'CastStart', 'PlayMotion', line=P, override='+'),
        # Fire Bolt tier 1, then tier 2 adds a node and tier 3 removes one
        node(2200201, 10, 'CastStart', 'PlayMotion', line=P, override='+'),
        node(2200201, 11, 'Moment', 'SpawnEffect', 'Bolt.Release', override='+'),
        node(2200201, 12, 'Moment', 'PlaySound', 'Bolt.Release', line=P, override='+'),
        node(2200211, 10, 'Hit', 'ShowVisual', line=P, override='+'),
        node(2200202, 100, 'Moment', 'SpawnEffect', 'Bolt.Release2', override='+'),
        node(2200203, 100, 'Moment', 'SpawnEffect', 'Bolt.Release2', override='+'),
        remove(2200203, 12),
        node(2200203, 101, 'Moment', 'ShowVisual', 'Bolt.Release', line=P, override='+'),
        # Guard Field: area with an active window that applies a buff to allies it touches
        node(2200204, 10, 'CastStart', 'SpawnEffect', order=20, override='+'),
        node(2200214, 1, 'Create', 'SetLifetime'), node(2200214, 2, 'Create', 'HitShape', order=20), node(2200214, 3, 'Create', 'HitWindow', order=30),
        node(2200214, 4, 'Hit', 'ApplyStatus'), node(2200214, 5, 'Create', 'ShowVisual', line=P),
        node(2200221, 1, 'StatusApply', 'StatModify'),
        # Charge Beam: charge, then pass -> strong beam, fail -> weak beam
        node(2200205, 10, 'CastStart', 'Charge', order=20, override='+'),
        node(2200205, 11, 'CheckPass', 'SpawnEffect', frm=10, override='+'),
        node(2200205, 12, 'CheckFail', 'SpawnEffect', frm=10, override='+'),
        node(2200205, 13, 'CheckPass', 'PlaySound', frm=10, line=P, override='+'),
    ]
    links = [
        ref(2200001, 1, 'Duration', 'Cast.Duration'),
        ref(2200002, 1, 'Duration', 'Hit.Life'), ref(2200002, 2, 'Radius', 'Hit.Radius'), ref(2200002, 3, 'Amount', 'Hit.Damage'),
        ref(2200003, 1, 'Duration', 'Bolt.Life'), ref(2200003, 2, 'Speed', 'Bolt.Speed'), ref(2200003, 3, 'Radius', 'Bolt.Radius'),
        ref(2200003, 4, 'Amount', 'Bolt.Damage'),
        fix(2200101, 10, 'PresentationKey', 2500101), fix(2200101, 11, 'BundleKey', 2200002), fix(2200101, 12, 'PresentationKey', 2500102),
        fix(2200102, 10, 'BundleKey', 2200112), ref(2200102, 10, 'OffsetX', 'Spike.Offset'), fix(2200102, 11, 'PresentationKey', 2500104),
        fix(2200112, 2, 'Shape', 'Box'), ref(2200112, 2, 'Width', 'Spike.Width'), ref(2200112, 2, 'Height', 'Spike.Height'),
        fix(2200112, 2, 'Radius', 0),
        fix(2200103, 10, 'BuffKey', 2110101), fix(2200103, 10, 'Target', 'Self'), ref(2200103, 10, 'Duration', 'Cry.Duration'),
        fix(2200103, 11, 'PresentationKey', 2500105),
        fix(2200111, 1, 'Stat', 'Attack'), fix(2200111, 1, 'Op', 'Multiply'), ref(2200111, 1, 'Value', 'Cry.Attack'), fix(2200111, 2, 'PresentationKey', 2500106),
        fix(2200104, 10, 'Mode', 'Dash'), ref(2200104, 10, 'Distance', 'Dash.Distance'), ref(2200104, 10, 'Duration', 'Dash.Duration'),
        fix(2200104, 11, 'PresentationKey', 2500103),
        fix(2200201, 10, 'PresentationKey', 2500201), fix(2200201, 11, 'BundleKey', 2200211), fix(2200201, 12, 'PresentationKey', 2500202),
        fix(2200211, 10, 'PresentationKey', 2500203),
        fix(2200202, 100, 'BundleKey', 2200211), ref(2200202, 11, 'AngleStep', 'Bolt.Spread'),
        fix(2200203, 100, 'BundleKey', 2200211), fix(2200203, 101, 'PresentationKey', 2500204),
        fix(2200204, 10, 'BundleKey', 2200214),
        ref(2200214, 1, 'Duration', 'Field.Life'), ref(2200214, 2, 'Radius', 'Field.Radius'), ref(2200214, 3, 'From', 'Field.ActiveFrom'),
        ref(2200214, 3, 'Until', 'Field.ActiveUntil'), fix(2200214, 4, 'BuffKey', 2110201), fix(2200214, 4, 'Target', 'Allies'),
        ref(2200214, 4, 'Duration', 'Guard.Duration'), fix(2200214, 5, 'PresentationKey', 2500205), fix(2200214, 5, 'Attach', 'Ground'),
        fix(2200221, 1, 'Stat', 'Defense'), ref(2200221, 1, 'Value', 'Guard.Defense'),
        ref(2200205, 10, 'MaxTime', 'Beam.MaxCharge'), ref(2200205, 10, 'MinTime', 'Beam.MinCharge'),
        fix(2200205, 11, 'BundleKey', 2200215), fix(2200205, 12, 'BundleKey', 2200216), fix(2200205, 13, 'PresentationKey', 2500206),
        ref(2200215, 1, 'Duration', 'Beam.Life'), ref(2200215, 2, 'Speed', 'Beam.Speed'), ref(2200215, 3, 'Radius', 'Beam.Radius'),
        ref(2200215, 4, 'Amount', 'Beam.Damage'), fix(2200215, 4, 'Kind', 'Normal'),
        ref(2200216, 4, 'Amount', 'Beam.WeakDamage'),
    ]
    resources = [
        (2500101, 'Demo_Slash', None, None, 'Slash swing motion'), (2500102, None, None, 'sfx_swing', 'Swing sound'),
        (2500103, 'Demo_Dash', None, None, 'Dash motion'), (2500104, None, 'fx_spike', None, 'Spike visual'),
        (2500105, None, None, 'sfx_shout', 'Shout'), (2500106, None, 'fx_aura_red', None, 'Attack aura'),
        (2500201, 'Demo_Cast', None, None, 'Cast motion'), (2500202, None, None, 'sfx_fire', 'Fire sound'),
        (2500203, None, 'fx_fire_hit', None, 'Fire hit'), (2500204, None, 'fx_fire_burst', None, 'Fire burst'),
        (2500205, None, 'fx_guard_ring', None, 'Guard ring'), (2500206, None, None, 'sfx_beam', 'Beam sound'),
    ]
    modifiers = [dict(Key=2400201, TargetSkillKeys='2100201,2100202,2100203', ValueName='Bolt.Damage', Op='Multiply', Value=0.1, Priority=None,
                      Desc='Example passive: +10% bolt damage')]
    return {
        'SkillMetaData': skills,
        'BuffMetaData': buffs,
        'ValueBundleData': [dict(Key=k, ParentKey=p, Desc=d) for k, p, d in value_bundles],
        'ValueData': [dict(BundleKey=b, Name=n, Value=v, Desc=d or None) for b, n, v, d in values],
        'ModifierData': modifiers,
        'ModuleBundleData': [dict(Key=k, Kind=kind, ParentKey=p, Desc=d) for k, kind, p, d in bundles],
        'ModuleNodeData': nodes,
        'ValueLinkData': links,
        'PresentationResourceData': [dict(PresentationKey=k, Skin=None, Clip=c, Effect=e, Sound=s, Desc=d) for k, c, e, s, d in resources],
    }


# ------------------------------------------------------------------ animation clips (Unity text format, keyframe events only)
ANIM = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  serializedVersion: 7
  m_Legacy: 0
  m_Compressed: 0
  m_UseHighQualityCurve: 1
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves: []
  m_FloatCurves: []
  m_PPtrCurves: []
  m_SampleRate: {rate}
  m_WrapMode: 0
  m_Bounds:
    m_Center: {{x: 0, y: 0, z: 0}}
    m_Extent: {{x: 0, y: 0, z: 0}}
  m_ClipBindingConstant:
    genericBindings: []
    pptrCurveMapping: []
  m_AnimationClipSettings:
    serializedVersion: 2
    m_AdditiveReferencePoseClip: {{fileID: 0}}
    m_AdditiveReferencePoseTime: 0
    m_StartTime: 0
    m_StopTime: {length}
    m_OrientationOffsetY: 0
    m_Level: 0
    m_CycleOffset: 0
    m_HasAdditiveReferencePose: 0
    m_LoopTime: 0
    m_LoopBlend: 0
    m_LoopBlendOrientation: 0
    m_LoopBlendPositionY: 0
    m_LoopBlendPositionXZ: 0
    m_KeepOriginalOrientation: 0
    m_KeepOriginalPositionY: 1
    m_KeepOriginalPositionXZ: 0
    m_HeightFromFeet: 0
    m_Mirror: 0
  m_EditorCurves: []
  m_EulerEditorCurves: []
  m_HasGenericRootTransform: 0
  m_HasMotionFloatCurves: 0
  m_Events:
{events}"""
ANIM_EVENT = """  - time: {time}
    functionName: {function}
    data: {name}
    objectReferenceParameter: {{fileID: 0}}
    floatParameter: 0
    intParameter: 0
    messageOptions: 0
"""
CLIPS = {
    'Demo_Slash': (60, 1, [('Start', 0), ('Windup', 0.1), ('Swing', 0.25), ('Recover', 0.5)]),
    'Demo_Cast': (60, 1, [('Start', 0), ('Release', 0.3)]),
    'Demo_Dash': (60, 0.5, [('Start', 0), ('Land', 0.2)]),
}


def anim_text(name, function):
    rate, length, keys = CLIPS[name]
    events = ''.join(ANIM_EVENT.format(time=t, function=function, name=k) for k, t in keys)
    return ANIM.format(name=name, rate=rate, length=length, events=events)


# ------------------------------------------------------------------ writing
def normalize_zip(data):
    """Rewrite an .xlsx with fixed entry times and document dates so the same rows always give the same bytes."""
    src = zipfile.ZipFile(io.BytesIO(data))
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        for info in src.infolist():
            fixed = zipfile.ZipInfo(info.filename, ZIP_TIME)
            fixed.compress_type = zipfile.ZIP_DEFLATED
            fixed.external_attr = info.external_attr
            body = src.read(info.filename)
            if info.filename == 'docProps/core.xml':
                stamp = FIXED_TIME.strftime('%Y-%m-%dT%H:%M:%SZ').encode()
                body = re.sub(rb'(<dcterms:(?:created|modified)[^>]*>)[^<]*(<)', rb'\g<1>' + stamp + rb'\g<2>', body)
            z.writestr(fixed, body)
    return out.getvalue()


def write_workbooks(cfg, reg, rows, folder):
    """Write every registry document with header rows and the given data rows. Returns {document: path}."""
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    out = {}
    for doc, file_name in cfg['documents'].items():
        tables = [t for t in reg['tables'] if t['document'] == doc]
        if not tables:
            continue
        wb = openpyxl.Workbook()
        wb.remove(wb.active)
        wb.properties.creator = 'SkillGraph Kit'
        wb.properties.created = wb.properties.modified = FIXED_TIME
        for t in tables:
            ws = wb.create_sheet(t['sheet'])
            write_header(ws, t, reg)
            for i, c in enumerate(t['columns'], start=1):
                ws.column_dimensions[openpyxl.utils.get_column_letter(i)].width = 30 if c['name'] in ('Desc', 'Note', 'NameRef') else 16
            for r, row in enumerate(rows.get(t['json'], []), start=t['headerRows'] + 1):
                for i, c in enumerate(t['columns'], start=1):
                    v = row.get(c['name'])
                    if v is not None and not (c['name'] == 'Override' and v == 'None'):
                        ws.cell(r, i, v)
        buf = io.BytesIO()
        wb.save(buf)
        path = folder / file_name
        path.write_bytes(normalize_zip(buf.getvalue()))
        out[doc] = str(path)
    return out


def write_json(path, obj):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((json.dumps(obj, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))


# A Unity project keeps the workbooks outside Assets and the clips inside Assets (so Unity imports them).
PROJECT_SETTINGS = {'dataDir': 'SkillData', 'keyframes': {'animRoot': 'Assets/SkillGraphDemo/Animations'}}


def make(target, lang='en', project=False):
    target = Path(target)
    settings = dict(SETTINGS, language=lang, **(PROJECT_SETTINGS if project else {}))
    write_json(target / 'skillgraph.json', settings)
    cfg = Config(settings, target)
    raw = (json.dumps(dictionary_json(), ensure_ascii=False, indent=1) + '\n').encode('utf-8')
    cfg.dictionary_path.parent.mkdir(parents=True, exist_ok=True)
    cfg.dictionary_path.write_bytes(raw)
    write_json(cfg.generated() / cfg['files']['display'], display_json(sha(raw)))
    for name in CLIPS:
        p = cfg.path(cfg['keyframes']['animRoot']) / (name + '.anim')
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(anim_text(name, cfg['keyframes']['function']).encode('utf-8'))
    d = Dictionary(cfg.dictionary_path, cfg)
    reg = load_registry(cfg, d)
    files = write_workbooks(cfg, reg, demo_rows(), cfg.data_dir)
    return dict(folder=str(target), files=files, dictionarySha256=d.hash)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--out', required=True)
    p.add_argument('--lang', default='en', choices=['en', 'ko'])
    p.add_argument('--project', action='store_true', help='Unity project layout (SkillData/ and Assets/SkillGraphDemo/Animations)')
    a = p.parse_args(argv)
    print(json.dumps(make(a.out, a.lang, a.project), ensure_ascii=False, indent=1))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
