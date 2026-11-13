# 2026-10-31 RTS Phase B: Population, Worker Repair, Technology, Meat Upkeep and Balance

## Context

Phase A is complete and delivered (2026-10-31): unit caps 6/8/5/5/4 with a total combat cap of 14,
stockpile caps 2000/1200/1000 with full-kind gathering diversion, three central renewable meat sheep,
instant paid repair for the player and AI key-building repair. Full suite 142/142; final whole-phase
review Ready to deliver = Yes.

Phase A's design (2026-10-29) defines this phase's scope:

> **Phase B:** house population, worker repair, technology/upgrades, meat consumption/upkeep, and a
> balance pass for unit stats, waves and starting economy.

Phase A is the foundation this builds on: `UnitLimits` already counts live + garrisoned + queued units
and exposes a refusal reason; `ResourceLimits` owns every stockpile clamp; `Building.RepairCost`
prices repair for both sides; `ResourceNode` sheep give meat a real source.

## Goals

- Make housing matter: population capacity limits army growth, so expansion is a real decision.
- Give players a second, cheaper repair path that consumes time instead of gold (workers), so the
  instant repair from Phase A becomes a *choice* rather than the only option.
- Give meat a real **sink** (upkeep) so the central sheep are worth fighting over.
- Add a small, legible technology layer that changes how a mid-game army feels.
- Correct the balance problems found in the 2026-10-20 audit and confirmed by play: the player starts
  with less gold than the AI, the first AI wave arrives before a new player can finish a barrack, and
  the gold-mine price/value makes economy trivial.

## Non-goals

- No AI minimum-building recovery, composition ratios or AI garrison — those stay in Phase C.
- No new units, buildings or art. Every mechanic reuses an existing object.
- No full HUD rebuild: the population readout and repair-order affordances are added incrementally.
- No change to the save **format** except the single technology mask described below, and that change
  must be backward compatible (old saves load as "no techs researched").

## Phase B rules

### B1. Population

**Recommended values** (all tunable):

| Source | Population |
|---|---:|
| Base per side (no building required) | 12 |
| Each `House` | +6 |
| Absolute cap | 48 |

| Unit | Pop |
|---|---:|
| Worker (Pawn) | 1 |
| Archer | 1 |
| Monk | 1 |
| Warrior | 2 |
| Lancer | 2 |

- Current starting forces cost: blue 10 (3 pawns, 2 archers, 1 warrior, 1 lancer, 1 monk), red 8 — both
  fit under the base 12, so nobody starts blocked, but blue has only 2 free population and must build a
  house before fielding more.
- Counts use the **same** rule as `UnitLimits`: living + garrisoned + queued.
- Existing units over the cap are never deleted; only new production is refused (reason `人口已满`
  shown in the production panel and the unit shop, exactly like the Phase A cap reasons).
- Houses under construction do not contribute population; a destroyed house lowers the cap and blocks
  further production until the side is under it again.
- Population is **derived** — no save fields.

### B2. Worker repair

- A worker ordered onto a damaged friendly building repairs it at **20 HP per second**, free, while
  standing adjacent.
- Order path: select workers, right-click a damaged friendly building (the existing right-click already
  distinguishes resources and enemies; this adds the friendly-damaged-building case).
- Several workers may stack on one building; each is a worker removed from the economy — that is the
  intended trade.
- Refused for: under construction, rubble, neutral scenery, full health, and enemy buildings (that is
  an attack order, unchanged).
- The worker flees when damaged and resumes afterwards, reusing the existing `Gatherer` retreat logic.
- Worker repair is a **task state**, not saved, exactly like a gathering target.
- Worker repair does not stack with the Phase A instant repair in a way that double-pays: instant repair
  remains instant and paid; worker repair is free and slow. A building can receive both over time.

### B3. Technology

Three lines, each **one level**, researched at the command centre. Each is queued like a production
order (cost paid up front, progress over time, one at a time per castle).

| Tech | Effect | Cost | Time |
|---|---|---|---:|
| 锻造 (Forge) | +2 damage for every combat unit | 250 gold + 100 wood | 45 s |
| 铠甲 (Armour) | +15 % max HP for every combat unit | 250 gold + 100 wood | 45 s |
| 采集 (Gathering) | +2 resources per trip for every worker | 200 gold | 45 s |

- Effects apply to units existing when the tech completes **and** units produced later.
- Combat bonuses are stored as multipliers/flat bonuses on `UnitActor`, not by editing prefab stats, so
  the prefab values stay the source of truth and a fresh game is unchanged.
- The AI researches in a fixed order (Forge → Armour → Gathering) whenever it has a castle, can afford
  it and is not already researching.
- HUD: a small technology strip in the command-centre selection panel, with cost, progress and
  `已研究` state. Added incrementally; no HUD rebuild.

**Save impact (the one schema change in Phase B):** a per-side `techMask` (int) is added to the save.
`SaveGameService.Version` goes from 1 to 2. Version 1 saves load with `techMask = 0` and the
version-2 field defaults — no data loss, no migration script. This is the only save change in the phase.

### B4. Meat upkeep

- Every **10 seconds**, each living **combat** unit costs **1 meat** (workers and buildings cost
  nothing). A 10-unit army costs 60 meat/minute.
