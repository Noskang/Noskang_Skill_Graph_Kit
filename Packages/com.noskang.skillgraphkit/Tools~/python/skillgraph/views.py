"""Generated, read-only views: the expanded sheet, the module dictionary workbook and the read-only graph model.
They are written only on command, after a lock check."""
import json
import os
import tempfile
from pathlib import Path

import openpyxl
from openpyxl.utils import get_column_letter

from .core import Dictionary, SkillDataError, localized
from .expand import ordered_nodes
from .messages import text
from .pipeline import run
from .validate import owners
from .workbook import atomic_save, lock_check, open_workbook

L = {
    'expandSheet': ('!Generated_Expanded', '!생성_펼침보기'),
    'expandHead': (['Owner key', 'Name', 'Bundle key', 'Bundle', 'Node', 'Line', 'Event', 'Event value', 'Module', 'Slot', 'Final value',
                    'Source', 'Set in', 'Override', 'Note'],
                   ['주인 Key', '이름', '묶음 Key', '묶음 설명', '노드', '줄', '이벤트', '이벤트 값', '모듈', '칸', '최종 값', '출처', '정한 묶음', '오버라이드', '메모']),
    'default': ('default', '기본값'), 'value': ('value {name}', '수치 {name}'), 'fixed': ('fixed in graph', '그래프 고정'),
    'missing': ('(missing)', '(없음)'), 'added': ('+ node ({key})', '+ 노드({key})'), 'slotOverride': ('slot override ({key})', '칸 덮어씀({key})'),
    'dictSheet': ('Module dictionary', '모듈 사전'), 'notes': ('!Notes', '!안내'),
    'dictNote': ('[Generated] Module dictionary view (read only). The source of truth is the C# module declarations. Dictionary hash {hash}',
                 '[생성] 모듈 사전 보기(읽기 전용). 기준은 C# 모듈 선언입니다. 사전 지문 {hash}'),
    'dictStale': (' [Note] The display file was made from a different dictionary.', ' [주의] 표시 파일이 지금 사전과 다른 사전에서 만들어졌습니다.'),
    'yes': ('yes', '예'), 'no': ('no', '아니오'), 'hidden': ('hidden (outside kind rules)', '보이지 않음(종류 규칙 밖)'),
    'unused': ('not run', '미실행'), 'advanced': (' (advanced)', ' (고급)'), 'all': ('all', '모두'), 'guess': (' [guess]', ' [추정]'),
}


def tr(cfg, _label, **kw):
    v = L[_label][1 if cfg['language'] == 'ko' else 0]
    return v.format(**kw) if kw else v


def expanded_view(r, cfg):
    data, mods, vals, reach, d = r['data'], r['mods'], r['vals'], r['reach'], r['dictionary']
    meta = {row['Key']: row for _, row in owners(data, cfg)}
    rows = []
    for key, bks in reach.items():
        m = meta[key]
        vb = vals[m['ValueBundleKey']]
        for b in bks:
            bd = mods[b]
            for nid, n in ordered_nodes(bd, 'Logic', d) + ordered_nodes(bd, 'Presentation', d):
                links = {slot: lk for (lnid, slot), lk in bd['links'].items() if lnid == nid}
                mod = d.by_name[n['Module']]
                for s in mod['slots'] or [['', '', '', '', '', '', '', None]]:
                    lk = links.get(s[0])
                    if lk is None:
                        final, src, from_b = (s[4] if s[0] else ''), (tr(cfg, 'default') if s[0] else ''), ''
                    elif lk['source'] == 'Ref':
                        v = vb.get(lk['value'])
                        final, src, from_b = (v[0] if v else tr(cfg, 'missing')), tr(cfg, 'value', name=lk['value']), f"{lk['_from']} / {v[1] if v else '-'}"
                    else:
                        final, src, from_b = lk['value'], tr(cfg, 'fixed'), str(lk['_from'])
                    marks = [tr(cfg, 'added', key=n['_from'])] if n['_mark'] else []
                    if lk and lk['_from'] != n['_from']:
                        marks.append(tr(cfg, 'slotOverride', key=lk['_from']))
                    rows.append([key, m['NameRef'], b, bd['desc'], nid, n['Line'], n['Event'], '' if n['EventValue'] is None else n['EventValue'],
                                 n['Module'], s[0], '' if final is None else final, src, from_b, ' / '.join(marks), n.get('Note') or ''])
    return tr(cfg, 'expandHead'), rows


