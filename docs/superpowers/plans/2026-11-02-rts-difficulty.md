# 难度选择实现计划（2026-11-02）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 ModeSelect 加「简单/普通/困难」三档电脑难度，只缩放 AI 节奏与起始资源，难度入档、局内不可改，普通档与现状完全等价。

**Architecture:** 静态 `Difficulty` 类持有档位与倍率；`AIController.Awake()` 在 `Start()` 拷贝计时器之前按倍率缩放实例字段（幂等）；ModeSelect 的 Board 底部空带加一行三按钮；`SaveGameData` 增加 `difficulty` 字段，读档在恢复 AI 之前覆盖当前档位。

**Tech Stack:** Tuanjie 2022.3.62t5、URP 14.1.0、UGUI（legacy Text，无 TMP）、Unity Test Framework / NUnit PlayMode、`TestRunnerApi`（`RegressionTestMenu`）、MCP Tuanjie bridge。

**设计文档（唯一事实来源）：** `docs/superpowers/specs/2026-11-02-rts-difficulty-design.md`
**断点：** `.superpowers/sdd/difficulty/RESUME.md`（每完成一个任务就更新）

## Global Constraints

- **只动 AI**：玩家资源、造价、单位数值一律不改。
- 三档数值（基于实例序列化值缩放，**不写绝对值**）：简单 firstAttack×1.5 / attackEvery×1.3 / firstProduce×1.3 / produceEvery×1.3 / 起始资源×0.75 / maxAttackers−1；普通**完全不变**；困难 ×0.8 / ×0.85 / ×0.85 / ×0.85 / ×1.25 / +1。
- 场景实测基线：`firstAttack=120 attackEvery=45 firstProduce=20 produceEvery=12 maxAttackers=5 startGold/Wood/Meat=600/300/200`。
- **禁止**重跑 `Tools/UI/生成三模式选择场景`（从零重建会抹掉既有精修）；只能用本计划的增量构建器。
- **场景风控闸**：写 `ModeSelect.scene` 前必须已给风险表（见 spec §6）、已做字节备份并记录 MD5。备份已存在：`.superpowers/sdd/difficulty/modeselect-before.scene`，MD5 `56e86ab40659949cd117d57b2f825a2c`。**本功能不写 `SampleScene`。**
- **绝不覆盖用户存档**：`%LOCALAPPDATA%Low/DefaultCompany/兵临城下/AtTheGates/save_slot_1.json` 有用户真实存档（MD5 `0d34d758ec3bd3eb7ee954eebf411076`）。任何写档探针先 `shutil.copy2` 备份、事后还原并核 MD5。
- 选中标记**不用字符**（内置 `LegacyRuntime.ttf` 缺字会变方框），用 `Image` 小条。
- 改完 `.cs` 后 `request_compile` 可能报 "No compilation was observed"：先 `AssetDatabase.ImportAsset(path, ForceUpdate)` + `AssetDatabase.Refresh()` 再编译；PlayMode 中不能编译。
- 全量日志 `Logs/RegressionTests.txt` 会被后续聚焦运行覆盖，**全量跑完立刻复制归档**。
- 基线：全量 **252/252**。新增测试 **11 条**（Task1 六条 + Task2 三条 + Task3 两条），完成后期望 **263/263**。
- 提交信息结尾带 `Co-Authored-By: Kimi <noreply@moonshot.ai>`。

## 工具命令（本工程真实可用形式）

```csharp
// 编译（execute_csharp_script，editor 模式）
AssetDatabase.Refresh();
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();

// 全量 PlayMode（editor 模式，跑完读 Logs/RegressionTests.txt）
RegressionTestMenu.RunPlayModeTests();

// 聚焦单条（fullName = 类名.方法名）
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Easy_ScalesPaceAndBank");
```

归档与 MD5（bash，用 python，不要用 `cp`，本机 `cp` 是交互别名）：

```bash
cd "/d/Unity/projects/At the Gates" && python -I -c "
import shutil,hashlib
shutil.copy2('Logs/RegressionTests.txt','.superpowers/sdd/difficulty/full-suite.log')
print(hashlib.md5(open('.superpowers/sdd/difficulty/full-suite.log','rb').read()).hexdigest())"
```

---

### Task 1: `Difficulty` 静态类 + `AIController` 接入

**Files:**
- Create: `Assets/Scripts/Difficulty.cs`
- Modify: `Assets/Scripts/AIController.cs`（字段区约 70 行处加标记；新增 `Awake`，放在 `OnEnable/OnDisable`（162-163）之后、`Start()`（165）之前）
- Test: `Assets/Tests/PlayMode/DifficultyTests.cs`

**Interfaces:**
- Consumes: `AIController` 的 public 字段 `firstAttack/attackEvery/firstProduce/produceEvery/startGold/startWood/startMeat/maxAttackers/active`。
- Produces: `Difficulty.Level { Easy=0, Normal=1, Hard=2 }`、`Difficulty.Current`、`Difficulty.DisplayName(Level)`、`Difficulty.Clamp(int)`、`Difficulty.Apply(AIController)`；`AIController.difficultyApplied`（public、NonSerialized）。Task 2/3 全部依赖这些名字。