- If the bank cannot pay the full amount, the side is **starved until it can**: no health loss, but
  production is blocked with the reason `食物不足` and monk healing is disabled. This is a soft, fully
  recoverable penalty that deliberately avoids a death spiral.
- Upkeep is rounded down per tick and never takes the bank negative.
- The HUD meat readout turns amber below 50 and red at 0.
- The central sheep (Phase A) and the AI's own sheep harvesting become strategically important; the AI
  should prefer meat when it is starving.
- Upkeep has **no persistent state** (a timer restarts after load).

### B5. Balance pass

Recommended corrections, each justified by a measured problem:

| Item | Now | Proposed | Why |
|---|---|---|---|
| Player starting gold | 500 | **600** (match the AI) | The AI starts with 600; the player is 100 behind in every opening |
| AI `firstAttack` | 90 s | **120 s** | A perfect player chain is castle 25 s + barrack 10 s + first warrior 6 s ≈ 41 s; 90 s leaves no room to recover from one mistake. The 2026-10-20 audit recommended 120+ |
| Gold mine cost / yield | 100 gold for 1500 | **300 gold for 1500** | 100 gold buying 1500 made economy trivial; 300 keeps the "invest early" decision honest |
| House cost | 50 gold | **75 gold** | Population now comes from houses, so they are worth more |
| AI `maxGatherers` | 4 | **5** | With upkeep the AI needs meat as well as gold/wood |
| Worker flee radius | 10 | **6**, resume 3 s after the threat clears | A single raider parked near the base currently zeroes the player's economy permanently |

Everything else (unit HP/damage/range/cooldown, wave size, resource per trip, building HP) stays as is
unless the implementation reveals a specific defect.

## Architecture

- `Assets/Scripts/Population.cs` (new): capacity from houses + base, used from
  `ProductionBuilding.CanQueue` and the HUD, with the same frame-cache/invalidate pattern as
  `UnitLimits`.
- `Assets/Scripts/UnitLimits.cs`: unchanged counting; adds the population cost table or delegates to
  `Population`.
- `Assets/Scripts/Technology.cs` (new): definitions, per-team mask, apply/remove bonuses, castle
  research queue.
- `Assets/Scripts/UnitActor.cs` / `CombatAI.cs` / `Healer.cs`: read the tech bonuses when computing
  damage, max HP and healing.
- `Assets/Scripts/Gatherer.cs`: worker-repair task (new state alongside gather states), reusing the
  retreat logic.
- `Assets/Scripts/Building.cs`: `RepairByWorkers(dt)` or a worker-facing repair entry point; no bank
  knowledge.
- `Assets/Scripts/SelectionManager.cs`: right-click routing for friendly damaged buildings.
- `Assets/Scripts/Economy.cs` / `AIController.cs`: upkeep tick and starvation state; AI research order,
  AI meat preference while starving.
- `Assets/Scripts/HUD.cs`: population readout, tech strip, `食物不足` reasons. Incremental editor tool
  for any new scene objects.
- `Assets/Scripts/SaveGameService.cs` / `SaveGameData.cs`: `techMask`, version 2, backward-compatible.
- `Assets/Scenes/SampleScene.scene`: only what the UI additions need (no new gameplay objects).

## Testing

1. Population: base capacity, house adds capacity, under-construction house does not, destroyed house
   lowers it, over-cap existing units stay, production refused with the right reason, queued units
   counted, player and AI identical rules.
2. Worker repair: rate, adjacency, refusal cases, multiple workers, retreat and resume, no double
   payment with instant repair.
3. Technology: cost, time, one-at-a-time, effect on existing and future units, AI order, save/load
   round trip including version-1 migration.
4. Upkeep: tick amount, rounding, never negative, starvation blocks production and healing, recovery
   when meat returns, no health loss.
5. Balance: the new starting values and AI timings are what the scene actually loads.
6. Regressions: the full Phase A suite (142 tests) plus A01/A02, save/load, garrison and worker suites.

## Acceptance

- A player who builds no house cannot grow past 12 population.
- Repair has two viable paths with a real cost/time trade.
- Meat flows every game and the sheep become contested.
- Technology is legible, bounded and survives save/load.
- The opening is survivable for a new player: no AI wave before 120 s, and the player starts level with
  the AI.
- All final claims come from a fresh full PlayMode run after the last code change.
- Physical GUI acceptance remains a separately disclosed boundary.

## Decisions needed from the user before planning

1. **Population numbers** — base 12 / house +6 / cap 48, with Warrior and Lancer costing 2? (My
   recommendation: yes; it makes heavy armies need houses.)
2. **Technology scope** — three one-level lines as tabled, or fewer (e.g. drop Gathering)?
3. **Upkeep severity** — soft (production blocked + no healing) as proposed, or harder (units lose HP)?
4. **Save version bump to 2** for `techMask` — acceptable, with version-1 saves loading as no techs?
5. **AI research** — should the AI get the same three techs in a fixed order (so the player is not
   alone in having them)?
6. **Balance items** — accept all six in the table, or drop some (gold mine 300 and house 75 are the
   two that change early-game feel most)?
