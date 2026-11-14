using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The opponent. It plays the same opening the player does: no base to start with, a
/// stockpile of its own, and a build order it works through as the money comes in —
/// command centre first, because that is also its drop-off, then production buildings.
///
/// It keeps its own bank rather than sharing <see cref="Economy"/>, so both sides can
/// spend independently. <see cref="Gatherer"/> credits whichever bank matches the
/// worker's team.
/// </summary>
public class AIController : MonoBehaviour
{
    public static readonly List<AIController> All = new List<AIController>();

    /// <summary>The AI that owns a team, so a worker's delivery knows where to land.</summary>
    public static AIController For(Team team)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].team == team) return All[i];
        return null;
    }

    [Header("阵营")]
    public Team team = Team.Red;
    public bool active = true;

    [Header("开局资源（AI 自己一本账）")]
    public int startGold = 500;
    public int startWood = 300;
    public int startMeat = 200;

    [Header("建造")]
    [Tooltip("按这个顺序盖，钱不够就等。指挥部放第一个 —— 它同时是卸货点。")]
    public GameObject[] buildOrder = new GameObject[0];
    [Tooltip("基地中心格，建筑都围着这里找地方。")]
    public Vector2Int homeCell = new Vector2Int(34, 4);
    public float firstBuildDelay = 5f;
    public float buildRetrySeconds = 7f;
    [Tooltip("围绕基地找位置的最大半径（格）。")]
    public int buildSearchRadius = 14;

    [Header("采集")]
    [Tooltip("最多派几个工人去采。")]
    public int maxGatherers = 4;
    public float gatherCheckSeconds = 3f;

    [Header("出兵 / 进攻节奏（秒）")]
    public float firstProduce = 6f;
    public float produceEvery = 9f;
    public float firstAttack = 45f;
    public float attackEvery = 38f;
    [Tooltip("每次进攻最多派几个单位。")]
    public int maxAttackers = 8;

    [Header("兵种配比（相对权重，0 = 不主动生产）")]
    [Tooltip("权重是相对值：权重 3 的职业期望数量是权重 1 的三倍。"
           + "实际选择看「该职业距离目标份额还差多少」，所以军队越大人越齐。")]
    public float workerWeight = 1f;
    public float warriorWeight = 3f;
    public float lancerWeight = 2f;
    public float archerWeight = 3f;
    public float monkWeight = 1f;

    // ---- private bank ----
    int gold, wood, meat;

    float buildTimer, produceTimer, attackTimer, gatherTimer, repairTimer, researchTimer;
    int nextBuild;
    int wave;

    /// <summary>Reused candidate buffer for <see cref="ChooseBest"/>; the produce tick runs
    /// a few times a minute, but the project keeps hot loops allocation-free on habit.</summary>
    readonly List<int> candidates = new List<int>();

    /// <summary>How often the AI looks for a key building worth repairing. Cadence
    /// only, deliberately not part of the save schema.</summary>
    const float RepairCheckSeconds = 3f;

    /// <summary>How often the AI looks at the next technology line. Cadence only; the
    /// line itself lives in <see cref="Technology"/> and is saved there.</summary>
    const float ResearchCheckSeconds = 3f;

    /// <summary>The fixed research order: Forge first for its immediate damage, then
    /// Armour, then Gathering last because it only pays back over a long game. Read by
    /// <see cref="Research"/>; the costs and durations come from
    /// <see cref="Technology"/> so both sides always pay the same.</summary>
    static readonly Technology.Tech[] ResearchOrder =
    {
        Technology.Tech.Forge,
        Technology.Tech.Armour,
        Technology.Tech.Gathering,
    };

    public int Gold => gold;
    public int Wood => wood;
    public int Meat => meat;
    public int NextBuildIndex => nextBuild;

    /// <summary>The AI's own stockpile for one resource kind. Mirrors
    /// <see cref="Economy.Get"/> so caps and dispatch rules can be shared.</summary>
    public int Get(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Gold: return gold;
            case ResourceKind.Wood: return wood;
            default:                return meat;
        }
    }

    /// <summary>True when this AI cannot bank any more of <paramref name="kind"/>.</summary>
    public bool IsFull(ResourceKind kind) => ResourceLimits.IsFull(Get(kind), kind);

    [System.Serializable]
    public class SaveState
    {
        public Team team;
        public bool active;
        public int gold, wood, meat;
        public int nextBuild, wave;
        public float buildTimer, produceTimer, attackTimer, gatherTimer;
    }

    public SaveState CaptureSaveState()
    {
        return new SaveState
        {
            team = team,
            active = active,
            gold = gold,
            wood = wood,
            meat = meat,
            nextBuild = nextBuild,
            wave = wave,
            buildTimer = buildTimer,
            produceTimer = produceTimer,
            attackTimer = attackTimer,
            gatherTimer = gatherTimer,
        };
    }

    /// <summary>Called after Start has initialised the AI clocks.</summary>
    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        team = state.team;
        active = state.active;
        gold = ResourceLimits.Clamp(state.gold, ResourceKind.Gold);
        wood = ResourceLimits.Clamp(state.wood, ResourceKind.Wood);
        meat = ResourceLimits.Clamp(state.meat, ResourceKind.Meat);
        nextBuild = state.nextBuild;
        wave = state.wave;
        buildTimer = state.buildTimer;
        produceTimer = state.produceTimer;
        attackTimer = state.attackTimer;
        gatherTimer = state.gatherTimer;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        gold = ResourceLimits.Clamp(startGold, ResourceKind.Gold);
        wood = ResourceLimits.Clamp(startWood, ResourceKind.Wood);
        meat = ResourceLimits.Clamp(startMeat, ResourceKind.Meat);
        buildTimer = firstBuildDelay;
        produceTimer = firstProduce;
        attackTimer = firstAttack;
        gatherTimer = 1f;
        repairTimer = RepairCheckSeconds;
        researchTimer = ResearchCheckSeconds;
    }

    // ------------------------------------------------------------------ bank

    /// <summary>This side's stockpile. Read-only twin of <see cref="Add"/>, so callers can
    /// measure how much of a haul actually landed against the storage cap.</summary>
    public int Stock(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Gold: return gold;
            case ResourceKind.Wood: return wood;
            default: return meat;
        }
    }

    public void Add(ResourceKind kind, int amount)
    {
        if (amount == 0) return;
        switch (kind)
        {
            case ResourceKind.Gold: gold = ResourceLimits.ClampAdd(gold, amount, kind); break;
            case ResourceKind.Wood: wood = ResourceLimits.ClampAdd(wood, amount, kind); break;
            default:
                meat = ResourceLimits.ClampAdd(meat, amount, kind);
                // A delivered load ends an AI famine at once, exactly like the player's.
                Upkeep.NoteMeatReceived(team);
                break;
        }
    }

    /// <summary>Return an already-paid order to this AI's own capped bank.</summary>
    public void Refund(ResourceCost cost)
    {
        Add(ResourceKind.Gold, cost.gold);
        Add(ResourceKind.Wood, cost.wood);
        Add(ResourceKind.Meat, cost.meat);
    }

    /// <summary>Meat-only upkeep payment from this AI's own bank. Returns the amount
    /// actually paid; the rest is simply unpaid, never debt. Routed through the same
    /// private <see cref="TrySpend"/> as every other AI purchase.</summary>
    public int SpendMeat(int amount)
    {
        if (amount <= 0) return 0;
        int paid = Mathf.Min(meat, amount);
        if (paid <= 0) return 0;
        return TrySpend(new ResourceCost(0, 0, paid)) ? paid : 0;
    }

    /// <summary>True while this side cannot feed its army. Production refuses new
    /// orders through <see cref="ProductionBuilding.CanQueue"/>, the gather dispatch
    /// goes meat-first, and <see cref="Research"/> holds new lines back: a famine is
    /// ended by meat, not by spending the gold on a technology.</summary>
    public bool IsStarving => Upkeep.IsStarving(team);

    public bool CanAfford(ResourceCost c) => gold >= c.gold && wood >= c.wood && meat >= c.meat;

    /// <summary>Could this AI still place anything at all? GameState's wipe-out check
    /// asks this before declaring the side dead: a bankrupt base-less AI is defeated,
    /// a rich one can rebuild. Money for units does not count — production needs a
    /// factory, and factories need buildings.</summary>
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

    bool TrySpend(ResourceCost c)
    {
        if (!CanAfford(c)) return false;
        gold -= c.gold; wood -= c.wood; meat -= c.meat;
        return true;
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        if (!active) return;
        float dt = Time.deltaTime;

        buildTimer -= dt;
        if (buildTimer <= 0f) { buildTimer = buildRetrySeconds; Build(); }

        gatherTimer -= dt;
        if (gatherTimer <= 0f) { gatherTimer = gatherCheckSeconds; AssignGatherers(); }

        produceTimer -= dt;
        if (produceTimer <= 0f) { produceTimer = produceEvery; Produce(); }

        attackTimer -= dt;
        if (attackTimer <= 0f) { attackTimer = attackEvery; LaunchAttack(); }

        repairTimer -= dt;
        if (repairTimer <= 0f) { repairTimer = RepairCheckSeconds; RepairBuildings(); }

        researchTimer -= dt;
        if (researchTimer <= 0f) { researchTimer = ResearchCheckSeconds; Research(); }
    }

    // ------------------------------------------------------------------ build

    void Build()
    {
        if (buildOrder == null || nextBuild >= buildOrder.Length) return;
        var prefab = buildOrder[nextBuild];
        if (prefab == null) { nextBuild++; return; }

        var b = prefab.GetComponent<Building>();
        if (b == null) { nextBuild++; return; }
        if (!b.isCommandCenter && !Building.HasCompletedCommandCentre(team)) return;
        if (!CanAfford(b.cost)) return;                 // wait until it can be paid for

        if (!FindSite(prefab, out Vector3 pos)) return; // no room just yet, try again later
        if (!TrySpend(b.cost)) return;

        var go = Instantiate(prefab, pos, Quaternion.identity);
        go.name = prefab.name + "_AI";
        var nb = go.GetComponent<Building>();
        if (nb != null) nb.MarkBuiltDuringMatch();
        nextBuild++;
        Debug.Log($"[AI] 建成 {prefab.name} @{pos}（余 金{gold} 木{wood}）");
    }

    /// <summary>Footprint width/depth without touching the prefab asset's runtime state.</summary>
    static void Footprint(GameObject prefab, out int w, out int d)
    {
        var b = prefab.GetComponent<Building>();
        var sr = prefab.GetComponentInChildren<SpriteRenderer>();
        w = b != null && b.footprint.x > 0 ? b.footprint.x
          : (sr != null && sr.sprite != null ? Mathf.Max(1, Mathf.RoundToInt(sr.sprite.bounds.size.x)) : 1);
        d = b != null && b.footprint.y > 0 ? b.footprint.y : (b != null ? Mathf.Max(1, b.depth) : 1);
    }

    /// <summary>Bottom-left cell of the footprint -> the position Building expects.</summary>
    static Vector3 PositionFor(int ax, int ay, int w) => new Vector3(ax + w * 0.5f, ay, 0f);

    /// <summary>Ring order around home: nearest first, so the base grows outward.</summary>
    bool FindSite(GameObject prefab, out Vector3 pos)
    {
        pos = Vector3.zero;
        var grid = MapGrid.Instance;
        if (grid == null) return false;

        Footprint(prefab, out int w, out int d);
        int homeLevel = grid.Level(new Vector3Int(homeCell.x, homeCell.y, 0));
        var solid = SolidCells();
        // Unit occupancy is constant for this one FindSite call. Build the set once
        // instead of running a full scene FindObjectsByType for every candidate.
        var unitCells = UnitCells(grid);

        for (int r = 0; r <= buildSearchRadius; r++)
        {
            for (int ax = homeCell.x - r; ax <= homeCell.x + r; ax++)
            for (int ay = homeCell.y - r; ay <= homeCell.y + r; ay++)
            {
                // ring only — the interior was tried on earlier passes
                if (r > 0 && ax > homeCell.x - r && ax < homeCell.x + r
                         && ay > homeCell.y - r && ay < homeCell.y + r) continue;
                if (!FitsWithUnits(grid, ax, ay, w, d, homeLevel, solid, unitCells)) continue;
                pos = PositionFor(ax, ay, w);
                return true;
            }
        }
        return false;
    }

    /// <summary>Minimum empty ring around a new building. Footprints are only one tile
    /// deep but the art is three to six tiles tall, so buildings placed one tile apart
    /// draw on top of each other and the base turns into mush.</summary>
    const int BuildingGap = 2;

    /// <summary>Cells taken by real buildings — trees excluded.
    ///
    /// Trees are <see cref="Building"/> components too, and there are 150 of them. Asking
    /// for two clear tiles around every candidate against *all* buildings means the AI
    /// never finds a site on a wooded map and quietly builds nothing. Trees are scenery;
    /// the base is allowed to grow next to them.</summary>
    static HashSet<Vector3Int> SolidCells()
    {
        var set = new HashSet<Vector3Int>();
        foreach (var b in Building.All)
        {
            if (b == null || !b.IsAlive) continue;
            var rn = b.GetComponent<ResourceNode>();
            if (rn != null && rn.kind == ResourceKind.Wood) continue;
            foreach (var c in b.Cells) set.Add(c);
        }
        return set;
    }

    static HashSet<Vector3Int> UnitCells(MapGrid grid)
    {
        var set = new HashSet<Vector3Int>();
        foreach (var unit in Object.FindObjectsByType<UnitMover>(FindObjectsSortMode.None))
            if (unit != null) set.Add(grid.WorldToCell(unit.transform.position));
        return set;
    }

    static bool Fits(MapGrid grid, int ax, int ay, int w, int d, int level, HashSet<Vector3Int> solid)
    {
        // Compatibility entry point for a one-off validation (and its regression test).
        return FitsWithUnits(grid, ax, ay, w, d, level, solid, UnitCells(grid));
    }

    static bool FitsWithUnits(MapGrid grid, int ax, int ay, int w, int d, int level,
                     HashSet<Vector3Int> solid, HashSet<Vector3Int> unitCells)
    {
        // The building itself must satisfy the same terrain/occupancy rules as the
        // player, including units currently standing in the footprint.
        var cells = new List<Vector3Int>(w * d);
        for (int i = 0; i < w; i++)
        for (int j = 0; j < d; j++)
            cells.Add(new Vector3Int(ax + i, ay + j, 0));
        if (!PlacementRules.ValidateCells(grid, cells, checkUnits: false, out _)) return false;
        for (int i = 0; i < cells.Count; i++)
            if (unitCells.Contains(cells[i])) return false;

        // Shared validation checks internal height consistency; the AI additionally
        // keeps its whole base on the command centre's original level.
        foreach (var c in cells)
            if (grid.Level(c) != level) return false;

        // and breathing room around it, measured against real buildings only
        for (int i = -BuildingGap; i < w + BuildingGap; i++)
        for (int j = -BuildingGap; j < d + BuildingGap; j++)
        {
            if (i >= 0 && i < w && j >= 0 && j < d) continue;   // already checked
            var c = new Vector3Int(ax + i, ay + j, 0);
            if (solid.Contains(c)) return false;
            if (grid.IsRamp(c)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ repair

    /// <summary>Spend the AI's own bank on the most valuable damaged key building.
    /// One repair per tick, instant and paid from this AI's ledger — the player's
    /// <see cref="Economy"/> is never touched. The price comes from
    /// <see cref="Building.RepairCost"/> so both sides pay the same.</summary>
    void RepairBuildings()
    {
        Building best = null;
        int bestPriority = 99;
        int bestMissing = 0;

        foreach (var b in Building.All)
        {
            if (b == null || b.team != team) continue;
            if (!b.IsAlive || b.IsUnderConstruction) continue;
            if (b.team == Team.Neutral) continue;
            int missing = b.MissingHealth;
            if (missing <= 0) continue;
            if (b.health * 100 > b.maxHealth * 70) continue;   // only at or below 70%

            int priority = RepairPriority(b);
            if (priority >= 99) continue;                     // towers are not auto-repaired
            if (priority < bestPriority
                || (priority == bestPriority && missing > bestMissing))
            {
                best = b;
                bestPriority = priority;
                bestMissing = missing;
            }
        }
        if (best == null) return;

        var cost = best.RepairCost;
        if (gold - cost.gold < 100) return;                    // keep a reserve for rebuilding
        if (wood - cost.wood < 50) return;
        if (meat - cost.meat < 25) return;
        if (!TrySpend(cost)) return;
        best.Repair(best.MissingHealth);
    }

    /// <summary>0 command centre, 1 production, 2 house, 99 everything else.</summary>
    static int RepairPriority(Building b)
    {
        if (b.isCommandCenter) return 0;
        if (b.GetComponent<ProductionBuilding>() != null) return 1;
        if (b.name.Contains("House")) return 2;
        return 99; // towers and other key-less buildings are not auto-repaired
    }

    // ------------------------------------------------------------------ research

    /// <summary>The first line in <see cref="ResearchOrder"/> this side has not
    /// researched, or null once all three are done.</summary>
    Technology.Tech? NextResearch()
    {
        for (int i = 0; i < ResearchOrder.Length; i++)
            if (!Technology.Has(team, ResearchOrder[i])) return ResearchOrder[i];
        return null;
    }

    /// <summary>Starts the next line in <see cref="ResearchOrder"/> from this AI's own
    /// bank. <see cref="Technology.BeginResearch(Team, Technology.Tech,
    /// System.Func{ResourceCost, bool}, out string)"/> owns every rule that matters —
    /// one line at a time, already-researched refusal, a completed command centre and
    /// the up-front cost — and <see cref="TrySpend"/> is the same literal path every
    /// other AI purchase uses, so the player's <see cref="Economy"/> is never touched
    /// and there is no second research implementation to drift. A starving side waits:
    /// it cannot afford to ignore its army, and meat is what ends the famine.</summary>
    /// <summary>The last refusal this AI logged, so an unaffordable technology is reported
    /// once instead of every <see cref="ResearchCheckSeconds"/>.</summary>
    string lastResearchRefusal;

    void Research()
    {
        if (IsStarving) return;
        var tech = NextResearch();
        if (!tech.HasValue) return;
        if (Technology.BeginResearch(team, tech.Value, TrySpend, out string reason))
        {
            lastResearchRefusal = null;
            return;
        }
        // A refusal used to be swallowed (`out _`), so an AI that could never afford its
        // next line looked identical to one with nothing left to research.
        string key = team + ":" + tech.Value + ":" + reason;
        if (key != lastResearchRefusal)
        {
            lastResearchRefusal = key;
            Debug.Log("[AI] " + team + " 研究 " + tech.Value + " 暂缓：" + reason);
        }
    }

    // ------------------------------------------------------------------ gather

    /// <summary>Send idle workers to the nearest resource once there is a drop-off.</summary>
    void AssignGatherers()
    {
        var dropoff = Building.FindNearestDropoff(HomePosition, team);
        if (dropoff == null) return;                       // no centre yet — nothing to haul to

        var idle = new List<Gatherer>();
        foreach (var u in UnitActor.All)
        {
            if (u == null || !u.IsAlive || u.team != team) continue;
            var g = u.GetComponent<Gatherer>();
            if (g == null || g.IsWorking) continue;
            idle.Add(g);
        }
        if (idle.Count == 0) return;

        // Budget = cap minus those already working. (The old formula subtracted
        // idle.Count as well, which *raised* the budget whenever idlers existed —
        // the AI ended up fielding more gatherers than maxGatherers.)
        int budget = Mathf.Max(0, maxGatherers - CountWorking());
        bool starving = Upkeep.IsStarving(team);
        foreach (var g in idle)
        {
            if (budget <= 0) break;
            // Same reachability rule as Gatherer.TryAutoAssign, plus the same
            // one-worker-per-node spread so AI workers do not stack on one deposit.
            // A kind whose AI bank is full is skipped: the load would be discarded.
            // A starving side looks for meat first — that is the only thing that ends
            // the famine — but still works when no meat node is reachable.
            var node = FindGatherNode(g, starving);
            if (node == null) continue;
            g.AssignAuto(node);
            budget--;
        }
    }

    /// <summary>The nearest node this worker can actually use. While starving, meat is
    /// tried first even when a gold mine or a tree is nearer; a side with no reachable
    /// meat keeps gathering rather than standing idle.</summary>
    ResourceNode FindGatherNode(Gatherer g, bool starving)
    {
        if (starving)
        {
            var meat = ResourceNode.FindNearestReachable(
                g.transform.position, 40f, null,
                candidate => candidate.kind == ResourceKind.Meat
                          && !Gatherer.IsTargetedByOther(candidate, g)
                          && !IsFull(candidate.kind));
            if (meat != null) return meat;
        }

        return ResourceNode.FindNearestReachable(
            g.transform.position, 40f, null,
            candidate => !Gatherer.IsTargetedByOther(candidate, g)
                      && !IsFull(candidate.kind));
    }

    int CountWorking()
    {
        int n = 0;
        foreach (var u in UnitActor.All)
        {
            if (u == null || !u.IsAlive || u.team != team) continue;
            var g = u.GetComponent<Gatherer>();
            if (g != null && g.IsWorking) n++;
        }
        return n;
    }

    Vector3 HomePosition
    {
        get
        {
            var grid = MapGrid.Instance;
            return grid != null
                ? grid.CellCenter(new Vector3Int(homeCell.x, homeCell.y, 0))
                : transform.position;
        }
    }

    // ------------------------------------------------------------------ army

    /// <summary>One shared worker budget for the whole production tick. Living workers
    /// and already-paid-for worker orders both occupy a slot, so several houses cannot
    /// each spend the same maxGatherers headroom.</summary>
    void Produce()
    {
        int workerBudget = Mathf.Max(0, maxGatherers - CountLivingWorkers() - CountQueuedWorkers());

        foreach (var pb in Object.FindObjectsByType<ProductionBuilding>(FindObjectsSortMode.None))
        {
            if (pb == null) continue;
            var b = pb.GetComponent<Building>();
            if (b == null || b.isGhost || b.team != team || !b.IsAlive || b.IsUnderConstruction) continue;
            if (pb.products == null || pb.products.Length == 0) continue;
            if (pb.Queue.Count >= pb.queueLimit) continue;

            // Only legal, affordable products become candidates; which of them wins is
            // decided below by how far its profession sits below its share of the army.
            candidates.Clear();
            for (int i = 0; i < pb.products.Length; i++)
            {
                if (pb.products[i].unitPrefab == null) continue;
                if (!CanAfford(pb.products[i].cost)) continue;
                if (workerBudget <= 0 && IsWorkerProduct(pb.products[i])) continue;
                if (!UnitLimits.CanProduce(team, pb.products[i].unitPrefab, out _)) continue;
                // Same order the real order path uses: a profession cap first, then
                // population. Without this a population-blocked cheap pick would be
                // selected, fail at TryQueue and idle the factory even though a
                // pricier lower-population product would fit.
                if (!Population.CanFit(team, pb.products[i].unitPrefab, out _)) continue;
                candidates.Add(i);
            }
            int pick = ChooseBest(pb, candidates);
            if (pick < 0) continue;

            // Selection above already proved affordability, so the spend cannot fail
            // here; no refund path is introduced for AI orders.
            bool workerProduct = IsWorkerProduct(pb.products[pick]);
            if (!pb.TryQueue(pick, payCost: false)) continue;
            if (!TrySpend(pb.products[pick].cost)) continue;
            if (workerProduct) workerBudget--;
        }
    }

    static readonly UnitRole[] AllRoles =
    {
        UnitRole.Worker, UnitRole.Warrior, UnitRole.Lancer,
        UnitRole.Archer, UnitRole.Monk, UnitRole.Other, UnitRole.OtherCombatant,
    };

    /// <summary>
    /// Which candidate to build next: the profession furthest below its share of the army.
    /// A role's target share is proportional to its weight, so with weights 3/2/3/1 the AI
    /// keeps roughly three warriors, two lancers, three archers and one monk at any size,
    /// instead of buying whatever happens to be cheapest — which used to leave the barracks
    /// producing warriors only, forever.
    ///
    /// The maximum is taken even when every deficit is negative, so a legal candidate always
    /// wins and a factory never idles. Ties break toward the cheaper product, which keeps a
    /// thin bank producing something rather than banking for the pricier line.
    /// </summary>
    public int ChooseBest(ProductionBuilding pb, List<int> candidates)
    {
        if (pb == null || pb.products == null || candidates == null || candidates.Count == 0)
            return -1;

        int side = 0;
        float totalWeight = 0f;
        for (int r = 0; r < AllRoles.Length; r++)
        {
            side += UnitLimits.Count(team, AllRoles[r]);
            totalWeight += WeightOf(AllRoles[r]);
        }

        int best = -1;
        float bestDeficit = float.MinValue;
        for (int k = 0; k < candidates.Count; k++)
        {
            int i = candidates[k];
            if (i < 0 || i >= pb.products.Length || pb.products[i].unitPrefab == null) continue;

            var role = UnitLimits.RoleOf(pb.products[i].unitPrefab);
            // Target is this role's SHARE of the army, not weight x size: without dividing by
            // the total weight a heavy role always out-scores a lighter one and the mix never
            // evens out (measured: 7 warriors to 1 lancer).
            float share = totalWeight > 0f ? WeightOf(role) / totalWeight : 0f;
            float deficit = share * (side + 1) - UnitLimits.Count(team, role);

            bool better = best < 0 || deficit > bestDeficit + 0.0001f;
            bool tie = best >= 0 && Mathf.Abs(deficit - bestDeficit) <= 0.0001f
                       && pb.products[i].cost.gold < pb.products[best].cost.gold;
            if (better || tie)
            {
                best = i;
                bestDeficit = deficit;
            }
        }
        return best;
    }

    /// <summary>Relative weight of a profession in this side's army. Unknown roles get a
    /// small weight so a future prefab still fields a few without dominating the mix.</summary>
    float WeightOf(UnitRole role)
    {
        switch (role)
        {
            case UnitRole.Worker: return workerWeight;
            case UnitRole.Warrior: return warriorWeight;
            case UnitRole.Lancer: return lancerWeight;
            case UnitRole.Archer: return archerWeight;
            case UnitRole.Monk: return monkWeight;
            default: return 0.5f;
        }
    }

    /// <summary>Living same-team units that count as workers. Over-cap survivors stay
    /// on the field; they only stop new worker production.</summary>
    int CountLivingWorkers()
    {
        int n = 0;
        foreach (var u in UnitActor.All)
        {
            if (u == null || !u.IsAlive || u.team != team) continue;
            if (u.GetComponent<Gatherer>() != null) n++;
        }
        return n;
    }

    /// <summary>Worker orders already queued in completed same-team factories.
    /// Military orders are deliberately excluded from the worker budget.</summary>
    int CountQueuedWorkers()
    {
        int n = 0;
        foreach (var pb in Object.FindObjectsByType<ProductionBuilding>(FindObjectsSortMode.None))
        {
            if (pb == null) continue;
            var b = pb.GetComponent<Building>();
            if (b == null || b.isGhost || b.team != team || !b.IsAlive || b.IsUnderConstruction) continue;
            if (pb.products == null) continue;

            foreach (int index in pb.Queue)
            {
                if (index < 0 || index >= pb.products.Length) continue;
                if (IsWorkerProduct(pb.products[index])) n++;
            }
        }
        return n;
    }

    static bool IsWorkerProduct(ProductionBuilding.Product product)
    {
        return product.unitPrefab != null
            && product.unitPrefab.GetComponent<Gatherer>() != null;
    }

    void LaunchAttack()
    {
        var pick = PickTarget();
        if (!pick.HasValue) return;
        Vector3 target = pick.Value;

        var attackers = UnitActor.All
            .Where(u => u != null && u.IsAlive && u.team == team && u.isCombatant)
            .OrderBy(u => (u.transform.position - target).sqrMagnitude)
            .Take(maxAttackers)
            .ToArray();
        if (attackers.Length == 0) return;

        wave++;
        var cell = MapGrid.Instance.WorldToCell(target);
        foreach (var u in attackers) SendTo(u, cell);
        Debug.Log($"[AI] 第 {wave} 波进攻，{attackers.Length} 个单位，目标 {target}");
    }

    /// <summary>Nearest enemy building, or the nearest enemy unit if none is left.</summary>
    Vector3? PickTarget()
    {
        Vector3 mine = HomePosition;
        Building best = null;
        float bestD = float.MaxValue;
        foreach (var b in Building.All)
        {
            if (b == null || !b.blocksMovement || !b.IsAlive) continue;
            if (b.team == team || b.team == Team.Neutral) continue;
            float d = (b.transform.position - mine).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        if (best != null) return best.transform.position;

        UnitActor u = null; float ud = float.MaxValue;
        foreach (var x in UnitActor.All)
        {
            if (x == null || !x.IsAlive || x.team == team || x.team == Team.Neutral) continue;
            float d = (x.transform.position - mine).sqrMagnitude;
            if (d < ud) { ud = d; u = x; }
        }
        return u != null ? u.transform.position : (Vector3?)null;
    }

    static void SendTo(UnitActor u, Vector3Int goalCell)
    {
        var grid = MapGrid.Instance;
        if (grid == null) return;
        var mover = u.GetComponent<UnitMover>();
        if (mover == null) return;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
        if (!start.HasValue) return;
        int region = grid.Region(start.Value);
        var dest = grid.NearestWalkableTo(start.Value, goalCell, 5, region);
        if (!dest.HasValue) return;
        var path = grid.FindPath(start.Value, dest.Value);
        if (path != null)
        {
            // A wave is an attack-move order, not a one-off walk. CombatAI then owns
            // resuming the destination after combat and re-pathing when a new building
            // invalidates the old route.
            var ai = u.GetComponent<CombatAI>();
            if (ai != null)
            {
                ai.OrderAttackMove(dest.Value);
                mover.SetPath(path, ai.CompleteOrder);
            }
            else mover.SetPath(path);
        }
    }
}
