# 兵临城下 —— 难度选择设计（2026-11-02）

状态：设计已经用户批准（三个关键决策均为用户选项），**尚未实现**。
分支：`feat/difficulty`（从 main `9910e9b` 切出）。
前置：四项长期挂账已于同日验收完毕（`../reports/2026-11-02-rts-deferred-acceptance.md`），全量测试基线 **252/252**。

## 1. 目标与范围

在人机模式入口提供 **简单 / 普通 / 困难** 三档电脑难度，默认普通。
普通档必须与当前行为**完全等价**（这是可测试的硬指标，不是口号）。

用户已定的三条边界：

1. **只动 AI**，玩家侧资源、造价、数值一律不变。
2. 强度用**温和档**（数值见下表）。
3. 难度**写入存档**、**局内不可修改**（暂停菜单不提供切换）。

不在本次范围：战役/对战两张锁定卡、主菜单过期的「1~6 建造」文案、四条 GUI 观感 Minor、音效（用户已决定不做）、任何 Phase B 平衡重调。

## 2. 现场实测（2026-11-02 只读回读，不是源码默认值）

`SampleScene` 中 `AIController`(Red) 的**序列化实际值**：

```
startGold/Wood/Meat = 600/300/200
firstAttack  = 120     attackEvery  = 45
firstProduce = 20      produceEvery = 12
maxAttackers = 5       maxGatherers = 5
firstBuildDelay = 6    active = True
```

玩家起始 600 金 / 300 木 / 200 肉（不变）。

> ⚠️ 这些值与 `AIController.cs` 里的 C# 默认值（45/38/6/9…）**不同**。难度缩放必须基于**实例当前序列化值**，否则会把 Phase B 调好的平衡一次抹掉。

`ModeSelect.scene` 实测布局（与 `BuildModeSelect.cs` 源码里的旧坐标也不同，以场景为准）：

- `Board`：anchoredPos `(0,-10)`，size `1400×700` → 板内 y 范围 `-360..+340`
- 三张卡片 `Human/Campaign/Versus`：x = `-440/0/+440`，**y = `-40`**，size `400×440` → 卡片底边 `y=-260`
- 因此板内底部存在一条 **`y=-260..-360` 的空带（100px）**，难度行放这里
- `Back`（返回主菜单）在 **Canvas** 底部锚点 y=32，不在 Board 内，与难度行天然不重叠
- 场景 MD5（改动前基线）：`56e86ab40659949cd117d57b2f825a2c`，字节备份 `.superpowers/sdd/difficulty/modeselect-before.scene`

## 3. 数值（温和档）

所有时间乘在实例当前值上；人数用**增减**而不是绝对值。

| 项 | 简单 | 普通 | 困难 |
|---|---|---|---|
| `firstAttack` | ×1.5 → **180s** | 120s | ×0.8 → **96s** |
| `attackEvery` | ×1.3 → **58.5s** | 45s | ×0.85 → **38.25s** |
| `firstProduce` | ×1.3 → **26s** | 20s | ×0.85 → **17s** |
| `produceEvery` | ×1.3 → **15.6s** | 12s | ×0.85 → **10.2s** |
| `startGold/Wood/Meat` | ×0.75 → **450/225/150** | 600/300/200 | ×1.25 → **750/375/250** |
| `maxAttackers` | −1 → **4** | 5 | +1 → **6** |

不变：`maxGatherers`、`firstBuildDelay`、建造顺序、兵种配比权重、AI 科技研究节奏、玩家一切数值。

## 4. 组件设计

### 4.1 新增 `Assets/Scripts/Difficulty.cs`（纯静态，无场景对象）

```csharp
public static class Difficulty
{
    public enum Level { Easy = 0, Normal = 1, Hard = 2 }   // 序号入档，永不重排
    public static Level Current = Level.Normal;            // 会话状态，跨场景存活
    public static string DisplayName(Level level);          // 简单/普通/困难
    public static Level Clamp(int raw);                     // 越界/未知 → Normal（旧档兜底）
    public static void Apply(AIController ai);              // 见 4.2
}
```

倍率表以 `Level` 为键的只读结构保存；`Normal` 直接返回，**一个字段都不写**，从结构上保证普通档零改动。

### 4.2 `AIController` 改动（两处，共约 6 行）

- 新增实例字段 `[System.NonSerialized] bool difficultyApplied;`
- 新增 `void Awake() { Difficulty.Apply(this); }`

`Apply` 语义：

1. `ai == null || ai.difficultyApplied` → 直接返回（**幂等**，防双重缩放）
2. `Current == Normal` → 置标记后返回
3. 否则按表缩放 `firstAttack/attackEvery/firstProduce/produceEvery/startGold/startWood/startMeat`，`maxAttackers += delta`（下限 1），置标记

