# Pause Menu and Full Match Save/Load Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an ESC pause menu and a single-slot complete-match save/load flow without regressing existing RTS behavior.

**Architecture:** Stable prefab GUIDs plus a `Resources` prefab catalog identify every saveable object. A JSON snapshot captures long-lived match state; loading reloads `SampleScene`, clears runtime objects, rebuilds them from the catalog, and reapplies state after one frame so existing `Awake/Start` initialization stays intact. Transient paths, projectiles, and attack targets are intentionally not serialized.

**Tech Stack:** Unity/Tuanjie 2022.3 C#, UGUI legacy `Text/Button`, `JsonUtility`, PlayMode NUnit tests, editor menu builders.

## Global Constraints

- Single save slot at `Application.persistentDataPath/AtTheGates/save_slot_1.json`.
- Save version is `1`; incompatible or malformed saves fail without reloading the scene.
- No auto-save, no multiple slots, no cloud save.
- ESC while placing a building cancels placement instead of opening the menu.
- ESC does not open the pause menu after the match has ended.
- Do not change normal gameplay update rules; only add capture/restore entry points.
- Existing 47 PlayMode regressions must remain green.
- Do not commit unless the user explicitly asks.

---

### Task 1: Stable prefab identity and catalog

**Files:**
- Create: `Assets/Scripts/SaveIdentity.cs`
- Create: `Assets/Scripts/SavePrefabCatalog.cs`
- Create: `Assets/Editor/BuildSaveCatalog.cs`
- Generate: `Assets/Resources/SavePrefabCatalog.asset`

**Interfaces:**
- Produces: `SaveIdentity.PrefabId : string`
- Produces: `SavePrefabCatalog.TryGetPrefab(string id, out GameObject prefab)`
- Produces: editor menu `Tools/Save/重建预制体目录`

- [ ] Add `SaveIdentity` with `[SerializeField] string prefabId` and `public string PrefabId`.
- [ ] Add `SavePrefabCatalog : ScriptableObject` with serializable entries `{ string id; GameObject prefab; }` and a dictionary lookup built in `OnEnable`.
- [ ] Add editor builder that scans `Assets/Prefabs`, adds `SaveIdentity` to every prefab containing `UnitActor`, `Building`, `ResourceNode`, or `ClearableBush`, sets the GUID, saves the prefab, and writes the catalog asset under `Assets/Resources/SavePrefabCatalog.asset`.
- [ ] Verify every `UnitActor`, `Building`, and `ClearableBush` currently in `SampleScene` resolves through `GetComponentInParent<SaveIdentity>()`.
- [ ] Compile and run the existing 47 tests unchanged.

### Task 2: Capture/restore APIs on gameplay components

**Files:**
- Modify: `Assets/Scripts/Unit.cs`
- Modify: `Assets/Scripts/Building.cs`
- Modify: `Assets/Scripts/ProductionBuilding.cs`
- Modify: `Assets/Scripts/AIController.cs`
- Modify: `Assets/Scripts/Gatherer.cs`
- Modify: `Assets/Scripts/ResourceNode.cs`
- Modify: `Assets/Scripts/TowerGarrison.cs`
- Modify: `Assets/Scripts/GameState.cs`

**Interfaces:**
- Produces: `UnitActor.CaptureState()` / `UnitActor.RestoreState(...)`
- Produces: `Building.CaptureState()` / `Building.RestoreState(...)`
- Produces: `Building.CaptureRubbleState()` / `Building.RestoreWreckedState()`
- Produces: `ProductionBuilding.CaptureQueue()` / `ProductionBuilding.RestoreQueue(...)`
- Produces: `AIController.CaptureState()` / `AIController.RestoreState(...)`
- Produces: `Gatherer.CaptureCargo()` / `Gatherer.RestoreCargo(...)`
- Produces: `ResourceNode.CaptureState()` / `ResourceNode.RestoreState(...)`
- Produces: `TowerGarrison.Archer`
- Produces: `GameState.CaptureSaveState()` / `GameState.RestoreSaveState(...)`

- [ ] Add public serializable state structs/classes in the owning scripts where the data is private.
- [ ] Ensure restore methods refresh health bars, visuals, blockers, construction bars, and production enabled state.
- [ ] `Building.RestoreWreckedState()` must preserve collider and footprint blocking and must not play the explosion FX.
- [ ] `ProductionBuilding.RestoreQueue` restores queue indices and remaining timer without charging resources.
- [ ] `Gatherer.RestoreCargo` restores cargo amount/kind and manual-idle flag; auto assignment may then resume through existing rules.
- [ ] `TowerGarrison.Archer` exposes the garrisoned `UnitActor` read-only.
- [ ] Run existing 47 tests after the state APIs compile but before save UI work.

### Task 3: Save file model, validation, capture, and restore

