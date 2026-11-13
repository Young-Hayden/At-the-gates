# 《兵临城下》最终玩法验收与 HUD 清晰化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不扩张玩法和保留现有像素木牌布局的前提下，完成其他玩法验收、修复可复现缺陷，并交付清晰易读的 HUD 与完整证据。

**Architecture:** 先运行既有 270 项回归与真实地图受控探针，任何产品缺陷单独走根因分析和 TDD 小修复。HUD 通过 `PolishHUD` 现有幂等机制完成样式与场景修改，`HUD.cs` 只负责运行时配色和中文生产标题；不重跑 BuildHUD。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、Unity UI (uGUI)、NUnit PlayMode、Tuanjie Codely MCP、Python 3（日志/MD5/YAML 审计）。

## Global Constraints

- 设计基线：`docs/superpowers/specs/2026-11-09-rts-final-gameplay-hud-design.md`。
- 起始分支为 `fix/tower-top-garrison`，已有驻塔修复与报告不得误算为本轮新改动。
- 不做难度系统、小地图降频、音效、整体布局重排、平衡重做或独立播放器打包。
- 保留 Cost 底边 `y=10`、Name `y=26/h=15`、Button `y=41/h=56`。用户明确批准 Cost 高度 `14→15`，顶边 25、与 Name 保留 1px；其他矩形不变。ResourceBar Backing 内缩为 `8px`，BuildBar/UnitPanel/SelectionPanel/ProductionPanel 为 `6px`。
- 底衬为黑色 alpha `0.58`；主文字 `(1.00,0.96,0.86,1)`；辅助文字 `(0.95,0.90,0.78,1)`。
- UnitName `14pt`、UnitCost `13pt`；除用户批准的 UnitCost 高度 `14→15` 外，其余矩形不变。
- 真实存档只读；临时存档只能写系统临时目录的唯一文件名并删除。
- 每次保存 `SampleScene` 前必须向用户展示风险+收益表并备份；保存后审计对象、动画速度和 AI 字段。
- 每个小阶段更新 `.superpowers/sdd/final-polish/RESUME.md`，日志及时归档。
- **不自动 git commit、merge 或 push；计划中的检查点均为文件检查点，不是提交授权。**
- 绝不同时运行两个 PlayMode 套件。

---

### Task 1: 建立最终功能基线与缺陷闸门

**Files:**
- Read: `Assets/Tests/PlayMode/*.cs`
- Read: `Logs/tower-fixes-2026-11-08-full-270.txt`
- Create: `Logs/final-polish-baseline-270.txt`
- Modify: `.superpowers/sdd/final-polish/RESUME.md`
- Create: `docs/superpowers/reports/2026-11-09-rts-final-gameplay-hud-verification.md`

**Interfaces:**
- Consumes: `RegressionTestMenu.RunPlayModeTests()`。
- Produces: 基线通过数与归档日志；只有基线通过才进入真实地图探针和 HUD 修改。

- [ ] **Step 1: 记录工作区与文件指纹**

Run:

```bash
git -C "D:/Unity/projects/At the Gates" status --short --branch
git -C "D:/Unity/projects/At the Gates" diff --check
python -I -c "from pathlib import Path; import hashlib; root=Path(r'D:/Unity/projects/At the Gates'); files=[root/'Assets/Scenes/SampleScene.scene',root/'Assets/Scenes/MainMenu.scene',Path(r'C:/Users/22492/AppData/LocalLow/DefaultCompany/兵临城下/AtTheGates/save_slot_1.json')]; [(print(p.name,p.stat().st_size,hashlib.md5(p.read_bytes()).hexdigest())) for p in files]"
```

Expected: 当前仅有驻塔两处受跟踪修改、驻塔报告和新设计文档；`diff --check` 无输出。记录三个 MD5，不修改文件。

- [ ] **Step 2: 运行完整基线**

MCP:

```csharp
RegressionTestMenu.RunPlayModeTests();
```

以 `unity_editor` 的 stopped/非编译状态和 `Logs/RegressionTests.txt` 的最终行为完成条件。Expected:

```text
[Regression] PlayMode run finished: Passed. Passed=270, Failed=0, Skipped=0, Inconclusive=0
```

- [ ] **Step 3: 立即归档基线**

Run:

```bash
python -I -c "from pathlib import Path; import shutil,hashlib; root=Path(r'D:/Unity/projects/At the Gates'); src=root/'Logs/RegressionTests.txt'; dst=root/'Logs/final-polish-baseline-270.txt'; assert not dst.exists(), dst; shutil.copyfile(src,dst); print(hashlib.md5(dst.read_bytes()).hexdigest())"
```

Expected: 新文件创建并输出 MD5。若基线不是 270/270，停止 HUD 工作；对失败项按 systematic-debugging 单独建立最小红绿修复任务，完成后重新建立基线。

- [ ] **Step 4: 写功能矩阵与检查点**

在验证报告中列出建造/经济、选择/编队/巡逻、采集/维修、生产/人口、战斗/驻塔/治疗、科技/维护费、AI、存档/生命周期、HUD/菜单。每项标注“既有套件”“真实地图探针”或“截图”，不得写成“全部无误”。更新 RESUME 的 Task 1 结果与日志 MD5。

---

### Task 2: 真实地图移动、采集、战斗与临时存档探针

**Files:**
- Create: `.superpowers/sdd/final-polish/GameplayAcceptanceProbe.cs`
- Create: `Logs/final-gameplay-live-2026-11-09.txt`
- Modify: `.superpowers/sdd/final-polish/RESUME.md`
- Modify: `docs/superpowers/reports/2026-11-09-rts-final-gameplay-hud-verification.md`

**Interfaces:**
- Consumes: `SelectionManager.IssueMove(Vector3)`、`Gatherer.TargetNode`、`Gatherer.Carrying`、`SaveGameService.SaveToPath`、`TryReadFromPath`。
- Produces: 真实 SampleScene 的 PASS/FAIL 证据；不写产品代码、不保存场景。

- [ ] **Step 1: 给出运行时风险表并创建探针**

| 改动 | 收益 | 风险 | 缓解/回滚 | 不可验证项 |
|---|---|---|---|---|
| PlayMode 移动现有单位、生成一个临时红靶、写唯一临时存档 | 验证真实地图输入/采集/战斗/存档格式 | 低：运行中单位状态变化 | 禁用 AI；停止播放回滚；临时文件 finally 删除；真实存档 MD5 对照 | 不等于真人鼠标和长期平衡验收 |

创建以下完整脚本：

