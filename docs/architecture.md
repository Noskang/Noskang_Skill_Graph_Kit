# 설계

> **Summary** — A skill is a tree of *bundles*. Each bundle holds *nodes* (event + module) and *value links* (which slot reads
> which number). Bundles and value bundles inherit in up to three layers (base → skill → tier) with `+` / `−` / override rows,
> like prefab variants. Python expands every layer, validates it against the module dictionary exported from C#, and exports
> index-based logic and presentation JSON. The Unity editor only draws; every edit is an operation sent to the backend.

## 1. 세 층과 세 표

기술 데이터는 세 종류의 행으로 나뉩니다.

| 층 | 무엇 | 표 |
|---|---|---|
| **메타** | 이름이 붙은 주인. 기술·버프 하나 = 모듈 묶음 하나 + 수치 묶음 하나 | `SkillMetaData`, `BuffMetaData` |
| **모듈 묶음** | 동작. 노드(이벤트 + 모듈)와 값 연결(칸 → 수치 이름 또는 고정 값) | `ModuleBundleData`, `ModuleNodeData`, `ValueLinkData` |
| **수치 묶음** | 숫자. `객체.이름 = 값` (예 `Bolt.Damage = 25`) | `ValueBundleData`, `ValueData`, `ModifierData` |

연출 리소스(모션 클립·효과·소리 이름)는 연출 키 하나에 스킨별 한 행인 `PresentationResourceData`에 따로 둡니다.

모듈 묶음에는 종류가 있습니다. 데모에서는 `Skill`(시전), `Effect`(생성되는 투사체·판정 영역), `Buff`(걸리는 상태)입니다. 종류 이름과 종류마다 쓸 수 있는 이벤트는 모듈 사전이 정합니다.

## 2. 노드 = 이벤트 + 모듈

노드 한 행은 "이 이벤트가 오면 이 모듈을 실행한다"입니다.

- **이벤트**: 시작 이벤트(`CastStart`), **시각 이벤트**(`Moment`·`Alarm`, 이벤트 값 = 시각), **출구 이벤트**(`CheckPass`·`CheckFail`, 발생 노드 = 통과/실패를 낸 노드), **구독 이벤트**(`OnHurt` 등, 이벤트 값 = 주체), 그 밖의 이벤트(`Create`·`Hit`·`StatusApply` …). 어느 이벤트가 어느 의미인지는 모듈 사전의 `startEvent`·`timedEvents`·`exitEvents`·`subscriptionEvents`가 정합니다.
- **모듈**: 칸(slot) 목록을 가진 동작 단위. 칸마다 자료형(`time`·`float`·`int`·`enum:SHAPE`·`key:moduleBundle` …), 단위, 역할(밸런스/구조), 기본값(없으면 필수), 실행기가 지원하는 값이 있습니다.
- **순서**: 같은 이벤트 안에서는 시각 → 우선순위(작은 수 먼저) → `Order` → 노드 ID.
- **판정 줄 / 연출 줄**: 노드마다 `Line`이 `Logic` 또는 `Presentation`입니다. 연출 노드는 판정 이벤트를 낼 수 없고, 연출 줄이 볼 수 있는 이벤트는 사전의 `presentationEvents`로 제한됩니다. 내보낼 때 두 줄은 다른 파일이 됩니다.

편집기는 같은 이벤트·시각·발생 노드를 가진 노드들을 **열(column)** 하나로 보고, 판정 그래프(위)와 연출 그래프(아래)를 같은 열 위치에 맞춰 그립니다.

## 3. 오버라이드 층

묶음은 `ParentKey`로 부모를 가리킵니다. 펼칠 때는 뿌리에서 내려오며 층마다 다음을 적용합니다.

| 자식 층의 노드 행 | 뜻 |
|---|---|
| `Override = +` | 이 층이 더한 노드(노드 ID가 부모에 없어야 함) |
| `Override = -` | 물려받은 노드를 이 층에서 뺌(그 노드의 칸 행도 함께) |
| 표시 없음 | 물려받은 노드의 시각·순서·우선순위만 덮어씀. 이벤트·모듈은 덮어쓸 수 없음(`−` 후 `+`) |

| 자식 층의 값 연결 행 | 뜻 |
|---|---|
| `Ref` / `Fixed` | 그 칸을 이 층에서 정함 |
| `Default` | 부모가 정한 값을 버리고 모듈 기본값으로 되돌림 |

수치 묶음도 부모를 가리키고, 같은 이름은 자식 값이 이깁니다. 기술 3단계는 보통 "공통 바탕 → 1단계 기술 → 2·3단계"처럼 모듈 묶음과 수치 묶음을 각각 상속합니다. 층 수와 이름은 설정의 `layers`가 정합니다(기본 3층).

