# 데이터 계약

> **Summary** — File formats between Excel, the Python backend, the Unity editor and a runtime: settings, table registry,
> header rows, cell types, module dictionary and display JSON, exported logic / presentation JSON, the edit model,
> edit operations, the aux file and the serve protocol.

## 설정 `skillgraph.json`

모든 키는 선택입니다(파일 내용이 `{}`이거나 빈 파일이어도 됨). 없는 키는 기본값을 쓰고, 상대 경로는 설정 파일이 있는 폴더 기준입니다. 객체 값은 기본값과 합쳐지고(`documents`·`bands`는 통째로 바뀜), 목록·글자는 바뀝니다.

| 키 | 기본값 | 뜻 |
|---|---|---|
| `language` | `en` | 검증 메시지·시트 이름 언어(`en`/`ko`) |
| `dataDir` | `Data` | 통합 문서 폴더 |
| `documents` | `Skills` → `Skills.xlsx`, `ModuleBundles` → `ModuleBundles.xlsx`, `Presentation` → `PresentationResources.xlsx` | 등록부 문서 ID → 파일 이름 |
| `optionalDocuments` | `[]` | 없어도 되는 문서 |
| `registry` | 패키지 `tables.json` | 표 등록부 파일 목록 |
| `folders` | `Generated`·`Editor`·`Practice`·`Library/SkillGraphKit/Backup` | 생성 파일·보조 파일·연습 공간·직전 사본 폴더 |
| `files` | `module_dictionary.json`·`module_display.json`·`skill_graph.json`·`ModuleDictionary.xlsx` | 파일 이름 |
| `formats` | `SkillGraph … 1` | 파일 형식 이름(`dictionary`는 받아들일 형식 목록) |
| `keys.layout` | `{band:3, owner:2, serial:2}` | Key 자릿수 |
| `keys.bands` | 기술 210 · 버프 211 · 모듈 묶음 220 · 수치 묶음 230 · 수정자 240 · 연출 250 | 역할 → 대역 |
| `keys.overflow` | `{moduleBundle: []}` | 대역이 차면 이어 쓸 대역 |
| `practiceOwner` | `98` | 연습 공간 주인 번호(제품에서는 오류) |
| `layers` | `Base`·`Owner skill`·`Tier` | 상속 층 이름(길이 = 최대 깊이) |
| `nodeIdStart` | `[1, 10, 100]` | 층마다 새 노드 ID 시작값 |
| `header.rows` | `7` | 머리말 줄 수 |
| `header.exportTokens` | `both`·`server`·`client`·`none` | 5행 내보내기 토큰 |
| `bundleKinds` | `skill: Skill`, `effect: Effect`, `buff: Buff` | 역할 → 묶음 종류 이름 |
| `owners` | 기술·버프 표 | 주인 표(표·종류·대역·로컬라이즈 코드 틀·내보낼 이름·함께 내보낼 열) |
| `tiers` | `SkillMetaData`의 `TierGroup`·`Tier` | 단계 묶음 열 |
| `columns.nodeNote` | `Note` | 노드 메모 열 이름 |
| `events` | `start: CastStart`, `timed: [Moment, Alarm]`, `exits: [CheckPass, CheckFail]` | 사전에 없을 때 쓰는 이벤트 의미 |
| `time` | `unit: s`, `grid: 0.025` | 시간 단위·격자(`null` = 격자 없음) |
| `modules.spawn` / `modules.status` | `SpawnEffect.BundleKey` / `ApplyStatus.BuffKey` | 다른 묶음을 가리키는 모듈·칸 |
| `dictionary.balanceKind` | `balance` | 사전의 밸런스 칸 역할 이름 |
| `rules.companions` | `[]` | `{kind, event, requires, spawnedOnly}` 동반 모듈 규칙 |
| `keyframes` | `function: SkillKeyframe`, `animRoot: Animations`, `resourceTable: PresentationResourceData`, `motion`(모션 모듈·칸 이름) | 키프레임 읽기 |
| `presentationDrawn` | `{}` | 실행기가 그리는 연출 이벤트(편집기 표시용) |
| `editor.baseColumns` | `{}` | 종류마다 늘 보이는 열(기본: 그 종류의 첫 이벤트) |
| `plugins` | `paths: []`, `validate: []`, `export: null` | `"모듈:함수"` 확장 |

## 표 등록부와 머리말

`tables.json`이 표마다 문서·시트 이름(언어별)·JSON 이름·키 열·열 목록을 정합니다. 시트의 머리말 7줄은:

| 행 | 내용 |
|---|---|
| 1 | JSON 이름(A1, 예 `ValueData`) |
| 2 | 열 설명 |
| 3 | 자료형 |
| 4 | 열 이름 |
| 5 | 내보내기 토큰 |
| 6 | 참조 |
| 7 | 범위 |

데이터는 8행부터입니다. 정의된 열 밖에 값이 있으면 오류, 수식·병합 셀은 오류, 숨긴 데이터 시트는 오류입니다. 칸에 `<empty>`라고 적으면 빈 문자열입니다(빈 칸은 열 기본값).

