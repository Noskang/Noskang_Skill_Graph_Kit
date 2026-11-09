# 변경 기록

> 영어 원문은 [CHANGELOG.md](CHANGELOG.md)에 있습니다. 판 번호는 [유의적 버전](https://semver.org/lang/ko/)을 따릅니다.

이 프로젝트의 주요 변경을 판마다 적습니다.

## [0.5.0] - 2026-11-03

보드 키: 기술이 도는 동안 노드가 쓰고 읽는 이름 붙은 값입니다. 쌓였다 소모되는 패시브와 콤보 수를 전용 코드 대신 그래프로 만듭니다. 보드 키가 없는 데이터는 0.4.0과 바이트까지 같게 내보내고 저장합니다.

### 더함
- **보드 키 표** `BoardKeyData`(ModuleBundles 통합 문서, 시트 `Board Keys`. 선택 표라서 이 시트가 없는 통합 문서도 됩니다): `Name`(객체.이름), `Type`(`int` · `float` · `bool` · `text` · `key:<대역>`), `Scope`(`Cast` = 시전 한 번과 거기서 만든 효과, `Actor` = 캐릭터 한 명, 게임이 보관), `Default`, `Desc`.
- 설정 `board.table`(키 표, `null`이면 보드를 쓰지 않음)과 `board.scopes`(범위마다 표시 이름 `label`, 그 범위를 쓸 수 있는 `kinds`, 호스트가 더하는 범위).
- 값 연결 출처 `Board`: 칸이 키의 값을 읽습니다. 모듈 칸 형식 `board` / `board:<형식>,…`(노드가 쓰는 키)와 `board:read[,<형식>,…]`(이름으로 읽는 키). board 칸은 Board 연결만 받습니다.
- 검사 `UnknownBoardKey`, `BoardTypeMismatch`, `BoardScopeMismatch`, `BoardWriteTarget`, `BoardKeyName`, `BoardKeyType`와 경고 `UnusedBoardKey`.
- 판정·연출 JSON: 표에 행이 있으면 `boardKeys`(이름 순). 연결은 `[칸, "board", 키 번호]`.
- 편집 동작 `addBoardKey`, `setBoardKey`(이름을 바꾸면 그 키를 쓰는 연결도 모두 바뀜), `removeBoardKey`(그 키를 쓰는 연결이 있으면 거절). `setLink`가 `source: "Board"`를 받습니다. 편집 모델에 `board` 덩어리와 칸 출처 `board`가 생겼습니다. 시트가 없는 통합 문서에 첫 키를 저장하면 머리말과 함께 시트를 만듭니다.
- 표 등록부: `"optional": true` 표(시트가 없으면 빈 표로 읽음)와 열 형식 `any`(값 또는 TRUE / FALSE).
- **런타임** `Blackboard`(형식에 맞춘 읽기·쓰기, 옛 값과 새 값을 주는 `Changed(BoardChange)`, `Values()` = 기본값과 다른 키, `Load(list, notify)` = 보드를 통째로 바꿈), `BoardKey`, `IBoardScopes`, `SkillData.BoardKeys` / `NewBoard(scope)`, `BoundNode.BoardKey(slot)`, 보드를 읽는 `Value` / `Number` / `Integer` / `Text`.
  `SkillRunner.Start(owner, actor)`와 `StartBuff(buff, event, actor)`가 캐릭터의 보드를 받습니다. 시전 하나에 Cast 보드가 하나 있고, 거기서 만든 효과가 같이 씁니다. `SkillRunner.Scopes`가 호스트 범위를 줍니다. `ModuleCall`은 Board 칸을 읽고 `Read`, `Key`, `Board`, `Write(slot, value)`를 갖습니다.
- **표준 보드 모듈** `BoardModules`(호스트가 골라 씀): `BoardSet`, `BoardAdd`(Min..Max), `BoardCheck`(통과 / 실패 출구, 연산자는 ≥ > = ≠ ≤ <로 보임), `BoardClear`. `AddTo(builder)`, `AddDisplay(display)`, `SkillRunner`용 `Handler(next)`, 호스트 실행기용 `Run(node, values, scopes, out result)`.
- **편집기**: 보드 탭(키 목록, 더하기 / 고치기 / 지우기, 고른 키를 쓰는 ▸·읽는 ◂ 칸 목록, 누르면 그 노드로 이동), 칸 출처 "◂▸ 보드 키"(형식이 맞는 키만), 카드 줄의 ◂ / ▸, 같은 그래프 안에서 쓰는 노드 → 읽는 노드로 옅은 점선(고른 키는 더 밝게), 영/한 글자 키 `tab.board`, `board.*`, `slot.board`, `row.board*`.
- 캡처 명세 키 `panel: "board"`, `boardKey`, `pick`(도구 막대의 기술 고르기처럼 주인을 바꿈). `"quit": false`로 돌렸을 때 열어 둔 창을 캡처 보고서에 적습니다(`kept`).
- 데모: Slash가 맞힌 횟수를 `Combo.Count`(Actor, 최대 3)에 세고, 세 번째 다음 휘두르기는 마무리 공격이 되며 수를 비웁니다. 명중 글자(`ShowText`, 새 데모 모듈)가 수를 보여 줍니다. `python -m skillgraph.demo --no-board`는 0.4.0 데모를 씁니다.
- 화면 08 – 10(보드 탭, 보드 칸, 한국어). 01 – 07은 새 탭 때문에 다시 찍었습니다.
- 각각의 시험(Python 66개, 패키지 EditMode 33개, 데모 포함 EditMode 39개).

### 바꿈
- 기술이나 버프를 바꾸면 배치의 상자 경계로 두 그래프를 새 주인에 맞춥니다(최대 100 %). 예전에는 50 ms 뒤에 GraphView의 전체 맞춤을 불렀는데, 새 상자를 재기 전에 돌면 옛 배율이 남을 수 있었습니다.

### 고침
- `"quit": false` 캡처가 이제 마지막 장을 화면에 남깁니다(캡처 창과 그 장이 연 창). 예전에는 장마다 창을 닫아 볼 것이 남지 않았습니다.
- 위쪽 막대의 기술 고르기를 풍선 도움말이 떠 있을 때 누르면, 목록이 열리기 전에 풍선을 치우고(잠깐 목록 위에 남을 수 있었음) 목록이 닫히면 되돌립니다.

## [0.4.0] - 2026-11-03

기술이 많은 프로젝트를 위해 위쪽 막대의 기술 고르기를 검색할 수 있습니다.

### 더함
- 위쪽 막대의 기술 / 버프 고르기가 기본 팝업 메뉴 대신 펼침 목록을 엽니다. 검색 칸 아래에 모든 기술과 버프를 Key 오름차순으로 보이고, 입력하면 "Key 이름" 글에 그 글자가 들어 있는 항목만 남깁니다(대소문자와 앞뒤 빈칸은 무시). 그래서 Key 일부나 이름 일부로 찾을 수 있습니다. 위 / 아래로 옮기고, Enter로 고르고, Esc로 닫고, 눌러서 고릅니다. 지금 고른 것은 표시됩니다. 닫힌 칸의 모양은 전과 같습니다.
- 펼침 목록의 글자 키 `picker.search`, `picker.count`, `picker.empty`.
- 각각의 시험(Python 55개, 패키지 EditMode 25개).

## [0.3.2] - 2026-11-02

### 고침
- 패키지 EditMode 시험이 끝나면 편집기 언어 설정을 되돌립니다. 이 설정은 그 PC의 모든 프로젝트가 같이 쓰기 때문에, 예전에는 시험을 돌리고 나면 다른 프로젝트의 편집기도 영어로 남았습니다.

## [0.3.1] - 2026-11-02

편집기 글자를 따로 두는 호스트를 위한 문구 설정을 더 넣었습니다. 기본값은 0.3.0의 글자입니다.

### 더함
- `editor.changeKeySeparator`: 저장 창 변경 목록에서 한 행의 Key 칸들을 잇는 글자(기본 `" / "`).
- `editor.boxCountNameMax`와 글자 키 `box.count`, `box.countSeparator`, `box.countCut`: 접힌 상자나 접힌 연출 열의 개수 줄(모듈 이름 길이, 줄 형식, 구분 글자, 자름 표시).
- 글자 키 `columnDialog.keyframeFrame`, `columnDialog.keyframeStart`, `columnDialog.keyframeNode`: 열 창 키프레임 목록 한 줄의 부분들. 이제 `column.keyframe` / `column.motionStart`를 따르지 않습니다. 기본 글자는 같습니다.
- 각각의 시험(Python 55개, 패키지 EditMode 22개).

## [0.3.0] - 2026-11-02

편집기 문구, 데이터 만들기 과정, Player 빌드를 따로 두는 호스트를 위한 설정입니다. 새 설정의 기본값은 모두 0.2.0 동작이라, 설정하지 않으면 패키지 화면과 데이터가 그대로입니다.

### 더함
- `editor.text`: 언어별 편집기 글자 덮어쓰기. 모듈별 출구 이름(`exit.<모듈>.<출구 이벤트>`)과 시각 열의 시간 아래 줄(`column.time.<이벤트>`, 비울 수 있음)도 됩니다.
- `editor.export`: 더 보기 메뉴의 데이터 만들기가 `python -m skillgraph export` 대신 프로젝트의 명령(`commands`, 자리 표시 `{project}` / `{config}` / `{out}`, `confirm`이면 먼저 물음)을 돌릴 수 있습니다.
- 메뉴 글자를 같이 쓰던 창 제목(`export.title`, `workspace.resetTitle`, `bundleDesc.title`, `bands.title`, `values.addTitle`)과 열 메뉴의 `column.addHere`에 따로 키를 둡니다. 기본 글자는 그대로입니다.
- 사용자가 언어를 고르기 전까지 편집기의 처음 언어가 설정 `language`를 따릅니다.
- 정의 기호 `SKILLGRAPH_EDITOR_ONLY`: 프로젝트가 런타임 어셈블리를 Player 빌드에서 뺄 수 있습니다.
- `skillgraph.codes(cfg)`와 `python -m skillgraph codes`: 모든 검증 코드를 수준·메시지 글과 함께 돌려줍니다. 설정 규칙 코드와 플러그인 `CODES` 표도 들어갑니다.
- `skillgraph.__all__`의 공개 Python 이름(`Backend`, `practice`, `load_workbook`, `load_registry`, `sha`, `codes`, …).
- 표시 설정: `editor.newSkillModuleParent`(새 기술 창의 부모), `editor.boxMinColumns`, `editor.valueUnits`, `editor.boxSummary`(접힌 상자 줄), `editor.compactCard`(한 줄 연출 카드: 시간 구간과 이름 없는 값).
- 글자 키 `values.titleTiers`(단계 묶음의 수치 제목. 첫 단계와 마지막 단계의 Key도 받음), `values.descField`, `bundleDesc.field`, `combo.desc`, `card.range`, `card.rangeEnd`. 기본 글자는 그대로입니다.
- 각각의 시험(Python 54개, 패키지 EditMode 21개).

### 바꿈
- `UnusedValue` 경고가 플러그인 검사 뒤에 나옵니다(모든 값을 마지막으로 훑는 단계).

### 고침
- 연습 공간이 데이터 폴더 밖에 둔 문서의 평평한 사본을 이제 읽습니다(예전에는 원래의 상대 경로를 찾다가 놓쳤습니다).
- 캡처: 편집 동작이 실패한 장은 모달 오류 창을 여는 대신 메시지와 함께 실패합니다(오류 창이 사람 없이 도는 캡처를 멈췄습니다).
- 캡처 창이 이제 편집기 세션(`Library/SkillGraphKit/session-product.json`)을 읽거나 쓰지 않습니다. 예전에는 캡처가 마지막 장의 편집 동작을 거기 남겨 다음 편집기 창에 이어졌습니다.

## [0.2.0] - 2026-10-30

단계 묶음, 복사 규칙, 메모 열이 기본값과 다른 호스트를 위한 설정입니다. 새 설정의 기본값은 모두 0.1.0 동작이라 지금 쓰는 설정 파일은 그대로 됩니다.

### 더함
- `tiers.owner`, `tiers.familyBy`, `tiers.exclude`: 단계 묶음을 아무 표(예: 직업 해금 표)에서나 읽고, 여러 열로 나누고, 패시브 칸 같은 행을 뺍니다.
- `rules.companions[].code`: 동반 모듈 규칙이 자기 검증 코드를 낼 수 있습니다(기본 `CompanionMissing`).
- `owners[].duplicateClear`: 복사한 주인에서 비우고 시작할 열(기본 `["DescLoc"]`).
- `columns.ownerNote`: 주인 메모 열(기본: `columns.nodeNote`와 같은 이름).
- 설정마다 시험(Python 50개).

### 고침
- 편집 모델이 주인 메모를 설정 대신 고정된 `Note` 열에서 읽던 것.

## [0.1.0] - 2026-10-30

첫 공개 판.

### 더함
- **Python 백엔드**(`Tools~/python/skillgraph`): 설정으로 움직이는 코어(Key 자릿수와 대역, 층, 이벤트 의미, 시간 격자, 묶음 종류, 주인 표, 폴더, 형식, 플러그인), 머리말 7줄 약속에 따른 Excel 읽기, 오버라이드 펼침(`+` / `−` / 덮어쓰기, 수치 묶음 상속), 모듈 사전에 따른 검증, 키프레임 시각 참조(`@노드:이름[±오프셋]`), 판정 / 연출 내보내기, 생성 보기, 영/한 메시지.
- **편집 백엔드**: 편집 동작 23개, 편집 모델, 변경 목록, Excel 다시 쓰기(고친 칸만, 새 행은 그 묶음 아래, 잠금·외부 변경 검사, 직전 사본 1개, 다시 읽어 대조), 보조 파일, 연습 공간, JSON 한 줄씩 주고받는 `serve`.
- **Unity 런타임**(`Noskang.SkillGraph`): 호스트가 선언하는 `ModuleSet`과 `ModuleDisplaySet`(JSON 내보내기가 바이트까지 같음), 수정자를 반영한 `SkillData` 묶기, 데모 `SkillRunner`, `SkillKeyframeReceiver`.
- **Unity 편집기**(`Noskang.SkillGraph.Editor`): GraphView 기술 그래프 편집기(판정과 연출 그래프를 같은 시간 열에, 편집 층, 단계 수치 표, 열과 키프레임 편집, 검색 창, 저장 창), 캡처 명령, 키프레임 도구, 모듈 사전 내보내기, 환경 설정 페이지, 영/한 화면.
- **데모**: 지어낸 기술 묶음(Slash, Ground Spike, War Cry, Dash, Fire Bolt I–III, Guard Field, Charge Beam), 재현되는 샘플 생성기, 플레이할 수 있는 장면이 있는 DemoProject.
- **시험**: Python 47개(턴제 프리셋 포함)와 Unity EditMode 19개.
- **문서**: README, 설계, 데이터 계약, 편집기 안내, 화면.