```csharp
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

IEnumerator Until(Func<bool> predicate, float seconds)
{
    float deadline = Time.time + seconds;
    while (!predicate() && Time.time < deadline) yield return null;
}

IEnumerator Run()
{
    string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs",
        "final-gameplay-live-2026-11-09.txt"));
    File.WriteAllText(path, "FINAL GAMEPLAY LIVE PROBE\n");
    int failures = 0;
    Action<bool, string, string> Record = (ok, name, details) =>
    {
        if (!ok) failures++;
        string line = (ok ? "PASS " : "FAIL ") + name + " " + details;
        File.AppendAllText(path, line + "\n");
        Debug.Log("[FinalGameplay] " + line);
    };

    if (!Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.scene")
    {
        Record(false, "precondition", "requires SampleScene PlayMode");
        yield break;
    }
    foreach (var ai in UnityEngine.Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
        ai.enabled = false;

    var grid = MapGrid.Instance;
    var selection = UnityEngine.Object.FindFirstObjectByType<SelectionManager>();
    UnitActor archer = null, worker = null;
    foreach (var actor in UnitActor.All)
    {
        if (actor == null || !actor.enabled || !actor.IsAlive || actor.team != Team.Blue) continue;
        if (archer == null && actor.projectile != null) archer = actor;
        if (worker == null && actor.GetComponent<Gatherer>() != null && actor.GetComponent<Gatherer>().Carrying == 0)
            worker = actor;
    }
    if (grid == null || selection == null || archer == null || worker == null)
    {
        Record(false, "precondition", "grid/selection/archer/empty-handed worker missing");
        yield break;
    }

    // Real-map ordinary movement.
    var mover = archer.GetComponent<UnitMover>();
    Vector3Int start = grid.WorldToCell(archer.transform.position);
    int region = grid.Region(start);
    Vector3Int? destination = null;
    foreach (var offset in new[] { new Vector3Int(2,0,0), new Vector3Int(-2,0,0),
        new Vector3Int(0,2,0), new Vector3Int(0,-2,0), new Vector3Int(3,0,0) })
    {
        var candidate = grid.NearestWalkableInRegion(start + offset, 2, region);
        if (candidate.HasValue && candidate.Value != start && grid.FindPath(start, candidate.Value) != null)
        {
            destination = candidate;
            break;
        }
    }
    if (!destination.HasValue)
    {
        Record(false, "ordinary-move", "no reachable control destination");
    }
    else
    {
        selection.selected.Clear();
        selection.selectedBuilding = null;
        selection.selected.Add(mover);
        selection.IssueMove(grid.CellCenter(destination.Value));
        Record(mover.Moving, "ordinary-move-command", "destination=" + destination.Value);
        yield return Until(() => !mover.Moving, 12f);
        Record(Vector3.Distance(archer.transform.position, grid.CellCenter(destination.Value)) < 0.2f,
            "ordinary-move-arrival", "position=" + archer.transform.position);
    }

    // Real-map explicit gather order and actual harvest.
    var gatherer = worker.GetComponent<Gatherer>();
    var workerMover = worker.GetComponent<UnitMover>();
    ResourceNode node = null;
    float best = float.PositiveInfinity;
    Vector3Int workerStart = grid.WorldToCell(worker.transform.position);
    int workerRegion = grid.Region(workerStart);
    foreach (var candidate in ResourceNode.All)
    {
        if (candidate == null || candidate.amount <= 0) continue;
        var near = grid.NearestWalkableInRegion(grid.WorldToCell(candidate.transform.position), 4, workerRegion);
        if (!near.HasValue || grid.FindPath(workerStart, near.Value) == null) continue;
        float distance = (candidate.transform.position - worker.transform.position).sqrMagnitude;
        if (distance < best) { best = distance; node = candidate; }
    }
    if (node == null)
    {
        Record(false, "gather-command", "no reachable non-empty resource");
    }
    else
    {
        int before = gatherer.Carrying;
        selection.selected.Clear();
        selection.selectedBuilding = null;
        selection.selected.Add(workerMover);
        selection.IssueMove(node.transform.position);
        Record(gatherer.TargetNode == node, "gather-command", "node=" + node.name);
        yield return Until(() => gatherer.Carrying > before, 45f);
        Record(gatherer.Carrying > before, "gather-harvest",
            "carrying=" + gatherer.Carrying + " before=" + before);
    }

    // Real-map ranged combat against one PlayMode-only red target.
    var archerCell = grid.WorldToCell(archer.transform.position);
    var targetCell = grid.NearestWalkableInRegion(archerCell + new Vector3Int(2,0,0),
        3, grid.Region(archerCell));
    if (!targetCell.HasValue)
    {
        Record(false, "ranged-combat", "no nearby walkable target cell");
    }
    else
    {
        var targetObject = new GameObject("Final gameplay red target (PlayMode only)");
        targetObject.transform.position = grid.CellCenter(targetCell.Value);
        var target = targetObject.AddComponent<UnitActor>();
        target.team = Team.Red;
        target.maxHealth = 30;
        target.health = 30;
        target.isCombatant = true;
        target.showHealthBar = false;
        archer.GetComponent<CombatAI>().ClearOrder();
        yield return Until(() => target == null || target.health < 30, 6f);
        Record(target == null || target.health < 30, "ranged-combat",
            target == null ? "target destroyed" : "health=" + target.health);
    }

    // Save format round-trip outside the real slot.
    string temporary = Path.Combine(Path.GetTempPath(), "atg-final-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        bool saved = snapshot != null && SaveGameService.SaveToPath(snapshot, temporary, out string saveError);
        bool read = saved && SaveGameService.TryReadFromPath(temporary, out var loaded, out string readError) && loaded != null;
        Record(read, "temporary-save-roundtrip", "capture=" + captureError + " path=" + temporary);
    }
    finally
    {
        foreach (string file in new[] { temporary, temporary + ".tmp", temporary + ".bak" })
            if (File.Exists(file)) File.Delete(file);
    }

    File.AppendAllText(path, "SUMMARY failures=" + failures + "\n");
    Debug.Log("[FinalGameplay] SUMMARY failures=" + failures);
}

return Run();
```

