# RTS First-Batch Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix D01, D02, D03, D04, A04, C01 and B09 with regression coverage, then report verified results and re-evaluate the next stage.

**Architecture:** Keep the existing runtime architecture. Use narrow scene/editor repairs for HUD and quick-build configuration, parameterized existing input paths for deterministic tests, a restore-only garrison entry for legacy saves, and a shared AI worker budget across all factories. Run everything through the existing isolated PlayMode suite and `RegressionTestMenu` logging.

**Tech Stack:** Tuanjie 2022.3.62t5, Unity Test Framework 1.1.33, NUnit, C#, UGUI, TestRunnerApi, MCP Tuanjie bridge.

## Global Constraints

- Only the seven approved defects, their tests, test-runner filtering and final verification report are in scope.
- Do not modify A01/A02 command behaviour, F01 pathfinding, unit balance, prices, waves, population, LOS or physics settings.
- Do not run full `BuildHUD.Rebuild`; preserve the existing HUD and all unrelated uncommitted work.
- Do not commit or push. Do not revert or overwrite unrelated user changes.
- Every defect first gets a failing regression or a recorded baseline failure, then the minimal implementation, then a green targeted run.
- Existing baseline failures must be reported separately from regressions caused by this work.
- A task is not complete because code compiled: its targeted behaviour must pass, and the final full suite plus runtime checks must be reported honestly.

## File Map

- `Assets/Editor/RegressionTestMenu.cs`: add exact-full-name filtered PlayMode runs while preserving the existing menu.
- `Assets/Editor/BuildHUD.cs`: create missing production icon/cost content and provide a narrow current-scene repair entry.
- `Assets/Scenes/SampleScene.scene`: bind production content and configure six quick-build prefabs; no layout rebuild.
- `Assets/Scripts/SelectionManager.cs`: make left-press origin explicit and testable; reject invalid garrison orders before pathing.
- `Assets/Scripts/RTSCamera.cs`: parameterize middle-drag/wheel handling and gate modal/UI input.
- `Assets/Scripts/BuildingPlacer.cs`: route numeric keys through a testable indexed quick-build entry.
- `Assets/Scripts/TowerGarrison.cs`: reject normal under-construction garrisoning and suppress firing until completion.
- `Assets/Scripts/SaveGameService.cs`: use restore-only garrison entry for legacy in-progress saves.
- `Assets/Scripts/Gatherer.cs`: resume loaded non-manual cargo through the existing drop-off state.
- `Assets/Scripts/AIController.cs`: share worker budget across living workers and all queued workers.
- `Assets/Tests/PlayMode/FirstBatchSceneTests.cs`: actual `SampleScene` wiring/configuration tests.
- `Assets/Tests/PlayMode/InputGateTests.cs`: deterministic selection/camera input tests.
- `Assets/Tests/PlayMode/TowerGarrisonTests.cs`: construction and legacy-restore garrison tests.
- `Assets/Tests/PlayMode/MovementAndCommandTests.cs`: loaded-cargo delivery and manual-idle tests.
- `Assets/Tests/PlayMode/AIProductionBudgetTests.cs`: living/queued/multi-factory worker-budget tests.
- `docs/superpowers/reports/2026-10-26-rts-first-batch-verification.md`: baseline, final results, runtime evidence and next-stage review.

---

### Task 1: Establish baseline and filtered test execution

**Files:**
- Modify: `Assets/Editor/RegressionTestMenu.cs:115-153`
- Create: `docs/superpowers/reports/2026-10-26-rts-first-batch-verification.md`

**Interfaces:**
- Consumes: existing `Tools/Regression/Run PlayMode Tests` and `Logs/RegressionTests.txt`.
- Produces: `RegressionTestMenu.RunPlayModeTest(string fullName)` for targeted red/green runs.

- [ ] **Step 1: Run the untouched full baseline**

Use MCP to request compilation, wait for idle, then execute:

```csharp
RegressionTestMenu.RunPlayModeTests();
return "Baseline PlayMode run started";
```

Wait for the run to finish and read `Logs/RegressionTests.txt`. Record test count, passed/failed/skipped counts, every failure and stack trace in the verification report. Do not treat the historical 56-test result as this baseline.

- [ ] **Step 2: Add an exact-name filtered entry without changing the menu**

Keep `RunPlayModeTests()` as the menu entry and route both methods through one private method:

```csharp
[MenuItem("Tools/Regression/Run PlayMode Tests")]
public static void RunPlayModeTests() { RunTests(null); }

public static void RunPlayModeTest(string fullName)
{
    if (string.IsNullOrEmpty(fullName)) throw new System.ArgumentException("A full test name is required", nameof(fullName));
    RunTests(new[] { fullName });
}

static void RunTests(string[] testNames)
{
    if (IsMenuOwnedRun && !EditorApplication.isPlayingOrWillChangePlaymode)
        SessionState.SetBool(OwnedRunKey, false);
    if (runner != null || IsMenuOwnedRun)
    {
        Debug.LogWarning("[Regression] A PlayMode test run is already active.");
        return;
    }

    EnsureCallbacks();
    SessionState.SetBool(OwnedRunKey, true);
    runner = ScriptableObject.CreateInstance<TestRunnerApi>();
    try
    {
        runner.Execute(new ExecutionSettings
        {
            filters = new[]
            {
                new Filter
                {
                    testMode = TestMode.PlayMode,
                    assemblyNames = new[] { TestAssembly },
                    testNames = testNames
                }
            }
        });
    }
    catch
    {
        SessionState.SetBool(OwnedRunKey, false);
        DestroyRunnerObjects(immediate: true);
        throw;
    }
}
```

- [ ] **Step 3: Verify the filtered runner**

Compile, then run:

```csharp
RegressionTestMenu.RunPlayModeTest("SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing");
return "Filtered control started";
```

Expected: log starts with `Test cases: 1` and finishes with `Passed=1, Failed=0`. If the full name differs, use the exact name printed by the baseline log; do not broaden the filter.

---

### Task 2: D01 production panel content and narrow scene repair

**Files:**
- Create: `Assets/Tests/PlayMode/FirstBatchSceneTests.cs`
- Modify: `Assets/Editor/BuildHUD.cs:125-145,430-489`
- Modify: `Assets/Scenes/SampleScene.scene`

**Interfaces:**
- Produces: `BuildHUD.RepairProductionPanel()` menu `Tools/首批修复/补齐工厂生产面板` and working `HUD.produceIcons/produceCosts` bindings.

- [ ] **Step 1: Write the failing actual-scene test**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class FirstBatchSceneTests : RegressionFixture
{
    [UnityTest]
    public IEnumerator ProductionPanel_HasBoundContent_AndRealFactoryCanQueue()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            "Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud);
        Assert.AreEqual(3, hud.produceButtons.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.IsNotNull(hud.produceButtons[i], $"Produce{i} button");
            Assert.IsNotNull(hud.produceIcons[i], $"Produce{i} icon binding");
            Assert.IsNotNull(hud.produceCosts[i], $"Produce{i} cost binding");
        }

        var prefab = LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab");
        var factoryGO = Object.Instantiate(prefab, new Vector3(0f, 0f, 0f), Quaternion.identity);
        var building = factoryGO.GetComponent<Building>();
        var factory = factoryGO.GetComponent<ProductionBuilding>();
        Object.FindFirstObjectByType<SelectionManager>().selectedBuilding = building;
        yield return null;

        Assert.IsTrue(hud.productionPanel.activeSelf);
        Assert.IsTrue(hud.produceButtons[0].gameObject.activeSelf);
        Assert.IsNotNull(hud.produceIcons[0].sprite);
        Assert.IsNotEmpty(hud.produceCosts[0].text);
        hud.produceButtons[0].onClick.Invoke();
        Assert.AreEqual(1, factory.Queue.Count);
    }
}
```

Run exact test. Expected before repair: FAIL on a null icon or cost binding.

- [ ] **Step 2: Make content creation explicit instead of conditional on placeholder data**

Add optional `bool alwaysCreateContent = false` to `IconButton`; pass `alwaysCreateContent: true` only for `Produce0/1/2`. Replace the icon/cost blocks with helpers so repair and generation share the same layout:

```csharp
static Image EnsureIcon(RectTransform parent, Sprite sprite)
{
    var existing = parent.Find("Icon")?.GetComponent<Image>();
    if (existing != null) { existing.sprite = sprite; return existing; }
    var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
    var rt = go.GetComponent<RectTransform>();
    rt.SetParent(parent, false);
    rt.anchorMin = Vector2.zero;
    rt.anchorMax = Vector2.one;
    rt.offsetMin = new Vector2(5, 17);
    rt.offsetMax = new Vector2(-5, -5);
    var image = go.GetComponent<Image>();
    image.sprite = sprite;
    image.preserveAspect = true;
    image.raycastTarget = false;
    return image;
}

