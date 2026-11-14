# RTS Priority Fixes and Map Pruning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复玩家/AI 判负卡死、羊中途读档错位和小地图模态输入穿透，校正一个 Tree1，并将 SampleScene 的陆地草从 99 丛减至 50 丛。

**Architecture:** 保留现有系统边界，只在 GameState/AIController、SaveGameService/SheepWander、Minimap 中做定点修改。羊不写新的移动状态，而是在保存记录中回到最后一个已登记占地。场景由独立编辑器工具预览并执行定点减量，保存前后以 YAML 对象级审计和完整回归兜底。

**Tech Stack:** 团结引擎 2022.3.62t5、Unity C#、uGUI、NUnit PlayMode Tests、UnityEditor.TestTools.TestRunner、Codely Bridge MCP、Python 只读/审计脚本。

## Global Constraints

- 设计来源：`docs/superpowers/specs/2026-11-14-rts-priority-fixes-map-pruning-design.md`。
- 只修改 `SampleScene.scene`；不得修改 MainMenu、ModeSelect、预制体、地形或真实存档。
- 草只减 `Garnish` 直接子级中名称以 `Bush` 开头的对象：99→50；WaterRock=94、SeaRock=28、Cloud=7 必须不变。
- Tree1 仅将目标实例 `(4.37,18.44,0)` 的 x 改为 3.50；其逻辑占地 `(3,18)` 前后不变。
- 不重新运行 HUD、Garnish 或 Decor 重建器；不清除动画速度或 AI 权重以外的“噪声”，本次这些字段也不得变化。
- 所有修复先观察新测试 RED，再写最小实现使其 GREEN；不得通过删除或放宽断言取得通过。
- 每次修改编辑器脚本后先 `request_compile`；PlayMode 套件不得并行。
- 场景保存前必须保留完整字节备份和 MD5；任何未批准差异都阻止保存或触发字节回滚。
- 每完成一个任务更新 `.superpowers/sdd/priority-fixes-map-pruning/RESUME.md`。
- 不提交、不推送；真实槽 `save_slot_1.json` 只允许读取和哈希。

---

### Task 1: 玩家与 AI 的可恢复性判定

**Files:**
- Modify: `Assets/Scripts/GameState.cs:30-32,193-218`
- Modify: `Assets/Scripts/AIController.cs:234-247`
- Modify: `Assets/Tests/PlayMode/WipeoutDefeatTests.cs`

**Interfaces:**
- Consumes: `HUD.buildPrefabs`、`Building.cost`、`Building.isCommandCenter`、`Building.HasLivingCommandCentre(Team)`、`Building.HasCompletedCommandCentre(Team)`、`AIController.buildOrder`、`AIController.NextBuildIndex`。
- Produces: 不改变公共签名；`GameState.CanStillRecover(Team)` 仍由团灭检查调用，`AIController.CanStillRecover()` 仍返回 bool。

- [ ] **Step 1: 写玩家与 AI 失败测试**

在 `WipeoutDefeatTests.cs` 添加以下测试和反射辅助。玩家用真实 House/Castle 预制体组成 HUD 建造表，直接驱动正式 `Evaluate`，而不是只测一个复制出来的规则：