- [ ] **Step 2: 打开真实场景并执行**

MCP editor:

```csharp
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene");
```

进入 PlayMode 后以 `script_path` 执行探针。Expected: `final-gameplay-live-2026-11-09.txt` 包含 ordinary move、gather command/harvest、ranged combat、temporary save roundtrip 各项 PASS，最后 `SUMMARY failures=0`。

- [ ] **Step 3: 停止播放并处理结果**

停止 PlayMode，不保存场景。若任一项 FAIL：保留日志、复现并定位根因；新缺陷使用独立测试文件和最小修复，不把修复混入 HUD diff。全部通过后，在报告矩阵中只给实际测得的路径标 PASS，交付/维修/暂停等仍引用 Task 1 的既有套件。

---

### Task 3: HUD 四项失败回归

**Files:**
- Create: `Assets/Tests/PlayMode/HudReadability20261109Tests.cs`
- Create: `Assets/Tests/PlayMode/HudReadability20261109Tests.cs.meta`（由 Unity 生成）
- Modify: `.superpowers/sdd/final-polish/RESUME.md`

**Interfaces:**
- Consumes: `HUD.selectionPanel`、`productionPanel`、`unitPanel`、`unitNames`、`unitCosts`、`productionTitle`、`BuildingTitle`（实现阶段复用）。
- Produces: 四个生产行为回归，后续 Task 4/5 必须使其转绿。