- [ ] **Step 1: 写失败测试**（先建文件，编译后聚焦跑，预期编译错误 `Difficulty` 不存在）

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Difficulty only ever scales the AI. Normal must be a structural no-op.</summary>
public class DifficultyTests : RegressionFixture
{
    internal static AIController MakeAI()
    {
        // AddComponent runs Awake immediately, which would scale the C# default values
        // before we could set them. Build the object inactive, set the fields, then
        // activate so Awake sees exactly the values these tests assert against.
        var go = new GameObject("DifficultyAI");
        go.SetActive(false);
        var ai = go.AddComponent<AIController>();
        ai.active = false;                 // keep Update() out of the assertion
        ai.firstAttack = 120f; ai.attackEvery = 45f;
        ai.firstProduce = 20f; ai.produceEvery = 12f;
        ai.startGold = 600; ai.startWood = 300; ai.startMeat = 200;
        ai.maxAttackers = 5;
        go.SetActive(true);                // Awake (and Difficulty.Apply) runs here
        return ai;
    }

    [TearDown]
    public void ResetDifficulty()
    {
        Difficulty.Current = Difficulty.Level.Normal;
        foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
            if (ai != null && ai.name == "DifficultyAI") Object.DestroyImmediate(ai.gameObject);
    }

    [Test]
    public void Normal_LeavesEveryFieldUntouched()
    {
        Difficulty.Current = Difficulty.Level.Normal;
        var ai = MakeAI();
        Difficulty.Apply(ai);
        Assert.AreEqual(120f, ai.firstAttack);
        Assert.AreEqual(45f, ai.attackEvery);
        Assert.AreEqual(20f, ai.firstProduce);
        Assert.AreEqual(12f, ai.produceEvery);
        Assert.AreEqual(600, ai.startGold);
        Assert.AreEqual(300, ai.startWood);
        Assert.AreEqual(200, ai.startMeat);
        Assert.AreEqual(5, ai.maxAttackers);
    }

    [Test]
    public void Easy_ScalesPaceAndBank()
    {
        Difficulty.Current = Difficulty.Level.Easy;
        var ai = MakeAI();
        Difficulty.Apply(ai);
        Assert.AreEqual(180f, ai.firstAttack, 0.01f);
        Assert.AreEqual(58.5f, ai.attackEvery, 0.01f);
        Assert.AreEqual(26f, ai.firstProduce, 0.01f);
        Assert.AreEqual(15.6f, ai.produceEvery, 0.01f);
        Assert.AreEqual(450, ai.startGold);
        Assert.AreEqual(225, ai.startWood);
        Assert.AreEqual(150, ai.startMeat);
        Assert.AreEqual(4, ai.maxAttackers);
    }

    [Test]
    public void Hard_ScalesPaceAndBank()
    {
        Difficulty.Current = Difficulty.Level.Hard;
        var ai = MakeAI();
        Difficulty.Apply(ai);
        Assert.AreEqual(96f, ai.firstAttack, 0.01f);
        Assert.AreEqual(38.25f, ai.attackEvery, 0.01f);
        Assert.AreEqual(17f, ai.firstProduce, 0.01f);
        Assert.AreEqual(10.2f, ai.produceEvery, 0.01f);
        Assert.AreEqual(750, ai.startGold);
        Assert.AreEqual(375, ai.startWood);
        Assert.AreEqual(250, ai.startMeat);
        Assert.AreEqual(6, ai.maxAttackers);
    }

    [Test]
    public void Apply_Twice_DoesNotDoubleScale()
    {
        Difficulty.Current = Difficulty.Level.Easy;
        var ai = MakeAI();
        Difficulty.Apply(ai);
        Difficulty.Apply(ai);
        Assert.AreEqual(180f, ai.firstAttack, 0.01f, "Second Apply must be a no-op.");
        Assert.AreEqual(450, ai.startGold);
        Assert.AreEqual(4, ai.maxAttackers);
    }

    [UnityTest]
    public IEnumerator Start_TimersUseScaledValues()
    {
        Difficulty.Current = Difficulty.Level.Easy;
        var ai = MakeAI();
        yield return null;      // Awake + Start have both run by now
        var timer = typeof(AIController).GetField("attackTimer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(timer, "AIController.attackTimer is the field Start() seeds.");
        Assert.AreEqual(ai.firstAttack, (float)timer.GetValue(ai), 0.01f,
            "Awake must scale before Start() copies firstAttack into attackTimer.");
        Assert.AreEqual(180f, (float)timer.GetValue(ai), 0.01f);
        Assert.AreEqual(450, ai.Gold, "Start() seeds the bank from the scaled startGold.");
    }

    [Test]
    public void Clamp_UnknownValue_FallsBackToNormal()
    {
        Assert.AreEqual(Difficulty.Level.Normal, Difficulty.Clamp(99));
        Assert.AreEqual(Difficulty.Level.Normal, Difficulty.Clamp(-1));
        Assert.AreEqual(Difficulty.Level.Hard, Difficulty.Clamp(2));
        Assert.AreEqual("简单", Difficulty.DisplayName(Difficulty.Level.Easy));
        Assert.AreEqual("普通", Difficulty.DisplayName(Difficulty.Level.Normal));
        Assert.AreEqual("困难", Difficulty.DisplayName(Difficulty.Level.Hard));
    }
}
```

- [ ] **Step 2: 编译并聚焦跑，确认失败**

```csharp
AssetDatabase.ImportAsset("Assets/Tests/PlayMode/DifficultyTests.cs", ImportAssetOptions.ForceUpdate);
AssetDatabase.Refresh();
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
```

Expected: 编译错误 `The type or namespace name 'Difficulty' could not be found`（证明测试真的在引用新类型）。

- [ ] **Step 3: 实现 `Difficulty.cs`**

```csharp
using UnityEngine;

/// <summary>
/// Skirmish difficulty. Only the AI is scaled — the player's economy, costs and unit
/// stats are identical on every level. Multipliers apply to the AI instance's own
/// serialized values (the scene overrides the C# defaults), never to absolute numbers,
/// so rebalancing the scene cannot be silently overwritten by a difficulty choice.
/// </summary>
public static class Difficulty
{
    public enum Level { Easy = 0, Normal = 1, Hard = 2 }   // ordinal is saved; never reorder