```csharp
static void InvokeEvaluate(GameState state)
{
    typeof(GameState).GetMethod("Evaluate",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .Invoke(state, null);
}

[Test]
public void Wipeout_OnlyLockedHouseAffordable_IsDefeat()
{
    var hudGO = new GameObject("Recovery HUD");
    var hud = hudGO.AddComponent<HUD>();
    hud.buildPrefabs = new[]
    {
        LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/House_Blue.prefab"),
        LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab"),
    };

    var gs = GameState.Instance;
    var blue = CreateActor("Locked-house blue", new Vector3Int(-2, 0, 0), Team.Blue);
    CreateActor("Watching red", new Vector3Int(-1, 0, 0), Team.Red);
    InvokeEvaluate(gs);                 // 记录 everFielded

    blue.TakeDamage(100000);            // 当帧离开 UnitActor.All
    Economy.gold = 75;                  // House=75，Castle=300
    Economy.wood = 300;
    Economy.meat = 200;
    InvokeEvaluate(gs);

    Assert.AreEqual(GameState.Outcome.Lose, gs.Result,
        "买不起主基地时，被前置锁住的民房不能算恢复手段。");
}

[Test]
public void Wipeout_CommandCentreAffordable_KeepsPlaying()
{
    var hudGO = new GameObject("Recovery HUD");
    var hud = hudGO.AddComponent<HUD>();
    hud.buildPrefabs = new[]
    {
        LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/House_Blue.prefab"),
        LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab"),
    };

    var gs = GameState.Instance;
    var blue = CreateActor("Recoverable blue", new Vector3Int(-2, 0, 0), Team.Blue);
    CreateActor("Watching red", new Vector3Int(-1, 0, 0), Team.Red);
    InvokeEvaluate(gs);
    blue.TakeDamage(100000);
    Economy.gold = 300;
    InvokeEvaluate(gs);

    Assert.AreEqual(GameState.Outcome.Playing, gs.Result,
        "买得起主基地时必须保留真正可恢复的 Positive control。");
}

AIController CreateRecoveryAi(int startingGold)
{
    var go = new GameObject("Recovery AI " + startingGold);
    go.SetActive(false);               // 先配置，再让 Awake 初始化银行
    var ai = go.AddComponent<AIController>();
    ai.team = Team.Red;
    ai.active = false;
    ai.startGold = startingGold;
    ai.startWood = 300;
    ai.startMeat = 200;
    ai.buildOrder = new[]
    {
        LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Castle_Red.prefab"),
        LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/House_Red.prefab"),
    };
    go.SetActive(true);
    return ai;
}

[Test]
public void AiRecovery_LaterCheapHouse_DoesNotBypassCurrentCastle()
{
    var ai = CreateRecoveryAi(75);
    Assert.AreEqual(0, ai.NextBuildIndex);
    Assert.IsFalse(ai.CanStillRecover(),
        "Build() 当前只会尝试 300 金 Castle，后续 75 金 House 不能制造假恢复能力。");
}

[Test]
public void AiRecovery_CurrentCastleAffordable_IsPositiveControl()
{
    var ai = CreateRecoveryAi(300);
    Assert.IsTrue(ai.CanStillRecover());
}
```

- [ ] **Step 2: 串行运行聚焦测试并确认 RED**

先请求编译，再逐个执行（禁止同时运行）：

```csharp
RegressionTestMenu.RunPlayModeTest("WipeoutDefeatTests.Wipeout_OnlyLockedHouseAffordable_IsDefeat");
RegressionTestMenu.RunPlayModeTest("WipeoutDefeatTests.AiRecovery_LaterCheapHouse_DoesNotBypassCurrentCastle");
```

预期：两条均 FAIL。玩家旧结果为 `Playing`；AI 旧 `CanStillRecover()` 为 true。两条 Positive control 应在旧代码上已通过，运行确认它们确实能区分行为。

- [ ] **Step 3: 最小修改 GameState**

将 `recoveryGoldFloor` 默认值和注释改为 300，并将 HUD 分支改为只接受当前可执行的建造：

```csharp
[Tooltip("无 HUD 时的团灭恢复金线。没有主基地时只有主基地能直接放置；当前 Castle 为 300 金。")]
public int recoveryGoldFloor = 300;
```

`CanStillRecover` 的玩家 HUD 循环使用：

```csharp
foreach (var p in hud.buildPrefabs)
{
    var b = p != null ? p.GetComponent<Building>() : null;
    if (b == null || !eco.CanAfford(b.cost)) continue;
    if (b.isCommandCenter)
    {
        if (!Building.HasLivingCommandCentre(team)) return true;
    }
    else if (Building.HasCompletedCommandCentre(team))
    {
        return true;
    }
}
return false;
```

无 HUD 分支保留 `eco.gold >= recoveryGoldFloor`，此时兜底值已经是 300。

- [ ] **Step 4: 最小修改 AIController**

