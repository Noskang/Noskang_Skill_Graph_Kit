"""Shared pieces: validation report, the module dictionary and the table registry."""
import hashlib
import json
import math
import re
from pathlib import Path

from .messages import text

NAME_RE = re.compile(r'^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+$')
LOC_RE = re.compile(r'^[A-Z0-9_]+(\.[A-Z0-9_]+)+$')
NUMERIC = ('int', 'float', 'time')
LINES = ('Logic', 'Presentation')


class SkillDataError(Exception):
    pass


def is_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def canonical(obj):
    return (json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False) + '\n').encode('utf-8')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def sha_file(path):
    return sha(Path(path).read_bytes())


# ------------------------------------------------------------------ report
AT_ORDER = ('sheet', 'bundle', 'node', 'slot', 'owner', 'values', 'name', 'modifier', 'doc', 'text')


def render_at(at, lang):
    parts = []
    if at.get('sheet') is not None:
        parts.append(text(lang, 'at.sheetRow', sheet=at['sheet'], row=at['row']) if at.get('row') is not None else str(at['sheet']))
        if at.get('col'):
            parts.append(str(at['col']))
    if at.get('owner') is not None:
        parts.append(text(lang, 'at.owner', owner=at['owner']))
    if at.get('bundle') is not None:
        parts.append(text(lang, 'at.bundle', bundle=at['bundle']))
    if at.get('node') is not None:
        parts.append(text(lang, 'at.node', node=at['node']))
    if at.get('slot') is not None:
        parts.append(text(lang, 'at.slot', slot=at['slot']))
    if at.get('values') is not None:
        parts.append(text(lang, 'at.values', values=at['values']))
    if at.get('name') is not None:
        parts.append(text(lang, 'at.value', name=at['name']))
    if at.get('modifier') is not None:
        parts.append(text(lang, 'at.modifier', key=at['modifier']))
    for k in ('doc', 'text'):
        if at.get(k):
            parts.append(str(at[k]))
    return ' '.join(parts)


class Issue:
    __slots__ = ('level', 'code', 'at', 'where', 'message')

    def __init__(self, level, code, at, where, message):
        self.level, self.code, self.at, self.where, self.message = level, code, at, where, message

    def __str__(self):
        return f'{self.code} | {self.where} | {self.message}'

    __repr__ = __str__

    def to_dict(self):
        return dict(level=self.level, code=self.code, where=self.where, message=self.message,
                    bundle=self.at.get('bundle'), node=self.at.get('node'), slot=self.at.get('slot'), owner=self.at.get('owner'))


class Report:
    def __init__(self, lang='en'):
        self.lang = lang
        self.errors, self.warnings = [], []

    def _issue(self, level, code, at, msg, kw):
        at = at if isinstance(at, dict) else dict(text=str(at))
        return Issue(level, code, at, render_at(at, self.lang), text(self.lang, msg or code, **kw))

    def e(self, code, at, _msg=None, **kw):
        """Add an error. _msg picks a message other than the code's own; kw fills the message."""
        self.errors.append(self._issue('error', code, at, _msg, kw))

    def w(self, code, at, _msg=None, **kw):
        self.warnings.append(self._issue('warning', code, at, _msg, kw))

    def extend(self, other):
        self.errors += other.errors
        self.warnings += other.warnings

    def codes(self):
        return sorted({x.code for x in self.errors + self.warnings})


# ------------------------------------------------------------------ module dictionary
class Dictionary:
    """Module dictionary exported from the host's C# module declarations, plus the editor display file next to it.

    Slot rows: [name, type, unit, kind, default, timing, description, supported]. The description comes from the display
    file and does not take part in the dictionary hash (changing wording never changes the data fingerprint)."""

    def __init__(self, path, cfg):
        raw = Path(path).read_bytes()
        self.path = Path(path)
        self.hash = sha(raw)
        d = json.loads(raw.decode('utf-8'))
        if d.get('format') not in cfg['formats']['dictionary']:
            raise SkillDataError(text(cfg['language'], 'dictionary.format', path=Path(path).name, format=d.get('format')))
        self.display, self.display_stale = None, False
        display_path = Path(path).with_name(cfg['files']['display'])
        if display_path.is_file():
            self.display = json.loads(display_path.read_text(encoding='utf-8'))
            self.display_stale = self.display.get('dictionaryHash') != self.hash
        lang = cfg['language']
        views = {v['name']: v for v in (self.display or {}).get('modules', [])}
        for m in d['modules']:
            v = views.get(m['name'], {})
            texts = {s[0]: localized(s[2], lang) for s in v.get('slots', [])}
            m['label'] = localized(v.get('label') or v.get('ko', ''), lang)
            m['note'] = localized(v.get('note', ''), lang)
            if len(m['slots'] and m['slots'][0]) == 7:
                m['slots'] = [s[:6] + [texts.get(s[0], '')] + s[6:] for s in m['slots']]
        ev = cfg['events']
        self.events = d['events']
        self.kind_events = {k: set(v) for k, v in d['kindEvents'].items()}
        self.presentation_events = set(d['presentationEvents'])
        self.subscription_events = set(d.get('subscriptionEvents', []))
        self.timed_events = set(d.get('timedEvents', ev['timed']))
        self.exit_events = list(d.get('exitEvents', ev['exits']))
        self.start_event = d.get('startEvent', ev['start'])
        self.subjects = list(d.get('subjects', []))
        self.slot_enums = d['slotEnums']
        self.modules = d['modules']
        self.by_name = {m['name']: m for m in self.modules}
        self.index = {m['name']: i for i, m in enumerate(self.modules)}

    def slot(self, module, name):
        for i, s in enumerate(self.by_name[module]['slots']):
            if s[0] == name:
                return i, s
        return None, None

    def is_subscription(self, node):
        return node['Event'] in self.subscription_events


# ------------------------------------------------------------------ table registry
def localized(value, lang):
    if isinstance(value, dict):
        return value.get(lang) or value.get('en') or next(iter(value.values()))
    return value


def load_registry(cfg, dictionary):
    lang = cfg['language']
    tables, enums = [], {}
    for path in cfg['registry']:
        d = json.loads(cfg.path(path).read_text(encoding='utf-8'))
        for t in d['tables']:
            t = dict(t, sheet=localized(t['sheet'], lang), headerRows=t.get('headerRows', cfg['header']['rows']))
            t['columns'] = [dict(c, desc=localized(c.get('desc', ''), lang)) for c in t['columns']]
            tables.append(t)
        enums.update(d.get('enums', {}))
    enums['BUNDLE_KIND'] = list(cfg.kinds)
    enums['EVENT'] = list(dictionary.events)
    enums['MODULE'] = [m['name'] for m in dictionary.modules]
    tokens = cfg['header']['exportTokens']
    return dict(tables=tables, enums=enums, tokens=tokens, by_json={t['json']: t for t in tables})