- [ ] **Step 1: 写完整失败测试**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class HudReadability20261109Tests
{
    static readonly Color MainTint = new Color(1f, 0.96f, 0.86f, 1f);

    [UnityTest]
    public IEnumerator TargetPanels_UseDarkReadableBackings()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        AssertBacking(hud.goldText.transform.parent.gameObject, "ResourceBar", 8f);
        AssertBacking(hud.buildButtons[0].transform.parent.gameObject, "BuildBar", 6f);
        AssertBacking(hud.unitPanel, "UnitPanel", 6f);
        AssertBacking(hud.selectionPanel, "SelectionPanel", 6f);
        AssertBacking(hud.productionPanel, "ProductionPanel", 6f);
    }

    [UnityTest]
    public IEnumerator UnitLabels_UseReadableSizes_AndStillFit()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        for (int i = 0; i < 5; i++)
        {
            var name = hud.unitNames[i];
            var cost = hud.unitCosts[i];
            Assert.AreEqual(14, name.fontSize, "UnitName " + i);
            Assert.AreEqual(13, cost.fontSize, "UnitCost " + i);
            Assert.LessOrEqual(name.preferredWidth, name.rectTransform.rect.width + 0.01f);
            Assert.LessOrEqual(name.preferredHeight, name.rectTransform.rect.height + 0.01f);
            Assert.LessOrEqual(cost.preferredWidth, cost.rectTransform.rect.width + 0.01f);
            Assert.LessOrEqual(cost.preferredHeight, cost.rectTransform.rect.height + 0.01f);
        }
    }

    [UnityTest]
    public IEnumerator ProductionTitle_UsesChineseBuildingName()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        var selection = Object.FindFirstObjectByType<SelectionManager>();
        SpawnBuilding("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab", new Vector3(-18, 8, 0));
        var barracks = GameObject.Find("Barracks_Blue").GetComponent<Building>();
        selection.selected.Clear();
        selection.selectedBuilding = barracks;
        yield return null;
        yield return null;
        StringAssert.StartsWith("兵营", hud.productionTitle.text);
    }

    [UnityTest]
    public IEnumerator NormalUnitCost_KeepsMainTint_AfterRuntimeRefresh()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        SpawnBuilding("Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab", new Vector3(-15, 10, 0));
        SpawnBuilding("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab", new Vector3(-18, 8, 0));
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(hud.unitOffers[0].cost.ToString(), hud.unitCosts[0].text);
        AssertColor(MainTint, hud.unitCosts[0].color);
    }

    static void AssertBacking(GameObject panel, string name, float inset)
    {
        Assert.IsNotNull(panel, name);
        var backing = panel.transform.Find("Backing") as RectTransform;
        Assert.IsNotNull(backing, name + "/Backing");
        var image = backing.GetComponent<Image>();
        Assert.IsNotNull(image);
        Assert.AreEqual(0.58f, image.color.a, 0.001f, name + " backing alpha");
        Assert.AreEqual(new Vector2(inset, inset), backing.offsetMin, name + " inset min");
        Assert.AreEqual(new Vector2(-inset, -inset), backing.offsetMax, name + " inset max");
        Assert.AreEqual(0, backing.GetSiblingIndex(), name + " backing must be behind content");
        Assert.IsFalse(image.raycastTarget, name + " backing must not steal clicks");
    }

    static Building SpawnBuilding(string path, Vector3 position)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        var go = Object.Instantiate(prefab, position, Quaternion.identity);
        go.name = prefab.name;
        var building = go.GetComponent<Building>();
        building.Recompute();
        return building;
    }

    static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.001f);
        Assert.AreEqual(expected.g, actual.g, 0.001f);
        Assert.AreEqual(expected.b, actual.b, 0.001f);
        Assert.AreEqual(expected.a, actual.a, 0.001f);
    }

    static IEnumerator LoadSample()
    {
        Time.timeScale = 1f;
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
            new UnityEngine.SceneManagement.LoadSceneParameters(
                UnityEngine.SceneManagement.LoadSceneMode.Single));
        yield return null;
        yield return null;
        foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
            ai.enabled = false;
    }
}
```

- [ ] **Step 2: 编译并逐项确认 RED**

先 `unity_editor request_compile`，再分别调用：

```csharp
RegressionTestMenu.RunPlayModeTest("HudReadability20261109Tests.TargetPanels_UseDarkReadableBackings");
RegressionTestMenu.RunPlayModeTest("HudReadability20261109Tests.UnitLabels_UseReadableSizes_AndStillFit");
RegressionTestMenu.RunPlayModeTest("HudReadability20261109Tests.ProductionTitle_UsesChineseBuildingName");
RegressionTestMenu.RunPlayModeTest("HudReadability20261109Tests.NormalUnitCost_KeepsMainTint_AfterRuntimeRefresh");
```

每次等 `Logs/RegressionTests.txt` 写出最终结果并单独归档。Expected: 分别因 Backing alpha/缺失、13/12pt、英文生产标题、旧正常造价色而 FAIL；不能是编译错误或空场景错误。若测试反而 PASS，先修正测试判别力，禁止继续实现。

---

### Task 4: 实现运行时配色、中文生产标题和增量样式入口

**Files:**
- Modify: `Assets/Scripts/HUD.cs:324-337,577-588`
- Modify: `Assets/Editor/PolishHUD.cs:66-71,330-413,470-496`
- Create: `Assets/Editor/ApplyHudReadability20261109.cs`
- Create: `Assets/Editor/ApplyHudReadability20261109.cs.meta`（由 Unity 生成）
- Modify: `.superpowers/sdd/final-polish/RESUME.md`

**Interfaces:**
- Consumes: Task 3 四项测试与 `PolishHUD.Apply(string,string,string)`。
- Produces: `ApplyHudReadability20261109.Apply()`；`BuildingTitle(Building)` 同时服务选中/生产标题；统一 UnitCost 正常色。

- [ ] **Step 1: 修改 PolishHUD 样式常量与字号**

```csharp
static readonly Color BackingTint = new Color(0f, 0f, 0f, 0.58f);
static readonly Color CaptionTint = new Color(0.95f, 0.90f, 0.78f, 1f);
static readonly Color ValueTint = new Color(1f, 0.96f, 0.86f, 1f);
static readonly Color CostTint = new Color(1f, 0.96f, 0.86f, 1f);
```

`PolishUnitPanel` 中修改两处字号，并将 UnitCost 高度从 14 调整为 15（用户批准的实测适配）；Name/Cost 底边和 Button 布局不变：

```csharp
var nameText = EnsureText(name, 14, TextAnchor.MiddleCenter, ValueTint, ref changed);
changed |= SetRect(panelCost, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                   new Vector2(x, UnitCostY), new Vector2(76f, 15f));
var costText = EnsureText(panelCost, 13, TextAnchor.MiddleCenter, CostTint, ref changed);
```

- [ ] **Step 2: 给生产与选中面板补 Backing**

将 `PolishProductionPanel` 完整替换为：

```csharp
static bool PolishProductionPanel(HUD hud)
{
    if (hud.productionPanel == null) return false;
    var panel = hud.productionPanel.transform as RectTransform;
    bool changed = EnsureBacking(panel, "Backing", new Vector2(6f, 6f), new Vector2(-6f, -6f), BackingTint);
    if (hud.productionTitle != null)
        EnsureText(hud.productionTitle.rectTransform, 20, TextAnchor.MiddleLeft, ValueTint, ref changed);
    if (hud.produceCosts != null)
        foreach (var text in hud.produceCosts)
            if (text != null)
                EnsureText(text.rectTransform, 10, TextAnchor.MiddleCenter, text.color, ref changed);
    Line("Production panel: inset readable backing and fitted cost/refusal text.");
    return changed;
}
```

将 `PolishSelectionPanel` 完整替换为：

```csharp
static bool PolishSelectionPanel(HUD hud)
{
    if (hud.selectionPanel == null) return false;
    var panel = hud.selectionPanel.transform as RectTransform;
    bool changed = EnsureBacking(panel, "Backing", new Vector2(6f, 6f), new Vector2(-6f, -6f), BackingTint);
    if (hud.selectionTitle != null)
        EnsureText(hud.selectionTitle.rectTransform, 22, TextAnchor.MiddleLeft, ValueTint, ref changed);
    var body = hud.selectionBody;
    if (body == null) return changed;
    EnsureText(body.rectTransform, 16, TextAnchor.UpperLeft, CaptionTint, ref changed);
    if (!Mathf.Approximately(body.lineSpacing, 1.04f)) { body.lineSpacing = 1.04f; changed = true; }
    Line("Selection panel: inset readable backing; body 16 pt / 1.04 spacing.");
    return changed;
}
```

- [ ] **Step 3: 修改 HUD 运行时颜色与生产标题**

在 `HUD.cs` 增加：

```csharp
static readonly Color UnitCostNormal = new Color(1f, 0.96f, 0.86f, 1f);
```

`RefreshProduction` 标题两处改为：

```csharp
productionTitle.text = BuildingTitle(building) + "   建造中 "
    + Mathf.RoundToInt(building.ConstructionProgress * 100f) + "%";