用与 `Build()` 相同的“只尝试下一项”语义替换全表扫描：

```csharp
public bool CanStillRecover()
{
    if (buildOrder == null) return false;
    for (int i = nextBuild; i < buildOrder.Length; i++)
    {
        var prefab = buildOrder[i];
        if (prefab == null) continue;  // Build() 会在后续 tick 跳过 null
        var b = prefab.GetComponent<Building>();
        if (b == null) continue;       // Build() 同样会跳过缺 Building 的条目
        if (!CanAfford(b.cost)) return false;
        if (b.isCommandCenter) return !Building.HasLivingCommandCentre(team);
        return Building.HasCompletedCommandCentre(team);
    }
    return false;
}
```

- [ ] **Step 5: 重跑聚焦测试**

设计 §3.3 的 299 金边界必须覆盖：两个低资源测试用 `[TestCase(75)]`、`[TestCase(299)]` 参数化，300 金正对照独立保留，共六个执行用例。75 金已在旧代码取得 RED，299 金走同一旧分支，修复后必须全部 GREEN。

预期：全部 PASS；日志逐条归档，随后更新 RESUME。此时 SampleScene 的序列化 `recoveryGoldFloor=50` 尚未改，真实场景修复在 Task 4 完成，不能提前宣称整条任务已覆盖真实配置。

---

### Task 2: 羊步行中途保存回到稳定占地

**Files:**
- Modify: `Assets/Scripts/SheepWander.cs:51-57,328-366`
- Modify: `Assets/Scripts/SaveGameService.cs:188-207`
- Test: `Assets/Tests/PlayMode/SheepWanderTests.cs`（新增一个 UnityTest）

**Interfaces:**
- Produces: `public Vector3 StableSavePosition { get; }`。非步行时返回当前 transform；步行时由当前 `Building.Cells` 推导稳定原点。
- Consumes: `SaveObjectRecord.position`、现有 `Building.SaveState` 与 `ResourceNode.SaveState`；存档版本保持 2。

- [ ] **Step 1: 在 SheepWanderTests 写真实中途保存失败测试**

```csharp
[UnityTest]
public IEnumerator SaveLoad_MidStep_RewindsToLastRegisteredFootprint()
{
    yield return LoadSampleScene();
    foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
        ai.active = false;
    foreach (var g in Gatherer.All.ToArray())
        if (g != null) g.enabled = false;

    var sheep = FindSheep("Sheep_Meat_1");
    var wander = sheep.GetComponent<SheepWander>();
    var building = sheep.GetComponent<Building>();
    var node = sheep.GetComponent<ResourceNode>();
    wander.autoWander = false;
    wander.stepSeconds = 1f;
    Assert.AreEqual(SheepStepResult.Moved,
        wander.TryStep(wander.CurrentCell + Vector3Int.right));

    Time.timeScale = 0f;
    typeof(SheepWander).GetMethod("AdvanceWalk",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .Invoke(wander, new object[] { 0.25f });
    Assert.IsTrue(wander.IsWalking);
    Assert.AreNotEqual(0f, sheep.transform.position.x % 1f);

    var expectedCells = building.Cells.ToArray();
    float expectedX = expectedCells.Min(c => c.x) + building.Width * 0.5f;
    float expectedY = expectedCells.Min(c => c.y);
    int expectedAmount = node.amount - 7;
    node.Harvest(7);

    var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
    Assert.IsNotNull(snapshot, captureError);
    var record = snapshot.buildings.Single(r => r.objectName == "Sheep_Meat_1");
    Assert.AreEqual(expectedX, record.position.x, 0.0001f,
        "保存记录必须回到仍登记在 Building.Cells 中的稳定原点，而不是半格插值。");
    Assert.AreEqual(expectedY, record.position.y, 0.0001f);

    string path = Path.Combine(Application.temporaryCachePath,
        "atg_midstep_stable_footprint.json");
    try
    {
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError), writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError), readError);
        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, restoreError);

        var after = FindSheep("Sheep_Meat_1");
        var afterWander = after.GetComponent<SheepWander>();
        var afterBuilding = after.GetComponent<Building>();
        Assert.IsFalse(afterWander.IsWalking);
        Assert.AreEqual(expectedX, after.transform.position.x, 0.0001f);
        Assert.AreEqual(expectedY, after.transform.position.y, 0.0001f);
        CollectionAssert.AreEquivalent(expectedCells, afterBuilding.Cells.ToArray());
        Assert.AreEqual(expectedAmount, after.GetComponent<ResourceNode>().amount);
        foreach (var cell in expectedCells)
            Assert.IsTrue(MapGrid.Instance.IsBlockedByBuilding(cell));
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
    }
}
```