편집기에는 이 구조가 그대로 보입니다. 물려받은 노드에는 정한 층 꼬리표가 붙고, 이 층이 더한 노드는 `＋`, 뺀 노드는 반투명 카드로 같은 자리에 남습니다. "부모에 적용"은 자식 층의 변경을 부모 층으로 옮기고, 실행 전에 부모를 쓰는 모든 기술을 영향 목록으로 보여 줍니다.

## 4. 칸마다 출처 하나

펼친 결과에서 칸 하나는 출처를 정확히 하나 가집니다.

- `← Bolt.Damage` — 주인의 수치 묶음 사슬에서 찾는 값(밸런스 칸은 이쪽을 권장)
- `■ Box` — 그래프에 고정한 값(구조 칸: 열거값·다른 묶음 Key 등)
- `· 0 (기본값)` — 모듈 선언의 기본값

같은 효과 묶음을 여러 기술이 만들 때, 수치 참조는 **그 효과를 만든 주인의 수치 묶음**에서 풉니다. 그래서 투사체 바탕 하나를 두고 기술마다 `Bolt.Damage`만 다르게 둘 수 있습니다.

## 5. 검증

검증은 모든 층을 펼친 결과를 봅니다. 검사 코드 예:

| 범주 | 코드 |
|---|---|
| 표·머리말 | `HeaderMismatch` `FormulaNotAllowed` `KeyBand` `EnumName` `DuplicateKey` |
| 상속 | `InheritanceCycle` `InheritanceDepth` `OverrideKindChange` `OverrideRemoveMissing` `FromNodeRemoved` |
| 사전 대조 | `EventForKind` `EventForModule` `LineMismatch` `RequiredSlot` `SlotEnum` `RefNotAllowed` `UnsupportedValue` `UnimplementedModule` |
| 연결 | `MissingValueName` `SpawnNotEffect` `SpawnCycle` `MetaBundleKind` `CompanionMissing` |
| 순서·시각 | `OrderTie` `OffGrid`(경고) `FixedTiming`(경고) `FixedBalance`(경고) `UnusedValue`(경고) |
| 키프레임 | `KeyframeMissing` `KeyframeMotionStart` `KeyframeOffsetNegative` `KeyframeUnreachable` |

제품 모드(게임 데이터 만들기)는 실행기가 없는 모듈·지원하지 않는 값도 오류로 막고, 연습 공간의 주인 번호를 쓴 Key를 막습니다. 게임 고유의 규칙은 설정의 `plugins.validate`(Python 함수)로 더합니다.

## 6. Excel 왕복

Excel이 원본이고 편집기는 Excel을 고쳐 씁니다.

1. **읽기**: 표 등록부(`tables.json`)의 머리말 7줄을 대조하고, 칸 형식을 검사해 행으로 읽습니다. `!`로 시작하는 시트는 메모로 보고 읽지 않습니다.
2. **편집**: 편집기는 행을 직접 바꾸지 않고 **편집 동작 목록**을 보냅니다. 백엔드가 처음 읽은 행에 동작을 차례로 적용하고 다시 펼쳐 검증합니다.
3. **변경 목록**: 처음 읽은 행과 편집한 행의 차이(칸 변경 · 새 행 · 지운 행)를 계산해 저장 창에 보입니다.
4. **쓰기**: 잠금(Excel 소유자 파일·쓰기 열기)과 "읽은 뒤 바뀜"(SHA-256)을 검사 → 바뀐 칸만 고치고, 새 행은 그 묶음 마지막 행 바로 아래에 끼우고(위 행 서식 복사), 지운 행은 아래 행을 올림 → 임시 파일 → 직전 사본 1개 → 교체 → **다시 읽어 편집 결과와 같은지 대조**(다르면 직전 사본으로 되돌림).

편집하지 않은 칸·시트·서식은 그대로 남습니다(시험으로 확인). 접힘·구간 이름 띠·노드 메모·조합·빈 열은 데이터가 아니므로 Excel 대신 **보조 파일**(JSON 하나)에 둡니다.

**연습 공간**은 제품 통합 문서의 사본 폴더입니다. 거기서 새로 만드는 기술은 예약된 주인 번호를 받고, 제품 모드 검증은 그 번호를 오류로 막습니다.

## 7. 키프레임 시각

시각 이벤트 값에 `@{모션 노드 ID}:{키프레임 이름}`을 적을 수 있습니다(뒤에 `+0.05`처럼 오프셋 가능).

- 키프레임은 `.anim` 안 애니메이션 이벤트입니다. 함수 이름은 설정의 `keyframes.function`(기본 `SkillKeyframe`), 문자열 인자가 키프레임 이름(영문자·숫자·밑줄)입니다.
- 모션 노드의 연출 키 → 연출 리소스 표의 기본 스킨 `Clip` → `.anim` 파일을 찾고, 프레임을 시전 시각으로 바꿉니다:
  `시각 = 모션 시작 + 키프레임 클립 시각 ÷ (재생 속도 × 클립 길이)`.
