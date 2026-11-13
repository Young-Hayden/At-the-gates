# RTS Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the A04 legacy-save evidence gap, harden D01 production-panel repair and empty-icon behaviour, and strengthen D04/C01 regression tests without expanding into new gameplay systems.

**Architecture:** Keep the current save/restore order and production UI structure. Add one real SaveGameService roundtrip test, move only the defensive garrison health-bar guard, preflight the existing editor repair before mutation, and synchronize icon visibility in the existing HUD refresh path. D04/C01 changes are tests only.

**Tech Stack:** Tuanjie 2022.3.62t5, Unity Test Framework 1.1.33, NUnit, C#, UGUI, existing RegressionTestMenu and isolated PlayMode scenes.

## Global Constraints

- Do not implement A01/A02, AI rebuilding/garrison, performance rewrites, population, technology, LOS or unit physics changes.
- Do not rebuild the full HUD, change layout, prices, unit stats, save version or the formal save slot.
- Do not install Input System. Physical GUI acceptance remains blocked by Windows lock screen and must be reported as unperformed.
- Preserve all pre-existing uncommitted work. No commits, pushes, worktrees or cleanup.
- Use test-first evidence for A04 and D01. Do not break correct production code merely to manufacture a RED result.
- Existing 81/81 is the starting reference; every final claim must come from a new machine run after the last code change.
- Editor repair probes use temporary objects only and never save a test scene.

## File Map

- `Assets/Tests/PlayMode/SaveLoadTests.cs`: full legacy under-construction garrison roundtrip.
- `Assets/Scripts/TowerGarrison.cs`: defensive health-bar hiding before construction early return.
- `Assets/Tests/PlayMode/FirstBatchSceneTests.cs`: runtime empty-icon visibility and direct scene/HUD quick-build identity checks.
- `Assets/Editor/BuildHUD.cs`: repair preflight, font fallback and empty-icon creation/update.
- `Assets/Scripts/HUD.cs`: synchronize production icon `enabled` with the resolved sprite.
- `Assets/Tests/PlayMode/PlacementTests.cs`: index-1 mapping and deferred-destruction-safe invalid-index control.
- `Assets/Tests/PlayMode/MovementAndCommandTests.cs`: post-restore resource-amount baseline.
- `docs/superpowers/reports/2026-10-27-rts-hardening-verification.md`: RED/GREEN, regressions, final scope and remaining GUI blocker.

---

### Task 1: A04 full legacy under-construction garrison roundtrip

**Files:**
- Modify: `Assets/Tests/PlayMode/SaveLoadTests.cs`
- Modify: `Assets/Scripts/TowerGarrison.cs:218-240`

**Interfaces:**
- Consumes: existing `SaveGameService.CaptureSnapshot`, `SaveToPath`, `TryReadFromPath`, `RestoreSnapshot`, `TowerGarrison.RestoreGarrison`, and `Building.FinishConstruction`.
- Produces: proof that legacy mid-construction garrisons restore, stay silent until completion, and keep the archer health bar hidden.

- [ ] **Step 1: Snapshot the current files and add the failing roundtrip test**

Add this test to `SaveLoadTests`:

```csharp
[UnityTest]
public IEnumerator LegacyUnderConstructionGarrison_RoundTripsAndFiresOnlyAfterCompletion()
{
    PaintRect(-8, -8, 24, 24);
    var tower = Instantiate(TowerPath, new Vector3Int(0, 0, 0), "Legacy construction tower");
    tower.GetComponent<Building>().MarkBuiltDuringMatch();
    var archer = Instantiate(ArcherPath, new Vector3Int(2, 0, 0), "Legacy archer");
    yield return null;

    var building = tower.GetComponent<Building>();
    Assert.IsTrue(building.IsUnderConstruction);
    var actor = archer.GetComponent<UnitActor>();
    actor.health = 41;
    Assert.IsTrue(tower.GetComponent<TowerGarrison>().RestoreGarrison(actor));

    var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
    Assert.IsNotNull(snapshot, captureError);
    string path = Path.Combine(Application.temporaryCachePath, "legacy_construction_garrison.json");
    Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError), writeError);
    Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError), readError);

    foreach (var u in UnityEngine.Object.FindObjectsByType<UnitActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        if (u != null && u.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(u.gameObject);
    foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        if (b != null && b.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(b.gameObject);
    yield return null;

    bool restored = false;
    string restoreError = null;
    yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
    {
        restored = ok;
        restoreError = error;
    });
    Assert.IsTrue(restored, restoreError);

    var restoredTower = GameObject.Find("Legacy construction tower");
    Assert.IsNotNull(restoredTower);
    var restoredBuilding = restoredTower.GetComponent<Building>();
    var garrison = restoredTower.GetComponent<TowerGarrison>();
    Assert.IsTrue(restoredBuilding.IsUnderConstruction);
    Assert.IsTrue(garrison.HasArcher);
    Assert.AreEqual(41, garrison.Archer.health);
    Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<Projectile>(),
        "A restored half-built tower must not fire.");

    var bar = garrison.Archer.GetComponentInChildren<HealthBar>(true);
    Assert.IsNotNull(bar);
    bar.gameObject.SetActive(true);
    var update = typeof(TowerGarrison).GetMethod(
        "Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    update.Invoke(garrison, null);
    Assert.IsFalse(bar.gameObject.activeSelf,
        "Construction early-return must still hide a re-shown garrison health bar.");

    typeof(Building).GetMethod("FinishConstruction",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .Invoke(restoredBuilding, null);
    var enemyGO = new GameObject("Restored tower target");
    enemyGO.transform.position = restoredTower.transform.position + Vector3.left * 2f;
    var enemy = enemyGO.AddComponent<UnitActor>();
    enemy.team = Team.Red;
    enemy.showHealthBar = false;
    enemy.maxHealth = 100;
    enemy.health = 100;
    enemy.isCombatant = true;
    update.Invoke(garrison, null);
    Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<Projectile>(),
        "The restored garrison must fire after construction completes.");
}
```