- [ ] **Step 2: 运行聚焦测试确认 RED**

```csharp
RegressionTestMenu.RunPlayModeTest("SheepWanderTests.SaveLoad_MidStep_RewindsToLastRegisteredFootprint");
```

预期：在 `record.position.x` 断言处 FAIL；旧代码记录的是 17.25，稳定原点为 17。

- [ ] **Step 3: 添加 StableSavePosition**

在 `SheepWander` 添加：

```csharp
/// <summary>
/// 保存中途步伐时回到仍登记在 Building.Cells 中的最后完整占地。
/// 不推进实时对象，也不把半格 transform 写入存档。
/// </summary>
public Vector3 StableSavePosition
{
    get
    {
        if (!IsWalking || building == null || building.Cells.Count == 0)
            return transform.position;
        int minX = building.Cells.Min(c => c.x);
        int minY = building.Cells.Min(c => c.y);
        return new Vector3(minX + building.Width * 0.5f, minY, transform.position.z);
    }
}
```

文件已有 `System.Collections.Generic`，需新增 `using System.Linq;`。

- [ ] **Step 4: SaveGameService 只修改写入记录**

在建筑记录创建完成、加入 `data.buildings` 之前加入：

```csharp
var wanderingSheep = b.GetComponent<SheepWander>();
if (wanderingSheep != null && wanderingSheep.IsWalking)
    record.position = wanderingSheep.StableSavePosition;
```

不增加 `SaveGameData` 字段，不改版本，不在 Capture 时调用 `FinishStep` 或修改实时 transform。

- [ ] **Step 5: 聚焦 GREEN 与既有羊测试**

先重跑新测试，预期 PASS；再运行现有：

```csharp
RegressionTestMenu.RunPlayModeTest("SheepWanderTests.SaveLoad_RestoredAnchor_IsTheRestoredPosition_NotTheOriginal");
RegressionTestMenu.RunPlayModeTest("CentralMeatTests.CentralSheep_SurviveSaveLoad_WithRegrowBlockingAndFootprintIntact");
```

预期全部 PASS，临时文件清理完成，更新 RESUME。

---

### Task 3: 小地图暂停/结算输入闸

**Files:**
- Modify: `Assets/Scripts/Minimap.cs:105-122`
- Create: `Assets/Tests/PlayMode/MinimapInputTests.cs`

**Interfaces:**
- Consumes: `PauseMenu.IsAnyOpen`、`GameState.Instance.IsOver`、`Minimap.OnPointerClick(PointerEventData)`。
- Produces: 无新增公共 API。

- [ ] **Step 1: 写三个输入测试**

新文件使用 `RegressionFixture`，构造带 RawImage 的真实 Minimap 和主相机。辅助代码如下：