```

```csharp
productionTitle.text = BuildingTitle(building) + q;
```

`RefreshUnitPanel` 正常造价分支改为：

```csharp
else { unitCosts[i].text = offer.cost.ToString(); unitCosts[i].color = UnitCostNormal; }
```

- [ ] **Step 4: 创建薄增量入口**

```csharp
using UnityEditor;

/// <summary>Final readability pass; reuses the existing idempotent backup,
/// layout validation and save pipeline. This never rebuilds the HUD.</summary>
public static class ApplyHudReadability20261109
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string BackupPath = ".superpowers/sdd/final-polish/sample-before.scene";
    const string LogPath = ".superpowers/sdd/final-polish/hud-readability-tool.log";

    [MenuItem("Tools/UI/Final HUD Readability 2026-11-09")]
    public static void Apply()
    {
        PolishHUD.Apply(ScenePath, BackupPath, LogPath);
    }
}
```

- [ ] **Step 5: 编译检查，不提前保存场景**

运行 `unity_editor request_compile`。Expected: 0 errors、0 warnings。此时只完成代码/工具实现；不要在未给出下一场景风险表前调用 Apply。

---

### Task 5: 场景增量保存、聚焦转绿与对象审计

**Files:**
- Modify: `Assets/Scenes/SampleScene.scene`
- Create: `.superpowers/sdd/final-polish/sample-before.scene`
- Create: `.superpowers/sdd/final-polish/hud-readability-tool.log`
- Create: `Logs/final-hud-red-*.txt`、`Logs/final-hud-green-*.txt`
- Modify: `.superpowers/sdd/final-polish/RESUME.md`

**Interfaces:**
- Consumes: `ApplyHudReadability20261109.Apply()` 与 Task 3 测试。
- Produces: 两个新 Backing、目标样式变化、无附带场景噪声；四项聚焦测试通过。

- [ ] **Step 1: 保存前再次向用户展示风险表**

| 改动 | 收益 | 风险 | 缓解/回滚 | 不可验证项 |
|---|---|---|---|---|
| 修改现有 Backing/文字；SelectionPanel、ProductionPanel 各新增一个 Backing | 提高正常与警告状态可读性 | 中低：遮挡、文字溢出、Unity 序列化噪声 | 整场景字节备份；幂等入口；对象/动画/AI 审计；发现噪声字节还原 | 非 16:9、极端 DPI、真人审美 |

只有展示该表后才执行下一步；本计划本身不代替执行时的显式风险提示。

- [ ] **Step 2: 执行场景修补**

在 stopped、MainMenu 非 dirty、编译完成后调用：

```csharp
ApplyHudReadability20261109.Apply();
```

Expected: 工具日志包含 backup、MD5 before/after、Layout validated、Scene saved。若校验失败，入口不得保存场景。

- [ ] **Step 3: 运行四项聚焦 GREEN**

逐个运行 Task 3 四个完整测试名并分别归档。Expected: 每项 `Passed=1, Failed=0`。若失败，只修与断言对应的实现；不得为通过而放宽 Backing、字号、中文或配色要求。

- [ ] **Step 4: 审计场景**

Run:

```bash
python -I -c "from pathlib import Path; import re,collections; root=Path(r'D:/Unity/projects/At the Gates'); before=(root/'.superpowers/sdd/final-polish/sample-before.scene').read_text(encoding='utf-8'); after=(root/'Assets/Scenes/SampleScene.scene').read_text(encoding='utf-8'); ids=lambda s: re.findall(r'^--- !u!\d+ &(-?\d+)',s,re.M); b,a=ids(before),ids(after); print('objects',len(b),len(a)); print('added',len(set(a)-set(b)),'removed',len(set(b)-set(a))); speeds=lambda s: re.findall(r'^\s*m_AnimationSpeed:.*$',s,re.M); print('animationSpeed_equal',speeds(before)==speeds(after)); fields=r'^\s*(workerWeight|warriorWeight|lancerWeight|archerWeight|monkWeight):.*$'; ai=lambda s: re.findall(fields,s,re.M); print('aiWeights_equal',ai(before)==ai(after)); assert not set(b)-set(a); assert speeds(before)==speeds(after); assert ai(before)==ai(after)"
```

Expected: removed=0、animationSpeed_equal=True、aiWeights_equal=True。新增只能来自 SelectionPanel/ProductionPanel 的 Backing GameObject/RectTransform/Image/CanvasRenderer（两套共 8 个 YAML 对象）；如有其他新增或动画/AI 差异，按已批准的序列化噪声清理或回滚流程处理。

- [ ] **Step 5: 幂等与断点**

重新调用 `ApplyHudReadability20261109.Apply()`，Expected: `No changes needed — scene not saved`，第二次 MD5 不变。将四项 RED/GREEN 日志、场景 MD5、对象审计结果写入 RESUME。

---

### Task 6: 两种分辨率截图、全量回归与最终交付

**Files:**
- Create: `.superpowers/sdd/final-polish/HudScreenshotProbe.cs`
- Create: `Logs/final-hud-warning-1920x1080.png`、`final-hud-normal-1920x1080.png`
- Create: `Logs/final-hud-warning-1280x720.png`、`final-hud-normal-1280x720.png`
- Create: `Logs/final-polish-full-274.txt`
- Modify: `docs/superpowers/reports/2026-11-09-rts-final-gameplay-hud-verification.md`
- Modify: `.superpowers/sdd/final-polish/RESUME.md`

**Interfaces:**
- Consumes: Task 2 玩法证据、Task 5 场景与四项测试。
- Produces: 四张实看图、最终全量日志和交付报告。若 Task 2 新增了缺陷测试，最终数按实际增加，不强行写成 274。

- [ ] **Step 1: 创建双状态截图探针**

```csharp
using System.Collections;
using System.IO;
using UnityEngine;

