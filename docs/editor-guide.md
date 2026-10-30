# 편집기 안내

> **Summary** — How to use the Skill Graph Editor (Tools › SkillGraph Kit › Skill Graph Editor): window layout, edit layers,
> slots and values, columns and keyframes, saving, the practice workspace, keyboard shortcuts, menus and the capture command.
> The editor starts in English; the `한` / `EN` toolbar button switches the UI to Korean. This guide gives both names, e.g. *Save(저장)*.

## 준비

1. **Edit › Preferences › SkillGraph Kit**: *Python*에 openpyxl이 있는 Python, *Settings*에 `skillgraph.json`(비우면 프로젝트 폴더의 `skillgraph.json`). 명령줄 `-skillgraphPython`·`-skillgraphConfig`, 환경 변수 `SKILLGRAPH_PYTHON`·`SKILLGRAPH_CONFIG`가 있으면 그쪽이 먼저입니다.
2. 모듈 선언을 바꿨다면 **Tools › SkillGraph Kit › Export Module Dictionary**.
3. **Tools › SkillGraph Kit › Skill Graph Editor**.

## 창 구성

![창 구성](images/01_overview_tiers.png)

| 자리 | 내용 |
|---|---|
| 도구 막대 | 기술·버프 고르기 · **Edit layer(편집 층)** · 보기 *Logic only / Both / Presentation edit / Fold presentation*(기능만 / 둘 다 / 연출 편집 / 연출 접기) · *Link pan(가로 연동)* · 오류·경고 수 · *Frame·All(맞추기·전체)* · *Panel* · *Undo·Redo·Save(되돌리기·다시하기·저장)* · *More(더 보기)* · `한`/`EN` |
| 위 그래프 | 판정 줄. 주 묶음 상자, 오른쪽에 생성되는 효과·걸리는 버프 상자(접힘) |
| 아래 그래프 | 연출 줄. 위와 같은 열 위치. "둘 다" 보기에서는 한 줄 카드 |
| 오른쪽 패널 | **Values(수치)** 단계별 표 · **Node(노드)** 메모·칸 편집·값 연결 후보 · **Resources(연출 리소스)** 스킨별 클립·효과·소리 |
| 검증 목록 | 오류·경고. 누르면 그 노드로 이동(접힌 상자는 펼침) |
| 상태 줄 | 고른 기술·편집 층·카드 줄 수·저장 전 편집 수 |

## 편집 층

도구 막대의 `바탕 › 기술 › 단계` 칩이 **어느 층에 쓸지** 정합니다. 마지막 층이 단계 묶음이면 단계마다 칩(T1·T2·T3)이 보이고, 누르면 그 단계 기술로 바뀝니다.

- 물려받은 노드에는 정한 층 꼬리표가 붙습니다. 이 층에서 바꾼 칸은 파란 왼쪽 줄로 보입니다.
- 이 층에서 물려받은 노드를 빼면 `−` 행이 생기고, 카드가 반투명하게 남습니다(오른쪽 단추 › 되돌리기).
- 이 층에서 더한 노드는 `＋`입니다.
- **부모에 적용**(오른쪽 단추)은 이 층의 변경을 부모 층으로 옮깁니다. 실행 전에 부모를 쓰는 모든 기술을 보여 줍니다.
- 물려받은 노드는 이 층에서 **시각·순서만** 바꿀 수 있습니다. 이벤트를 바꾸려면 정한 층으로 가거나 `−` 후 `＋`.

## 노드와 칸

![노드 탭](images/02_charge_branch.png)