    /// <summary>Chosen on the mode-select screen. Static on purpose: it must survive the
    /// scene switch into the match, and a save load overwrites it.</summary>
    public static Level Current = Level.Normal;

    struct Pace
    {
        public float firstAttack, attackEvery, firstProduce, produceEvery, startResources;
        public int attackersDelta;
    }

    static Pace Of(Level level)
    {
        switch (level)
        {
            case Level.Easy:
                return new Pace { firstAttack = 1.5f, attackEvery = 1.3f, firstProduce = 1.3f,
                                  produceEvery = 1.3f, startResources = 0.75f, attackersDelta = -1 };
            case Level.Hard:
                return new Pace { firstAttack = 0.8f, attackEvery = 0.85f, firstProduce = 0.85f,
                                  produceEvery = 0.85f, startResources = 1.25f, attackersDelta = 1 };
            default:
                return new Pace { firstAttack = 1f, attackEvery = 1f, firstProduce = 1f,
                                  produceEvery = 1f, startResources = 1f, attackersDelta = 0 };
        }
    }

    public static string DisplayName(Level level)
    {
        switch (level)
        {
            case Level.Easy: return "简单";
            case Level.Hard: return "困难";
            default: return "普通";
        }
    }

    /// <summary>Old saves have no difficulty field and corrupt values are possible;
    /// both mean Normal, never an out-of-range enum.</summary>
    public static Level Clamp(int raw)
    {
        if (raw == (int)Level.Easy) return Level.Easy;
        if (raw == (int)Level.Hard) return Level.Hard;
        return Level.Normal;
    }