```csharp
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MinimapInputTests : RegressionFixture
{
    Minimap CreateMinimap(out Camera cam, out GameObject pauseGO, out GameObject panel)
    {
        cam = Selection.cam;
        var rts = cam.gameObject.AddComponent<RTSCamera>();
        rts.enabled = false;
        rts.autoMapBounds = false;
        rts.useStartPosition = false;
        rts.mapBounds = new Rect(-50f, -50f, 100f, 100f);
        cam.transform.position = new Vector3(10f, 10f, -10f);

        var go = new GameObject("Minimap under test", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(200f, 100f);
        rt.position = Vector3.zero;
        var map = go.AddComponent<Minimap>();
        map.mainCamera = cam;
        map.rtsCamera = rts;

        panel = new GameObject("Pause panel");
        panel.SetActive(false);
        pauseGO = new GameObject("Pause owner");
        var pause = pauseGO.AddComponent<PauseMenu>();
        pause.panel = panel;
        return map;
    }

    static PointerEventData CentreClick()
    {
        return new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = Vector2.zero,
        };
    }

    [Test]
    public void Click_NormalMatch_MovesCamera()
    {
        var map = CreateMinimap(out var cam, out _, out _);
        map.OnPointerClick(CentreClick());
        Assert.AreNotEqual(new Vector3(10f, 10f, -10f), cam.transform.position);
    }

    [Test]
    public void Click_Paused_DoesNotMoveCamera()
    {
        var map = CreateMinimap(out var cam, out _, out _);
        var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
        pause.SetOpen(true);
        Vector3 before = cam.transform.position;
        map.OnPointerClick(CentreClick());
        Assert.AreEqual(before, cam.transform.position);
        pause.SetOpen(false);
    }

    [Test]
    public void Click_GameOver_DoesNotMoveCamera()
    {
        var map = CreateMinimap(out var cam, out _, out _);
        GameState.Instance.End(GameState.Outcome.Lose);
        Vector3 before = cam.transform.position;
        map.OnPointerClick(CentreClick());
        Assert.AreEqual(before, cam.transform.position);
    }
}
```

若独立 RectTransform 的坐标转换在测试环境不接受 null camera，则把它放入 Screen Space Overlay Canvas，并以 `RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center))` 生成点击位置；不得直接调用 RTSCamera 或只测 `IsAnyOpen`。

- [ ] **Step 2: 运行暂停与结算测试确认 RED，正对照保持 GREEN**

```csharp
RegressionTestMenu.RunPlayModeTest("MinimapInputTests.Click_Paused_DoesNotMoveCamera");
RegressionTestMenu.RunPlayModeTest("MinimapInputTests.Click_GameOver_DoesNotMoveCamera");
RegressionTestMenu.RunPlayModeTest("MinimapInputTests.Click_NormalMatch_MovesCamera");
```

预期：前两条旧代码均移动相机并 FAIL；第三条 PASS。

- [ ] **Step 3: 添加最小模态闸**

在 `OnPointerClick` 左键判断后加入：

```csharp
if (PauseMenu.IsAnyOpen) return;
if (GameState.Instance != null && GameState.Instance.IsOver) return;
```

- [ ] **Step 4: 三条聚焦测试重跑**

预期三条全部 PASS，暂停测试结束后 `Time.timeScale=1`，更新 RESUME。

---

### Task 4: Tree1、恢复兜底值与草地减量场景实施

**Files:**
- Create: `Assets/Editor/PruneMap20261114.cs`
- Create: `Assets/Tests/PlayMode/MapPruning20261114Tests.cs`
- Modify: `Assets/Scenes/SampleScene.scene`（只能由批准的 Apply 方法保存）
- Backup: `.superpowers/sdd/priority-fixes-map-pruning/SampleScene.before.scene`

**Interfaces:**
- `PruneMap20261114.Preview()`：只读输出 49 个候选及非目标计数，不标 dirty、不保存。
- `PruneMap20261114.Apply()`：复查前置条件，备份后删除候选、修 Tree1 和 recoveryGoldFloor，保存一次。
- `PruneMap20261114.SelectForRemoval()`：内部确定性选择，供 Preview/Apply 复用，保证两份清单一致。

- [ ] **Step 1: 写场景钉死测试**