- 칸 줄 기호: `←` 수치 참조 · `■` 고정 · `▲` 밸런스 칸을 그래프에 고정(경고, 수치 참조 권장) · `·` 기본값 · `!` 필요.
- 칸을 누르면 **수치 탭**에서 그 수치 행이 강조되고, 수치 행에 마우스를 올리면 그 수치를 쓰는 칸과 열 머리가 모두 강조됩니다.
- **노드 탭**: 칸마다 출처(기본값 / ← 수치 / ■ 고정)와 값을 고르고 *적용*. `↺`는 이 층의 칸 되돌리기.
- **값 연결 후보**: 묶음이 주로 쓰는 객체 이름 + 칸 이름(예 `Bolt.Speed`). *연결*은 없는 수치를 0으로 만들고 연결합니다.
- 단계 칩(`▸ 고급 2`, `＋Box`)을 누르면 접힌 칸이 펼쳐집니다.
- 검사 노드(출구가 있는 모듈)는 바닥줄에 `통과 ● / 실패 ○` 포트가 있고, 출구 열이 그 옆에 계단처럼 붙습니다.
- **노드 더하기**: 열 머리의 `＋`(아래 그래프는 `＋연출`) → 검색 창. 그 열의 이벤트·줄에서 쓸 수 있는 모듈만 보이고, 실행기가 없는 모듈은 `(미구현)`으로 막힙니다. 맨 위 *조합 넣기*는 저장해 둔 노드 묶음을 넣습니다.
- **노드 옮기기**: 다른 열로 끌면 이벤트가 바뀌고, 같은 열 안에서 끌면 순서가 바뀝니다. 끄는 동안 놓일 열이 빛나고, 이 층에서 못 옮기면 붉게 빛납니다.
- **Delete**: 고른 노드를 이 층에서 뺍니다(노드마다 편집 하나, Ctrl+Z로 하나씩 되돌림).

## 열과 키프레임

![열 바꾸기](images/06_column_keyframe_dialog.png)

- 열 머리 오른쪽 단추: **Change column(열 바꾸기)** 그 열의 판정·연출 노드 모두, **Remove column(열 지우기)** 노드도 함께·그 노드가 낸 출구 열까지, 종류마다 늘 있는 기본 열은 **Clear column(열 비우기)** 노드만 뺌, **Add column(열 더하기)**. 묶음 제목의 `+ column(＋ 열)`도 같습니다.
- 노드가 없는 열은 보조 파일에 빈 열로 남고, 노드가 들어오면 Excel 행이 됩니다.
- 시각 열은 초 · 수치 이름 · **키프레임**으로 정합니다. 키프레임을 고르면 `Swing : 0.25s`처럼 환산 시각이 보이고, 오프셋(`+0.05`)을 더할 수 있습니다. 모션 노드를 고르면 노드 탭에 그 애니메이션의 키프레임 목록이 나옵니다.
- 키프레임이 없는 클립은 Project 창에서 고른 뒤 **Tools › SkillGraph Kit › Add Missing Keyframes to Selected Clips**(자세가 바뀌는 프레임마다 `Start`·`Pose1`… 를 둠, 이미 있는 클립은 건드리지 않음).

## 수치 표

- 행 = 수치 이름, 열 = 단계. **흰 굵은 값** = 그 단계 수치 묶음이 정한 값, 회색 = 물려받은 값.
- 값을 누르면 고치고, 오른쪽 단추로 되돌리기·부모에 적용·설명 고치기. 열 머리를 누르면 그 단계로 바뀝니다.
- 기술이 거는 버프의 수치는 아래에 따로 보입니다(`넘김` = 버프 걸기 노드가 버프에 넘기는 값).

## 저장

1. **Save(저장)**(Ctrl+S) → 변경 목록 창: 어느 통합 문서 · 시트 · 몇 행 · 무슨 열이 어떻게 바뀌는지.
2. 저장하면 잠금·외부 변경을 다시 검사하고, 바뀐 칸만 쓰고, 직전 사본 1개를 백업 폴더(`Library/SkillGraphKit/Backup`)에 두고, 다시 읽어 대조합니다.
3. 검증 오류가 있으면 저장하지 않습니다. Excel이 열려 있으면(`~$` 소유자 파일) 닫으라고 알립니다.

저장 전 편집은 `Library/SkillGraphKit/session-product.json`에 남아 편집기를 닫았다 열어도 이어집니다. **More › Reload(더 보기 › 다시 읽기)** 는 편집을 버리고 Excel을 다시 읽습니다.

