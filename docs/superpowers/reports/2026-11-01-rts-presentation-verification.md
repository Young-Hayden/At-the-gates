# Presentation phase verification — sheep wandering + UI refinement (P1–P4)

Final verification pass, 2026-11-01. Phase plan:
`docs/superpowers/plans/2026-10-31-rts-presentation-sheep-and-ui.md`.

## 1. Delivered

| Task | Delivered | Tests |
|---|---|---|
| P1 sheep wandering | `SheepWander.cs`, `MapGrid` blocking-delta API, sheep prefab animator; third sheep relocated to (22,3)+(23,3) with the user's approval | `SheepWanderTests` 9/9 |
| P2 HUD refinement | `PolishHUD.cs` (`Tools/UI/精修 HUD`), null-safe `HUD.cs` wiring | `HudPolishTests` 6/6 (5 at delivery, +1 after review) |
| P3 menu refinement | `PolishMenus.cs` (`Tools/UI/精修菜单/…`), pause/loading/main-menu contrast fixes | `MenuPolishTests` 3/3 |
| P4 verification | this report; ledger + RESUME updated | full suite below |

**Full PlayMode suite at the end: 236/236 passed, 0 failed, 0 skipped, 193.210 s.**
Log `Logs/…` archived as `.superpowers/sdd/presentation/task-p4-review-fixes-full-suite.log`
(md5 `397d873e6bc0e00ed6ad48133bf41001`).

