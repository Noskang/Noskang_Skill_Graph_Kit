"""Keyframe time references.

A timed event value may read '@{motion node}:{keyframe}' or '@{motion node}:{keyframe}+0.1'. The keyframe is an animation
event (function name from settings) inside the .anim clip that the motion node plays. Its clip time k becomes cast time

    motion start + k / (speed * clip length)

because the runtime samples the motion at normalized time = cast time * speed. The number goes into the exported data,
so a designer can move a pose in the animation and the hit time follows.
"""
import json
import re
from pathlib import Path

from .core import is_num

KEYFRAME_RE = re.compile(r'^@(\d+):(.+)$')
KEYFRAME_OFFSET_RE = re.compile(r'^(.+?)([+-]\d+(?:\.\d+)?)$')
KEYFRAME_NAME_RE = re.compile(r'^[A-Za-z][A-Za-z0-9_]*$')
_ANIM_CACHE = {}


class KeyframeError(Exception):
    def __init__(self, code, _msg=None, **kw):
        super().__init__(code)
        self.code, self.msg, self.kw = code, _msg or code, kw


def is_keyframe_ref(v):
    return isinstance(v, str) and KEYFRAME_RE.match(v) is not None


def _yaml_text(raw):
    raw = raw.strip()
    if raw.startswith('"'):
        return json.loads(raw)
    if raw.startswith("'"):
        return raw[1:-1].replace("''", "'")
    return raw


def read_anim(path, function):
    """Frame rate, clip length and keyframe events (name -> seconds) from a text-serialized Unity .anim file."""
    text = Path(path).read_text(encoding='utf-8')
    rate = float(re.search(r'^  m_SampleRate: ([0-9.]+)$', text, re.M).group(1))
    begin = re.search(r'^    m_StartTime: ([0-9.eE+-]+)$', text, re.M)
    stop = re.search(r'^    m_StopTime: ([0-9.eE+-]+)$', text, re.M)
    length = (float(stop.group(1)) - (float(begin.group(1)) if begin else 0.0)) if stop else 1.0
    keys = {}
    block = re.search(r'^  m_Events:\n((?:  - .*\n|    .*\n)*)', text, re.M)
    if block:
        for ev in re.split(r'^  - ', block.group(1), flags=re.M):
            fn = re.search(r'functionName: (.*)', ev)
            if not fn or fn.group(1).strip() != function:
                continue
            name = _yaml_text(re.search(r'data: (.*)', ev).group(1))
            keys[name] = float(re.search(r'time: ([0-9.eE+-]+)', ev).group(1))
    return dict(rate=rate, length=length, keys=keys, path=Path(path))


def anim_clip(name, cfg):
    """Clip name -> .anim info, or None. Clip names are unique under the animation root."""
    root = cfg.path(cfg['keyframes']['animRoot'])
    index = _ANIM_CACHE.get(root)
    if index is None:
        index = _ANIM_CACHE[root] = {q.stem: q for q in sorted(root.rglob('*.anim'))} if root.is_dir() else {}
    path = index.get(name)
    return read_anim(path, cfg['keyframes']['function']) if path else None


def clear_cache():
    _ANIM_CACHE.clear()


def slot_value(b, nid, slot, d):
    n = b['nodes'][nid]
    lk = b['links'].get((nid, slot))
    if lk is not None:
        return lk['source'], lk['value']
    _, s = d.slot(n['Module'], slot)
    return 'Default', s[4] if s else None


