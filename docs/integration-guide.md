# 다른 액션 게임에 붙이기

> **Summary** — A worked example of adding SkillGraph Kit to a new action game, in order: settings, C# module declarations,
> workbooks, the runtime handler and one validation plugin. *Sample Brawler* is a made-up 2D brawler used only for this guide.

이 문서는 지어낸 2D 횡스크롤 격투 액션 게임 **Sample Brawler**에 SkillGraph Kit을 붙이는 가장 작은 예입니다. 실제 게임에 쓰인 사례가 아니라 설명을 위해 만든 예입니다. 데모보다 작게 **이벤트 3개 · 모듈 3개 · 기술 2개**만 씁니다. README [내 프로젝트에 붙이기](../README.md#내-프로젝트에-붙이기)의 단계를 이 예로 풀어 썼습니다.

게임마다 다른 것은 세 곳에만 둡니다. 코어(패키지)는 고치지 않습니다.

| 순서 | 곳 | 누가 | 이 예에서 |
|---|---|---|---|
| 1 | 설정 `skillgraph.json` | 프로그래머 | 통합 문서 폴더, 상속 2층, 검사 플러그인 |
| 2 | 모듈 선언(C#) | 프로그래머 | 이벤트 3개, 모듈 `Lunge` · `Hitbox` · `Damage` |
| 3 | 통합 문서(Excel) | 기획자 | 기술 `Jab` · `Lunge Punch`와 수치 |
| 4 | 실행기 연결(C#) | 프로그래머 | 모듈마다 게임 동작 한 줄 |
| 5 | 검사 플러그인(Python) | 프로그래머 | 피해가 40을 넘으면 경고 |

이 문서의 설정 · 선언 · 표 · 처리기 · 플러그인은 0.5.0의 Python 백엔드와 C# 런타임으로 그대로 돌려 확인했습니다(검증 오류 0건, 아래 [실행 순서](#4-실행기-연결)대로 노드가 돎). Unity 화면 부분은 빼고 확인했고, 저장소의 자동 시험에는 들어 있지 않습니다.

## 만들 기술

| 기술 | Key | 동작 | 수치 |
|---|---|---|---|
| Jab | 2100101 | 0.1초에 판정 영역을 켜고, 맞으면 피해 | 피해 6 · 거리 0.9m |
| Lunge Punch | 2100102 | 시작하자마자 앞으로 2.5m 돌진, 0.2초에 판정 영역, 맞으면 피해 | 피해 14 · 거리 1.4m |

"맞으면 피해"는 두 기술이 같으므로 **공통 바탕** 묶음에 한 번만 두고 두 기술이 이어받습니다. 기획자는 기술마다 다른 수치와 노드만 적습니다.

## 0. 패키지 넣기

`Packages/manifest.json`의 `dependencies`에 패키지를 더합니다([README](../README.md#내-프로젝트에-붙이기) 1단계). 편집기를 쓰려면 Python 3.10 이상과 openpyxl 3.1이 필요하고, **Edit › Preferences › SkillGraph Kit** 의 *Python* 칸에 그 `python.exe`를 고릅니다. 게임 실행(판정 JSON 읽기)에는 Python이 필요 없습니다.

## 1. 설정

프로젝트 폴더(Assets 옆)에 `skillgraph.json`을 둡니다. 적지 않은 키는 모두 기본값입니다.

```json
{
 "language": "ko",
 "dataDir": "SkillData",
 "layers": ["Base", "Skill"],
 "nodeIdStart": [1, 10],
 "plugins": {"paths": ["Tools/SkillGraph"], "validate": ["brawler_rules:damage_cap"]}
}
```

| 키 | 뜻 |
|---|---|
| `language` | 검증 메시지와 시트 이름을 한국어로 |
| `dataDir` | 통합 문서 폴더(Unity가 가져오지 않게 Assets 밖) |
| `layers` · `nodeIdStart` | 상속을 2층(공통 바탕 → 기술)으로. 단계(1·2·3단계)가 없는 게임입니다. 층마다 새 노드 ID가 1, 10부터 |
| `plugins` | [5절](#5-검사-플러그인)의 게임 고유 검사. `paths`는 설정 파일 폴더 기준 |

Key 대역(기술 210 · 모듈 묶음 220 · 수치 묶음 230 …), 시간 격자(0.025초), 시작·시각 이벤트 이름 같은 나머지는 기본값 그대로입니다. 전체 목록은 [데이터 계약 — 설정](data-contract.md#설정-skillgraphjson)에 있습니다.

## 2. 모듈 선언

`ModuleSetProvider`를 상속한 클래스 하나에 이벤트 · 묶음 종류 · 모듈을 적습니다. 편집기가 이 클래스를 찾아 모듈 사전을 내보냅니다.

```csharp
using Noskang.SkillGraph;
using static Noskang.SkillGraph.ModuleSet;

namespace SampleBrawler
{
    // Sample Brawler의 모듈 사전: 이벤트 3개, 모듈 3개.
    public sealed class BrawlerModules : ModuleSetProvider
    {
        private const SlotRole B = SlotRole.Balance;
        private const ModuleLine Logic = ModuleLine.Logic;

        public override ModuleSet Build() => new ModuleSet.Builder()
            .Events("CastStart", "Moment", "Hit")
            .Kind("Skill", "CastStart", "Moment", "Hit")
            .Timed("Moment").Start("CastStart")
            .Module("Lunge", Logic, new[] { "CastStart", "Moment" },
                Slot("Distance", "float", "m", B, null, "cast"),
                Slot("Duration", "time", "s", B, 0.15, "cast"))
            .Module("Hitbox", Logic, new[] { "CastStart", "Moment" },
                Slot("Reach", "float", "m", B, null, "cast"),
                Slot("Active", "time", "s", B, 0.1, "cast"))
            .Module("Damage", Logic, new[] { "Hit" },
                Slot("Amount", "float", "", B, null, "hit"),
                Slot("Stun", "time", "s", B, 0, "hit"))
            .Build();
    }
}
```

- **이벤트**: `CastStart`(시전 시작, 시각 0) · `Moment`(값이 시각인 이벤트) · `Hit`(게임의 충돌 판정이 "맞았다"고 알림).
- **묶음 종류**: `Skill` 하나에 세 이벤트를 모두 허용합니다. 투사체 · 버프처럼 따로 사는 묶음이 필요하면 `Kind("Effect", …)` · `Kind("Buff", …)`를 더합니다(데모처럼).
- **칸**: `Slot(이름, 형식, 단위, 역할, 기본값, 읽는 때)`. 기본값이 `null`이면 꼭 정해야 하는 칸입니다. 역할 `Balance`는 수치 표에서 값을 받는 것이 권장되는 칸입니다.
- 편집기에 보일 이름 · 한국어 설명 · 색은 `BuildDisplay`(선택)에 둡니다. 예는 데모의 [`DemoModules.cs`](../DemoProject/Assets/SkillGraphDemo/Scripts/DemoModules.cs).

선언을 고칠 때마다 **Tools › SkillGraph Kit › Export Module Dictionary** 로 `SkillData/Generated/module_dictionary.json`을 다시 냅니다.

## 3. 통합 문서

통합 문서 세 개(`Skills.xlsx` · `ModuleBundles.xlsx` · `PresentationResources.xlsx`)는 데모를 본떠 만드는 것이 가장 빠릅니다. 데모 데이터를 임시 폴더에 만들고 `Data/*.xlsx`만 `SkillData/`로 복사한 뒤 행을 지웁니다([README](../README.md#내-프로젝트에-붙이기) 5단계). 머리말 7줄과 열 이름은 그대로 둡니다.

그다음 아래 행을 적습니다(표에는 주요 열만 적었습니다). 행을 적은 뒤에는 그래프 편집기에서 열어 고칠 수 있습니다.

**모듈 묶음**(`ModuleBundles.xlsx`)

| 묶음 Key | 종류 | 부모 | 설명 |
|---|---|---|---|
| 2200001 | Skill | | 공통 바탕: 맞으면 피해 |
| 2200101 | Skill | 2200001 | Jab |
| 2200102 | Skill | 2200001 | Lunge Punch |

| 묶음 | 노드 | 이벤트 | 이벤트 값 | 모듈 | Override |
|---|---|---|---|---|---|
| 2200001 | 1 | Hit | | Damage | |
| 2200101 | 10 | Moment | `Hit.Time` | Hitbox | `+` |
| 2200102 | 10 | CastStart | | Lunge | `+` |
| 2200102 | 11 | Moment | `Hit.Time` | Hitbox | `+` |

| 묶음 | 노드 | 칸 | 출처 | 값 |
|---|---|---|---|---|
| 2200001 | 1 | Amount | Ref | `Hit.Damage` |
| 2200101 | 10 | Reach | Ref | `Hit.Reach` |
| 2200102 | 10 | Distance | Ref | `Punch.Distance` |
| 2200102 | 11 | Reach | Ref | `Hit.Reach` |

시각(`Hit.Time`)도 숫자 대신 수치 이름으로 적습니다. 숫자를 바로 적으면 검증이 `FixedTiming` 경고로 수치 참조를 권합니다.

**기술과 수치**(`Skills.xlsx`)

| 기술 Key | 이름 | 모듈 묶음 | 수치 묶음 |
|---|---|---|---|
| 2100101 | Jab | 2200101 | 2300101 |
| 2100102 | Lunge Punch | 2200102 | 2300102 |

| 수치 묶음 | 부모 | 수치 |
|---|---|---|
| 2300001 | | `Hit.Reach = 0.9` · `Hit.Time = 0.1` |
| 2300101 | 2300001 | `Hit.Damage = 6` |
| 2300102 | 2300001 | `Hit.Damage = 14` · `Hit.Reach = 1.4` · `Hit.Time = 0.2` · `Punch.Distance = 2.5` |

Jab은 거리와 시각을 공통 바탕에서 이어받고, Lunge Punch는 같은 이름을 다시 적어 덮어씁니다. 연출 리소스 표(`PresentationResources.xlsx`)는 이 예에서 비워 둡니다.

검증은 편집기 **More(더 보기) › Build skill data** 나, 패키지의 `Tools~/python` 폴더에서 이렇게 돌립니다.

```bash
python -X utf8 -m skillgraph check --config <프로젝트>/skillgraph.json
```

## 4. 실행기 연결

내보낸 판정 JSON을 게임이 읽습니다. 내보내기는 **More › Build skill data** 로 폴더를 고르거나, 명령줄 `python -X utf8 -m skillgraph export --config <…> --out <프로젝트>/Assets/StreamingAssets/SkillData`로 합니다(`skills.manifest.json`과 판정 · 연출 JSON, 세 파일).

```csharp
using System.IO;
using System.Linq;
using Noskang.SkillGraph;
using UnityEngine;

namespace SampleBrawler
{
    public sealed class BrawlerSkills : MonoBehaviour, IModuleHandler
    {
        public Fighter fighter;            // 게임 쪽 캐릭터 코드(이 문서에는 없음)
        private SkillRunner runner;

        private void Start()
        {
            var set = new BrawlerModules().Build();
            var folder = Path.Combine(Application.streamingAssetsPath, "SkillData");
            var manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder, "skills.manifest.json")));
            var presentation = JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder, manifest["presentationFile"].AsText())));
            var names = presentation["valueNames"].AsArray().Select(x => x.AsText()).ToList();
            var data = SkillData.Load(File.ReadAllBytes(Path.Combine(folder, manifest["logicFile"].AsText())), set, names);
            runner = new SkillRunner(data, this);
        }

        private void Update() => runner.Tick(Time.deltaTime);

        public SkillInstance Cast(int skillKey) => runner.Start(skillKey);           // 예: 2100102 = Lunge Punch
        public void OnHitboxTouched(SkillInstance cast) => runner.Raise(cast, "Hit"); // 게임의 충돌 판정이 부름

        public ModuleResult Run(ModuleCall call)
        {
            switch (call.Node.Module.Name)
            {
                case "Lunge":  fighter.Lunge((float)call.Number("Distance"), (float)call.Number("Duration")); break;
                case "Hitbox": fighter.OpenHitbox(call.Instance, (float)call.Number("Reach"), (float)call.Number("Active")); break;
                case "Damage": fighter.DealDamage((float)call.Number("Amount"), (float)call.Number("Stun")); break;
            }
            return ModuleResult.Continue;
        }
    }
}
```

- `SkillData.Load`는 판정 JSON의 사전 지문이 지금 선언과 같을 때만 읽습니다. 선언을 고쳤으면 사전과 데이터를 다시 냅니다.
- `call.Number("Reach")`는 노드가 정한 출처(수치 표 · 고정 값 · 기본값)에서 그 기술의 값을 꺼냅니다. 처리기는 값이 어디서 왔는지 몰라도 됩니다.
- `Hit`은 게임이 알립니다. 판정 영역이 상대에 닿으면 `OnHitboxTouched`를 부르면 됩니다.

Lunge Punch를 쓰고 0.3초에 맞혔을 때 처리기가 받는 순서입니다(확인 실행 기록).

```text
t=0.000  CastStart  Lunge   Distance=2.5  Duration=0.15
t=0.200  Moment     Hitbox  Reach=1.4     Active=0.1
t=0.300  Hit        Damage  Amount=14     Stun=0
```

`SkillRunner`는 데모용 실행기입니다(한 사람, 프레임 단위, 결정성 · 네트워크 없음). 서버나 자기 실행기를 쓰는 게임은 같은 `SkillData`의 노드를 자기 순서로 돌립니다([설계 10절](architecture.md#10-런타임)).

## 5. 검사 플러그인

게임만의 규칙은 Python 함수 하나로 덧붙입니다. 코어 검증이 끝난 뒤 불리고, `ctx['vals']`에 수치 묶음마다 이어받기까지 펼친 값이 들어 있습니다.

`Tools/SkillGraph/brawler_rules.py`

```python
CODES = {'DamageCap': ('warning', 'Damage over 40: {name} = {value}', '피해가 40을 넘습니다: {name} = {value}')}


def damage_cap(ctx):
    for bundle, values in ctx['vals'].items():
        for name, (value, _) in values.items():
            if name.endswith('.Damage') and isinstance(value, (int, float)) and value > 40:
                ctx['report'].w('DamageCap', dict(values=bundle, name=name), name=name, value=value)
```

Lunge Punch의 `Hit.Damage`를 45로 올리면 검증 결과에 경고가 한 줄 붙습니다.

```text
DamageCap | 수치 묶음 2300102 수치 Hit.Damage | DamageCap {'name': 'Hit.Damage', 'value': 45}
```

- `ctx['report'].w`는 경고, `.e`는 오류입니다. 오류가 있으면 데이터를 내보내지 않습니다.
- `CODES` 표는 `python -m skillgraph codes --config <…>`가 내는 검증 코드 목록(수준 · 영/한 문장)에 들어갑니다. 검사 결과 줄에는 위처럼 코드 이름과 값이 나옵니다.
- 내보낸 JSON에 게임 값을 덧붙이려면 `plugins.export`(함수 하나: `logic, pres, ctx`)를 씁니다.

## 그다음

| 하고 싶은 것 | 할 일 |
|---|---|
| 투사체 · 장판처럼 따로 사는 효과 | `Kind("Effect", "Create", "Hit", …)`와 효과를 만드는 모듈을 선언하고, 설정 `modules.spawn`이 그 모듈 · 칸을 가리키게 함(예 `{"module": "SpawnHitbox", "slot": "Hitbox"}`). 데모의 `SpawnEffect`가 예 |
| 1·2·3단계 기술 | `layers`를 3층으로(기본값). 단계는 기술 메타의 `TierGroup` · `Tier` 열 |
| 콤보 수 · 스택 | 보드 키([README](../README.md#내-프로젝트에-붙이기) 7단계, [설계 12절](architecture.md#12-보드-키)) |
| 애니메이션에 맞춘 타격 시각 | 시각 이벤트 값에 `@노드:키프레임`([설계 7절](architecture.md#7-키프레임-시각)) |
| 모션 · 소리 · 효과 그림 | 연출 줄 모듈(`ModuleLine.Presentation`)과 연출 리소스 표 |
| 턴제처럼 시간 축이 다른 게임 | 이벤트 · 시간 단위 · 격자를 설정으로 바꿈(시험 `tests/test_turn_based.py`) |