Progression: 213 (phase B) → 222 (P1) → 227 (P2) → 230 (P3) → 232 (backlog) → 235 (backlog round 2)
→ 236 (after the independent review's fixes).

## 2. Asset state

| Asset | Final MD5 | Backup |
|---|---|---|
| `Assets/Scenes/SampleScene.scene` | `4f51757e04ebfa29b51ca27490e3f340` | `task-p2-scene-before.scene` (`94f9ae63…` = phase-B state), `task-p2-scene-after.scene` (`95c7c207…` = after P2), `p3-sample-before-pausepolish.scene` (`95c7c207…`) |
| `Assets/Scenes/MainMenu.scene` | `d1ee7be3dc50cdc624eeb0864f637e92` | `p3-mainmenu-before.scene` |
| `Assets/Resources/LoadingScreen.prefab` | `727ef5add672164c6dc450caecb1e05a` | `p3-loading-before.prefab` |
| `Assets/Scripts/HUD.cs` | `8d806008…` | — |
| `Assets/Editor/PolishHUD.cs` / `PolishMenus.cs` | new | — |

Rollback for the game scene:
`python -c "import shutil; shutil.copy2('.superpowers/sdd/presentation/task-p2-scene-before.scene','Assets/Scenes/SampleScene.scene')"`
restores the phase-B state; each later stage has its own pre-change copy in the same folder.

## 3. Screens inspected (PlayMode `ScreenCapture`, actually read)

| Screen | File | What I saw |
|---|---|---|
| Game HUD | `Screenshots/p2_applied_sample.png` | resource bar reading `600金 300木 196肉 人口 10/12`, build bar with two-line costs and 1–6 badges, unit panel with framed portraits/names/costs and dim overlays, framed minimap, no floating status band |
| Castled selection | `Screenshots/p4_selection_final.png` | selection panel + five body lines fitting above the tech strip, three tech buttons with their prices inside the frame, repair button |
| Main menu | `Screenshots/p3_mainmenu_final.png` | title bar, rules card, centred buttons, troop row; unchanged content, labels now centred |
| Pause menu | `Screenshots/p3_pause_final.png` | outlined `游 戏 暂 停`, five buttons, `尚无存档` now dark brown and legible |
| Quit confirm | `Screenshots/p3_confirm_after.png` | outlined title, dark hint line, three buttons |
| Loading screen | `Screenshots/p3_loading_after.png` | outlined `加 载 中 …`, darkened tip, units and progress bar unchanged |

Before/after pairs exist for every screen (`p3_*_before.png` vs `*_after/_final.png`).

## 4. Scope audit

Files whose modification time falls in this session (2026-11-01 after 09:00), under `Assets/`:

```
Assets/Editor/PolishHUD.cs            (+ .meta)      new tool
Assets/Editor/PolishMenus.cs          (+ .meta)      new tool
Assets/Scripts/HUD.cs                                wiring only
Assets/Tests/PlayMode/HudPolishTests.cs (+ .meta)    new tests
Assets/Tests/PlayMode/MenuPolishTests.cs (+ .meta)   new tests
Assets/Scenes/SampleScene.scene                      P2 + P3 writes
Assets/Scenes/MainMenu.scene                         P3 write
Assets/Resources/LoadingScreen.prefab                P3 write
```

No gameplay script, no balance value and no unrelated prefab was touched by this phase. (The many
other files that show as modified against HEAD are the older uncommitted batch; they were not
touched today — timestamps confirm it.)

## 5. Review notes (self-review, not independent)

The phase plan calls for an independent review of each task. P1's review was carried out by a
separate reviewer before this session; **P2 and P3 were reviewed by the author only** and that is a
real limitation, stated plainly rather than papered over.

Points I checked while reviewing:

- **No gameplay code in the diff.** `HUD.cs` reads gameplay state and paints; it never mutates it.
  `PolishHUD`/`PolishMenus` only touch scene objects.
- **Null-safety.** `HUD.cs`'s new fields all have empty/`null` defaults and the old code paths remain
  when they are unwired, proven by the 222-test run before the scene write.
- **Idempotence.** Every tool reports `No changes needed` on a second run and leaves MD5s unchanged.
- **Anti-vacuity.** The two "this should not happen / this must hold" style assertions that mattered
  were mutated: deleting the footprint update in P1 turned its test red; raising a label's font to
  120 px turned the P2 fit test red. Both scenes were restored byte-for-byte and the MD5s re-verified.
- **Known residual risks.** `MenuPolishTests` would be the first thing to fail if someone re-runs the
  scene builders; `HudPolishTests` likewise guards the HUD. `ScreenCapture` images are not a
  substitute for physical GUI acceptance, which is still outstanding.

## 6. Independent review (2026-11-01) and what it changed

A separate reviewer audited P2/P3 plus both backlog rounds against the specs, the scene backups and
the source. Verdict: **Ready to deliver = Yes, 0 Critical, 0 Important, 4 Minor.** All four were
accepted and fixed:

| # | Finding | Fix |
|---|---|---|
| 1 | `HudPolishTests`' dim assertions were vacuous against *deleting* the branch: at match start every slot is unavailable and every overlay is on, which is exactly what the deleted branch looks like. The "status line has no 人口" assertion was also trivially true on an empty string. | The dim test now builds a barracks, gives money, and demands the warrior slot become buyable **and undimmed**; a new test nulls `populationText` and demands the status line pick the readout up. Both proved by mutation (below). |
| 2 | The label test measured against `label.rect`, not the button, and its `splitFace` predicate (`anchorMin.y != anchorMax.y`) was true for *every* stretched label — so the centring check never actually ran. | Fit is now measured against the **button** rect; `splitFace` means "does not span the full height", so centring runs on the 10 full-face labels. Split-face buttons must keep label and price inside and clear of each other; icon+text buttons (attack/repair) are exempt from centring but must keep label and icon inside and clear of each other — which exposed that their label sits 15 px right of centre **by design** (it makes room for the icon). |
| 3 | The shop's "食物不足" sat **before** "队列已满" while `CanQueue` checks the queue first, and the backlog note claimed the orders matched. | `HUD.RefreshUnitPanel` now checks the queue first; the note is corrected. |
| 4 | The minimap's `-306` offset was encoded only in `BuildTechPanel`; `BuildMinimap` and `EnhanceDemolitionUI` still wrote `-218`. | Both now write `-306` with a comment; the layout guard test still pins the shipped result. |

**New mutation proofs** (both red, then byte-restored):

- deleting `unitDims[i].enabled = !ok` → `An available slot must not be dimmed… Expected: False But was: True`
  (`backlog/mutation-f-red.log`);
- deleting the status-line fallback → `With no population cell the status line must carry the
  readout; it reads "". Expected: String containing "人口"` (`backlog/mutation-g-red.log`);
  `HUD.cs` restored byte-for-byte, MD5 `0aac0dab…`.

`HudPolishTests` is now six tests. Full suite after the fixes: **236/236, 0 failed, 193.210 s**
(`task-p4-review-fixes-full-suite.log` md5 `397d873e…`); the run left `SampleScene` at `0ddbdbd6…`.

The reviewer also stated what it could **not** verify: no PlayMode runs, no independent frame-time
re-measurement, the minimap tool's "wrong position → corrected" direction, and physical GUI feel.

## 7. Known gaps deliberately left

- Physical GUI acceptance (no one has run the game by hand on the target display).
- `quickBuild`'s six debug hotkeys are still wired in `SampleScene` (pressing 1–6 enters placement).
- The backlog listed in the P2/P3 reports (missing-meat gate on the unit shop, `Gatherer.Credit`
  return value, AI research logging, `UnitLimits` unknown-role accounting, minimap position not
  encoded in `BuildTechPanel`, garrisoned archers and armour, etc.).
- ~~The settings gear floats at the canvas corner outside the board~~ — corrected 2026-11-01: it is a
  child of `Board`, 20 px inside the parchment (`Screenshots/p4_gear_crop.png`). No change needed.