- 내보낸 판정·연출 JSON에는 숫자로 들어갑니다. 실행기는 키프레임을 알 필요가 없습니다.
- 모션이 발사 뒤에 시작하거나 속도를 수치로 참조하면 시각을 하나로 정할 수 없어 오류입니다. 반복 구간 뒤의 키프레임, 0초 앞으로 가는 오프셋도 오류입니다.

## 8. 모듈 표시 정보

판정에 쓰지 않는 글자(이름·메모·칸 설명)와 화면 규칙은 **표시 정보**에 따로 둡니다. 사전 지문(hash)에 들어가지 않으므로 문구를 고쳐도 데이터가 바뀌지 않습니다.

- **계열**: 노드 머리 띠의 색·아이콘(시전·흐름, 생성·수명, 이동, 판정·피해, 상태·버프, 연출).
- **단계(stage)**: 첫 단계는 늘 보이고, 나머지는 칩(`▸ 고급 2`)으로 접힙니다. 정하지 않은 단계는 `＋Box`처럼 빈 칩입니다. 카드는 기본 5줄까지만 칸을 보입니다.
- **종류 칸 규칙**: 종류 칸 값(예 `Shape = Box`)에 따라 보이는 칸이 바뀝니다. 노드 머리에 `▸ Box`가 붙습니다.
- **택1·미실행 칸**: 여러 칸 중 하나만 쓰는 묶음, 실행기가 아직 읽지 않는 칸 표시.
- 글자는 영어/한국어 두 가지를 함께 둡니다(`{"en":…, "ko":…}`).

## 9. 설정과 확장 지점

호스트 게임마다 다른 값은 모두 `skillgraph.json`에 둡니다([목록](data-contract.md#설정-skillgraphjson)).

| 바꿀 수 있는 것 | 예 |
|---|---|
| Key 자릿수와 대역 | 대역 3 + 주인 2 + 일련 2, 기술 210·버프 211·모듈 묶음 220 … |
| 층 | 이름·깊이, 층마다 노드 ID 시작값 |
| 이벤트 의미 | 시작·시각·출구 이벤트 |
| 시간 축 | 단위(`s`, `turn`)·격자(0.025, 1, 없음) |
| 묶음 종류·주인 표 | `Skill`/`Effect`/`Buff` 대신 `Action`/`Summon`/`Status` |
| 표 등록부 | 열 추가·시트 이름·문서 이름 |
| 규칙 | "Hit 노드가 있는 Effect는 HitShape가 필요" 같은 동반 모듈 규칙 |
| 플러그인 | `plugins.validate`(검사 추가), `plugins.export`(내보낸 JSON 보강) |

턴제 시험(`tests/test_turn_based.py`)은 이벤트·종류·Key 자릿수(3+1+3)·두 층·1턴 격자를 모두 바꾼 설정으로 같은 코어를 돌립니다.

## 10. 런타임

- `ModuleSet`(C#)은 호스트가 선언하는 모듈 사전입니다. `ExportJson()`이 Python과 같은 바이트를 쓰고, `Hash`가 판정 JSON의 `dictionaryHash`와 같아야 불러옵니다.
- `SkillData.Load()`는 판정 JSON을 노드·칸 단위로 묶습니다. 칸 값은 주인의 수치 표(수정자 적용 뒤)에서 읽습니다.
- `SkillRunner`는 데모용 실행기입니다. 시작 이벤트를 내고, 시각 노드를 시간에 맞춰 실행하고, 출구 모듈이 `Pass`/`Fail`을 돌려주면 출구 이벤트를 냅니다. 모듈의 실제 동작은 호스트의 `IModuleHandler`가 맡습니다.

## 11. 화면 원칙

편집기 화면은 다음 원칙을 따릅니다.

- 위 = 판정, 아래 = 연출. 같은 열 위치, 가로 이동·확대 연동.
- 노드 카드는 칸을 적게: 정한 칸과 필수 칸만, 나머지는 `▸ 기본값 N개`·단계 칩.
- 출처 기호: `←` 수치 참조(초록) · `■` 고정(파랑) · `▲` 밸런스 칸 고정(주황, 수치 참조 권장) · `·` 기본값(회색) · `!` 필요(빨강).
- 검사 출구 열은 검사 노드 옆에 계단처럼 붙고, 통과(●)·실패(○) 선이 이어집니다.
- 묶음 제목 = 기획 문장(그룹 설명 열), 구간 이름 띠로 열 여러 개를 묶어 이름 붙임.
- 효과·버프 묶음은 접힌 한 줄 상자로 오른쪽에 쌓고, 생성 선을 따라 펼침.
- 겹침 없는 자동 배치(크기가 고정 값이라 계산만으로 정해짐, EditMode 시험이 모든 기술에 대해 확인).
