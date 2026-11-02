"""Issue codes: every code the backend can report, with its level and message text, for host manuals and tools.

codes(cfg) lists the core codes below, the codes named by settings rules (rules.companions[].code) and the codes a validate
plugin module declares in a module-level CODES table: {code: (level, en, ko)} with level 'error' or 'warning'.
A test keeps LEVELS in step with the rep.e / rep.w / KeyframeError calls in this package.
"""
import importlib

from .messages import MESSAGES

ERROR, WARNING = 'error', 'warning'

LEVELS = {
    # cell parsing
    'TextRequired': ERROR, 'LocFormat': ERROR, 'NumberRequired': ERROR, 'IntegerRequired': ERROR, 'KeyBand': ERROR,
    'EnumName': ERROR, 'InvalidEnum': ERROR, 'EmptyListItem': ERROR, 'ValueType': ERROR,
    # sheets and headers
    'MissingDocument': ERROR, 'MissingSheet': ERROR, 'HiddenDataSheet': ERROR, 'MergedCells': ERROR, 'HeaderJsonName': ERROR,
    'HeaderMismatch': ERROR, 'HeaderRowsGuess': ERROR, 'ExtraCell': ERROR, 'FormulaNotAllowed': ERROR, 'RequiredCell': ERROR,
    'DuplicateKey': ERROR, 'SheetsMismatch': ERROR,
    # inheritance and overrides
    'InheritanceCycle': ERROR, 'InheritanceDepth': ERROR, 'MissingParent': ERROR, 'DefaultOnRoot': ERROR, 'OverrideOnRoot': ERROR,
    'OverrideAddExists': ERROR, 'OverrideKindChange': ERROR, 'OverrideRemoveMissing': ERROR, 'OverrideTargetMissing': ERROR,
    # references, values and keys
    'MissingReference': ERROR, 'MissingValueName': ERROR, 'ValueName': ERROR, 'ValueNameCase': ERROR, 'RefName': ERROR,
    'RefNotAllowed': ERROR, 'PracticeKey': ERROR, 'MetaBundleKind': ERROR, 'UnusedValue': WARNING,
    # bundles, nodes and slots
    'UnknownModule': ERROR, 'UnimplementedModule': ERROR, 'UnknownSlot': ERROR, 'RequiredSlot': ERROR, 'SlotType': ERROR,
    'SlotEnum': ERROR, 'SlotKeyBand': ERROR, 'UnsupportedValue': ERROR, 'KindMismatch': ERROR, 'LineMismatch': ERROR,
    'LinkNodeMissing': ERROR, 'EventForKind': ERROR, 'EventForModule': ERROR, 'EventValueRequired': ERROR,
    'EventValueUnexpected': ERROR, 'FromNodeMissing': ERROR, 'FromNodeNotCheck': ERROR, 'FromNodeRemoved': ERROR,
    'FromNodeUnexpected': ERROR, 'OrderTie': ERROR, 'PresentationEvent': ERROR, 'PresentationToLogic': ERROR,
    'SpawnCycle': ERROR, 'SpawnNotEffect': ERROR, 'SubscriptionSubject': ERROR, 'CompanionMissing': ERROR,
    'FixedBalance': WARNING, 'FixedTiming': WARNING, 'OffGrid': WARNING,
    # keyframe time references
    'KeyframeEvent': ERROR, 'KeyframeName': ERROR, 'KeyframeMissing': ERROR, 'KeyframeClip': ERROR, 'KeyframeMotion': ERROR,
    'KeyframeMotionSpeed': ERROR, 'KeyframeMotionStart': ERROR, 'KeyframeOffsetNegative': ERROR, 'KeyframeUnreachable': ERROR,
    'KeyframeAfterUntil': WARNING,
}


def _texts(code):
    """Message variants of a code: the code itself and its sub-messages ('Code.reason')."""
    return [dict(key=k, en=v[0], ko=v[1]) for k, v in MESSAGES.items() if k == code or k.startswith(code + '.')]


def codes(cfg=None):
    """[{code, level, source, messages: [{key, en, ko}]}] sorted by code. source: 'core', 'rules' or 'plugin:<module>'."""
    out = {c: dict(code=c, level=lv, source='core', messages=_texts(c)) for c, lv in LEVELS.items()}
    if cfg is not None:
        rules = cfg['rules'].get('companions', [])
        if rules and all(r.get('code') for r in rules):
            del out['CompanionMissing']  # every rule names its own code, so the default code cannot appear
        for rule in rules:
            c = rule.get('code')
            if c and c not in out:
                out[c] = dict(code=c, level=ERROR, source='rules', messages=_texts('CompanionMissing'))
        cfg.plugins()
        for spec in cfg['plugins'].get('validate', []):
            name = spec.partition(':')[0]
            for c, (lv, en, ko) in getattr(importlib.import_module(name), 'CODES', {}).items():
                msg = dict(key=c, en=en, ko=ko, source='plugin:' + name)
                if c in out:  # a plugin may report a core code with its own text (the core level stays)
                    out[c]['messages'].append(msg)
                    out[c]['source'] += ' plugin:' + name
                else:
                    out[c] = dict(code=c, level=lv, source='plugin:' + name, messages=[msg])
    return [out[c] for c in sorted(out)]
