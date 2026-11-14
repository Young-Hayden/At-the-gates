# Fixed 2560×1440 Display and Attack Range Indicators Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 固定游戏为 2560×1440 全屏，把箭塔防御射程提高到 5.5，并为选中的兵种与箭塔显示攻击范围圈；全套自检后提交并推送 GitHub。

**Architecture:** `GameSettings` 统一固定显示；`AttackRangeIndicator` 提供可复用的世界空间圆环，`SelectionManager` 只维护选中对象的圈。`TowerGarrison` 用 5.5 作为最低防御射程。Canvas 参考保持 1920×1080，由 CanvasScaler 将既有 UI 在 2560 输出上整体放大，不重排、不重建界面。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、uGUI CanvasScaler、LineRenderer、NUnit PlayMode、Unity Test Runner、Codely Bridge MCP、Python 对象审计、现有 GitHub 同步脚本。

## Global Constraints

- 运行时固定：`2560×1440`、`FullScreenMode.FullScreenWindow`。
- 三个场景与 `LoadingScreen.prefab` 的 CanvasScaler 参考分辨率必须保持 `1920×1080`；运行输出为 2560×1440。
- 箭塔防御最低射程 `5.5`；弓手射程更高时取更高值。
- 范围圈仅选中时显示；每个选中 UnitActor 一个，选中箭塔一个。
- 圈无 Collider、无 RaycastTarget，不参与选择/寻路/战斗。
- 不修改 Tower_Blue/Tower_Red 预制体，不新增美术资源，不改存档 schema。
- 用户视觉裁定后，场景/预制体/Canvas 均不得改动；Canvas 参考保持 1920×1080，其他对象差异必须为零。
- 完整自检前不提交；发布前必须向用户展示最终变更摘要并取得推送确认。
- 不 force push；网络重置时使用现有 `127.0.0.1:7897` 代理重试。

---

### Task 1: 固定显示设置与菜单固定控件

**Files:**
- Modify: `Assets/Scripts/GameSettings.cs`
- Modify: `Assets/Scripts/UI/MenuSettingsUI.cs`
- Create: `Assets/Tests/PlayMode/FixedDisplayTests.cs`

**Interfaces:**
- Produces: `GameSettings.FixedWidth=2560`、`FixedHeight=1440`、`FixedResolutionText="2560×1440"`、`FixedFullscreenMode=FullScreenMode.FullScreenWindow`。
- Produces: `GameSettings.ApplyDisplay()` 固定调用 2560×1440。
- Produces: `MenuSettingsUI.ConfigureFixedDisplay()` 运行时收敛分辨率控件。

- [ ] **Step 1: 写失败测试**

`FixedDisplayTests.cs` 增加：

```csharp
[Test]
public void GameSettings_UsesFixed2560Fullscreen()
{
    Assert.AreEqual(2560, GameSettings.FixedWidth);
    Assert.AreEqual(1440, GameSettings.FixedHeight);
    Assert.AreEqual("2560×1440", GameSettings.FixedResolutionText);
    Assert.AreEqual(FullScreenMode.FullScreenWindow, GameSettings.FixedFullscreenMode);
}

[UnityTest]
public IEnumerator MainMenuSettings_ShowsOnlyFixedResolutionAndFullscreen()
{
    EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/MainMenu.scene",
        new LoadSceneParameters(LoadSceneMode.Single));
    yield return null;

    var ui = Object.FindFirstObjectByType<MenuSettingsUI>();
    Assert.IsNotNull(ui);
    Assert.IsNotNull(ui.resolutionButtons[0]);
    Assert.AreEqual("2560×1440",
        ui.resolutionButtons[0].GetComponentInChildren<Text>().text);
    Assert.IsFalse(ui.resolutionButtons[0].interactable);
    Assert.IsFalse(ui.resolutionButtons[1].gameObject.activeSelf);
    Assert.IsFalse(ui.resolutionButtons[2].gameObject.activeSelf);
    Assert.IsFalse(ui.fullscreenButton.interactable);
    Assert.AreEqual("全屏：固定 2560×1440", ui.fullscreenLabel.text);
}
```

- [ ] **Step 2: 运行测试确认 RED**

```csharp
RegressionTestMenu.RunPlayModeTest("FixedDisplayTests.GameSettings_UsesFixed2560Fullscreen");
RegressionTestMenu.RunPlayModeTest("FixedDisplayTests.MainMenuSettings_ShowsOnlyFixedResolutionAndFullscreen");
```

预期：常量/固定字段不存在导致编译失败；补最小常量和空方法后，菜单测试因旧按钮仍可切换而失败。记录真实 RED。