    /// <summary>Idempotent per AI instance. Called from AIController.Awake so Start()
    /// seeds its timers and bank from the scaled values. A load restores the timers
    /// afterwards and never re-applies this, so there is no double scaling.</summary>
    public static void Apply(AIController ai)
    {
        if (ai == null || ai.difficultyApplied) return;
        ai.difficultyApplied = true;
        if (Current == Level.Normal) return;     // structural no-op: write nothing at all

        var p = Of(Current);
        ai.firstAttack *= p.firstAttack;
        ai.attackEvery *= p.attackEvery;
        ai.firstProduce *= p.firstProduce;
        ai.produceEvery *= p.produceEvery;
        ai.startGold = Mathf.RoundToInt(ai.startGold * p.startResources);
        ai.startWood = Mathf.RoundToInt(ai.startWood * p.startResources);
        ai.startMeat = Mathf.RoundToInt(ai.startMeat * p.startResources);
        ai.maxAttackers = Mathf.Max(1, ai.maxAttackers + p.attackersDelta);
    }
}
```

- [ ] **Step 4: 接入 `AIController`**

在私有计时器声明（约 70 行）附近加：

```csharp
    /// <summary>Set by Difficulty.Apply so a second call (or a test) cannot scale twice.
    /// Not serialized: a fresh scene load must start from the authored values again.</summary>
    [System.NonSerialized] public bool difficultyApplied;
```

在 `OnDisable()` 之后、`void Start()` 之前加：

```csharp
    void Awake()
    {
        // Must precede Start(): Start() copies firstProduce/firstAttack into the running
        // timers, so scaling afterwards would not affect the current countdown.
        Difficulty.Apply(this);
    }
```

- [ ] **Step 5: 编译并聚焦跑本任务 6 条**

```csharp
AssetDatabase.Refresh();
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
// 编译结束后
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Normal_LeavesEveryFieldUntouched");
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Easy_ScalesPaceAndBank");
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Hard_ScalesPaceAndBank");
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Apply_Twice_DoesNotDoubleScale");
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Start_TimersUseScaledValues");
RegressionTestMenu.RunPlayModeTest("DifficultyTests.Clamp_UnknownValue_FallsBackToNormal");
```

Expected: 6 条全 PASS。**若 `Start_TimersUseScaledValues` 失败而其它通过 = Awake/Start 时序问题，先查时序，不要改测试数值。**

- [ ] **Step 6: 在真实场景回读（防"场景序列化覆盖导致不生效"的历史坑）**

editor 模式打开 `SampleScene` 后进 Play，跑：

```csharp
Difficulty.Current = Difficulty.Level.Easy;
foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
    Debug.Log("[DIFF] live " + ai.team + " firstAttack=" + ai.firstAttack + " attackEvery=" + ai.attackEvery
        + " gold=" + ai.Gold + " maxAttackers=" + ai.maxAttackers);
```

注意：`Difficulty.Current` 必须在**场景加载前**设置才能看到 180/450；在已运行的对局里设置不会改变当轮倒计时（已知行为，不是 bug）。正确做法是设置后 `LoadingScreen.Load("SampleScene")` 再读。预期日志 `firstAttack=180 attackEvery=58.5 gold=450 maxAttackers=4`；普通档应为 `120/45/600/5`。**看完退出 Play，不要保存场景。**

- [ ] **Step 7: Commit**

```bash
cd "/d/Unity/projects/At the Gates" && git add Assets/Scripts/Difficulty.cs Assets/Scripts/Difficulty.cs.meta Assets/Scripts/AIController.cs Assets/Tests/PlayMode/DifficultyTests.cs Assets/Tests/PlayMode/DifficultyTests.cs.meta && git commit -m "feat(difficulty): scale AI pace and bank by difficulty level" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 2: 难度入档

**Files:**
- Modify: `Assets/Scripts/SaveGameData.cs:9` 附近（加字段）
- Modify: `Assets/Scripts/SaveGameService.cs`（`CaptureSnapshot` 133 起的数据初始化；`RestoreSnapshot` 339 起）
- Test: `Assets/Tests/PlayMode/DifficultyTests.cs`（追加 3 条）

**Interfaces:**
- Consumes: `Difficulty.Current`、`Difficulty.Clamp(int)`。
- Produces: `SaveGameData.difficulty`（int，默认 1）、`SaveGameService.RestoreDifficulty(SaveGameData data)`（public static void，供 `RestoreSnapshot` 与测试共用）。

- [ ] **Step 1: 追加失败测试**

```csharp
    [Test]
    public void SaveData_DefaultDifficulty_IsNormal()
    {
        Assert.AreEqual(1, new SaveGameData().difficulty,
            "A save written before this feature must read back as Normal.");
    }

    [Test]
    public void RestoreDifficulty_AppliesSavedValue_AndClamps()
    {
        Difficulty.Current = Difficulty.Level.Easy;
        SaveGameService.RestoreDifficulty(new SaveGameData { difficulty = 2 });
        Assert.AreEqual(Difficulty.Level.Hard, Difficulty.Current,
            "The save wins over whatever the menu had selected.");
        SaveGameService.RestoreDifficulty(new SaveGameData { difficulty = 99 });
        Assert.AreEqual(Difficulty.Level.Normal, Difficulty.Current);
    }

    [Test]
    public void LegacyJson_WithoutField_LoadsAsNormal()
    {
        // Same shape as a pre-difficulty slot: the key is simply absent.
        var legacy = JsonUtility.FromJson<SaveGameData>("{\"version\":2,\"sceneName\":\"SampleScene\"}");
        Assert.IsNotNull(legacy);
        SaveGameService.RestoreDifficulty(legacy);
        Assert.AreEqual(Difficulty.Level.Normal, Difficulty.Current);
    }
```

- [ ] **Step 2: 编译并聚焦跑，确认失败**

Expected: 编译错误 `'SaveGameData' does not contain a definition for 'difficulty'` 与 `'SaveGameService' does not contain a definition for 'RestoreDifficulty'`。

- [ ] **Step 3: 实现**

`SaveGameData.cs` 在 `public int version = SaveGameService.Version;` 之后加：

```csharp
    /// <summary>Difficulty ordinal (<see cref="Difficulty.Level"/>). Defaults to Normal so a
    /// slot written before this feature loads unchanged; the version number stays at 2.</summary>
    public int difficulty = 1;
```

`SaveGameService.cs` 在 `CaptureSnapshot` 的 `data` 初始化块里加一行（与 `gameState = ...` 同级）：

```csharp
            difficulty = (int)Difficulty.Current,
```

并新增方法（放在 `CancelPendingLoad()` 之后）：

```csharp
    /// <summary>The saved difficulty wins over the menu selection. Called at the very start of
    /// RestoreSnapshot, before any AI state is applied, so the AI's Awake-time scaling and the
    /// restored timers describe the same match.</summary>
    public static void RestoreDifficulty(SaveGameData data)
    {
        Difficulty.Current = Difficulty.Clamp(data != null ? data.difficulty : 1);
    }
```

`RestoreSnapshot` 在 `string error = Validate(data);` **之前**加：

```csharp
        RestoreDifficulty(data);
```

- [ ] **Step 4: 编译并聚焦跑 3 条**

Expected: 3 条 PASS；既有 `SaveLoadTests` 全绿（聚焦跑 `SaveLoadTests` 中任意 2 条确认没破坏）。

- [ ] **Step 5: 真实场景探针验证写档字段（不写用户槽）**

Play 模式（`SampleScene`）：

```csharp
Difficulty.Current = Difficulty.Level.Hard;
var data = SaveGameService.CaptureSnapshot(out string err);
Debug.Log("[DIFF] capture err=" + err + " difficulty=" + (data == null ? -99 : data.difficulty));
```

Expected: `difficulty=2`。**只调 `CaptureSnapshot`，不要调 `Save`**（Save 会覆盖用户真实存档）。

- [ ] **Step 6: Commit**

```bash
cd "/d/Unity/projects/At the Gates" && git add Assets/Scripts/SaveGameData.cs Assets/Scripts/SaveGameService.cs Assets/Tests/PlayMode/DifficultyTests.cs && git commit -m "feat(difficulty): persist the chosen level in the save slot" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 3: ModeSelect 难度行 UI（**写场景，风控闸任务**）

**Files:**
- Modify: `Assets/Scripts/UI/ModeSelectMenu.cs`
- Create: `Assets/Editor/BuildDifficultyRow.cs`
- Modify: `Assets/Scenes/ModeSelect.scene`（**只允许由构建器写入**）
- Test: `Assets/Tests/PlayMode/DifficultyTests.cs`（追加 2 条，场景级）

**Interfaces:**
- Consumes: `Difficulty.Current`、`Difficulty.Clamp`、`Difficulty.DisplayName`。
- Produces: `ModeSelectMenu.difficultyButtons`（`Button[]`，顺序 Easy/Normal/Hard）、`ModeSelectMenu.SetDifficulty(int level)`、`ModeSelectMenu.RefreshDifficultyVisuals()`（public void，测试与 Start 共用）。

**开工前核对（缺一项就停）**：风险表已给用户（spec §6）；备份存在且 MD5 = `56e86ab40659949cd117d57b2f825a2c`；当前分支 `feat/difficulty`；编辑器不在 PlayMode。

- [ ] **Step 1: 追加场景级失败测试**

```csharp
    [UnityTest]
    public IEnumerator ModeSelect_HasThreeWiredButtons_DefaultNormal()
    {
        Difficulty.Current = Difficulty.Level.Normal;
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
            "ModeSelect", UnityEngine.SceneManagement.LoadSceneMode.Single);
        yield return null;
        var menu = Object.FindFirstObjectByType<ModeSelectMenu>();
        Assert.IsNotNull(menu);
        Assert.IsNotNull(menu.difficultyButtons, "difficultyButtons must be wired by the builder.");
        Assert.AreEqual(3, menu.difficultyButtons.Length);
        foreach (var b in menu.difficultyButtons) Assert.IsNotNull(b);
        Assert.AreEqual("简单", menu.difficultyButtons[0].GetComponentInChildren<UnityEngine.UI.Text>().text);
        Assert.AreEqual("普通", menu.difficultyButtons[1].GetComponentInChildren<UnityEngine.UI.Text>().text);
        Assert.AreEqual("困难", menu.difficultyButtons[2].GetComponentInChildren<UnityEngine.UI.Text>().text);