def motion_keyframes(b, nid, data, d, cfg):
    """Keyframes of one motion node: start time, speed, clip and [(name, frame, rate, cast seconds)]."""
    m = cfg['keyframes']['motion']
    n = b['nodes'].get(nid)
    if n is None or n['Module'] != m['module']:
        raise KeyframeError('KeyframeMotion', 'KeyframeMotion.notMotion', node=nid, module=m['module'])
    if n['Event'] == d.start_event:
        start = 0.0
    elif n['Event'] in d.timed_events and is_num(n['EventValue']):
        start = float(n['EventValue'])
    else:
        raise KeyframeError('KeyframeMotionStart', node=nid, event=n['Event'], value=n['EventValue'])
    if m.get('while'):
        _, w = slot_value(b, nid, m['while'], d)
        if w in m.get('whileUnresolved', []):
            raise KeyframeError('KeyframeMotionStart', 'KeyframeMotionStart.while', node=nid)
    src, speed = slot_value(b, nid, m['speed'], d)
    if src == 'Ref' or not is_num(speed) or speed <= 0:
        raise KeyframeError('KeyframeMotionSpeed', node=nid, value=speed)
    src, key = slot_value(b, nid, m['key'], d)
    if src == 'Ref' or not is_num(key):
        raise KeyframeError('KeyframeMotion', 'KeyframeMotion.key', node=nid, value=key)
    rows = data.get(cfg['keyframes']['resourceTable'], [])
    row = next((r for r in rows if r['PresentationKey'] == int(key) and not r.get('Skin')), None)
    if row is None or not row.get('Clip'):
        raise KeyframeError('KeyframeMotion', 'KeyframeMotion.clip', key=int(key))
    clip = row['Clip'].split(',')[0].strip()
    anim = anim_clip(clip, cfg)
    if anim is None:
        raise KeyframeError('KeyframeClip', clip=clip)
    scale = float(speed) * anim['length']
    extra = {}
    for name in ('until', 'loopFrom', 'loopPeriod'):
        value = slot_value(b, nid, m[name], d)[1] if m.get(name) else 0
        extra[name] = value if is_num(value) else 0
    keys = []
    for name, sec in sorted(anim['keys'].items(), key=lambda x: x[1]):
        frame = round(sec * anim['rate'])
        keys.append((name, frame, anim['rate'], round(start + frame / anim['rate'] / scale, 6)))
    return dict(node=nid, start=start, speed=float(speed), length=anim['length'], key=int(key), clip=clip, keys=keys, **extra)


def split_keyframe(text, names):
    """'Name' or 'Name+offset' -> (name, offset seconds). A full name that exists wins over an offset reading."""
    if text in names:
        return text, 0.0
    m = KEYFRAME_OFFSET_RE.match(text)
    if m and m.group(1) in names:
        return m.group(1), float(m.group(2))
    return text, 0.0


def keyframe_time(b, ref, data, d, cfg):
    """'@node:name[+-offset]' -> (seconds, info)."""
    m = KEYFRAME_RE.match(ref)
    info = motion_keyframes(b, int(m.group(1)), data, d, cfg)
    name, offset = split_keyframe(m.group(2), {k[0] for k in info['keys']})
    hit = next((k for k in info['keys'] if k[0] == name), None)
    if hit is None:
        raise KeyframeError('KeyframeMissing', clip=info['clip'], name=m.group(2), names=', '.join(k[0] for k in info['keys']))
    if info['loopPeriod'] > 0 and hit[3] >= info['start'] + info['loopFrom'] + info['loopPeriod'] - 1e-9:
        raise KeyframeError('KeyframeUnreachable', name=hit[0], sec=hit[3])
    if not KEYFRAME_NAME_RE.match(hit[0]):
        raise KeyframeError('KeyframeName', name=hit[0])
    total = round(hit[3] + offset, 6)
    if total < 0:
        raise KeyframeError('KeyframeOffsetNegative', name=hit[0], sec=hit[3], offset=offset)
    return total, dict(info, name=hit[0], frame=hit[1], rate=hit[2], keySeconds=hit[3], offset=offset)


def validate_keyframes(data, mods, rep, d, cfg):
    grid, unit = cfg['time']['grid'], cfg['time']['unit']
    for k, b in mods.items():
        for nid, n in b['nodes'].items():
            if not is_keyframe_ref(n['EventValue']):
                continue
            at = dict(bundle=k, node=nid)
            if n['Event'] not in d.timed_events:
                rep.e('KeyframeEvent', at, event=n['Event'])
                continue
            try:
                sec, info = keyframe_time(b, n['EventValue'], data, d, cfg)
            except KeyframeError as error:
                rep.e(error.code, at, error.msg, **error.kw)
                continue
            if grid and not cfg.on_grid(sec):
                rep.w('OffGrid', at, value=sec, grid=grid, unit=unit)
            if info['until'] > 0 and info['keySeconds'] >= info['until'] - 1e-9:
                rep.w('KeyframeAfterUntil', at, name=info['name'], sec=info['keySeconds'], until=info['until'])