def expand_view(cfg, data_dir=None):
    files = cfg.documents(data_dir)
    r = run(files, cfg, True)
    if r['errors']:
        raise SkillDataError('\n'.join(map(str, r['errors'][:40])))
    target = Path(files[r['registry']['by_json'][cfg.owner_tables()[0]['table']]['document']])
    warnings = lock_check(target, cfg['language'])
    head, rows = expanded_view(r, cfg)
    wb = open_workbook(target)
    sheet = tr(cfg, 'expandSheet')
    if sheet in wb.sheetnames:
        del wb[sheet]
    ws = wb.create_sheet(sheet)
    ws.append(head)
    for row in rows:
        ws.append(row)
    for i in range(1, len(head) + 1):
        ws.column_dimensions[get_column_letter(i)].width = 40 if i in (4, 15) else 14
    ws.freeze_panes = 'A2'
    atomic_save(wb, target, cfg.backup_dir / Path(target).name)
    return dict(file=str(target), rows=len(rows), warnings=warnings)


def dictionary_view(cfg):
    d = Dictionary(cfg.dictionary_path, cfg)
    target = cfg.generated() / cfg['files']['dictionaryView']
    warnings = lock_check(target, cfg['language'])
    head = ['Module', 'Label', 'Line', 'Group', 'Implemented', 'Slot', 'Type', 'Unit', 'Kind', 'Default', 'Timing', 'Description',
            'Supported', 'Events', 'Exits', 'Random', 'Family', 'SlotLabel', 'Stage', 'KindRule', 'Choose', 'Unused']
    views = {v['name']: v for v in (d.display or {}).get('modules', [])}
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = tr(cfg, 'dictSheet')
    ws.append(head)
    for m in d.modules:
        view = views.get(m['name'], {})
        labels = {s[0]: localized(s[1], cfg['language']) for s in view.get('slots', [])}
        stage_of = {x: st['name'] + (tr(cfg, 'advanced') if st.get('advanced') else '') for st in view.get('stages', []) for x in st['slots']}
        kind = view.get('kind')
        choose = view.get('choose')
        for s in m['slots'] or [['', '', '', '', None, '', '', None]]:
            rules = []
            if kind and s[0] and s[0] != kind['slot']:
                for rule in kind['rules']:
                    if s[0] in rule['slots']:
                        rules.append(f"{kind['slot']}={'/'.join(rule['values']) or tr(cfg, 'all')}"
                                     + (f" @{'/'.join(rule['events'])}" if rule.get('events') else '') + ('' if rule.get('verified', True) else tr(cfg, 'guess')))
                if not rules:
                    rules.append(tr(cfg, 'hidden'))
            ws.append([m['name'], m.get('label', ''), m['line'], m.get('group', ''), tr(cfg, 'yes') if m['implemented'] else tr(cfg, 'no'),
                       s[0], s[1], s[2], s[3], '' if s[4] is None else str(s[4]), s[5], s[6], '' if s[7] is None else ','.join(map(str, s[7])),
                       ','.join(m['events']), ','.join(m['exits']), tr(cfg, 'yes') if m.get('random') else '', view.get('family') or '',
                       labels.get(s[0], ''), stage_of.get(s[0], ''), ' / '.join(rules),
                       choose['name'] if choose and s[0] in choose['slots'] else '', tr(cfg, 'unused') if s[0] in view.get('unused', []) else ''])
    for i, h in enumerate(head, start=1):
        ws.column_dimensions[get_column_letter(i)].width = 46 if h in ('Description', 'Events', 'KindRule') else 16
    ws.freeze_panes = 'C2'
    note = wb.create_sheet(tr(cfg, 'notes'))
    note['A1'] = tr(cfg, 'dictNote', hash=d.hash) + (tr(cfg, 'dictStale') if d.display_stale else '')
    atomic_save(wb, target, cfg.backup_dir / target.name)
    return dict(file=str(target), modules=len(d.modules), dictionarySha256=d.hash, warnings=warnings)