IEnumerator Capture(string path)
{
    yield return null;
    yield return null;
    ScreenCapture.CaptureScreenshot(path);
    yield return null;
    yield return null;
    Debug.Log("[FinalHudScreenshot] " + path);
}

IEnumerator Run()
{
    foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
        ai.enabled = false;
    var selection = Object.FindFirstObjectByType<SelectionManager>();
    UnitActor archer = null;
    foreach (var actor in UnitActor.All)
        if (actor.team == Team.Blue && actor.IsAlive && actor.projectile != null) { archer = actor; break; }
    selection.selected.Clear();
    selection.selectedBuilding = null;
    selection.selected.Add(archer.GetComponent<UnitMover>());
    string suffix = Screen.width + "x" + Screen.height;
    yield return Capture(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs",
        "final-hud-warning-" + suffix + ".png")));

    var castlePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab");
    var barracksPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab");
    Object.Instantiate(castlePrefab, new Vector3(-15,10,0), Quaternion.identity).name = castlePrefab.name;
    var barracks = Object.Instantiate(barracksPrefab, new Vector3(-18,8,0), Quaternion.identity);
    barracks.name = barracksPrefab.name;
    selection.selected.Clear();
    selection.selectedBuilding = barracks.GetComponent<Building>();
    yield return null;
    yield return null;
    yield return Capture(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs",
        "final-hud-normal-" + suffix + ".png")));
}