**자료형**

| 형식 | 값 |
|---|---|
| `string` · `loc` | 글자(`loc`는 `UPPER.DOT.CODE`) |
| `int` · `float` · `time` | 숫자 |
| `key:<역할>` | 그 대역의 Key(예 `key:skill`, 숫자 대역 `key:250`도 됨) |
| `enum:<이름>[:기본]` | 열거 **이름**(숫자는 오류) |
| `list:<형식>` | 쉼표 목록 |
| `value` | 숫자 또는 글자(수치 값, 이벤트 값, 값 연결 값) |

**표**: `SkillMetaData` `BuffMetaData` `ValueBundleData` `ValueData` `ModifierData`(Skills 문서), `ModuleBundleData` `ModuleNodeData` `ValueLinkData`(ModuleBundles 문서), `PresentationResourceData`(Presentation 문서). 열 목록은 `Tools~/python/skillgraph/tables.json`.

**수치 이름**: `객체.이름`, 단마다 대문자 시작(`Bolt.Damage`, `Field.ActiveFrom`). 대소문자만 다른 이름은 오류입니다.

**이벤트 값**: 시각 이벤트는 숫자(초) · 수치 이름 · `@노드:키프레임[±오프셋]`. 구독 이벤트는 주체 이름. 그 밖의 이벤트는 비움.

## 모듈 사전 `module_dictionary.json`

C# `ModuleSet.ExportJson()`이 쓰고(정본), Python `json.dumps(indent=1, ensure_ascii=False)`와 같은 바이트입니다. 지문은 이 파일 UTF-8 바이트의 SHA-256입니다.

```json
{
 "format": "SkillGraph Module Dictionary 1",
 "events": ["CastStart", "Moment", "..."],
 "kindEvents": {"Skill": ["CastStart", "..."], "Effect": ["..."], "Buff": ["..."]},
 "presentationEvents": ["..."],
 "subscriptionEvents": ["OnHurt"], "subjects": ["Self", "Ally", "Enemy", "Any"],
 "timedEvents": ["Moment", "Alarm"], "exitEvents": ["CheckPass", "CheckFail"], "startEvent": "CastStart",
 "slotEnums": {"SHAPE": ["Circle", "Box"], "...": []},
 "modules": [
  {"name": "HitShape", "line": "Logic", "group": "common", "events": ["Create"],
   "slots": [["Shape", "enum:SHAPE", "", "structure", "Circle", "spawn", null], ["Radius", "float", "m", "balance", 0, "spawn", null]],
   "exits": [], "random": false, "implemented": true}
 ]
}
```

칸 줄: `[이름, 자료형, 단위, 역할(balance|structure), 기본값(null = 필수), 계산 시점, 지원 값(null = 모두)]`.
`events` 순서가 내보낸 JSON의 이벤트 번호, `modules` 순서가 모듈 번호, `kindEvents` 순서가 묶음 종류 번호입니다.

## 표시 정보 `module_display.json`

`ModuleDisplaySet.ExportJson(hash)`. 지문에 들어가지 않습니다. `dictionaryHash`가 다르면 편집기가 "다른 사전에서 만든 표시 파일"이라고 알립니다.

```json
{"format": "SkillGraph Module Display 1", "dictionaryHash": "…",
 "families": [{"name": "HitDamage", "label": {"en": "Hit & damage", "ko": "판정·피해"}, "color": "#AE4646", "icon": "✕"}],
 "events": {"Hit": {"en": "Hit", "ko": "명중"}}, "enumLabels": {},
 "modules": [{"name": "HitShape", "label": {"en": "…", "ko": "…"}, "family": "HitDamage", "note": {"en": "…", "ko": "…"},
   "slots": [["Shape", {"en": "Shape", "ko": "모양"}, {"en": "Circle or box", "ko": "원 또는 상자"}]],
   "stages": [{"name": "Basic", "advanced": false, "slots": ["Shape", "Radius"]}, {"name": "Box", "advanced": true, "slots": ["Width", "Height"]}],
   "kind": {"slot": "Shape", "rules": [{"values": ["Box"], "events": [], "slots": ["Width", "Height"], "verified": true}]},
   "choose": null, "unused": []}]}
```

## 내보낸 데이터

`python -m skillgraph export --out <폴더>`가 세 파일을 씁니다. 파일 이름에 내용 SHA-256이 들어갑니다.