## 연습 공간

**More › Open practice workspace(더 보기 › 연습 공간 열기)** 는 제품 통합 문서를 `<dataDir>/Practice/`에 복사해 그쪽을 엽니다. 제목 옆에 *연습 공간* 표시가 붙고, 새로 만드는 기술은 연습 주인 번호(기본 98)를 받습니다. 제품 데이터 만들기는 그 번호를 오류로 막습니다. *연습 공간 초기화*는 다시 복사합니다.

## More(더 보기) 메뉴

| 항목 | 하는 일 |
|---|---|
| Build skill data(스킬 데이터 만들기) | 고른 폴더에 판정·연출 JSON과 manifest(`python -m skillgraph export`). 저장 전 편집이 없고 제품 작업 공간일 때만 |
| 새 기술 / 새 효과 객체 묶음 / 새 버프 | 메타·모듈 묶음·수치 묶음을 같은 뒤 번호로 만듦 |
| 이 기술 복제 | 새 Key로 복사, 효과 묶음도 함께 복사할 수 있음(생성 노드가 복사본을 가리킴) |
| 이 묶음을 부모로 새 묶음 · 묶음 설명 · 구간 이름 띠 | 편집 층 묶음에 대해 |
| 실행 기록 겹치기 | `{"bundle":…, "node":…}` 한 줄씩인 JSON Lines를 읽어 노드마다 `실행 n회` |

## 단축키

| 키 | |
|---|---|
| F / A | 고른 노드에 맞추기 / 전체 |
| Ctrl+Z / Ctrl+Y | 되돌리기 / 다시하기 |
| Ctrl+S | 저장 |
| Delete | 고른 노드 빼기 |
| 휠 / 가운데 끌기 | 확대 / 이동 |

## 캡처 명령

설명서 그림을 같은 상태로 다시 찍는 명령입니다. 화면이 있는 편집기에서만 됩니다(batchmode 불가). 편집기 안에서는 **Tools › SkillGraph Kit › Run Capture Spec…** 으로 명세를 고릅니다. 데모 명세의 `out`은 `docs/images/*.png`(저장소에 커밋된 그림)를 덮어쓰므로, 다른 그림을 찍을 때는 `out`을 바꿉니다.

```bash
Unity -projectPath DemoProject -executeMethod Noskang.SkillGraph.Editor.SkillGraphCapture.Run -skillgraphCapture <절대 경로>/screens.json
```

명세는 장마다 기술·층·보기·패널·창 크기·맞춤·고른 노드·펼친 상자·열 칩·언어와, 번호 강조(`callouts`)·누를 곳 빨간 상자(`click`)·둘레 자르기(`crop`)·열어 둘 메뉴나 창(`open`)을 적습니다. 편집 동작(`ops`)은 화면에만 적용하고 저장하지 않으며, 열린 창은 확인을 누르지 않습니다. 운영체제 메뉴는 찍을 수 없어 같은 항목을 창 안에 그려 찍습니다. 전체 키 목록은 `SkillGraphCapture.cs` 맨 위 설명, 예시는 [`DemoProject/Capture/screens.json`](../DemoProject/Capture/screens.json).

## Python 명령줄

```bash
cd Packages/com.noskang.skillgraphkit/Tools~/python
python -X utf8 -m skillgraph check --config <skillgraph.json>        # 제품 규칙 검증 + manifest
python -X utf8 -m skillgraph validate --config <skillgraph.json>     # 형식만 검증
python -X utf8 -m skillgraph export --config <…> --out <폴더>        # 판정·연출 JSON
python -X utf8 -m skillgraph dictionary-view --config <…>            # [생성] 모듈 사전 Excel
python -X utf8 -m skillgraph expand-view --config <…>                # 펼친 결과를 원본 Skills.xlsx 안 '!' 시트로 씀(원본 파일이 바뀜, 직전 사본은 백업 폴더)
python -X utf8 -m skillgraph.demo --out <폴더> [--project] [--lang ko]   # 데모 데이터 만들기
```