return Run();
```

- [ ] **Step 2: 分别截取 1920×1080 与 1280×720**

每种分辨率执行：打开全新 SampleScene → PlayMode；`unity_gameview set_resolution` 使用 `width=1920,height=1080` 或 `width=1280,height=720`；运行截图探针；停止播放且不保存。第二分辨率前重新加载场景，避免第一轮的临时工厂残留。

Expected: 四张 PNG 均实际存在且尺寸与文件名一致。逐张 Read：资源/兵种栏、选中面板、生产面板无出屏、无文字裁切、Backing 不遮文字；warning 与 normal 配色均可读。若截图不合格，回到对应样式最小修改并重跑聚焦/截图。

- [ ] **Step 3: 最终完整回归**

在 stopped 状态执行 `RegressionTestMenu.RunPlayModeTests()`，立即归档为 `Logs/final-polish-full-274.txt`（若新增缺陷测试则使用实际数量命名）。Expected: 全部 PASS、0 Failed、0 Skipped。记录耗时和 MD5。

- [ ] **Step 4: 最终完整性与 Console**

恢复 `MainMenu.scene`，确认 stopped、未编译、dirty=false。Console 若日志过长，用 Python 完整解析 JSON 并统计 type，不截断目测。复核 SampleScene/MainMenu/真实存档 MD5；SampleScene 只能有批准的 HUD 差异，MainMenu 与真实存档必须回到原始指纹。

- [ ] **Step 5: 写最终报告与检查点**

报告必须包含：功能矩阵及证据等级、Task 2 实测、任何新缺陷根因与红绿记录、HUD 前后差异、四张截图、聚焦与全量结果、Console 统计、场景对象审计、真实存档未变、未验证边界。更新 RESUME 为“已交付，未提交”。不自动提交或合并；是否提交由用户决定。