**Files:**
- Create: `Assets/Scripts/SaveGameData.cs`
- Create: `Assets/Scripts/SaveGameService.cs`
- Create: `Assets/Scripts/SaveGameRuntime.cs`
- Test: `Assets/Tests/PlayMode/SaveLoadTests.cs`

**Interfaces:**
- Produces: `SaveGameService.HasSave`, `SaveGameService.Save()`, `SaveGameService.TryRead()`, `SaveGameService.RequestLoad()`, `SaveGameService.ConsumePendingLoad()`
- Produces: `SaveGameService.CaptureSnapshot()` / `SaveGameService.RestoreSnapshot(...)`
- Produces: `SaveGameRuntime` startup coroutine for pending loads.

- [ ] Write a failing PlayMode roundtrip test covering economy, unit health/position, worker cargo, garrisoned archer, construction, production queue, resource amount, rubble blocking, and camera.
- [ ] Verify it fails because the save service does not exist.
- [ ] Implement JSON DTOs with `version`, timestamp, scene, economy, AI, buildings, units, bushes, and camera sections.
- [ ] Implement atomic single-slot writes and full validation/version checks.
- [ ] Implement capture in this order: economy, GameState, AI, buildings (including production/construction/resource/garrison), units, bushes, camera.
- [ ] Implement restore in this order: validate catalog IDs, destroy existing saveable objects, instantiate buildings/units/bushes, wait one frame, apply states, recreate garrisons, restore managers and camera, resume time.
- [ ] Verify the roundtrip test passes and existing tests remain green.
- [ ] Add failing tests for malformed JSON and unsupported version; verify both return failure without throwing or changing the current scene.

### Task 4: ESC pause menu

**Files:**
- Create: `Assets/Scripts/UI/PauseMenu.cs`
- Create: `Assets/Editor/BuildPauseMenu.cs`
- Modify: `Assets/Scenes/SampleScene.scene` via editor builder
- Test: `Assets/Tests/PlayMode/PauseMenuTests.cs`

**Interfaces:**
- Consumes: `SaveGameService` from Task 3.
- Produces: pause menu with Continue / Save / Load / Restart / Quit buttons.

- [ ] Write a failing test that opens the menu through `PauseMenu.SetOpen(true)`, asserts `Time.timeScale == 0`, closes it, and asserts time resumes.
- [ ] Write a failing test that a placement ghost plus ESC leaves `IsPlacing == false` and does not open the menu.
- [ ] Implement `PauseMenu.Update` so ESC is ignored when `BuildingPlacer.IsPlacing` or `GameState.IsOver`.
- [ ] Use unscaled time for menu visuals; restore `Time.timeScale = 1` before load/restart.
- [ ] Save button writes the snapshot and updates a status label with local time; failure shows the returned reason.
- [ ] Load button is disabled without a valid save; successful parse requests load and reloads `SampleScene`.
- [ ] Restart calls `GameState.RestartLevel()`; Quit uses `Application.Quit()` with editor play-mode exit.
- [ ] Build the modal with existing `Carved_9Slides` and `Button_Blue_9Slides` art; add `SaveGameRuntime` and `PauseMenu` references in `SampleScene`.
- [ ] Run the pause tests and existing suite.

### Task 5: Main-menu continue entry

**Files:**
- Modify: `Assets/Scripts/UI/MainMenu.cs`
- Create: `Assets/Editor/AddContinueButton.cs`
- Modify: `Assets/Scenes/MainMenu.scene` via editor builder
- Test: `Assets/Tests/PlayMode/SaveLoadTests.cs`

**Interfaces:**
- Consumes: `SaveGameService.HasSave` / `RequestLoad()`.

- [ ] Add `continueButton` to `MainMenu`, wire it in `Start`, and set interactable from `SaveGameService.HasSave`.
- [ ] Add editor tool that opens `MainMenu.scene` additively, inserts a blue “继续游戏” button above “开始游戏”, assigns the field, and saves.
- [ ] On click, validate the save, set the pending-load flag, and load `SampleScene`.
- [ ] Add a test/assertion that the button is disabled with no save and enabled with a valid one.
- [ ] Run the full PlayMode suite.

### Task 6: Full verification and visual checks

**Files:**
- Create: `docs/superpowers/reports/2026-10-19-pause-menu-save-load-verification.md`

- [ ] Run all PlayMode tests and record exact pass/fail counts.
- [ ] Play `SampleScene`, open ESC, save, continue, load, and verify resources, units, production, rubble, tower archer, and camera visually.
- [ ] Capture a pause-menu screenshot, a main-menu continue screenshot, and a post-load tower/rubble screenshot.
- [ ] Run an RTS gameplay pass: movement, pathing around rubble, worker gathering, production, garrison approach/attack, camera, restart, exit.
- [ ] Report newly discovered issues without fixing them unless they are regressions caused by this work.
