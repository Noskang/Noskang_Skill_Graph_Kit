"""Project settings: everything a host game decides (key bands, layer names, events, folders, formats).

A project keeps one JSON file (default name ``skillgraph.json``). Every key is optional; missing keys fall back to
DEFAULTS below. Relative paths are resolved against the folder that holds the settings file.
"""
import copy
import importlib
import json
import sys
from pathlib import Path

PACKAGE = Path(__file__).resolve().parent

DEFAULTS = {
    'language': 'en',
    # Workbook documents (registry document id -> file name inside the data folder).
    'dataDir': 'Data',
    'documents': {'Skills': 'Skills.xlsx', 'ModuleBundles': 'ModuleBundles.xlsx', 'Presentation': 'PresentationResources.xlsx'},
    'optionalDocuments': [],
    'registry': [str(PACKAGE / 'tables.json')],
    'folders': {'generated': 'Generated', 'editor': 'Editor', 'practice': 'Practice', 'backup': 'Library/SkillGraphKit/Backup'},
    'files': {'dictionary': 'module_dictionary.json', 'display': 'module_display.json', 'aux': 'skill_graph.json',
              'dictionaryView': 'ModuleDictionary.xlsx'},
    'formats': {
        'dictionary': ['SkillGraph Module Dictionary 1'],
        'display': 'SkillGraph Module Display 1',
        'logic': 'SkillGraph Logic 1',
        'presentation': 'SkillGraph Presentation 1',
        'graphView': 'SkillGraph View 1',
        'editModel': 'SkillGraph Edit 1',
        'aux': 'SkillGraph Aux 1',
    },
    # Keys are fixed-width integers: band digits + owner digits + serial digits (e.g. 210 03 01 -> 2100301).
    'keys': {
        'layout': {'band': 3, 'owner': 2, 'serial': 2},
        'bands': {'skill': 210, 'buff': 211, 'moduleBundle': 220, 'valueBundle': 230, 'modifier': 240, 'presentation': 250},
        'overflow': {'moduleBundle': []},
    },
    'practiceOwner': 98,
    # Inheritance layers, root first. The chain may not be longer than this list.
    'layers': ['Base', 'Owner skill', 'Tier'],
    'nodeIdStart': [1, 10, 100],
    'header': {'rows': 7, 'exportTokens': {'both': 'both', 'server': 'server', 'client': 'client', 'none': 'none'}},
    'bundleKinds': {'skill': 'Skill', 'effect': 'Effect', 'buff': 'Buff'},
    # Owner tables: rows that give a module bundle + value bundle a name (skills, buffs).
    # duplicateClear: columns a duplicated owner starts empty (ids that must stay unique, texts that describe the original).
    'owners': [
        {'table': 'SkillMetaData', 'kind': 'skill', 'band': 'skill', 'loc': 'SKILL.K{key}.NAME', 'export': 'skills', 'exportColumns': [],
         'duplicateClear': ['DescLoc']},
        {'table': 'BuffMetaData', 'kind': 'buff', 'band': 'buff', 'loc': 'BUFF.K{key}.NAME', 'export': 'buffs', 'exportColumns': ['Category'],
         'duplicateClear': ['DescLoc']},
    ],
    # Tier families: rows of <table> name an owner (<owner> column), its family (<group>) and its tier (<level>).
    # familyBy: columns that split a family further (default: [group]); exclude: {column: [values]} rows to skip.
    'tiers': {'table': 'SkillMetaData', 'owner': 'Key', 'group': 'TierGroup', 'level': 'Tier', 'familyBy': None, 'exclude': {}},
    # Note columns. ownerNote None = same column name as nodeNote.
    'columns': {'nodeNote': 'Note', 'ownerNote': None},
    'events': {
        'start': 'CastStart',              # time zero of a cast; also the default event of a new column
        'timed': ['Moment', 'Alarm'],      # events whose value is a time (seconds, a value name or a keyframe)
        'exits': ['CheckPass', 'CheckFail'],
    },
    # Time axis. grid = smallest step the runtime ticks on (None = no grid). unit is for display only.
    'time': {'unit': 's', 'grid': 0.025},
    'modules': {
        'spawn': {'module': 'SpawnEffect', 'slot': 'BundleKey'},
        'status': {'module': 'ApplyStatus', 'slot': 'BuffKey'},
    },
    'dictionary': {'balanceKind': 'balance'},
    # Simple structural rules: an <event> node in a <kind> bundle needs a <requires> module next to it.
    'rules': {'companions': []},
    'keyframes': {
        'function': 'SkillKeyframe',
        'animRoot': 'Animations',
        'resourceTable': 'PresentationResourceData',
        'motion': {'module': 'PlayMotion', 'speed': 'Speed', 'key': 'PresentationKey', 'while': 'While',
                   'whileUnresolved': ['Released'], 'until': 'Until', 'loopFrom': 'LoopFrom', 'loopPeriod': 'LoopPeriod'},
    },
    # Presentation modules the host runtime can draw, per event (shown in the editor; empty = no hint).
    'presentationDrawn': {},
    # Editor: columns a bundle kind always shows (default: the kind's first event).
    'editor': {'baseColumns': {}},
    # Host extensions: "module:function" strings. paths are added to sys.path first.
    'plugins': {'paths': [], 'validate': [], 'export': None},
}