- [ ] **Step 3: 实现 GameSettings 固定显示**

删除 `ResolutionIndex` 和可变 `Fullscreen` 的运行时使用，保留无关的边缘平移/速度设置。加入：

```csharp
public const int FixedWidth = 2560;
public const int FixedHeight = 1440;
public const string FixedResolutionText = "2560×1440";
public const FullScreenMode FixedFullscreenMode = FullScreenMode.FullScreenWindow;

public static void ApplyDisplay()
{
    if (Application.isEditor) return;
    Screen.SetResolution(FixedWidth, FixedHeight, FixedFullscreenMode);
}
```

`ResetToDefaults` 只重置 EdgePan/PanSpeed。

- [ ] **Step 4: 实现菜单固定控件**

在 `MenuSettingsUI.Awake` 起始调用 `ConfigureFixedDisplay()`；不要给分辨率/全屏按钮添加切换监听。方法：

```csharp
void ConfigureFixedDisplay()
{
    for (int i = 0; i < resolutionButtons.Length; i++)
    {
        var button = resolutionButtons[i];
        if (button == null) continue;
        if (i == 0)
        {
            button.gameObject.SetActive(true);
            button.interactable = false;
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = GameSettings.FixedResolutionText;
                label.color = selectedTint;
            }
        }
        else button.gameObject.SetActive(false);
    }

    if (fullscreenButton != null) fullscreenButton.interactable = false;
    if (fullscreenLabel != null)
        fullscreenLabel.text = "全屏：固定 " + GameSettings.FixedResolutionText;
}
```

删除 `SetResolution`、`ToggleFullscreen` 与 `Refresh` 内旧分辨率/全屏分支。保留边缘平移与速度。

- [ ] **Step 5: 聚焦 GREEN**

两条测试 PASS；随后跑 MenuPolishTests，预期无回归。

---

### Task 2: 箭塔 5.5 防御与选中范围圈

**Files:**
- Create: `Assets/Scripts/AttackRangeIndicator.cs`
- Modify: `Assets/Scripts/TowerGarrison.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Modify: `Assets/Scripts/HUD.cs`
- Modify: `Assets/Tests/PlayMode/TowerGarrisonTests.cs`
- Create: `Assets/Tests/PlayMode/AttackRangeTests.cs`

**Interfaces:**
- Produces: `TowerGarrison.defenseRange=5.5f`、`TowerGarrison.ActiveRange`。
- Produces: `AttackRangeIndicator.Create(Transform)`、`Show(Vector3,float,Color,int)`、`Hide()`、`Radius`、`IsVisible`。
- Consumes: `SelectionManager.selected`、`SelectionManager.selectedBuilding`、`UnitActor.attackRange`。

- [ ] **Step 1: 写箭塔与范围圈失败测试**

在 `TowerGarrisonTests` 增加：

```csharp
[UnityTest]
public IEnumerator DefenseRange_IsFivePointFive_AndFiresAtFiveButNotSix()
{
    PaintRect(-10, -8, 28, 24);
    var tower = SpawnTower(new Vector3Int(3, 3));
    var archer = SpawnArcher(new Vector3Int(-3, -3));
    yield return null;
    var garrison = tower.GetComponent<TowerGarrison>();
    Assert.AreEqual(5.5f, garrison.defenseRange, 0.001f);
    Assert.AreEqual(5.5f, garrison.ActiveRange, 0.001f);
    Assert.IsTrue(garrison.Garrison(archer.GetComponent<UnitActor>()));
    Assert.AreEqual(5.5f, garrison.ActiveRange, 0.001f);

    var farMiss = SpawnEnemy(new Vector3Int(-3, 3));   // distance 6.0
    yield return new WaitForSeconds(0.35f);
    Assert.IsNull(FindProjectile(), "A target at 6.0 must stay outside the 5.5 tower range.");
    Object.Destroy(farMiss.gameObject);
    yield return null;

    SpawnEnemy(new Vector3Int(-2, 3));                 // distance 5.0
    yield return WaitUntil(() => FindProjectile() != null, 2f);
    Assert.IsNotNull(FindProjectile(), "A target at 5.0 must now be inside the 5.5 range.");
}
```

`AttackRangeTests.cs` 至少包含：

```csharp
[UnityTest]
public IEnumerator SelectedUnits_ShowOneRingEach_AndUpdateOnDeselect()
{
    PaintRect(-6, -6, 14, 14);
    var a = new GameObject("Range unit A");
    a.AddComponent<UnitMover>();
    var actorA = a.AddComponent<UnitActor>();
    actorA.attackRange = 3.2f;
    var b = new GameObject("Range unit B");
    b.AddComponent<UnitMover>();
    var actorB = b.AddComponent<UnitActor>();
    actorB.attackRange = 4.7f;

    Selection.selected.Clear();
    Selection.selected.Add(a.GetComponent<UnitMover>());
    Selection.selected.Add(b.GetComponent<UnitMover>());
    InvokeUpdateRings();
    yield return null;

    var rings = Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None);
    Assert.AreEqual(2, rings.Length);
    CollectionAssert.AreEquivalent(new[] { 3.2f, 4.7f }, rings.Select(r => r.Radius).ToArray());
    Assert.IsTrue(rings.All(r => r.GetComponent<Collider2D>() == null));

    Selection.selected.RemoveAt(0);
    InvokeUpdateRings();
    yield return null;
    Assert.AreEqual(1, Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None)
        .Count(r => r.IsVisible));
}