def graph_model(r, cfg, source):
    """Read-only graph model: expanded bundles with the source of every slot, and the validation list."""
    issues = [i.to_dict() for i in r['errors'] + r['warnings']]
    model = dict(format=cfg['formats']['graphView'], source=source, issues=issues, owners=[], bundles=[], events=[])
    if 'mods' not in r:
        return model
    d, mods, vals, data, reach = r['dictionary'], r['mods'], r['vals'], r['data'], r['reach'] or {}
    model['dictionaryHash'], model['events'] = d.hash, list(d.events)
    meta = {row['Key']: row for _, row in owners(data, cfg)}
    owners_of = {}
    for key, bks in sorted(reach.items()):
        m = meta[key]
        model['owners'].append(dict(key=key, name=m['NameRef'], values=m['ValueBundleKey'], bundles=list(bks)))
        for b in bks:
            owners_of.setdefault(b, []).append(key)
    for k in sorted(mods):
        b = mods[k]
        nodes = []
        for line in ('Logic', 'Presentation'):
            for nid, n in ordered_nodes(b, line, d):
                links = {slot: lk for (lnid, slot), lk in b['links'].items() if lnid == nid}
                mod = d.by_name.get(n['Module'])
                slots = []
                for s in (mod['slots'] if mod else None) or []:
                    lk = links.get(s[0])
                    if lk is None:
                        slots.append(dict(name=s[0], type=s[1], source='default', value=s[4], definedIn=None, resolved=[]))
                    elif lk['source'] == 'Ref':
                        resolved = []
                        for owner in owners_of.get(k, []):
                            v = vals.get(meta[owner]['ValueBundleKey'], {}).get(lk['value'])
                            resolved.append(dict(owner=owner, value=v[0] if v else None, definedIn=v[1] if v else None))
                        slots.append(dict(name=s[0], type=s[1], source='ref', value=lk['value'], definedIn=lk['_from'], resolved=resolved))
                    else:
                        slots.append(dict(name=s[0], type=s[1], source='fixed', value=lk['value'], definedIn=lk['_from'], resolved=[]))
                nodes.append(dict(id=nid, line=line, event=n['Event'], eventValue=n['EventValue'], module=n['Module'],
                                  implemented=bool(mod and mod['implemented']), order=n['Order'], priority=n['Priority'] or 0,
                                  fromNode=n['FromNode'], definedIn=n['_from'], added=bool(n['_mark']), note=n.get('Note') or '', slots=slots))
        model['bundles'].append(dict(key=k, kind=b['kind'], description=b['desc'] or '', owners=owners_of.get(k, []), nodes=nodes))
    return model


def write_json(path, obj):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix='.' + path.stem + '.', suffix='.json', dir=path.parent)
    with os.fdopen(fd, 'wb') as f:
        f.write((json.dumps(obj, ensure_ascii=False, sort_keys=True, indent=1, default=str) + '\n').encode('utf-8'))
    os.replace(name, path)


def graph_view(cfg, out, data_dir=None, product=True):
    r = run(cfg.documents(data_dir), cfg, product)
    model = graph_model(r, cfg, 'product' if product else 'sample')
    write_json(out, model)
    return dict(file=str(out), bundles=len(model['bundles']), owners=len(model['owners']), errors=len(r['errors']), warnings=len(r['warnings']))