- [ ] **Step 2: Run the exact test before production changes**

Execute through Tuanjie MCP after compilation:

```csharp
RegressionTestMenu.RunPlayModeTest("SaveLoadTests.LegacyUnderConstructionGarrison_RoundTripsAndFiresOnlyAfterCompletion");
return "A04 roundtrip RED started";
```

Expected: restoration itself succeeds, but the test fails because a re-shown bar remains active during the construction early return. If full restoration fails first, fix the test fixture only if its arrangement is wrong; otherwise report the real restore defect and do not hide it behind the health-bar assertion.

- [ ] **Step 3: Move the defensive health-bar guard before the early return**

In `TowerGarrison.Update`, after `if (!HasArcher) return;`, place the existing defensive block:

```csharp
if (archerBar != null && archerBar.gameObject.activeSelf)
    archerBar.gameObject.SetActive(false);

if (building == null || !building.IsAlive || building.isGhost || building.IsUnderConstruction)
    return;
```

Keep target acquisition and timer decrement after the early return. Do not change `Garrison`, `RestoreGarrison`, `SaveGameService` or construction timing.

- [ ] **Step 4: Rerun the roundtrip and existing garrison/save controls**

Run the exact new test, then `SaveLoadTests.FullSnapshot_RoundTripsCoreMatchState`, `TowerGarrisonTests.UnderConstructionTower_RejectsNormalGarrison_AndRestoreCannotFireEarly`, and `TowerGarrisonTests.EnemyInRange_ProjectileSpawnsAboveTowerAtArcher`. Expected: all pass; the new test is also the health-bar regression proof.

---

### Task 2: D01 preflight, font fallback and empty-icon visibility

**Files:**
- Modify: `Assets/Editor/BuildHUD.cs:269-286,447-475`
- Modify: `Assets/Scripts/HUD.cs:267-276`
- Modify: `Assets/Tests/PlayMode/FirstBatchSceneTests.cs`

**Interfaces:**
- Consumes: existing `BuildHUD.EnsureIcon`, `EnsureCost`, `RepairProductionPanel`, and `HUD.RefreshProduction`.
- Produces: no-mutation validation failures and `produceIcons[i].enabled == (resolved sprite != null)` during every refresh.

- [ ] **Step 1: Add the failing runtime empty-icon test**

Add `using UnityEngine.UI;` to `FirstBatchSceneTests.cs`, then add:

```csharp
[UnityTest]
public IEnumerator ProductionPanel_EmptyIcon_IsHiddenAndRecoversWhenSpriteArrives()
{
    var factoryGO = new GameObject("Iconless factory", typeof(Building), typeof(ProductionBuilding));
    var factory = factoryGO.GetComponent<ProductionBuilding>();
    var productGO = new GameObject("Iconless product");
    factory.products = new[]
    {
        new ProductionBuilding.Product { unitPrefab = productGO, icon = null, cost = new ResourceCost(10) }
    };

    var panel = new GameObject("Production panel", typeof(RectTransform));
    var buttonGO = new GameObject("Produce0", typeof(RectTransform), typeof(Image), typeof(Button));
    var button = buttonGO.GetComponent<Button>();
    var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
    iconGO.transform.SetParent(buttonGO.transform, false);
    var icon = iconGO.GetComponent<Image>();
    icon.enabled = true;
    var costGO = new GameObject("Cost", typeof(RectTransform), typeof(Text));
    costGO.transform.SetParent(buttonGO.transform, false);

    var hud = new GameObject("HUD under test").AddComponent<HUD>();
    hud.productionPanel = panel;
    hud.produceButtons = new[] { button };
    hud.produceIcons = new[] { icon };
    hud.produceCosts = new[] { costGO.GetComponent<Text>() };
    hud.produceBacks = new[] { buttonGO.GetComponent<Image>() };
    Selection.selectedBuilding = factoryGO.GetComponent<Building>();

    yield return null;
    Assert.IsFalse(icon.enabled, "A null product icon must not render an empty white Image.");

    var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    factory.products[0].icon = sprite;
    yield return null;
    Assert.IsTrue(icon.enabled, "The icon must become visible when a sprite is assigned.");
    Assert.AreSame(sprite, icon.sprite);

    factory.products[0].icon = null;
    yield return null;
    Assert.IsFalse(icon.enabled, "Removing the sprite must hide the Image again.");
}
```