[UnityTest]
public IEnumerator SelectedFinishedTower_ShowsFivePointFive_ButHalfBuiltTowerDoesNot()
{
    PaintRect(-6, -6, 14, 14);
    var prefab = LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab");
    var tower = Object.Instantiate(prefab, Grid.CellCenter(new Vector3Int(0, 0)), Quaternion.identity);
    tower.name = "Range tower";
    var building = tower.GetComponent<Building>();
    building.Recompute();
    Selection.selected.Clear();
    Selection.selectedBuilding = building;
    InvokeUpdateRings();
    yield return null;
    var ring = Object.FindFirstObjectByType<AttackRangeIndicator>();
    Assert.IsNotNull(ring);
    Assert.AreEqual(5.5f, ring.Radius, 0.001f);

    var halfBuilt = Object.Instantiate(prefab, Grid.CellCenter(new Vector3Int(3, 0)), Quaternion.identity);
    halfBuilt.GetComponent<Building>().Recompute();
    halfBuilt.GetComponent<Building>().MarkBuiltDuringMatch();
    Selection.selectedBuilding = halfBuilt.GetComponent<Building>();
    InvokeUpdateRings();
    yield return null;
    Assert.IsFalse(Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None)
        .Any(r => r.IsVisible));
}
```

辅助 `InvokeUpdateRings()`：

```csharp
void InvokeUpdateRings()
{
    typeof(SelectionManager).GetMethod("UpdateRings",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        .Invoke(Selection, null);
}
```

- [ ] **Step 2: 运行新测试确认 RED**

预期：`defenseRange`/`ActiveRange`/`AttackRangeIndicator` 不存在导致编译 RED；补签名空壳后，5.0 命中、6.0 不命中与范围圈计数产生行为 RED。分别保留证据。

- [ ] **Step 3: 实现 AttackRangeIndicator**

新增组件，使用共享材质和 96 段 LineRenderer：

```csharp
public float Radius { get; private set; }
public bool IsVisible => line != null && line.enabled;

public static AttackRangeIndicator Create(Transform parent)
{
    var go = new GameObject("AttackRangeRing");
    if (parent != null) go.transform.SetParent(parent, false);
    return go.AddComponent<AttackRangeIndicator>();
}

public void Show(Vector3 center, float radius, Color tint, int sortingOrder)
{
    transform.position = center;
    line.enabled = true;
    line.startColor = line.endColor = tint;
    line.sortingOrder = sortingOrder;
    if (!Mathf.Approximately(Radius, radius)) Rebuild(radius);
}

public void Hide()
{
    line.enabled = false;
}
```

`Rebuild(radius)` 设置 96 段闭合圆、线宽 `0.08f`、`useWorldSpace=false`。材质用 `Shader.Find("Sprites/Default")`，静态共享。Awake 中禁用 Collider（本组件不添加任何 Collider）。

- [ ] **Step 4: 实现箭塔 5.5**

`TowerGarrison` 加入 public 字段和属性；驻塔范围用最大值：

```csharp
[Tooltip("完成建造的塔自身最低防御射程；驻塔弓手更高时取弓手射程。")]
public float defenseRange = 5.5f;
public float ActiveRange => HasArcher ? range : defenseRange;
```

`TryGarrison` 中：`range = Mathf.Max(defenseRange, candidate.attackRange);`。

HUD 的建筑详情中，若存在 `TowerGarrison` 且非半建，追加 `"\n防御射程 " + tower.ActiveRange.ToString("0.#")`。

- [ ] **Step 5: 实现 SelectionManager 圈池**

新增列表 `List<AttackRangeIndicator> rangeIndicators`，在 `UpdateRings` 末尾调用 `UpdateAttackRangeRings()`。逻辑：

- 每个选中且存活的 UnitActor → 一个圈，半径 `Mathf.Max(0.1f, actor.attackRange)`。
- 选中已建成的 TowerGarrison → 一个圈，半径 `tower.ActiveRange`。
- 圈中心跟随 transform；颜色蓝/红/金；排序 `YSort.OrderFor(y)-2`。
- 需要的圈超过池容量时只增长一次；多余圈 `Hide()`，不销毁。
- 半建/死亡/驻塔后不在选中列表的塔或单位不显示。

- [ ] **Step 6: 聚焦 GREEN 与既有塔回归**

运行新两条与 TowerGarrisonTests 全类；再跑 Selection/Command 相关聚焦类，确认无回归。记录日志。

---

### Task 3: 2560 ProjectSettings、UI 比例与视觉验证

**Files:**
- Modify: `ProjectSettings/ProjectSettings.asset`
- Verify unchanged: `Assets/Scenes/SampleScene.scene`
- Verify unchanged: `Assets/Scenes/MainMenu.scene`
- Verify unchanged: `Assets/Scenes/ModeSelect.scene`
- Verify unchanged: `Assets/Resources/LoadingScreen.prefab`
- Verify unchanged: four editor builders keep 1920×1080 reference
- Create: `Assets/Tests/PlayMode/DisplayResolutionTests.cs`

**Interfaces:**
- Consumes: `GameSettings.FixedWidth/Height` from Task 1.
- Produces: CanvasScaler 参考保持 1920×1080；PlayerSettings 默认 2560×1440 全屏。

- [ ] **Step 1: 写场景/播放器设置失败测试**

`DisplayResolutionTests.cs`：

```csharp
[Test]
public void PlayerSettings_Default2560x1440_FixedWindow()
{
    Assert.AreEqual(2560, PlayerSettings.defaultScreenWidth);
    Assert.AreEqual(1440, PlayerSettings.defaultScreenHeight);
    Assert.AreEqual(FullScreenMode.FullScreenWindow, PlayerSettings.fullScreenMode);
    Assert.IsFalse(PlayerSettings.resizableWindow);
}

[UnityTest]
public IEnumerator SampleScene_CanvasReference_Is2560x1440()
{
    EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
        new LoadSceneParameters(LoadSceneMode.Single));
    yield return null;
    foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
        Assert.AreEqual(new Vector2(2560, 1440), scaler.referenceResolution);
}

