# 2026-10-19 Pause Menu / Save Load Verification

## Automated regression

Command: `Tools/Regression/Run PlayMode Tests`

Final result: **56 passed, 0 failed, 0 skipped, 0 inconclusive** (30.837s).

Key new coverage:

- `MovementAndCommandTests.AutoAssign_ThreeWorkers_ChooseDistinctNearestNodes`
- `RubbleAndGarrisonCommandTests.DestroyedRubble_KeepsFootprintAndColliderBlocking`
- `RubbleAndGarrisonCommandTests.RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy`
- `SaveLoadTests.FullSnapshot_RoundTripsCoreMatchState`
- `SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing`
- `SaveLoadTests.UnsupportedVersion_ReturnsFalseWithoutThrowing`
- `PauseMenuTests.SetOpen_PausesAndClosesWithoutLeavingTimeStopped`
- `PauseMenuTests.Escape_WhilePlacingBuilding_DoesNotOpenPauseMenu`
- `PauseMenuTests.Escape_AfterCancelInSameFrame_DoesNotReopenPauseMenu`
- `PauseMenuTests.Escape_AfterMatchEnded_DoesNotOpenPauseMenu`
- `PauseMenuTests.MainMenu_ContinueButton_OnlyExistsWhenSaveIsValid`

The ESC same-frame case was found during self-check: if `BuildingPlacer.Update` cancelled the
ghost before `PauseMenu.Update`, the same ESC could open the menu. `HandleEscape` now also
checks `BuildingPlacer.LastCancelFrame == Time.frameCount`.

## Live integration

- Saved a live SampleScene snapshot through the pause menu: 14 units, 246 buildings,
  99 bushes, 1 AI, 420,898 bytes.
- Mutated resources, unit health/position and camera, then loaded through `PauseMenu.LoadGame`.
- Scene reload restored economy `600/300/200`, 14 units, 246 buildings, unit health and
  positions, and resumed time at 1.
- Main menu `继续游戏` was enabled with the valid save and successfully loaded/restored
  `SampleScene`.
- `PauseMenu.RestartGame` reloaded a fresh match (`timeScale=1`, 14 units, 246 buildings,
  start resources).
- `PauseMenu.QuitGame` exited Play Mode in the editor.
- Staged tower + garrison + persistent rubble + enemy scenario survived save/reload:
  garrison archer present with 55 HP, rubble cells still blocked, and the tower archer
  subsequently killed the test enemy.

## Visual artifacts

- `screenshots/Pause_Menu.png` — pause menu without a save.
- `screenshots/Pause_Menu_Saved.png` — load enabled and last-save timestamp.
- `screenshots/MainMenu_Continue.png` — main menu with Continue between Start and Quit.
- `screenshots/MainMenu_WithSave.png` — three-button layout while a valid save exists.
- `screenshots/MainMenu_NoSave.png` — Continue removed; Start/Quit return to the original two-button layout.
- `screenshots/MainMenu_Rules_Updated.png` — updated rules panel with garrison, rubble and pause-menu instructions.
- `screenshots/Tower_Before.png` / `Tower_After.png` — tower-top placement before/after the 2.0 offset.
- `screenshots/PostLoad_Tower_Rubble.png` — restored tower garrison and rubble.
- `screenshots/Fresh_Match.png` — fresh-match HUD and camera.

## Fixed after the first verification pass

1. **Starting workers stacked on one cell:** three blue pawns all auto-assigned the same
   gold mine and shared its approach tile. Automatic assignment now skips nodes already
   targeted by another live gatherer; AI dispatch uses the same rule. Live check:
   `Pawn_Blue_0 -> Mine_Gold_1`, `Pawn_Blue_1 -> Tree6`, `Pawn_Blue_2 -> Tree6` at three
   distinct cells.
2. **Main-menu rules outdated:** rules now cover persistent rubble, tower garrison,
   ESC menu, save/load, and the corrected Barracks/Archery production split.
3. **Empty Continue shell:** the button now exists only when a valid save is present.
   With no save it is hidden and Start/Quit return to their original spacing; with a save
   the compact three-button layout returns. `MainMenu.Awake` hides it before `Start` to
   avoid a one-frame flash. Layout covered by
   `MainMenu_ContinueButton_OnlyExistsWhenSaveIsValid` without touching the user's save file.

## Findings intentionally left unfixed

1. **Known save limitation:** in-flight paths, current attack targets, and active haul targets
   are not serialized. Units stop after loading and the AI/reassignment rules take over.
   A player worker restored with cargo but no target can remain idle until reassigned.
2. **Minor UI polish:** the pause panel has a large empty lower section below the five buttons.
3. **Unreproduced observation:** one early full-scene load left the camera at zoom 25 instead
   of the saved 15. Direct restore and two subsequent full loads all restored 15 correctly;
   no deterministic cause was found.

## Cleanup

The test save at `%LOCALAPPDATA%Low/DefaultCompany/兵临城下/AtTheGates/save_slot_1.json`
was removed after verification. No user save existed before the test.