Run exact test before production edits. Expected: FAIL on the first `Assert.IsFalse(icon.enabled)`.

- [ ] **Step 2: Synchronize HUD icon visibility**

In `HUD.RefreshProduction`, after resolving `want`:

```csharp
if (produceIcons[i].sprite != want) produceIcons[i].sprite = want;
produceIcons[i].enabled = want != null;
```

In `BuildHUD.EnsureIcon`, make both existing and newly created images follow the same rule:

```csharp
static Image EnsureIcon(RectTransform parent, Sprite sprite)
{
    var existing = parent.Find("Icon")?.GetComponent<Image>();
    if (existing != null)
    {
        existing.sprite = sprite;
        existing.enabled = sprite != null;
        return existing;
    }

    var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
    var rt = go.GetComponent<RectTransform>();
    rt.SetParent(parent, false);
    rt.anchorMin = Vector2.zero;
    rt.anchorMax = Vector2.one;
    rt.offsetMin = new Vector2(5, 17);
    rt.offsetMax = new Vector2(-5, -5);
    var image = go.GetComponent<Image>();
    image.sprite = sprite;
    image.enabled = sprite != null;
    image.preserveAspect = true;
    image.raycastTarget = false;
    return image;
}
```

- [ ] **Step 3: Add repair preflight before any mutation**

At the start of `RepairProductionPanel`, after finding `hud`, validate before creating anything:

```csharp
if (hud.produceButtons == null || hud.produceIcons == null || hud.produceCosts == null)
    throw new System.InvalidOperationException("Production panel arrays must not be null.");
int count = hud.produceButtons.Length;
if (hud.produceIcons.Length != count || hud.produceCosts.Length != count)
    throw new System.InvalidOperationException(
        $"Production panel array length mismatch: buttons={count}, icons={hud.produceIcons.Length}, costs={hud.produceCosts.Length}.");
for (int i = 0; i < count; i++)
    if (hud.produceButtons[i] == null || hud.produceButtons[i].GetComponent<RectTransform>() == null)
        throw new System.InvalidOperationException($"Production button {i} or its RectTransform is missing.");
```

Use this font logic before the validated loop:

```csharp
var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
```

In `EnsureCost`, only assign `text.font = font` when `font != null`. Keep the validated loop and narrow scene save unchanged.

- [ ] **Step 4: Verify invalid editor structures do not mutate and runtime test is green**

In an editor-only temporary scene fixture, construct one valid button with `produceButtons.Length=1`, `produceIcons.Length=0`, `produceCosts.Length=1`. Invoke `RepairProductionPanel`; expected `InvalidOperationException` containing `length mismatch`, with the button's child count and array references unchanged. Destroy temporary objects without saving. Then make all three arrays length 1 and invoke again; expected non-null Icon/Cost children and no array error.

Run the new PlayMode test and `FirstBatchSceneTests.ProductionPanel_HasBoundContent_AndRealFactoryCanQueue`. Expected: both pass. Also run `ProductionDependencyTests` to confirm normal products still queue.

---

### Task 3: D04/C01 regression hardening only

**Files:**
- Modify: `Assets/Tests/PlayMode/FirstBatchSceneTests.cs`
- Modify: `Assets/Tests/PlayMode/PlacementTests.cs:16-52`
- Modify: `Assets/Tests/PlayMode/MovementAndCommandTests.cs:268-307`

**Interfaces:**
- Consumes: existing `BuildingPlacer.TryQuickBuild(int)` and scene `HUD.buildPrefabs`.
- Produces: tests that reject wrong index mapping, stale ghost destruction and pre-restore harvesting false positives.

- [ ] **Step 1: Compare quick-build identity directly with HUD**

After obtaining `placer` in `QuickBuild_SceneOrder_MatchesBuildMenu`, add:

```csharp
var hud = Object.FindFirstObjectByType<HUD>();
Assert.IsNotNull(hud);
Assert.AreEqual(hud.buildPrefabs.Length, placer.quickBuild.Length);
for (int i = 0; i < expected.Length; i++)
    Assert.AreSame(hud.buildPrefabs[i], placer.quickBuild[i],
        $"Quick build slot {i} must reference the exact HUD prefab.");
```

Keep the existing expected-name assertions.

- [ ] **Step 2: Make valid-index and invalid-index tests discriminating**

Add `const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";` to `PlacementTests`. Replace the valid test with:

```csharp
[Test]
public void TryQuickBuild_ValidIndex_BeginsRequestedPrefab()
{
    var house = LoadPrefab<GameObject>(HousePath);
    var castle = LoadPrefab<GameObject>(CastlePath);
    Placer.quickBuild = new[] { house, castle };

    Assert.IsTrue(Placer.TryQuickBuild(1));
    Assert.IsTrue(Placer.IsPlacing);
    Assert.IsNotNull(GameObject.Find("__Ghost_Castle_Blue"));
    Assert.IsNull(GameObject.Find("__Ghost_House_Blue"));
}
```

Convert the invalid-index test to `IEnumerator` and after both invalid calls add:

```csharp
yield return null;
var ghosts = Object.FindObjectsByType<Building>(FindObjectsInactive.Include, FindObjectsSortMode.None)
    .Where(b => b != null && b.isGhost && b.gameObject.name == "__Ghost_Castle_Blue")
    .ToArray();
Assert.AreEqual(1, ghosts.Length);
Assert.AreSame(existingGhost, ghosts[0].gameObject);
```

Add `using System.Linq;`. Remove the now-unused `InvokeTryQuickBuild` reflection helper and its unnecessary `System.Reflection` import if no later test uses it; retain reflection used by existing placement tests.

- [ ] **Step 3: Capture the C01 post-restore resource baseline**

Immediately after the restore assertions in `RestoreSaveState_NonManualCargo_DeliversAndResumesWork`, add:

```csharp
int amountAtRestore = node.amount;
```

Replace the final wait/assert pair with:

```csharp
yield return WaitUntil(() => node.amount < amountAtRestore, 3f);
Assert.Less(node.amount, amountAtRestore,
    "Resuming work must harvest below the amount present after restoration.");
```

These are regression controls and may pass against correct current code; do not change production code to force a failure.

- [ ] **Step 4: Run all three hardened controls and related suites**

Run the scene-order test, both quick-build tests, and `MovementAndCommandTests.RestoreSaveState_NonManualCargo_DeliversAndResumesWork`. Then run `PlacementTests`, `MovementAndCommandTests`, and `FirstBatchSceneTests` fixtures or their exact leaf tests. Expected: all pass, and review can see why each would fail under its named incorrect implementation.

---

### Task 4: Full verification and next-stage handoff

**Files:**
- Create: `docs/superpowers/reports/2026-10-27-rts-hardening-verification.md`
- Review only: every file changed in Tasks 1-3

**Interfaces:**
- Consumes: all previous machine logs and snapshots.
- Produces: final evidence-backed decision on whether to start the separately designed A01/A02 project.

- [ ] **Step 1: Run the full suite after the final code change**

Compile and wait for editor idle, then execute:

```csharp
RegressionTestMenu.RunPlayModeTests();
return "Hardening final full suite started";
```

Read `Logs/RegressionTests.txt`. Record exact total, passed, failed, skipped, inconclusive and duration. Do not infer the final number from added tests. Any failure must be reproduced by exact leaf name and classified before reporting.

- [ ] **Step 2: Check final scene, save and scope state**

Confirm Tuanjie is stopped, active scene and dirty state are expected, and no formal `save_slot_1.json` was created or overwritten. Use task snapshots/no-index diffs to confirm that only the planned scripts, tests and report changed. Do not attribute the user's extensive pre-existing prefab/art modifications to this work.

- [ ] **Step 3: Report results and remaining blocker**

Write `docs/superpowers/reports/2026-10-27-rts-hardening-verification.md` with:
- starting 81/81 reference and final machine result;
- A04 full roundtrip and health-bar evidence;
- D01 invalid-structure/no-mutation, font and empty-icon evidence;
- D04/C01 hardening coverage;
- failures, skipped or unperformed checks;
- explicit statement that physical drag/wheel/middle/Alpha1-6 remain unperformed because Windows locked;
- recommendation to begin the separately designed A01/A02 project only if no Critical/Important regression remains.

- [ ] **Step 4: Independent task/final review and ledger update**

Request read-only spec and quality review of the complete no-index diff and report. Resolve any Critical/Important findings before claiming completion. Record remaining Minor items and the physical GUI blocker in `.superpowers/sdd/progress.md`. Do not commit or start A01/A02 implementation in this plan.