def merge(base, extra):
    out = copy.deepcopy(base)
    for k, v in (extra or {}).items():
        if isinstance(v, dict) and isinstance(out.get(k), dict) and k not in ('documents', 'bands'):
            out[k] = merge(out[k], v)
        else:
            out[k] = copy.deepcopy(v)
    return out


class Config:
    def __init__(self, data=None, root=None):
        self.data = merge(DEFAULTS, data or {})
        self.root = Path(root or Path.cwd()).resolve()
        self._plugins = None

    @classmethod
    def load(cls, path=None):
        if path is None:
            return cls()
        path = Path(path).resolve()
        text = path.read_text(encoding='utf-8').strip()
        return cls(json.loads(text) if text else {}, path.parent)

    def __getitem__(self, key):
        return self.data[key]

    def get(self, key, default=None):
        return self.data.get(key, default)

    def path(self, value):
        p = Path(value)
        return p if p.is_absolute() else self.root / p

    # ---- folders
    @property
    def data_dir(self):
        return self.path(self['dataDir'])

    def generated(self, base=None):
        return Path(base or self.data_dir) / self['folders']['generated']

    @property
    def dictionary_path(self):
        return self.generated() / self['files']['dictionary']

    @property
    def practice_dir(self):
        return self.data_dir / self['folders']['practice']

    @property
    def backup_dir(self):
        return self.path(self['folders']['backup'])

    def aux_path(self, base):
        return Path(base) / self['folders']['editor'] / self['files']['aux']

    def documents(self, base=None):
        base = Path(base or self.data_dir)
        out = {}
        for doc, name in self['documents'].items():
            p = base / name
            out[doc] = str(p) if p.is_file() or doc not in self['optionalDocuments'] else None
        return out

    # ---- keys
    def band(self, role):
        if isinstance(role, int) or str(role).isdigit():
            return int(role)
        return int(self['keys']['bands'][role])

    def key_digits(self):
        lay = self['keys']['layout']
        return lay['band'] + lay['owner'] + lay['serial']

    def key_band(self, key):
        lay = self['keys']['layout']
        return int(str(int(key))[:lay['band']])

    def key_owner(self, key):
        lay = self['keys']['layout']
        return int(str(int(key))[lay['band']:lay['band'] + lay['owner']])

    def key_serial(self, key):
        lay = self['keys']['layout']
        return int(str(int(key))[lay['band'] + lay['owner']:])

    def make_key(self, band, owner, serial):
        lay = self['keys']['layout']
        if not (0 <= owner < 10 ** lay['owner'] and 0 < serial < 10 ** lay['serial']):
            raise ValueError(f'owner/serial out of range: {owner}/{serial}')
        return (self.band(band) * 10 ** lay['owner'] + owner) * 10 ** lay['serial'] + serial

    def is_key(self, value, band=None):
        digits = self.key_digits()
        if not (isinstance(value, int) and not isinstance(value, bool) and 10 ** (digits - 1) <= value < 10 ** digits):
            return False
        return band is None or self.key_band(value) == self.band(band)

    def max_serial(self):
        return 10 ** self['keys']['layout']['serial'] - 1

    # ---- kinds
    def kind(self, role):
        return self['bundleKinds'][role]

    @property
    def kinds(self):
        return list(self['bundleKinds'].values())

    def owner_tables(self):
        return [dict(o, kindName=self.kind(o['kind'])) for o in self['owners']]

    def owner_table_of(self, key):
        band = self.key_band(key)
        for o in self.owner_tables():
            if self.band(o['band']) == band:
                return o
        return None

    @property
    def max_depth(self):
        return len(self['layers'])

    # ---- time
    def on_grid(self, seconds):
        grid = self['time']['grid']
        if not grid:
            return True
        steps = seconds / grid
        return abs(steps - round(steps)) <= 1e-6

    def snap(self, v):
        grid = self['time']['grid']
        if not grid or isinstance(v, bool) or not isinstance(v, (int, float)):
            return v
        return round(round(v / grid) * grid, 9)

    # ---- plugins
    def plugins(self):
        if self._plugins is None:
            p = self['plugins']
            for folder in p.get('paths', []):
                folder = str(self.path(folder))
                if folder not in sys.path:
                    sys.path.insert(0, folder)
            self._plugins = dict(validate=[_load(x) for x in p.get('validate', [])], export=_load(p['export']) if p.get('export') else None)
        return self._plugins


def _load(spec):
    module, _, name = spec.partition(':')
    return getattr(importlib.import_module(module), name)