```csharp
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MapPruning20261114Tests : RegressionFixture
{
    [UnityTest]
    public IEnumerator ShippedMap_HasFiftyBushes_AndLeavesOtherGarnishUntouched()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var root = GameObject.Find("Garnish");
        Assert.IsNotNull(root);
        var children = root.transform.Cast<Transform>().ToArray();
        Assert.AreEqual(50, children.Count(t => t.name.StartsWith("Bush")));
        Assert.AreEqual(94, children.Count(t => t.name.StartsWith("WaterRock")));
        Assert.AreEqual(28, children.Count(t => t.name.StartsWith("SeaRock")));
        Assert.AreEqual(7, children.Count(t => t.name.StartsWith("Cloud")));
    }

    [UnityTest]
    public IEnumerator ShippedMap_OffsetTreeMatchesExistingBlocker_AndRecoveryFloorIsCastleCost()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var tree = Object.FindObjectsByType<Building>(FindObjectsSortMode.None)
            .SingleOrDefault(b => b.name == "Tree1"
                && Mathf.Abs(b.transform.position.y - 18.44f) < 0.001f
                && Mathf.Abs(b.transform.position.x - 3.50f) < 0.001f);
        Assert.IsNotNull(tree, "目标 Tree1 必须移动到其原有逻辑占地上。");
        Assert.AreEqual(new Vector3Int(3, 18, 0), tree.Cells[0]);
        Assert.IsTrue(MapGrid.Instance.IsBlockedByBuilding(new Vector3Int(3, 18, 0)));
        Assert.AreEqual(300, GameState.Instance.recoveryGoldFloor);
    }
}
```

- [ ] **Step 2: 运行两条场景测试确认 RED**

预期：草计数实际 99；Tree1 查找结果为 null 且 recoveryGoldFloor=50。记录非目标计数确实为 94/28/7，防止测试写成恒真。此处更正早期把 WaterRock+SeaRock 合计 122 误记为 WaterRock 单项的统计笔误，非目标对象范围不变。

- [ ] **Step 3: 实现确定性 Preview/Apply**

`PruneMap20261114` 使用以下明确规则，禁止调用旧生成器：

```csharp
const int RequiredBushCount = 99;
const int RequiredRemovalCount = 49;
const int RetainedBushCount = 50;

static Vector2Int BinOf(Vector3 p) => new Vector2Int(
    Mathf.FloorToInt(p.x / 10f), Mathf.FloorToInt(p.y / 10f));

static float RemovalScore(Transform candidate, Vector3 blue, Vector3 red,
    System.Collections.Generic.IEnumerable<Transform> all)
{
    float spawnDistance = Mathf.Min(
        Vector2.Distance(candidate.position, blue),
        Vector2.Distance(candidate.position, red));
    int neighbours = all.Count(other => other != candidate
        && Vector2.Distance(candidate.position, other.position) <= 2.01f);
    // 越小越先删：先初始建设区，再密集丛；坐标只用于最后稳定排序。
    return spawnDistance - neighbours * 1000f;
}
```

蓝/红初始中心分别取场上存活蓝方单位和红方单位位置的算术平均。候选按 `BinOf` 分组；各组配额先取 `Mathf.Floor(n * 49.0 / 99.0)`，余数按 `n * 49.0 / 99.0 - floor` 从大到小分配，直到总计 49；同分时按 bin.x、bin.y 字典序。组内按 `RemovalScore` 升序，再按 position.x、position.y、position.z、对象名排序，取前 quota 个。

Preview 必须输出：

```text
bush=99 remove=49 remain=50 WaterRock=94 SeaRock=28 Cloud=7
blueCentre=(...) redCentre=(...)
REMOVE <bin> <name> <position>
TREE before=(4.37,18.44,0) after=(3.50,18.44,0) blocker=(3,18)
recoveryGoldFloor before=50 after=300
```

Apply 在删除前逐项硬检查：

```csharp
if (EditorApplication.isPlayingOrWillChangePlaymode)
    throw new System.InvalidOperationException("Leave PlayMode before pruning.");
if (bushes.Count != 99 || candidates.Count != 49)
    throw new System.InvalidOperationException("Unexpected bush/candidate count.");
if (waterRocks != 94 || seaRocks != 28 || clouds != 7)
    throw new System.InvalidOperationException("Non-target garnish changed.");
if (tree == null || Vector3.Distance(tree.position, new Vector3(4.37f, 18.44f, 0f)) > 0.001f)
    throw new System.InvalidOperationException("Target Tree1 not at approved baseline.");
var gameState = Object.FindFirstObjectByType<GameState>();
if (gameState == null || gameState.recoveryGoldFloor != 50)
    throw new System.InvalidOperationException("Unexpected recovery floor.");
```