**판정** `skills.logic.<sha>.json`
```json
{"format": "SkillGraph Logic 1", "dictionaryHash": "…",
 "bundles": [{"key": 2200101, "kind": 0, "nodes": [
   {"id": 11, "event": 1, "at": 0.25, "from": null, "priority": 0, "order": 10, "module": 1, "links": [[0, "fixed", 2200002]]}]}],
 "values": [{"key": 2300101, "entries": [[14, 0.5], [21, 12]]}],
 "modifiers": [{"key": 2400201, "targets": [2100201], "name": 7, "op": 2, "value": 0.1, "priority": 0}],
 "skills": [{"key": 2100101, "bundle": 2200101, "values": 2300101}],
 "buffs": [{"key": 2110101, "bundle": 2200111, "values": 2300111, "category": 0}]}
```
- `event`·`module`·`kind`·열거 값은 번호입니다(사전 순서).
- `at`: 시각 숫자(격자에 맞춤) · `["ref", 수치 번호]` · 구독 주체 번호 · `null`.
- `links`: `[칸 번호, "fixed", 값]` 또는 `[칸 번호, "ref", 수치 번호]`.
- 수치 번호는 연출 파일의 `valueNames` 위치입니다.
- 주인 목록 이름(`skills`·`buffs`)은 설정 `owners[].export`입니다.

**연출** `skills.presentation.<sha>.json`: `valueNames`, 연출 줄 `bundles`, `metas`(이름·설명 코드·아이콘), `resources`(키·스킨·클립·효과·소리).

**manifest** `skills.manifest.json`: 두 파일 이름·SHA-256, 사전 SHA-256, 원본 통합 문서 이름·SHA-256, 도구 버전.

## 편집 모델과 편집 동작

편집기와 백엔드는 한 줄 JSON을 주고받습니다(`python -m skillgraph serve`).

```json
{"id": 3, "cmd": "model", "workspace": "product", "ops": [{"op": "setValue", "values": 2300201, "name": "Bolt.Damage", "value": 26}], "base": {"Skills": "<sha256>"}}
```

| `cmd` | 돌려주는 것 |
|---|---|
| `ping` | 형식 이름 |
| `model` | 편집 모델(아래), `externalChange`, `pending` |
| `changes` | 변경 목록, 보조 파일 변경 여부, 저장을 막는 이유, 오류·경고 수 |
| `save` | 쓴 파일, 직전 사본, 새 `base`. 검증 오류가 있으면 막음(`allowErrors`로 무시) |
| `practice` | `action: create|reset` |

**편집 모델**(`SkillGraph Edit 1`) 윗단 키: `workspace`·`settings`(층·시간·종류·Key 자릿수·생성/버프 모듈·기본 열)·`dictionary`(이벤트·종류별 이벤트·모듈·칸·열거)·`display`·`issues`·`owners`(사슬·수치 사슬·단계 묶음·버프)·`bundles`(펼친 노드·칸 출처·정한 층·뺀 노드·영향 목록·키프레임)·`values`·`keyNames`·`resources`·`aux`·`ops`(동작 결과: 새 Key·노드 ID).

**편집 동작**

| `op` | 필드 |
|---|---|
| `setLink` | `bundle node slot source(Ref|Fixed|Default|null) value` |
| `addNode` | `bundle event [eventValue fromNode line module order priority links note]` |
| `removeNode` · `revert` | `bundle node [slot]` |
| `moveNode` | `bundle node [event eventValue fromNode order priority line]` |
| `applyToParent` | `bundle node [slot]` |
| `newBundle` | `[kind parent ownerNumber key serial desc]` |
| `setBundle` | `bundle [desc parent]` |
| `setValue` | `values name value(null = 지움) [desc]` |
| `applyValueToParent` | `values name` |
| `newValues` | `[parent ownerNumber key serial desc]` |
| `newSkill` | `kind [ownerNumber name moduleParent valueParent desc]` |
| `duplicateSkill` | `owner [withEffects name ownerNumber]` |
| `setMeta` | `owner fields{열: 값}` |
| `saveCombo` · `insertCombo` · `removeCombo` | `name bundle nodes` / `bundle combo [event eventValue fromNode]` / `name` |
| `setMemo` · `setBands` · `setCollapsed` | `bundle node text` / `bundle bands[{name from to}]` / `bundle collapsed` |
| `addColumn` · `setColumn` · `removeColumn` | `bundle event [eventValue fromNode]` (+ `toEvent toEventValue toFromNode`) |

실패한 동작은 "N번째 동작(op): 이유"로 돌아오고 편집기는 그 동작을 목록에서 뺍니다.

## 보조 파일

`<dataDir>/Editor/skill_graph.json`(`SkillGraph Aux 1`). 빌드·내보내기와 무관합니다.

```json
{"format": "SkillGraph Aux 1",
 "bundles": {"2200201": {"collapsed": false, "bands": [{"name": "Wind-up", "from": "CastStart", "to": "CastStart"}],
             "memos": {"11": "first bolt"}, "columns": [{"event": "Moment", "value": 0.5, "from": null}]}},
 "combos": [{"name": "Damage pack", "desc": "", "nodes": [{"id": 3, "line": "Logic", "event": "Hit", "eventValue": null, "fromNode": null, "module": "Damage", "links": []}]}]}
```
`columns`는 아직 노드가 없는 빈 열이고, 노드가 들어오면 백엔드가 지웁니다.