static Text EnsureCost(RectTransform parent, Font font, string value)
{
    var existing = parent.Find("Cost")?.GetComponent<Text>();
    if (existing != null) { existing.text = value; return existing; }
    var text = Label(parent, "Cost", new Vector2(0, 0), new Vector2(0, -1),
        new Vector2(0, 2), parent.sizeDelta.x, 16, 11,
        TextAnchor.MiddleCenter, new Color(1f, 0.97f, 0.85f));
    text.font = font;
    text.text = value;
    return text;
}
```

In `IconButton`, call `EnsureIcon` when `icon != null || alwaysCreateContent`, and `EnsureCost` when `!string.IsNullOrEmpty(cost) || alwaysCreateContent`. Preserve existing `IconSwap` behaviour for buttons that supply pressed/disabled icons.

- [ ] **Step 3: Add the narrow repair entry and run it once**

```csharp
[MenuItem("Tools/首批修复/补齐工厂生产面板")]
public static void RepairProductionPanel()
{
    var hud = Object.FindFirstObjectByType<HUD>();
    if (hud == null) throw new System.InvalidOperationException("Open SampleScene before repairing its production panel.");
    var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    for (int i = 0; i < hud.produceButtons.Length; i++)
    {
        var button = hud.produceButtons[i];
        if (button == null) continue;
        var rt = button.GetComponent<RectTransform>();
        hud.produceIcons[i] = EnsureIcon(rt, hud.produceIcons[i] != null ? hud.produceIcons[i].sprite : null);
        hud.produceCosts[i] = EnsureCost(rt, font, hud.produceCosts[i] != null ? hud.produceCosts[i].text : "");
    }
    EditorUtility.SetDirty(hud);
    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
}
```

Do not call `Build()` or `Rebuild()`. Run only `BuildHUD.RepairProductionPanel()` with `SampleScene` active.

- [ ] **Step 4: Run targeted and existing production tests**

Expected: new scene test PASS; `ProductionDependencyTests` remain at their baseline result. Inspect the scene diff to ensure no unrelated Canvas was replaced.

---

### Task 3: D02 left-press origin and box selection

**Files:**
- Create: `Assets/Tests/PlayMode/InputGateTests.cs`
- Modify: `Assets/Scripts/SelectionManager.cs:90-99,171-186,186-269,290-305`

**Interfaces:**
- Produces: `SelectionManager.HandleLeftMouseInput(Vector3 world, Vector2 screen, bool down, bool held, bool up, bool overUI)`; `Update` passes real input to this exact method.

- [ ] **Step 1: Write failing UI-origin and world-origin tests**

```csharp
[UnityTest]
public IEnumerator LeftPress_StartedOnUI_NeverStartsBoxSelection()
{
    PaintRect(-4, -4, 12, 12);
    var mover = new GameObject("Selectable").AddComponent<UnitMover>();
    mover.transform.position = Grid.CellCenter(new Vector3Int(0, 0, 0));
    var actor = mover.gameObject.AddComponent<UnitActor>();
    actor.team = Team.Blue;
    yield return null;

    Vector3 world = Selection.cam.ScreenToWorldPoint(new Vector2(20f, 20f));
    Selection.HandleLeftMouseInput(world, new Vector2(20f, 20f), true, true, false, true);
    Selection.HandleLeftMouseInput(world, new Vector2(100f, 100f), false, true, false, false);
    Selection.HandleLeftMouseInput(world, new Vector2(100f, 100f), false, false, true, false);
    Assert.IsEmpty(Selection.selected);
}
```

Add the positive control with identical coordinates but `overUI:false` on the initial down; expected before implementation: method missing, test fails; after implementation UI sequence selects nothing and world sequence selects the unit.

- [ ] **Step 2: Implement the single parameterized path**

Add `bool leftPressStartedOnWorld;` and use this method from `Update`:

```csharp
public void HandleLeftMouseInput(Vector3 world, Vector2 screen,
    bool down, bool held, bool up, bool overUI)
{
    if (down)
    {
        leftPressStartedOnWorld = !overUI;
        dragging = false;
    }

    if (overUI || !leftPressStartedOnWorld)
    {
        dragging = false;
        if (up) leftPressStartedOnWorld = false;
        return;
    }

    if (down) dragStart = screen;
    if (held && !dragging && Vector2.Distance(dragStart, screen) > DragThreshold)
        dragging = true;

    if (up)
    {
        if (dragging) BoxSelect(screen);
        else ClickSelect(world);
        dragging = false;
        leftPressStartedOnWorld = false;
    }
}
```

Change `BoxSelect()` to `BoxSelect(Vector2 releaseScreen)` and use `releaseScreen` instead of `Input.mousePosition`. On pause, placement consumption and armed-attack ownership, clear `dragging` and `leftPressStartedOnWorld`; on UI frames still call this method with real down/up values before returning so press origin cannot go stale.

- [ ] **Step 3: Run both targeted tests and existing command tests**

Expected: both PASS; no baseline regressions in `MovementAndCommandTests`, `PlacementTests`, `PauseMenuTests` or garrison command tests.

---

### Task 4: D03 camera UI/modal gate

**Files:**
- Modify: `Assets/Scripts/RTSCamera.cs:39-47,86-147`
- Modify: `Assets/Tests/PlayMode/InputGateTests.cs`

**Interfaces:**
- Produces: `RTSCamera.HandlePointerInput(Vector3 position, Vector2 screen, Vector2 scroll, bool middleDown, bool middleUp, bool overUI, bool modalOpen)` returning the adjusted world position.

- [ ] **Step 1: Write failing UI, modal and world-control tests**

Construct a camera with `autoMapBounds=false`, `useStartPosition=false`, a fixed `mapBounds`, then disable the component to prevent real `Update` during the deterministic calls:

```csharp
[Test]
public void Camera_UIAndModalInput_DoNotMoveOrZoom()
{
    var go = new GameObject("Camera gate", typeof(Camera), typeof(RTSCamera));
    var cam = go.GetComponent<Camera>();
    cam.orthographic = true;
    var rts = go.GetComponent<RTSCamera>();
    rts.autoMapBounds = false;
    rts.useStartPosition = false;
    rts.enabled = false;
    Vector3 before = go.transform.position;
    float zoom = cam.orthographicSize;

    Vector3 after = rts.HandlePointerInput(before, new Vector2(100, 100),
        new Vector2(0, 2), true, false, true, false);
    Assert.AreEqual(before, after);
    Assert.AreEqual(zoom, cam.orthographicSize);

    after = rts.HandlePointerInput(before, new Vector2(100, 100),
        new Vector2(0, 2), true, false, false, true);
    Assert.AreEqual(before, after);
    Assert.AreEqual(zoom, cam.orthographicSize);
    Object.DestroyImmediate(go);
}
```

Add a positive control with `overUI:false, modalOpen:false` and assert wheel zoom changes. Expected before implementation: method missing; after implementation both gates pass and the control still zooms.

- [ ] **Step 2: Implement pointer handling and preserve world-started drags**

```csharp
public Vector3 HandlePointerInput(Vector3 p, Vector2 screen, Vector2 scroll,
    bool middleDown, bool middleUp, bool overUI, bool modalOpen)
{
    if (modalOpen)
    {
        dragging = false;
        return p;
    }

    if (middleDragPan)
    {
        if (middleDown && !overUI)
        {
            dragAnchorWorld = cam.ScreenToWorldPoint(screen);
            dragging = true;
        }
        if (middleUp) dragging = false;
        if (dragging)
            p += dragAnchorWorld - cam.ScreenToWorldPoint(screen);
    }

    if (!overUI && Mathf.Abs(scroll.y) > 0.01f)
    {
        Vector3 before = cam.ScreenToWorldPoint(screen);
        float size = Mathf.Clamp(cam.orthographicSize - scroll.y * zoomSpeed, minZoom, maxZoom);
        if (!Mathf.Approximately(size, cam.orthographicSize))
        {
            cam.orthographicSize = size;
            p += before - cam.ScreenToWorldPoint(screen);
        }
    }
    return p;
}
```

`Update` computes `overUI` and `modalOpen = PauseMenu.IsAnyOpen || (GameState.Instance != null && GameState.Instance.IsOver)`, calls this exact method, and includes `!modalOpen` in edge pan. Keep final zoom/bounds clamping unchanged.

- [ ] **Step 3: Run camera tests and minimap smoke check**

Expected: targeted tests PASS; wheel over minimap does not change main-camera zoom, while `Minimap.OnPointerClick` still sets its requested position.

---

### Task 5: D04 configured and testable quick build

**Files:**
- Modify: `Assets/Scripts/BuildingPlacer.cs:18-20,186-193`
- Modify: `Assets/Scenes/SampleScene.scene`
- Modify: `Assets/Tests/PlayMode/FirstBatchSceneTests.cs`
- Modify: `Assets/Tests/PlayMode/PlacementTests.cs`

**Interfaces:**
- Produces: `BuildingPlacer.TryQuickBuild(int index)` returning whether a valid indexed prefab was accepted.

- [ ] **Step 1: Write failing scene-order and indexed-entry tests**

In `FirstBatchSceneTests`, load `SampleScene` and assert:

```csharp
var placer = Object.FindFirstObjectByType<BuildingPlacer>();
string[] expected =
{
    "House_Blue", "Barracks_Blue", "Archery_Blue",
    "Monastery_Blue", "Tower_Blue", "Castle_Blue"
};
Assert.AreEqual(expected.Length, placer.quickBuild.Length);
for (int i = 0; i < expected.Length; i++)
    Assert.AreEqual(expected[i], placer.quickBuild[i]?.name);