        var normalMark = menu.difficultyButtons[1].transform.Find("SelectedMark");
        var easyMark = menu.difficultyButtons[0].transform.Find("SelectedMark");
        Assert.IsNotNull(normalMark, "Selected mark is a graphic, not a glyph (font has no checkmark).");
        Assert.IsTrue(normalMark.gameObject.activeSelf, "Normal is the default selection.");
        Assert.IsFalse(easyMark.gameObject.activeSelf);

        menu.SetDifficulty(2);
        Assert.AreEqual(Difficulty.Level.Hard, Difficulty.Current);
        Assert.IsTrue(menu.difficultyButtons[2].transform.Find("SelectedMark").gameObject.activeSelf);
        Assert.IsFalse(normalMark.gameObject.activeSelf, "Only one button may show the mark.");
    }

    [UnityTest]
    public IEnumerator ModeSelect_RowFitsBoard_AndClearsCardsAndBack()
    {
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
            "ModeSelect", UnityEngine.SceneManagement.LoadSceneMode.Single);
        yield return null;
        var row = GameObject.Find("DifficultyRow");
        Assert.IsNotNull(row, "The builder must create Board/DifficultyRow.");
        var rowRt = row.GetComponent<RectTransform>();
        var board = GameObject.Find("Board").GetComponent<RectTransform>();
        Assert.IsTrue(Contains(board, rowRt), "The row must stay inside the board.");
        foreach (var cardName in new[] { "Human", "Campaign", "Versus" })
        {
            var card = GameObject.Find(cardName).GetComponent<RectTransform>();
            Assert.IsFalse(Overlaps(card, rowRt), cardName + " card overlaps the difficulty row.");
        }
        var back = GameObject.Find("Back").GetComponent<RectTransform>();
        Assert.IsFalse(Overlaps(back, rowRt), "Back button must stay clickable and clear.");
    }

    static Rect WorldRect(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y),
                               Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
    }
    static bool Overlaps(RectTransform a, RectTransform b)
    {
        var ra = WorldRect(a); var rb = WorldRect(b);
        return ra.xMin < rb.xMax - 0.5f && ra.xMax > rb.xMin + 0.5f
            && ra.yMin < rb.yMax - 0.5f && ra.yMax > rb.yMin + 0.5f;
    }
    static bool Contains(RectTransform outer, RectTransform inner)
    {
        var a = WorldRect(outer); var b = WorldRect(inner);
        return b.xMin >= a.xMin - 0.5f && b.xMax <= a.xMax + 0.5f
            && b.yMin >= a.yMin - 0.5f && b.yMax <= a.yMax + 0.5f;
    }