**时序依据**：`AIController.Start()` 把 `firstProduce/firstAttack` 拷进私有 `produceTimer/attackTimer`（`AIController.cs:165-175`），而 Unity 保证同场景对象的 `Awake` 全部先于 `Start`，所以在 `Awake` 应用即可让计时器拿到缩放后的值。**不允许**改成在 `Start` 里应用或在运行时改字段（运行中改 `firstAttack` 不影响当轮倒计时，这是已踩过的坑）。

**读档不双重缩放**：`RestoreSaveState` 只回写银行、`nextBuild/wave` 与四个计时器，不回写 `firstAttack/attackEvery`，所以 Awake 缩放过一次的字段继续生效，计时器以存档为准。场景重载会重新从原始序列化值出发，不会累积倍率。

### 4.3 `ModeSelectMenu` + 难度行 UI

`ModeSelectMenu` 新增：

```csharp
public Button[] difficultyButtons;      // 顺序 = Easy, Normal, Hard
public void SetDifficulty(int level);   // 设 Difficulty.Current 并刷新选中态
```

- `Start()` 里为三个按钮接 `SetDifficulty(0/1/2)`，并按 `Difficulty.Current` 初始化选中态（**从 ModeSelect 退回再进要保留上次选择**）
- `StartHuman()` 不变，仍 `LoadingScreen.Load(gameScene)`；难度由静态类携带进对局
- 选中态：选中的按钮换 `Button_Blue_9Slides_Pressed` 贴图（**文件名已核实存在于 `Assets/Art/UI/Buttons/`**，`IconSwap` 已在用同套）+ 文字奶油白；未选中保持原 `Button_Blue_9Slides`、文字略暗。**不靠颜色单独传达**：选中按钮正下方加一条 `96×4` 的金色 `Image` 小条（`SelectedMark`，随选中切换 `SetActive`）。**不要用「✓」「●」之类字符**——内置 `LegacyRuntime.ttf` 缺字会变方框，纯图形标记没有字体风险，且三按钮文字恒定、切换时不跳动

新增增量构建器 `Assets/Editor/BuildDifficultyRow.cs`，菜单 `Tools/UI/添加难度选择`：

- **find-or-create**，可重复运行且第二次报 `No changes needed`；**绝不调用 `BuildModeSelect.Build()` 从零重建**（会抹掉既有精修，历史教训）
- 在 `Board` 下建 `DifficultyRow`：anchoredPos `(0,-312)`，size `560×56`（占 y `-340..-284`，与卡片底 `-260` 留 24px、与板底 `-360` 留 20px）
- 内部：一个 `电脑难度` 标签（宽 110）+ 三个按钮（各 `120×56`，间距 16，22pt），按左到右 简单/普通/困难 排布
- 把三按钮按顺序写进 `ModeSelectMenu.difficultyButtons`
- 保存前后打印 MD5；无序列化变化时不存盘（沿用 `BuildTechPanel` 的"无改动不保存"做法）

### 4.4 存档

`SaveGameData` 增加 `public int difficulty = 1;`（默认普通）。

- `SaveGameService.Save`：写 `(int)Difficulty.Current`
- `RestoreSnapshot`：在**恢复任何 AI 之前**执行 `Difficulty.Current = Difficulty.Clamp(data.difficulty)`
- 版本号保持 `Version=2 / OldestReadableVersion=1` 不变；旧档缺该字段时 JSON 反序列化保留默认值 1=普通，无需迁移代码
- 读档路径上 `Difficulty.Current` 被存档覆盖，因此"困难档存、菜单里选简单再读"不会串难度

## 5. 测试（新增 `Assets/Tests/PlayMode/DifficultyTests.cs`）

| 用例 | 断言 |
|---|---|
| `Normal_LeavesEveryFieldUntouched` | 新 AI 实例 Awake 后六个时间/资源字段与人数**逐项等于**原始序列化值 |
| `Easy_ScalesPaceAndBank` | firstAttack = 原值×1.5、attackEvery×1.3、起始资源×0.75、maxAttackers−1 |
| `Hard_ScalesPaceAndBank` | 同上按困难倍率 |
| `Apply_Twice_DoesNotDoubleScale` | 二次 `Apply` 后所有字段不变 |
| `Start_TimersUseScaledValues` | Easy 下新 AI 的 `attackTimer == firstAttack`（反射读私有计时器） |
| `RestoreDifficulty_AppliesSavedValue_AndClamps` | 存档 difficulty=2 时即使菜单选了简单，恢复后也是 Hard；99 → Normal |
| `SaveData_DefaultDifficulty_IsNormal` | `new SaveGameData().difficulty == 1` |
| `LegacyJson_WithoutField_LoadsAsNormal` | 缺 difficulty 字段的 JSON（等价旧档）→ `Current == Normal` |
| `Clamp_UnknownValue_FallsBackToNormal` | 越界值 → Normal；`DisplayName` 返回 简单/普通/困难 |
| `ModeSelect_HasThreeWiredButtons_DefaultNormal` | 场景级：三按钮存在、顺序正确、默认选中普通 |
| `ModeSelect_RowFitsBoard_AndClearsCardsAndBack` | 难度行矩形在 Board 内、与三卡片及 Back **矩形不相交**（同级元素全覆盖，历史坑） |
| `SetDifficulty_PersistsAcrossMenuReentry` | 选困难 → 重进 ModeSelect → 仍选中困难 |