先用 Python/`shutil.copy2` 或 Write 工具完成字节备份并核对 MD5，再调用 Apply。Apply 删除候选根对象、设置 `tree.position = new Vector3(3.50f,18.44f,0f)`、设置上述局部 `gameState.recoveryGoldFloor=300`，只调用一次 `EditorSceneManager.SaveScene`。禁止调用 `AssetDatabase.DeleteAsset`，因为只删场景实例，不删 prefab。

- [ ] **Step 4: 保存前后对象级审计**

保存前记录原场景 MD5；保存后检查：

- Bush 根减少 49，保留 50；删除清单与 Preview 的 name+position 一一对应。
- WaterRock/SeaRock/Cloud、ResourceNode、Building、UnitActor、Tilemap 数量与基线一致。
- 目标 Tree1 只有 x 改变，且 `(3,18)` 仍是它的唯一占地。
- `m_AnimationSpeed` 和 AI 五个权重逐对象相等；不得出现未批准 YAML 对象变化。
- `git diff --check` 无输出。

任何检查失败：不继续测试，使用字节备份恢复场景并核对 MD5。通过后才重跑 Task 4 两条测试，预期 GREEN；再跑 ProjectDoctor，预期 `Footprint=0`。

- [ ] **Step 5: 截图目检与 RESUME**

在 1280×720 下进入新对局，采集原主视角和至少一个减量明显区域；确认保留草地没有整片消失、没有资源或 HUD 被误删。截图只证明该分辨率，不代替真人审美。更新 RESUME，记录删除数量、Tree1 前后坐标、场景 MD5 和备份路径。

---

### Task 5: 完整回归与交付审计

**Files:**
- Create: `docs/superpowers/reports/2026-11-14-rts-priority-fixes-map-pruning.md`
- Update: `.superpowers/sdd/priority-fixes-map-pruning/RESUME.md`

**Interfaces:**
- Consumes: Task 1–4 的聚焦日志、场景备份、截图和 MD5。
- Produces: 带实际测试数、对象级差异和验证边界的交付报告。

- [ ] **Step 1: 运行聚焦回归合集**

逐项串行运行本计划新增测试，不得并发；确认没有跳过或 Inconclusive。若某项失败，回到对应任务查根因，不得直接跑全量掩盖失败。

- [ ] **Step 2: 运行一次完整 PlayMode 套件**

先归档旧 `Logs/RegressionTests.txt`，然后执行：

```csharp
RegressionTestMenu.RunPlayModeTests();
```

预期基线为原 274 项加本计划新增测试；实际总数以 TestRunner 输出为准。要求 `Failed=0, Skipped=0, Inconclusive=0`。完成立即复制完整日志并核对 Passed 行数，不能用后续聚焦日志覆盖全量证据。

- [ ] **Step 3: 最终运行态与文件完整性**

- Console 按 `types=["error","warning"]` 查询，目标 0。
- 最终停止 PlayMode，恢复 MainMenu，`dirty=false`。
- 真实槽仍应为 424,419 字节、MD5 `0d34d758ec3bd3eb7ee954eebf411076`；若用户在此期间自行改档，则记录新事实，不得恢复用户新档。
- `git diff --check` 无输出；`git status --short` 只包含本计划批准的代码、测试、编辑器工具、场景和文档。
- 临时 `atg_midstep_stable_footprint.json*` 等探针文件为 0。

- [ ] **Step 4: 报告交付边界**

报告逐项列出根因、修改文件、RED/GREEN 证据、全量结果、Tree1/草地对象审计和截图。明确：本轮自动化点击不等同于真人 OS 键鼠；未做独立播放器、其他 DPI、长期主动运营和平衡验收；旧档中的草地仍按旧档恢复。不得声称已提交或推送。