```

注意：`GameObject.Find("Human")` 依赖对象名唯一；若场景里有重名，改用 `board.transform.Find("Human")`。`SelectedMark` 初始状态由 `Start()` 决定，故 `LoadSceneAsync` 后必须 `yield return null`。

- [ ] **Step 2: 编译并聚焦跑，确认失败**

Expected: `ModeSelectMenu` 无 `difficultyButtons` 编译错误（UI 部分）；先实现 Step 3 后再跑矩形用例看其失败（`DifficultyRow` 不存在）。

- [ ] **Step 3: `ModeSelectMenu` 增加难度逻辑**

```csharp
    [Header("难度（简单/普通/困难，顺序固定）")]
    public Button[] difficultyButtons = new Button[0];

    public void SetDifficulty(int level)
    {
        Difficulty.Current = Difficulty.Clamp(level);
        RefreshDifficultyVisuals();
    }

    /// <summary>Selected state is a sprite swap plus a small graphic bar. No checkmark glyph:
    /// the built-in font would render it as a box.</summary>
    public void RefreshDifficultyVisuals()
    {
        if (difficultyButtons == null) return;
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            var b = difficultyButtons[i];
            if (b == null) continue;
            bool selected = (int)Difficulty.Current == i;
            var image = b.GetComponent<Image>();
            if (image != null && difficultyNormalSprite != null && difficultySelectedSprite != null)
                image.sprite = selected ? difficultySelectedSprite : difficultyNormalSprite;
            var mark = b.transform.Find("SelectedMark");
            if (mark != null) mark.gameObject.SetActive(selected);
        }
    }

    [Header("难度按钮贴图（由构建器填入）")]
    public Sprite difficultyNormalSprite;
    public Sprite difficultySelectedSprite;
```

`Start()` 末尾追加：

```csharp
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            int level = i;                      // capture for the closure
            if (difficultyButtons[i] != null)
                difficultyButtons[i].onClick.AddListener(() => SetDifficulty(level));
        }
        RefreshDifficultyVisuals();             // keeps the previous choice on re-entry
```

- [ ] **Step 4: 增量构建器 `BuildDifficultyRow.cs`（完整）**

```csharp
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Adds the difficulty row to the existing ModeSelect scene. Incremental and
/// idempotent: never rebuilds the scene (that would erase the approved menu polish).</summary>
public static class BuildDifficultyRow
{
    const string ScenePath = "Assets/Scenes/ModeSelect.scene";
    const string BlueButton = "Assets/Art/UI/Buttons/Button_Blue_9Slides.png";
    const string BluePressed = "Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png";
    static readonly string[] Labels = { "简单", "普通", "困难" };