**测试条数**：11 条（数值/幂等/时序/钳制 6 + 存档 3 + 场景 UI 2），全量期望 252 + 11 = **263/263**。`SetDifficulty_PersistsAcrossMenuReentry` 与 UI 第一条的选中态断言重叠，实现时并入同一条；要严格覆盖"退回再进保留选择"可在该用例末尾再加载一次 ModeSelect。**端到端读档恢复（`RestoreSnapshot` 里那一行）只有手工探针与代码审查证据**——直接调用 `RestoreDifficulty` 的单测对"删掉那行"没有判别力，报告里必须照实写，不得宣称做过该行的变异证明。

**变异证明（硬规则，本项目的空转测试顽疾）**：①删掉 `Awake` 里的 `Apply` 调用 → Easy/Hard 单测必须变红；②读档恢复那一行的判别证明用**端到端手工探针**做（构造 difficulty=0 的存档走 `RestoreSnapshot`，正常应恢复成 Easy；删掉那行则停留在原档位），不伪装成单测。每次证明后**字节还原并核 MD5**，再复跑聚焦套件转绿。

**真实对局冒烟**：简单档实测 AI 首波出发 ≈180s、困难 ≈96s（探针记录首波时间戳，容差 ±5s），普通 ≈120s 作为对照。只读探针，不写用户存档；若必须写档，先备份 `%LOCALAPPDATA%Low/DefaultCompany/兵临城下/AtTheGates/save_slot_1.json` 并事后还原核 MD5。

**收尾**：全量套件（基线 252 + 新增）跑绿并**立刻归档日志**（`Logs/RegressionTests.txt` 会被后续聚焦运行覆盖）。

## 6. 风险把控

| 改动 | 收益 | 风险 | 缓解 | 回滚路径 |
|---|---|---|---|---|
| 写 `ModeSelect.scene` | 可选难度 | **中**：遮挡卡片/Back、误重建抹掉精修 | 增量构建器；放在实测空带；前后字节备份+MD5；对象级增量审计；截图目检；矩形不相交测试 | `python shutil.copy2` 用 `modeselect-before.scene` 还原并核 MD5 `56e86ab4…` |
| `AIController` 加 Awake | 难度生效 | **中**：晚于 Start 则不生效 | `Start_TimersUseScaledValues` 钉死 | `git checkout -- Assets/Scripts/AIController.cs` |
| 倍率缩放 | 保住 Phase B 平衡 | **高（已踩过）**：场景序列化覆盖导致不生效 | 从运行实例回读；不写绝对值 | 同上 |
| 重复 Apply / 读档 | 数值稳定 | **中**：双重缩放 | 实例标记 + 幂等测试 + 变异证明① | 同上 |
| 存档加字段 | 不串难度 | **中**：旧档兼容、恢复顺序 | 默认普通；恢复在 AI 之前；真实旧载荷测试 | `git checkout -- Assets/Scripts/SaveGameService.cs` |
| UI 选中态 | 可读性 | **低**：只靠颜色不可辨 | 贴图 + 文字双重编码 | 还原场景 |
| 测试本身 | 防回归 | **高（顽疾）**：空转假绿 | 两条变异证明 + 还原核 MD5 | — |
| 冒烟 | 实测节奏 | **高**：覆盖用户真实存档 | 探针不写档；必须写时先备份+还原核 MD5 | 备份还原 |
| 平衡 | 三档手感 | **中**：困难过强/简单过弱 | 倍率集中在 `Difficulty` 一处可调；首波时间冒烟 | 调倍率即可 |

**不可验证项（写进交付报告）**：真实键鼠点击手感；困难档长局平衡是否过强、简单档是否过空；这些需要用户实玩。

## 7. 交付清单（明天实现时逐项勾）

1. `Difficulty.cs`（新）、`AIController.cs`（Awake+标记）、`SaveGameService.cs`（字段+两处）
2. `ModeSelectMenu.cs`（按钮数组+SetDifficulty+选中态）
3. `BuildDifficultyRow.cs`（新，增量构建器）+ 写 `ModeSelect.scene`
4. `DifficultyTests.cs`（新，11 条）+ 变异证明日志（①单测、②端到端探针）
5. 三档首波时间冒烟记录、全量套件日志归档、ModeSelect 截图
6. 验收报告 + 断点更新；**不主动提交代码**，完成后由用户决定合并