```

In `PlacementTests`, assign `Placer.quickBuild = new[] { prefab }`, invoke `TryQuickBuild(0)`, assert true and `Placer.IsPlacing`; assert `TryQuickBuild(-1)` and `TryQuickBuild(1)` are false. Expected before implementation: empty scene array and missing indexed method.

- [ ] **Step 2: Implement indexed entry and configure the scene**

```csharp
public bool TryQuickBuild(int index)
{
    if (quickBuild == null || index < 0 || index >= quickBuild.Length) return false;
    if (quickBuild[index] == null) return false;
    Begin(quickBuild[index]);
    return true;
}
```

`HandleQuickKeys` loops `i < quickBuild.Length && i < 9` and returns after `TryQuickBuild(i)` succeeds. In `SampleScene`, copy `HUD.buildPrefabs` into `BuildingPlacer.quickBuild` in the same six-element order using a narrow editor C# mutation; mark only the placer dirty and save the scene. Do not auto-populate at runtime and do not change unlock rules.

- [ ] **Step 3: Run quick-build and placement tests**

Expected: new tests PASS and all existing placement tests retain baseline results.

---

### Task 6: A04 construction-safe garrison and legacy restore

**Files:**
- Modify: `Assets/Scripts/TowerGarrison.cs:58-81,208-240`
- Modify: `Assets/Scripts/SelectionManager.cs:530-585`
- Modify: `Assets/Scripts/SaveGameService.cs:481-487`
- Modify: `Assets/Tests/PlayMode/TowerGarrisonTests.cs`

**Interfaces:**
- Produces: `TowerGarrison.Garrison(UnitActor candidate)` for normal orders and `TowerGarrison.RestoreGarrison(UnitActor candidate)` for save restoration only.

- [ ] **Step 1: Write failing construction tests**

```csharp
[UnityTest]
public IEnumerator UnderConstructionTower_RejectsNormalGarrison_AndRestoreCannotFireEarly()
{
    PaintRect(-8, -8, 24, 24);
    var tower = SpawnTower(new Vector3Int(3, 3));
    tower.MarkBuiltDuringMatch();
    var archer = SpawnArcher(new Vector3Int(-3, -3));
    yield return null;
    Assert.IsTrue(tower.IsUnderConstruction);

    var garrison = tower.GetComponent<TowerGarrison>();
    Assert.IsFalse(garrison.Garrison(archer));
    Assert.IsTrue(garrison.RestoreGarrison(archer));
    SpawnEnemy(new Vector3Int(0, 3));
    yield return new WaitForSeconds(0.25f);
    Assert.IsNull(FindProjectile());

    typeof(Building).GetMethod("FinishConstruction",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tower, null);
    yield return WaitUntil(() => FindProjectile() != null, 2f);
    Assert.IsNotNull(FindProjectile());
}
```

Expected before implementation: normal garrison succeeds, restore method is missing, and a projectile can spawn during construction.

- [ ] **Step 2: Implement normal and restore entry points**

Route both through one private method with an `allowUnderConstruction` flag. Shared checks include candidate validity, team, existing garrison, `building != null`, `building.IsAlive` and `!building.isGhost`; only the normal entry rejects `IsUnderConstruction`. The restore entry exists solely for old save compatibility.

At the start of the player `TryGarrison` order, reject null/ghost/dead/under-construction towers, show the existing blocked marker and consume the command before any path is created. In `TowerGarrison.Update`, return without decrementing the firing timer while the building is null, dead or under construction.

Change save restoration from:

```csharp
tower.Garrison(actor)
```

to:

```csharp
tower.RestoreGarrison(actor)
```

- [ ] **Step 3: Run garrison and save suites**

Expected: new test PASS; all existing `TowerGarrisonTests`, `RubbleAndGarrisonCommandTests` and `SaveLoadTests` retain baseline results. Verify no projectile appears before construction completion and normal completed towers still fire.

---

### Task 7: C01 loaded cargo resumes delivery without overriding manual idle

**Files:**
- Modify: `Assets/Scripts/Gatherer.cs:103-113`
- Modify: `Assets/Tests/PlayMode/MovementAndCommandTests.cs`

**Interfaces:**
- Consumes: existing `Phase.ToDropoff`, `StepToDropoff`, `TryAutoAssign`, `StopWork`.
- Produces: no new save fields or version change.

- [ ] **Step 1: Write failing loaded-cargo test and manual-idle control**

```csharp
[UnityTest]
public IEnumerator RestoreSaveState_NonManualCargo_DeliversAndResumesWork()
{
    PaintRect(-8, -4, 20, 12);
    Economy.wood = 0;
    var worker = CreateWorker("Loaded restore worker", new Vector3Int(0, 0, 0), false);
    worker.GetComponent<UnitMover>().speed = 20f;
    worker.dropoffRetrySeconds = 0.1f;
    var node = CreateNode("Resume after delivery", new Vector3Int(1, 0, 0));
    CreateDropoff(new Vector3Int(3, 0, 0));

    worker.RestoreSaveState(new Gatherer.SaveState
    {
        carrying = 5,
        carryingKind = ResourceKind.Wood,
        manualIdle = false
    });
    worker.autoStart = true;
    Assert.AreEqual("运送中", worker.Status);

    yield return WaitUntil(() => Economy.wood >= 5, 5f);
    Assert.AreEqual(5, Economy.wood);
    Assert.AreEqual(0, worker.Carrying);
    yield return WaitUntil(() => worker.TargetNode == node, 2f);
    Assert.AreSame(node, worker.TargetNode);
}
```

Add a control restoring `manualIdle:true` with cargo and no drop-off; after 0.25 seconds assert no movement, no credit and retained cargo. Expected before implementation: loaded worker remains idle and never delivers.

- [ ] **Step 2: Implement the minimal restore branch**

```csharp
public void RestoreSaveState(SaveState state)
{
    if (state == null) return;
    StopWork();
    carrying = Mathf.Clamp(state.carrying, 0, carryCapacity);
    carryingKind = state.carryingKind;
    manualIdle = state.manualIdle;

    if (manualIdle) return;
    if (carrying > 0)
    {
        dropoff = null;
        dropoffRetry = 0f;
        phase = Phase.ToDropoff;
    }
    else TryAutoAssign();
}
```

Do not credit during restore and do not serialize a path.

- [ ] **Step 3: Run cargo, worker and save tests**

Expected: new tests PASS; `WorkerBehaviorTests`, `MovementAndCommandTests` and `SaveLoadTests` have no new failures.

---

### Task 8: B09 shared AI worker-production budget

**Files:**
- Modify: `Assets/Scripts/AIController.cs:360-381`
- Create: `Assets/Tests/PlayMode/AIProductionBudgetTests.cs`

**Interfaces:**
- Produces: private `CountLivingWorkers()`, `CountQueuedWorkers()` and `IsWorkerProduct(ProductionBuilding.Product product)` helpers used by `Produce`; no public save-schema change. `ProductionBuilding.Product` is the existing nested type at `Assets/Scripts/ProductionBuilding.cs:13-21`; do not add a duplicate DTO.

- [ ] **Step 1: Write failing multi-factory budget test**

Create an inactive AI with `maxGatherers=2`, `startGold=1000`, one living red worker and two completed red houses. Invoke the same private `Produce()` used by `Update` through reflection:

```csharp
var produce = typeof(AIController).GetMethod("Produce",
    BindingFlags.Instance | BindingFlags.NonPublic);