    [MenuItem("Tools/UI/添加难度选择")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.name == "Menu Canvas");
        if (canvas == null) { Debug.LogError("[难度] 找不到 Menu Canvas"); return; }
        var menu = canvas.GetComponent<ModeSelectMenu>();
        var board = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "Board");
        if (menu == null || board == null) { Debug.LogError("[难度] 找不到 ModeSelectMenu 或 Board"); return; }

        bool changed = false;
        var row = board.Find("DifficultyRow") as RectTransform;
        if (row == null)
        {
            var go = new GameObject("DifficultyRow", typeof(RectTransform));
            row = go.GetComponent<RectTransform>();
            row.SetParent(board, false);
            changed = true;
        }
        changed |= SetRect(row, new Vector2(0f, -312f), new Vector2(560f, 56f));

        var label = row.Find("Label") as RectTransform;
        if (label == null) { label = MakeText(row, "Label", "电脑难度", 22); changed = true; }
        changed |= SetRect(label, new Vector2(-225f, 0f), new Vector2(110f, 40f));

        var buttons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            string name = "Difficulty_" + i;
            var rt = row.Find(name) as RectTransform;
            if (rt == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                rt = go.GetComponent<RectTransform>();
                rt.SetParent(row, false);
                var img = go.GetComponent<Image>();
                img.sprite = LoadSprite(BlueButton);
                if (img.sprite != null && img.sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
                go.GetComponent<Button>().targetGraphic = img;
                changed = true;
            }
            changed |= SetRect(rt, new Vector2(-94f + 136f * i, 0f), new Vector2(120f, 56f));
            var text = rt.Find("Label") as RectTransform;
            if (text == null) { text = MakeText(rt, "Label", Labels[i], 22); changed = true; }
            var textComponent = text.GetComponent<Text>();
            if (textComponent.text != Labels[i]) { textComponent.text = Labels[i]; changed = true; }
            Stretch(text);
            var mark = rt.Find("SelectedMark") as RectTransform;
            if (mark == null)
            {
                var go = new GameObject("SelectedMark", typeof(RectTransform), typeof(Image));
                mark = go.GetComponent<RectTransform>();
                mark.SetParent(rt, false);
                var img = go.GetComponent<Image>();
                img.color = new Color(0.98f, 0.80f, 0.35f, 1f);
                img.raycastTarget = false;
                changed = true;
            }
            changed |= SetRect(mark, new Vector2(0f, -30f), new Vector2(96f, 4f));
            buttons[i] = rt.GetComponent<Button>();
        }

        if (menu.difficultyButtons == null || menu.difficultyButtons.Length != 3
            || !menu.difficultyButtons.SequenceEqual(buttons))
        { menu.difficultyButtons = buttons; changed = true; }
        var normal = LoadSprite(BlueButton);
        var pressed = LoadSprite(BluePressed);
        if (menu.difficultyNormalSprite != normal) { menu.difficultyNormalSprite = normal; changed = true; }
        if (menu.difficultySelectedSprite != pressed) { menu.difficultySelectedSprite = pressed; changed = true; }

        if (!changed) { Debug.Log("[难度] No changes needed; save skipped"); return; }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[难度] 已写入难度行并保存 ModeSelect.scene");
    }

    static bool SetRect(RectTransform rt, Vector2 pos, Vector2 size)
    {
        // Compare component-wise: `changed |= unityObject` is always true (implicit bool).
        bool changed = false;
        if (rt.anchorMin != new Vector2(0.5f, 0.5f)) { rt.anchorMin = new Vector2(0.5f, 0.5f); changed = true; }
        if (rt.anchorMax != new Vector2(0.5f, 0.5f)) { rt.anchorMax = new Vector2(0.5f, 0.5f); changed = true; }
        if (rt.pivot != new Vector2(0.5f, 0.5f)) { rt.pivot = new Vector2(0.5f, 0.5f); changed = true; }
        if (rt.anchoredPosition != pos) { rt.anchoredPosition = pos; changed = true; }
        if (rt.sizeDelta != size) { rt.sizeDelta = size; changed = true; }
        return changed;
    }

    static RectTransform MakeText(Transform parent, string name, string content, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.97f, 0.88f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static Sprite LoadSprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
```

**贴图文件名已于 2026-11-02 核实**：普通 = `Assets/Art/UI/Buttons/Button_Blue_9Slides.png`，选中 = `Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png`（目录里确实存在，无需再猜）。

- [ ] **Step 5: 编译 → 跑构建器 → 立即核验**

```csharp
AssetDatabase.Refresh();
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
// 编译完成后（确认不在 PlayMode）
BuildDifficultyRow.Apply();
BuildDifficultyRow.Apply();   // 第二次必须报 "No changes needed; save skipped"
```

bash 核验对象级增量（只允许新增难度行相关对象，0 删除）：

```bash
cd "/d/Unity/projects/At the Gates" && md5sum Assets/Scenes/ModeSelect.scene && git diff --no-index --stat .superpowers/sdd/difficulty/modeselect-before.scene Assets/Scenes/ModeSelect.scene
```

Expected: 只有新增块（DifficultyRow/Label/Difficulty_0..2/SelectedMark）+ `ModeSelectMenu` 的按钮引用；**不得出现卡片、Title、Back 的修改**。有就还原备份重来。

- [ ] **Step 6: 截图目检（必须真的看）**

Play 模式加载 ModeSelect 后，用**第 1~2 帧**截图法（等 0.7s 会拿到切换后的场景）：

```csharp
UnityEngine.SceneManagement.SceneManager.LoadScene("ModeSelect");
// 下一帧
ScreenCapture.CaptureScreenshot(@"D:\Unity\projects\At the Gates\Logs\difficulty_modeselect.png");
```

看：三按钮在 Board 底部空带内、不压卡片、Back 不被遮、文字无方框、默认普通带金色小条。再 `SetDifficulty(2)` 截一张确认标记移动。

- [ ] **Step 7: 聚焦跑 2 条 UI 测试**

Expected: 2 条 PASS。若矩形用例失败，调 `DifficultyRow` 的 y（−312 附近微调）而不是放宽断言。

- [ ] **Step 8: Commit**

```bash
cd "/d/Unity/projects/At the Gates" && git add Assets/Scripts/UI/ModeSelectMenu.cs Assets/Editor/BuildDifficultyRow.cs Assets/Editor/BuildDifficultyRow.cs.meta Assets/Scenes/ModeSelect.scene Assets/Tests/PlayMode/DifficultyTests.cs && git commit -m "feat(difficulty): add the level picker to the mode select screen" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 4: 变异证明 + 三档冒烟 + 全量 + 交付

**Files:**
- Create: `docs/superpowers/reports/2026-11-02-rts-difficulty-verification.md`
- Modify: `.superpowers/sdd/difficulty/RESUME.md`
- Create: `.superpowers/sdd/difficulty/mutation-*.log`、`full-suite.log`、`smoke-*.log`

**Interfaces:** 无新接口；只用前面任务的 `Difficulty.Apply`、`SaveGameService.RestoreDifficulty`、`AIController.Awake`。

- [ ] **Step 1: 变异证明 A（删 `Awake` 里的 `Difficulty.Apply(this);`）**

临时注释该行 → 编译 → 聚焦跑 `DifficultyTests.Start_TimersUseScaledValues` 与 `Easy_ScalesPaceAndBank`。
Expected: **必须变红**（Easy 用例 `Expected: 180 But was: 120`）。若仍绿 = 测试空转，停下来修测试。
随后**字节还原**并核 MD5：

```bash
cd "/d/Unity/projects/At the Gates" && python -I -c "
import shutil,hashlib
shutil.copy2('.superpowers/sdd/difficulty/AIController.before-probe.cs','Assets/Scripts/AIController.cs')
print(hashlib.md5(open('Assets/Scripts/AIController.cs','rb').read()).hexdigest())"
```

（探针前先用同样方式把 `AIController.cs` 复制成 `AIController.before-probe.cs`。）日志存 `mutation-a-red.log`。

- [ ] **Step 2: 变异证明 B（删 `RestoreSnapshot` 里的 `RestoreDifficulty(data);`）**

聚焦跑 `RestoreDifficulty_AppliesSavedValue_AndClamps`。
**注意**：这条直接调 `SaveGameService.RestoreDifficulty`，删掉 `RestoreSnapshot` 里的调用它仍然绿——所以本用例**不能**作为该删除的判别证明。真正的判别证明需要端到端：在 Play 模式手工构造 `SaveGameData{difficulty=2}` → `RestoreSnapshot` → 断言 `Difficulty.Current==Hard`。若不愿跑整条读档，**在报告里明确写"读档恢复这一行只有代码审查与手工探针证据，没有自动化判别证明"**，不得声称已做变异证明。
手工探针（Play，SampleScene）：

```csharp
var d = SaveGameService.CaptureSnapshot(out _);
d.difficulty = 0;
Difficulty.Current = Difficulty.Level.Hard;
var routine = SaveGameService.RestoreSnapshot(d, (ok, err) => Debug.Log("[DIFF] restore ok=" + ok + " err=" + err));
// 用宿主组件 StartCoroutine 跑完后读 Difficulty.Current，预期 Easy(0)
```

- [ ] **Step 3: 三档首波时间冒烟（不写档）**

每档：设置 `Difficulty.Current` → `LoadingScreen.Load("SampleScene")` → 挂探针（与下述类同条提交）：

```csharp
public class FirstWaveProbe : MonoBehaviour
{
    float t0; int seen;
    void Start() { t0 = Time.time; Debug.Log("[WAVE] start difficulty=" + Difficulty.Current); }
    void Update()
    {
        if (Time.time - t0 < 1f) return;
        foreach (var ai in AIController.All)
        {
            if (ai == null) continue;
            var f = typeof(AIController).GetField("wave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int w = (int)f.GetValue(ai);
            if (w > seen)
            {
                seen = w;
                Debug.Log("[WAVE] first wave at t=" + (Time.time - t0).ToString("F1")
                    + " expectedFirstAttack=" + ai.firstAttack + " difficulty=" + Difficulty.Current);
                enabled = false;
            }
        }
    }
}
```

Expected（容差 ±5s）：简单 ≈180s、普通 ≈120s、困难 ≈96s。普通作为对照组必须≈120，否则说明普通档被改动了。
用 `Time.timeScale=3` 加速时**等场景起来后再设**（`PauseMenu.Start` 会归 1）。日志存 `smoke-easy/normal/hard.log`。

- [ ] **Step 4: 全量套件 + 立刻归档**

```csharp
RegressionTestMenu.RunPlayModeTests();
```

Expected: **263/263**（252 基线 + 11 新增）。跑完立刻：

```bash
cd "/d/Unity/projects/At the Gates" && python -I -c "
import shutil; shutil.copy2('Logs/RegressionTests.txt','.superpowers/sdd/difficulty/full-suite.log')"
```

若有旧测试变红：先判断它是否钉住了"AI 恒定 120s"这类**已被本功能合法改变**的假设——是则改写测试钉住新意图（普通档仍 120s），并把旧断言失败当作变异证据；不是则当真回归修。

- [ ] **Step 5: 现场还原与交付报告**

- 退出 Play，编辑器 `dirty=false`；`git status` 只剩报告/断点新文件。
- `md5sum Assets/Scenes/SampleScene.scene` 必须仍是 `d324369ebd8a183be9e431d36b69da64`（本功能不写它）。
- 用户存档 MD5 必须仍是 `0d34d758ec3bd3eb7ee954eebf411076`；若被动过，用备份还原。
- 报告写清：三档实测首波时间、变异证明哪条做了哪条没做、UI 截图路径、**不可验证项（真实键鼠手感、长局平衡）**。

- [ ] **Step 6: 更新断点并请用户决定是否合并 main**（不自行合并）

---

## Self-Review 记录

- spec §3 数值 → Task 1 测试逐项对应；§4.1/4.2 → Task 1；§4.3 → Task 3；§4.4 → Task 2。测试共 **11 条**（Task1 6 + Task2 3 + Task3 2），全量期望 **263/263**，已在 Global Constraints 与 Task 4 统一。
- **夹具陷阱（自审抓到并已修）**：`AddComponent<AIController>()` 会立刻执行 `Awake` → 若先加组件再赋值，缩放会作用在 C# 默认值上，测试必然失败。`MakeAI()` 已改为**先 `SetActive(false)` 建对象、赋完字段再 `SetActive(true)`**。
- spec §5 的 `SetDifficulty_PersistsAcrossMenuReentry` 与 `ModeSelect_HasThreeWiredButtons_DefaultNormal` 中的重进保留选择语义重复，已合并进 Task 3 第一条的 `RefreshDifficultyVisuals` 断言；若要严格覆盖"重进"，可在该用例末尾再 `LoadSceneAsync("ModeSelect")` 一次并断言困难仍选中。
- 变异证明 B 的判别力问题已在 Task 4 Step 2 写明，**不允许**把直接调用 `RestoreDifficulty` 的单测当作删行证明。
- 类型一致性：`Difficulty.Clamp(int)`、`DisplayName(Level)`、`Apply(AIController)`、`AIController.difficultyApplied`、`SaveGameService.RestoreDifficulty(SaveGameData)`、`ModeSelectMenu.SetDifficulty(int)/RefreshDifficultyVisuals()/difficultyButtons/difficultyNormalSprite/difficultySelectedSprite` 在 Tasks 1-4 中名字一致。