[UnityTest]
public IEnumerator MainMenu_CanvasReference_Is2560x1440()
{
    EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/MainMenu.scene",
        new LoadSceneParameters(LoadSceneMode.Single));
    yield return null;
    foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
        Assert.AreEqual(new Vector2(2560, 1440), scaler.referenceResolution);
}

[UnityTest]
public IEnumerator ModeSelect_CanvasReference_Is2560x1440()
{
    EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/ModeSelect.scene",
        new LoadSceneParameters(LoadSceneMode.Single));
    yield return null;
    foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
        Assert.AreEqual(new Vector2(2560, 1440), scaler.referenceResolution);
}

[Test]
public void LoadingScreenPrefab_CanvasReference_Is2560x1440()
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/Resources/LoadingScreen.prefab");
    Assert.IsNotNull(prefab);
    var scaler = prefab.GetComponentInChildren<CanvasScaler>(true);
    Assert.IsNotNull(scaler);
    Assert.AreEqual(new Vector2(2560, 1440), scaler.referenceResolution);
}
```

若 `PlayerSettings.fullScreenMode` 在团结版本不可用，则读取 `ProjectSettings.asset` 中 `fullscreenMode: 1` 与 `resizableWindow: 0`，不得删除测试覆盖。

**用户视觉裁定后的修正：** 上列 Canvas 测试名中的 `2560x1440` 改为 `Remains1920x1080`，期望值改为 `(1920,1080)`；PlayerSettings 测试仍保持 2560×1440 FullScreenWindow。这样同时钉住“运行 2560”和“UI 参考 1920 自动放大”。

- [ ] **Step 2: 运行测试确认 RED**

预期：PlayerSettings 仍1920×1080；修正后的 Canvas 测试在场景仍被临时改成2560时失败（证明测试能区分），随后恢复场景为1920参考并通过。

- [ ] **Step 3: 备份并修改设置/场景**

- 字节备份 MainMenu、ModeSelect、SampleScene、LoadingScreen.prefab 到 `.superpowers/sdd/fixed-display-attack-ranges/`。
- `ProjectSettings.asset`：`defaultScreenWidth: 2560`、`defaultScreenHeight: 1440`；保持 `fullscreenMode: 1`、`resizableWindow: 0`。
- **用户视觉裁定后：** 四个 CanvasScaler 必须保持 `1920×1080`，四个 Editor 构建器也保持 1920×1080；不得把参考改成 2560。
- 用 Python 对象级比较证明四个 UI 资产与备份逐字节一致；只允许 ProjectSettings 两行变化。

- [ ] **Step 4: Game View 设为 2560×1440 并跑聚焦 GREEN**

使用 `unity_gameview` 切换；若固定预设工具失败，记录失败并使用 2560×1440 的 GameView 预设，不改源码伪造结果。重跑全部 Display/Attack/Tower 聚焦测试。

- [ ] **Step 5: 视觉与性能证据**

在 2560×1440 下截图并逐张 Read：

1. 主菜单设置面板：只显示固定 2560×1440、全屏固定。
2. 对局选中弓手/战士/工人/僧侣：每人范围圈正确且可读。
3. 选中空箭塔：5.5 防御圈。
4. 多选 6 个以上单位：圈不遮挡 HUD，无异常闪烁。

性能探针：多选 8 个单位持续 3 秒，记录 GC/帧分配与对象数，确认范围圈对象数稳定、无每帧新增。

- [ ] **Step 6: 截图目录**

把最终截图复制到 `docs/screenshots/`（README 使用的发布图）并在报告中记录原始 `Logs/` 路径、尺寸和 MD5。

---

### Task 4: 完整自检、本地提交与 GitHub 发布

**Files:**
- Create: `docs/superpowers/reports/2026-11-14-rts-fixed-display-attack-ranges.md`
- Update: `.superpowers/sdd/fixed-display-attack-ranges/RESUME.md`
- Update: `.superpowers/sdd/progress.md`

**Interfaces:**
- Consumes: Task 1–3 的测试、截图、场景审计和性能数据。
- Produces: 本地提交 + 桌面 GitHub 副本提交 + 远程 SHA 核对。

- [ ] **Step 1: 完整聚焦回归**

串行运行所有新增测试与受影响的 Menu/Tower/HUD/Selection 测试。任何失败停止，不进入全量。

- [ ] **Step 2: 完整 PlayMode 套件**

先归档旧 `Logs/RegressionTests.txt`，在 2560×1440 下运行一次完整套件。预期基线 286 + 新增用例，全部 PASS，0 Failed/Skipped/Inconclusive。立即归档并核对 Passed 行数。

- [ ] **Step 3: 最终完整性闸**

- 编译 0 errors / 0 warnings。
- Console 0 Error/0 Warning。
- ProjectDoctor 0/0。
- 2560 截图逐张 Read。
- MainMenu/ModeSelect/SampleScene/LoadingScreen 的对象级审计仅允许批准改动。
- 真实存档 MD5 不变，测试临时文件为 0，编辑器 stopped、scene/project clean。
- 生成最终变更摘要和测试总数。

- [ ] **Step 4: 用户发布确认**

向用户展示：最终测试数、场景变化、分辨率证据、提交文件列表、当前本地/远程 SHA。取得明确“可以推送”确认后才执行外部推送。

- [ ] **Step 5: 本地 Git 提交**

在 `D:\Unity\projects\At the Gates`：

```bash
git add -A
git commit -m "feat: fix RTS audit issues and add attack range display"
```

提交信息结尾按系统要求加入：

```text
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