produce.Invoke(ai, null);
int queued = houses.Sum(h => h.GetComponent<ProductionBuilding>().Queue.Count);
Assert.AreEqual(1, queued, "One living worker leaves only one slot across both houses.");
produce.Invoke(ai, null);
queued = houses.Sum(h => h.GetComponent<ProductionBuilding>().Queue.Count);
Assert.AreEqual(1, queued, "A second tick must not exceed living plus queued budget.");
Assert.AreEqual(960, ai.Gold);
```

Add a control with `maxGatherers=0` and a red barracks; `Produce()` must still queue its affordable military product. Expected before implementation: two houses accept two workers despite the living worker and budget.

- [ ] **Step 2: Implement one shared budget per production tick**

At the start of `Produce`:

```csharp
int workerBudget = Mathf.Max(0, maxGatherers - CountLivingWorkers() - CountQueuedWorkers());
```

Inside the existing factory loop, after an affordable product is selected:

```csharp
bool workerProduct = pb.products[pick].unitPrefab.GetComponent<Gatherer>() != null;
if (workerProduct && workerBudget <= 0) continue;
if (!pb.TryQueue(pick, payCost: false)) continue;
if (!TrySpend(pb.products[pick].cost)) continue;
if (workerProduct) workerBudget--;
```

`CountLivingWorkers` iterates `UnitActor.All`, includes only living same-team actors with a `Gatherer`, and `CountQueuedWorkers` iterates completed/living same-team factories and sums only queue entries whose `unitPrefab` has a `Gatherer`. Preserve existing ghost, construction, queue-limit and affordability checks.

To keep spending atomic if `TrySpend` can ever fail after `TryQueue`, instead check `CanAfford` immediately before queueing as the existing product selection already does, then call `TryQueue` and `TrySpend` without a new refund path; add an assertion in tests that failed queueing does not reduce gold. Do not use player `Economy.Refund` for AI cancellations.

- [ ] **Step 3: Run AI budget and economic behaviour tests**

Expected: new tests PASS; military control still queues; no baseline regressions in worker, production or save tests.

---

### Task 9: Full verification, runtime checks and next-stage review

**Files:**
- Modify: `docs/superpowers/reports/2026-10-26-rts-first-batch-verification.md`
- Review only: all files changed by Tasks 1-8

- [ ] **Step 1: Run the complete suite after compilation**

Run `RegressionTestMenu.RunPlayModeTests()` and read `Logs/RegressionTests.txt`. Compare exact test names against baseline. Record passed/failed/skipped, every remaining failure and whether it is pre-existing, newly introduced or newly covered.

- [ ] **Step 2: Perform actual-scene runtime verification**

With `SampleScene` active and no unsaved unrelated play-mode changes to preserve:

1. Select a real factory and capture the populated production panel; queue one unit.
2. Verify UI-origin dragging does not change selection; world-origin box selection still works.
3. Verify wheel/middle drag over HUD and while paused do not move the camera; minimap click still navigates.
4. Verify each configured quick-build slot enters the expected ghost and locked buildings remain locked.
5. Verify an under-construction tower rejects normal garrison and does not fire; a completed tower still works.
6. Save/load a non-manual cargo worker and observe delivery then resumed work; verify manual idle remains idle.
7. Run AI through multiple production ticks and confirm living plus queued workers never exceeds four while military production continues.

Capture only necessary screenshots/log evidence. Stop Play Mode before any further scene edit and do not save play-mode changes.

- [ ] **Step 3: Review the final diff and report**

Run `git status --short` and `git diff --` only for touched tracked files; for untracked files review their actual contents. Confirm no full HUD rebuild, no unrelated prefab/art changes, no commit and no accidental play-mode scene save.

Write the verification report with:
- baseline versus final counts;
- per-ID implementation and test evidence;
- failed, skipped or unperformed checks;
- any newly discovered issue with reproduction details;
- next-stage recommendation based on measured risk/benefit, without implementing the next stage.

Expected completion: all seven targeted behaviours verified, or an explicit list of what remains broken and why. Do not claim full success if any part was not run.
