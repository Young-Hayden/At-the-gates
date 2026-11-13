# 2026-10-29 RTS Phase A: Unit Caps, Stockpile Limits and Repair

## Context

The A01/A02 command-intent project is complete and approved at 96/96. The next approved direction is a phased expansion:

- **Phase A (this design):** unit caps, resource stockpile limits, central renewable meat, instant paid repair and AI key-building repair.
- **Phase B (separate):** house population, worker repair, technology/upgrades, meat consumption/upkeep, and a balance pass for unit stats, waves and starting economy.
- **Phase C (separate):** AI minimum-building recovery, composition ratios and AI garrison, integrated with the final Phase B rules.

Phase A is deliberately a foundation layer. Its unit-limit and resource-limit services must be extendable by Phase B without rewriting production or gathering logic.

## Goals

- Prevent either side from hoarding unlimited combat units.
- Give every profession an individual cap and add a total combat cap.
- Cap resource stockpiles and make automatic gathering avoid full resource kinds.
- Give meat a real source and a real sink via repair.
- Add instant paid repair for the player and automatic key-building repair for the AI.
- Preserve existing starting forces, unit values, wave timing, player starting resources and save compatibility.

## Non-goals

- No house population system, worker repair, technology, meat consumption/upkeep or balance retuning in Phase A. These belong to Phase B.
- No AI minimum-building recovery, composition ratios or AI garrison. These belong to Phase C.
- No change to unit HP/damage/range/speed, wave timing, player starting resources or resource values.
- No full HUD rebuild. The repair button is added incrementally.
- No save-version bump. All new limits are derived from live state; repair changes existing building health; sheep use the existing building/resource save path.

## Phase A Rules

### Unit caps

Per side, defaults:

| Role | Cap |
|---|---:|
| Worker | 6 |
| Warrior | 8 |
| Lancer | 5 |
| Archer | 5 |
| Monk | 4 |
| **Total combat** | **14** |

- Total combat includes Warrior, Lancer, Archer and Monk. Workers are counted only against the worker cap.
- Existing units above a cap are never deleted. They remain playable and simply block new production of that role/total.
- Counts include living active units, garrisoned units and queued production orders.
- Player and AI use the same limits.
- Unknown unit prefabs count only toward the total combat cap, not a role cap.

### Stockpile limits

Per side:

| Resource | Cap |
|---|---:|
| Gold | 2000 |
| Wood | 1200 |
| Meat | 1000 |

- `Add` clamps at the cap; overflow is discarded.
- Automatic gathering skips full resource kinds.
- If a worker finishes delivering a load and its current node's kind is full, it clears that job and automatically picks a non-full kind.
- Manual assignment to a full kind remains possible, but delivery is clamped.
- AI gathering uses the same full-kind filter.

### Central meat

- Three neutral sheep at `(18,2)`, `(19,5)`, `(20,7)` in the main central region.
- Each sheep: 300 meat, 20 per trip, 120-second regrow, existing `ResourceNode`/`Building`/`SaveIdentity`.
- Placement must preserve main-region connectivity and avoid building/unit footprints. The implementation validates this before saving the scene.
- Both sides can harvest; the group is contested but reachable.

### Repair

- Player selects a damaged friendly building and presses a new `维修` button.
- Repair is instant, restores all missing health, and does not create a worker task or construction state.
- Only alive, completed, non-neutral buildings can be repaired. Full-health, under-construction, wrecked and neutral buildings are refused.
- Cost:

  > `units = ceil(missingHealth / 10)`
  > cost = `units` gold + `units` wood + `units` meat.

- The player button shows the current cost/refusal reason and is disabled when unaffordable or ineligible.
- AI auto-repairs key buildings: command centre, production buildings and houses. Towers are not auto-repaired in Phase A.
- AI repairs when health is at or below 70%, using its own bank, and only if after repair it retains at least 100 gold, 50 wood and 25 meat.
- AI repair priority: command centre, production, house. It checks once every 3 seconds and performs at most one repair per check.

## Architecture

- `Assets/Scripts/UnitLimits.cs` (new)
  - Central role classification, per-team live/garrisoned/queued counts, per-frame cache and invalidation.
  - `CanProduce(Team, GameObject, out string reason)` used by player and AI production.
  - `Invalidate(Team)` after queue changes, spawns, deaths and load/restore.
- `Assets/Scripts/ResourceLimits.cs` (new)
  - Shared caps, `Clamp` and `IsFull(current, kind)` helpers.
- `Assets/Scripts/ProductionBuilding.cs`
  - `CanQueue` consults `UnitLimits` for combat/worker caps and exposes a refusal reason for the HUD.
  - Successful queue invalidates the team's count cache.
- `Assets/Scripts/HUD.cs` / `SelectionManager.cs`
  - Production buttons show cap refusal; unit shop shows `已达上限` when blocked.
  - New repair button; `SelectionManager.RepairSelected()` spends player resources and calls the building repair method once.
- `Assets/Editor/BuildRepairButton.cs` (new, incremental editor tool)
  - Adds the repair button to the existing SelectionPanel without rebuilding HUD.
- `Assets/Scripts/Building.cs`
  - `MissingHealth`, `RepairCost`, `CanRepair` and a repair helper. No knowledge of player/AI banks.
- `Assets/Scripts/Economy.cs` / `AIController.cs`
  - Clamp additions to stockpile caps; AI repair pass.
- `Assets/Scripts/Gatherer.cs` / `AIController.cs`
  - Full-kind filters and automatic switching after a delivery.
- `Assets/Scenes/SampleScene.scene`
  - Three sheep instances and the incremental repair button.

Save behavior:

- Unit caps and resource caps are derived; no new save fields.
- Repair changes `Building.health`, already saved.
- Sheep are existing saveable buildings/resource nodes.
- AI repair has no persistent state.

## Error handling and edge cases

- Production refusal never deducts resources.
- Cap cache is invalidated after successful queueing so two factories cannot spend the same headroom in one tick.
- Corrupt or missing prefabs count toward total only, never crash the cap scan.
- A full resource delivery clamps once; no negative stockpile or overflow into another kind.
- Repair spends once and only then repairs; a failed repair leaves health and resources unchanged.
- Repair does not target ghosts, neutral scenery, rubble or construction previews.
- AI repair does not touch player buildings or the player bank.
- Sheep placement is rejected/retried if the cells become occupied or disconnect the main region.

## Testing

1. Unit caps: role cap and total combat cap for player and AI; living + queued + garrisoned counts; over-cap existing units remain but new queue is refused without spending.
2. Stockpile: clamp at 2000/1200/1000; overflow discarded; automatic gathering skips full kinds and switches after delivery; manual delivery clamped.
3. Meat: three sheep are reachable, harvestable by both teams, regrow in 120 seconds and survive save/load.
4. Repair: cost formula by missing HP; instant full repair; one spend; full/under-construction/rubble/neutral refusal; player button state.
5. AI repair: key-building priority, 70% trigger, reserve floor, no tower repair, own bank only.
6. Regressions: existing 96 tests, A01/A02 command behavior, worker/healer/AI/save/garrison and production dependency suites.

## Acceptance

- No unlimited army/resource snowball under normal play.
- Existing starting forces fit the caps and are not removed.
- Repair is usable by the player and AI under the same cost formula.
- All final claims come from a fresh full PlayMode run after the last code change.
- Physical GUI acceptance remains a separately disclosed boundary.