核对 `git status` clean，记录本地 SHA。

- [ ] **Step 6: 同步桌面发布副本**

在 `C:\Users\22492\Desktop\GitHub提交`：

```bash
python -I sync_from_source.py all
python -I sync_from_source.py all --apply
git -C at-the-gates status --porcelain
git -C at-the-gates diff --stat
```

确认只同步预期 Assets/ProjectSettings/Packages/docs；不得同步 Library/Temp/本地 AI 插件依赖。

- [ ] **Step 7: 提交并推送桌面仓库**

```bash
git -C at-the-gates add -A
git -C at-the-gates commit -m "feat: add fixed 2560x1440 display and attack range indicators"
git -C at-the-gates push origin main
```

若上传阶段连接重置，使用：

```bash
git -C at-the-gates -c http.proxy=http://127.0.0.1:7897 \
  -c https.proxy=http://127.0.0.1:7897 push origin main
```

禁止 force push。

- [ ] **Step 8: 远程核对与报告**

```bash
curl -s https://api.github.com/repos/Young-Hayden/At-the-gates/commits/main
```

核对远程 SHA 等于桌面仓库 HEAD，记录 URL。写最终报告与 RESUME，明确：真人 OS 键鼠、独立播放器、非2560显示器、长期平衡仍未验。
