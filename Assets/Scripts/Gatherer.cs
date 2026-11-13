using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Worker loop: walk to a resource node → harvest → haul it to the nearest drop-off
/// building → repeat until the node runs dry.
///
/// Attach to any unit. Right-clicking a <see cref="ResourceNode"/> with units selected
/// assigns them (see <see cref="SelectionManager"/>).
/// </summary>
[RequireComponent(typeof(UnitMover))]
public class Gatherer : MonoBehaviour
{
    /// <summary>Live gatherers, used so automatic assignment can avoid stacking every
    /// worker on the same nearest node and the same approach tile.</summary>
    public static readonly List<Gatherer> All = new List<Gatherer>();
    [Tooltip("一趟最多带多少资源。")]
    public int carryCapacity = 20;

    [Tooltip("找不到卸货点时，每隔这么久重试一次。")]
    public float dropoffRetrySeconds = 1f;
    [Tooltip("资源点不可达时，每隔这么久才重新请求一次路径。")]
    public float unreachableRetrySeconds = 0.75f;

    [Header("开局上岗")]
    [Tooltip("出场时自己去找最近的资源点，不用玩家右键点。只对玩家阵营生效——" +
             "AI 的工人由 AIController 统一派活，否则会绕过它的 maxGatherers 预算。")]
    public bool autoStart = true;
    [Tooltip("自动上岗时最多找多远（世界单位）。")]
    public float autoStartRange = 30f;
    [Tooltip("闲下来多久自己再找活干（秒）。0 = 只在出场时找一次。\n" +
             "资源点采空后工人会原地站着不动，关掉的话需要玩家手动重新指派。")]
    public float retaskSeconds = 2f;

    [Tooltip("只有够得着节点才挥得下家伙（世界单位）。")]
    public float harvestReach = 2.0f;

    [Header("受命维修")]
    [Tooltip("站在受损友方建筑旁时每秒恢复的生命值。免费、慢——应急的付费维修仍然更快。")]
    public float repairRate = 20f;
    [Tooltip("必须站到占地旁边才修得动（到最近占地格中心的距离，世界单位）。")]
    public float repairReach = 2.0f;

    [Header("受袭撤退")]
    [Tooltip("敌人进入这个半径时，基地里的工人不会恢复工作（世界单位）。")]
    public float fleeSafeRadius = 6f;
    [Tooltip("威胁离开后，在基地等这么久才重新开工（秒）。")]
    public float fleeResumeSeconds = 3f;
    [Tooltip("没有可用基地时，每隔这么久再找一次（秒）。")]
    public float baseRetrySeconds = 1f;

    enum Phase { Idle, ToNode, Harvesting, ToDropoff, ToClear, Clearing, Fleeing, AtBase, ToRepair, Repairing }

    UnitMover mover;
    UnitActor actor;
    float retaskTimer;
    Phase phase = Phase.Idle;
    ResourceNode node;
    ClearableBush clearTarget;
    Building dropoff;
    float timer;
    float clearTimer;
    float dropoffRetry;
    float nodeRetry;
    bool manualIdle;
    bool nodeManual;
    int carrying;
    ResourceKind carryingKind;
    bool retreating;
    bool wasManualIdle;
    Building retreatBase;
    Building repairTarget;
    Building resumeRepair;
    float baseRetryTimer;
    float safeTimer;
    bool subscribed;

    public bool IsWorking => phase != Phase.Idle;
    public bool IsHarvesting => phase == Phase.Harvesting;
    public bool IsManuallyIdle => manualIdle;
    /// <summary>True from the hit until the worker has waited out
    /// <see cref="fleeResumeSeconds"/> at a base with no threat nearby.</summary>
    public bool IsRetreating => retreating;
    /// <summary>Base the worker is fleeing to or waiting at, if any.</summary>
    public Building RetreatBase => retreatBase;
    /// <summary>Total path requests made through <see cref="GoTo"/> — resource nodes,
    /// drop-offs, and clear targets alike. The unreachable-retry regression asserts
    /// this does not grow during the node retry interval.</summary>
    public int PathAttempts { get; private set; }
    public ResourceNode TargetNode => node;
    /// <summary>The damaged friendly building this worker was ordered to repair, or
    /// null. Like a gathering target this is a transient task state and is never saved.</summary>
    public Building RepairTarget => repairTarget;
    /// <summary>True while the worker is standing beside its target and applying repair.</summary>
    public bool IsRepairing => phase == Phase.Repairing;
    public int Carrying => carrying;
    public ResourceKind CarryingKind => carryingKind;

    /// <summary>How much one trip actually yields, including the researched Gathering
    /// bonus. Applied to the node's per-trip amount (and to the worker's capacity, so a
    /// full 20/20 trip really becomes 22) without editing the node or the prefab.</summary>
    public int PerTripAmount(ResourceNode target) =>
        (target != null ? target.perTrip : 0) + Technology.CarryBonus(UnitTeam);

    /// <summary>Carry capacity including the researched Gathering bonus.</summary>
    public int EffectiveCarryCapacity => carryCapacity + Technology.CarryBonus(UnitTeam);

    [System.Serializable]
    public class SaveState
    {
        public int carrying;
        public ResourceKind carryingKind;
        public bool manualIdle;
    }

    public SaveState CaptureSaveState()
    {
        return new SaveState
        {
            carrying = carrying,
            carryingKind = carryingKind,
            manualIdle = manualIdle,
        };
    }

    /// <summary>Restores cargo and the player's manual-idle choice. The current node,
    /// drop-off target and path are transient and will be chosen by the existing rules.</summary>
    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        StopWork();
        carrying = Mathf.Clamp(state.carrying, 0, EffectiveCarryCapacity);
        carryingKind = state.carryingKind;
        manualIdle = state.manualIdle;

        // A loaded worker that was not parked by the player must finish the
        // trip instead of standing still with the cargo forever.
        if (manualIdle) return;
        if (carrying > 0)
        {
            dropoff = null;
            dropoffRetry = 0f;
            phase = Phase.ToDropoff;
        }
        else TryAutoAssign();
    }
    public string Status
    {
        get
        {
            switch (phase)
            {
                case Phase.ToNode:      return "去采集";
                case Phase.Harvesting:  return "采集中";
                case Phase.ToDropoff:   return "运送中";
                case Phase.ToClear:     return "去清草";
                case Phase.Clearing:    return "清草中";
                case Phase.ToRepair:    return "去维修";
                case Phase.Repairing:   return "维修中";
                case Phase.Fleeing:     return "撤退中";
                case Phase.AtBase:      return "基地待命";
                default:                return carrying > 0 ? "待命(持" + carrying + ")" : "空闲";
            }
        }
    }

    void Awake()
    {
        mover = GetComponent<UnitMover>();
        actor = GetComponent<UnitActor>();
        ApplyNoncombat();
    }

    void OnEnable()
    {
        actor ??= GetComponent<UnitActor>();
        ApplyNoncombat();
        SubscribeDamage();
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        UnsubscribeDamage();
    }

    /// <summary>
    /// Workers never fight. Enforcing the values here as well as in the prefabs keeps
    /// the rule true for scene instances and for anything that assembles a worker at
    /// runtime; a stale prefab cannot turn a worker back into a soldier.
    /// </summary>
    void ApplyNoncombat()
    {
        if (actor == null) return;
        actor.isCombatant = false;
        actor.attackDamage = 0;
        actor.projectile = null;
    }

    void SubscribeDamage()
    {
        if (subscribed || actor == null) return;
        actor.Damaged += OnDamaged;
        subscribed = true;
    }

    void UnsubscribeDamage()
    {
        if (!subscribed || actor == null) return;
        actor.Damaged -= OnDamaged;
        subscribed = false;
    }

    /// <summary>The regression fixtures (and anything else that assembles a worker by
    /// hand) may add the <see cref="UnitActor"/> after this component, which leaves
    /// Awake with no actor to configure. Pick it up on the first update instead.</summary>
    void EnsureActor()
    {
        if (actor != null) return;
        actor = GetComponent<UnitActor>();
        if (actor == null) return;
        ApplyNoncombat();
        SubscribeDamage();
    }

    /// <summary>
    /// Walks a freshly produced worker straight to the nearest deposit.
    ///
    /// Without this the opening reads as broken: four workers stand still on a map full
    /// of trees and gold, the resource bar never moves, and the player has no way of
    /// knowing that a right-click on a tree is what starts the economy.
    /// </summary>
    void Start()
    {
        TryAutoAssign();
    }

    /// <summary>
    /// Looks for work if this worker is idle: on spawn, and again whenever a deposit it
    /// was working runs dry.
    ///
    /// The first half stops the opening from reading as broken; the second half stops it
    /// from reading as broken ten minutes later, when the mine is empty and four workers
    /// are standing next to a forest doing nothing.
    /// </summary>
    void TryAutoAssign()
    {
        if (!autoStart || manualIdle) return;
        // Automatic work must never pull a hurt worker out of its retreat.
        if (retreating || phase != Phase.Idle || carrying > 0) return;
        // An idle worker with a live node may simply have finished a delivery or
        // completed a clear. Reassigning is harmless and lets regrown nodes resume.

        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        if (actor != null && actor.team != player) return;    // AI workers are AIController's call

        // Distance alone can select a deposit across water or on another plateau.
        // The shared helper requires an approachable cell in this worker's own
        // connected region; AIController uses the same rule for its dispatch.
        // A full kind is skipped entirely: harvesting it would only discard the load.
        if (MapGrid.Instance == null) return;
        var team = actor != null ? actor.team : Team.Neutral;
        var best = ResourceNode.FindNearestReachable(
            transform.position, autoStartRange, null,
            candidate => !IsTargetedByOther(candidate, this)
                      && !IsKindFull(team, candidate.kind));
        if (best != null) AssignAuto(best);
    }

    Team UnitTeam => actor != null ? actor.team : Team.Neutral;

    /// <summary>True when the bank that owns <paramref name="team"/> is already at the
    /// cap for <paramref name="kind"/> and further deliveries would be discarded.
    /// Automatic dispatch uses this to avoid sending workers to a full kind; the
    /// manual order path deliberately ignores it.</summary>
    public static bool IsKindFull(Team team, ResourceKind kind)
    {
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        if (team == player)
            return Economy.Instance != null &&
                   ResourceLimits.IsFull(Economy.Instance.Get(kind), kind);
        var ai = AIController.For(team);
        return ai != null && ResourceLimits.IsFull(ai.Get(kind), kind);
    }

    /// <summary>True when another live gatherer already has this node as its target.
    /// Automatic assignment uses this to spread starting workers across deposits;
    /// explicit player orders are never blocked.</summary>
    public static bool IsTargetedByOther(ResourceNode target, Gatherer self)
    {
        if (target == null) return false;
        for (int i = 0; i < All.Count; i++)
        {
            var other = All[i];
            if (other != null && other != self && other.node == target) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ API

    /// <summary>Send this worker to a node on an explicit player order. Pass null to
    /// stop working. A manual order may point at a full kind: deliveries are clamped
    /// and the job is retained instead of being auto-diverted.</summary>
    public void Assign(ResourceNode target) => AssignInternal(target, manual: true);

    /// <summary>Send this worker to a node as an automatic assignment. When this
    /// job's kind fills up, a delivery clears it so the worker can move on.</summary>
    public void AssignAuto(ResourceNode target) => AssignInternal(target, manual: false);

    void AssignInternal(ResourceNode target, bool manual)
    {
        CancelRetreat();
        manualIdle = false;
        node = target;
        nodeManual = target != null && manual;
        clearTarget = null;
        repairTarget = null;
        clearTimer = 0f;
        dropoff = null;
        timer = 0f;
        dropoffRetry = 0f;
        nodeRetry = 0f;
        mover.Stop();

        // Every explicit assignment delivers an existing load first, even when the
        // new node produces the same resource kind. The retained node resumes after.
        phase = carrying > 0 ? Phase.ToDropoff : node != null ? Phase.ToNode : Phase.Idle;
    }

    /// <summary>Send this worker to clear a decorative bush. Gives no resources.
    /// If the worker is carrying anything, delivery happens first.</summary>
    public void AssignClear(ClearableBush target)
    {
        CancelRetreat();
        manualIdle = false;
        node = null;
        nodeManual = false;
        clearTarget = target;
        repairTarget = null;
        clearTimer = 0f;
        dropoff = null;
        timer = 0f;
        dropoffRetry = 0f;
        nodeRetry = 0f;
        mover.Stop();
        phase = carrying > 0 ? Phase.ToDropoff : target != null ? Phase.ToClear : Phase.Idle;
    }

    /// <summary>Explicit player order: repair a damaged friendly building for free.
    /// A worker that is still carrying a load delivers it first and then walks back to
    /// the building. Right-clicking an ineligible building never reaches this method —
    /// <see cref="SelectionManager"/> only routes damaged, finished, friendly ones.</summary>
    public void AssignRepair(Building target) => AssignRepairInternal(target);

    void AssignRepairInternal(Building target)
    {
        CancelRetreat();
        manualIdle = false;
        node = null;
        nodeManual = false;
        clearTarget = null;
        clearTimer = 0f;
        dropoff = null;
        timer = 0f;
        dropoffRetry = 0f;
        nodeRetry = 0f;
        repairTarget = target;
        mover.Stop();
        phase = carrying > 0 ? Phase.ToDropoff : target != null ? Phase.ToRepair : Phase.Idle;
    }

    /// <summary>Ordinary move orders leave the worker idle and keep any cargo in hand.</summary>
    public void StopWork()
    {
        CancelRetreat();
        node = null;
        nodeManual = false;
        clearTarget = null;
        repairTarget = null;
        clearTimer = 0f;
        dropoff = null;
        timer = 0f;
        dropoffRetry = 0f;
        nodeRetry = 0f;
        mover.Stop();
        phase = Phase.Idle;
        manualIdle = true;
    }

    // --------------------------------------------------------------- retreat

    /// <summary>
    /// A nonlethal hit sends the worker home. The load stays in hand until it reaches
    /// a completed friendly drop-off, and the previous manual-idle choice is restored
    /// once the area has been quiet for <see cref="fleeResumeSeconds"/>.
    /// </summary>
    void OnDamaged()
    {
        if (actor == null || !actor.IsAlive) return;

        wasManualIdle = manualIdle;
        retreating = true;
        phase = Phase.Fleeing;
        // Remember a repair job across the retreat so the worker goes back to the same
        // building once it is safe. A second hit while fleeing must not wipe it.
        if (repairTarget != null) resumeRepair = repairTarget;
        repairTarget = null;
        node = null;
        nodeManual = false;
        clearTarget = null;
        dropoff = null;
        retreatBase = null;
        baseRetryTimer = 0f;
        safeTimer = 0f;
        timer = 0f;
        clearTimer = 0f;
        dropoffRetry = 0f;
        nodeRetry = 0f;
        mover.Stop();
    }

    void CancelRetreat()
    {
        retreating = false;
        retreatBase = null;
        resumeRepair = null;
        baseRetryTimer = 0f;
        safeTimer = 0f;
    }

    /// <summary>
    /// A retreat base that was destroyed or is still being built cannot accept a
    /// delivery. Unity's fake-null makes <c>retreatBase != null</c> false for a
    /// destroyed object, so the reference is checked with <see cref="ReferenceEquals"/>
    /// before <see cref="Combatants.IsGone"/>.
    /// </summary>
    bool RetreatBaseUnusable()
    {
        if (ReferenceEquals(retreatBase, null)) return false;   // nothing selected yet
        return Combatants.IsGone(retreatBase) || retreatBase.IsUnderConstruction;
    }

    void StepFleeing()
    {
        // Validate before the moving early-out: a base that dies or starts being built
        // while the worker is walking must not be reached, and its rubble must not
        // accept the load.
        if (RetreatBaseUnusable())
        {
            retreatBase = null;
            mover.Stop();
            baseRetryTimer = Mathf.Max(0.05f, baseRetrySeconds);
            return;
        }

        if (mover.Moving) return;

        if (retreatBase == null)
        {
            baseRetryTimer -= Time.deltaTime;
            if (baseRetryTimer > 0f) return;
            baseRetryTimer = Mathf.Max(0.05f, baseRetrySeconds);
            retreatBase = Building.FindNearestDropoff(transform.position, UnitTeam);
            if (retreatBase == null) return;      // no base yet — wait, do not go back to work
        }

        // The base may be visible but unreachable (across water, freshly walled in).
        // Drop it and let the retry interval find another one instead of asking for a
        // path every frame.
        if (!GoTo(retreatBase.transform.position, OnReachedRetreatBase))
            retreatBase = null;
    }

    void OnReachedRetreatBase()
    {
        if (!retreating) return;

        // The base may have been destroyed during the walk. Never credit cargo to
        // rubble: drop the target and keep retreating toward a surviving one.
        if (ReferenceEquals(retreatBase, null) ||
            Combatants.IsGone(retreatBase) || retreatBase.IsUnderConstruction)
        {
            retreatBase = null;
            phase = Phase.Fleeing;
            baseRetryTimer = Mathf.Max(0.05f, baseRetrySeconds);
            return;
        }

        if (carrying > 0)
        {
            int amount = carrying;
            var kind = carryingKind;
            Vector3 where = retreatBase.transform.position;
            carrying = 0;
            int credited = Credit(UnitTeam, kind, amount);
            if (credited > 0) ShowDelivery(where, kind, credited);
        }

        dropoff = null;
        phase = Phase.AtBase;
        safeTimer = Mathf.Max(0f, fleeResumeSeconds);
    }

    void StepAtBase()
    {
        // Losing the base while waiting there sends the worker looking for another
        // completed one instead of standing on the wreck.
        if (ReferenceEquals(retreatBase, null) ||
            Combatants.IsGone(retreatBase) || retreatBase.IsUnderConstruction)
        {
            retreatBase = null;
            phase = Phase.Fleeing;
            baseRetryTimer = Mathf.Max(0.05f, baseRetrySeconds);
            safeTimer = 0f;
            return;
        }

        // Anything that can still fight near the base keeps the worker here.
        if (Combatants.HasThreat(UnitTeam, transform.position, fleeSafeRadius))
        {
            safeTimer = Mathf.Max(0f, fleeResumeSeconds);
            return;
        }

        safeTimer -= Time.deltaTime;
        if (safeTimer > 0f) return;

        retreating = false;
        retreatBase = null;
        manualIdle = wasManualIdle;

        // A worker that was hurt mid-repair goes back to the same building once the
        // area is safe. The job is dropped if the target died, finished or otherwise
        // stopped being repairable while the worker was away.
        var resume = resumeRepair;
        resumeRepair = null;
        if (!manualIdle && CanRepairTarget(resume)) { AssignRepairInternal(resume); return; }

        phase = Phase.Idle;
        retaskTimer = 0f;
    }

    // --------------------------------------------------------------- runtime

    void Update()
    {
        EnsureActor();

        switch (phase)
        {
            case Phase.ToNode:     StepToNode();     break;
            case Phase.Harvesting: StepHarvest();    break;
            case Phase.ToDropoff:  StepToDropoff();  break;
            case Phase.ToClear:    StepToClear();    break;
            case Phase.Clearing:   StepClearing();   break;
            case Phase.ToRepair:   StepToRepair();   break;
            case Phase.Repairing:  StepRepair();     break;
            case Phase.Fleeing:    StepFleeing();    break;
            case Phase.AtBase:     StepAtBase();     break;
        }

        // Standing around with nothing to do? Go find something.
        if (autoStart && retaskSeconds > 0f && phase == Phase.Idle && carrying <= 0)
        {
            retaskTimer -= Time.deltaTime;
            if (retaskTimer <= 0f) { retaskTimer = Mathf.Max(0.5f, retaskSeconds); TryAutoAssign(); }
        }
        else
        {
            retaskTimer = 0f;
        }
    }

    void StepToNode()
    {
        if (node == null || node.Depleted) { FinishTrip(false); return; }
        if (mover.Moving) return;
        if (nodeRetry > 0f)
        {
            nodeRetry -= Time.deltaTime;
            if (nodeRetry > 0f) return;
        }
        // Keep both the node and the ToNode phase on failure. Connectivity may
        // return later; the bounded timer prevents a path request every frame.
        if (!GoTo(node.transform.position, OnReachedNode))
            nodeRetry = Mathf.Max(0.05f, unreachableRetrySeconds);
    }

    void OnReachedNode()
    {
        nodeRetry = 0f;
        phase = Phase.Harvesting;
        timer = 0f;
    }

    void StepHarvest()
    {
        if (node == null || node.Depleted) { FinishTrip(true); return; }
        // Safety net: only swing when actually beside the deposit. The pathing used to
        // let a worker "arrive" at its own cell and harvest from several tiles away.
        if (Vector2.Distance(transform.position, node.transform.position) > harvestReach)
        { phase = Phase.ToNode; return; }
        timer += Time.deltaTime;
        if (timer < Mathf.Max(0.01f, node.harvestSeconds)) return;
        timer = 0f;

        int want = Mathf.Min(PerTripAmount(node), EffectiveCarryCapacity - carrying);
        int got = node.Harvest(want);
        if (got > 0) { carrying += got; carryingKind = node.kind; }

        if (carrying >= EffectiveCarryCapacity || node.Depleted || got == 0) FinishTrip(true);
    }

    /// <summary>Harvest done (or the node died): head for a drop-off with whatever
    /// is being carried, otherwise go back for another scoop.</summary>
    void FinishTrip(bool harvested)
    {
        if (carrying <= 0)
        {
            if (clearTarget != null) { phase = Phase.ToClear; return; }
            if (repairTarget != null) { phase = Phase.ToRepair; return; }
            phase = (node != null && !node.Depleted) ? Phase.ToNode : Phase.Idle;
            return;
        }
        dropoff = null;
        dropoffRetry = 0f;
        phase = Phase.ToDropoff;
    }

    void StepToDropoff()
    {
        if (carrying <= 0)
        {
            if (clearTarget != null) { phase = Phase.ToClear; return; }
            if (repairTarget != null) { phase = Phase.ToRepair; return; }
            phase = node != null && !node.Depleted ? Phase.ToNode : Phase.Idle;
            return;
        }

        // A demolished drop-off is still a non-null C# reference. Cancel its path as
        // well as its target: otherwise the stale arrival callback banks the load at
        // the old site even when no living drop-off remains.
        if (Combatants.IsGone(dropoff))
        {
            if (!ReferenceEquals(dropoff, null)) mover.Stop();
            dropoff = null;
        }

        if (dropoff == null)
        {
            dropoffRetry -= Time.deltaTime;
            if (dropoffRetry > 0f) return;
            dropoffRetry = dropoffRetrySeconds;
            dropoff = Building.FindNearestDropoff(transform.position, UnitTeam);
            if (dropoff == null) return;             // no base yet — keep retrying
        }

        if (mover.Moving) return;
        if (!GoTo(dropoff.transform.position, OnReachedDropoff)) dropoff = null;
    }

    void OnReachedDropoff()
    {
        // The building may have died while the worker was walking. Match the retreat
        // path: never consume cargo at rubble; wait for another completed drop-off.
        if (ReferenceEquals(dropoff, null) || Combatants.IsGone(dropoff) ||
            dropoff.IsUnderConstruction || !dropoff.isDropoff)
        {
            dropoff = null;
            dropoffRetry = 0f;
            phase = Phase.ToDropoff;
            return;
        }

        if (carrying > 0)
        {
            int amount = carrying;
            var kind = carryingKind;
            Vector3 where = dropoff != null ? dropoff.transform.position : transform.position;
            carrying = 0;
            int credited = Credit(UnitTeam, kind, amount);
            if (credited > 0) ShowDelivery(where, kind, credited);
        }
        dropoff = null;
        if (clearTarget != null) { phase = Phase.ToClear; return; }
        if (repairTarget != null) { phase = Phase.ToRepair; return; }

        // The kind may have filled while this load was in transit. An automatic job
        // would only harvest more overflow, so clear it and look for an unfull kind.
        // A manual order is the player's call and keeps its target.
        if (node != null && IsKindFull(UnitTeam, node.kind))
        {
            if (nodeManual) { phase = Phase.ToNode; return; }
            node = null;
            nodeManual = false;
            phase = Phase.Idle;
            TryAutoAssign();          // player workers switch at once; AI dispatch polls
            return;
        }

        phase = node != null && !node.Depleted ? Phase.ToNode : Phase.Idle;
    }

    /// <summary>Banks the load with whoever owns this worker. The AI keeps its own
    /// stockpile, so crediting everything to <see cref="Economy"/> would hand the player
    /// the enemy's income.
    ///
    /// Returns how much actually landed: storage is capped, so a full stockpile swallows
    /// part or all of a haul. The floating "+20" used to report the whole load regardless.</summary>
    static int Credit(Team team, ResourceKind kind, int amount)
    {
        var playerTeam = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        if (team == playerTeam || team == Team.Neutral)
            return CreditToEconomy(kind, amount);

        var ai = AIController.For(team);
        if (ai != null)
        {
            int before = ai.Stock(kind);
            ai.Add(kind, amount);
            return Mathf.Max(0, ai.Stock(kind) - before);
        }
        return CreditToEconomy(kind, amount);
    }

    static int CreditToEconomy(ResourceKind kind, int amount)
    {
        var eco = Economy.Instance;
        if (eco == null) return 0;
        int before = Stock(eco, kind);
        eco.Add(kind, amount);
        return Mathf.Max(0, Stock(eco, kind) - before);
    }

    static int Stock(Economy eco, ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Gold: return eco.gold;
            case ResourceKind.Wood: return eco.wood;
            default: return eco.meat;
        }
    }

    /// <summary>A "+20" over the drop-off and a small sparkle, so banking a load is
    /// something you can see happen rather than only read off the resource bar.</summary>
    static void ShowDelivery(Vector3 where, ResourceKind kind, int amount)
    {
        Color tint;
        switch (kind)
        {
            case ResourceKind.Gold: tint = new Color(1f, 0.83f, 0.29f); break;
            case ResourceKind.Wood: tint = new Color(0.82f, 0.66f, 0.40f); break;
            case ResourceKind.Meat: tint = new Color(1f, 0.55f, 0.48f); break;
            default:                tint = Color.white; break;
        }

        FloatingText.Show(where + new Vector3(0f, 1.6f, 0f), "+" + amount, tint);

        var bank = FxBank.Instance;
        if (bank != null && kind == ResourceKind.Gold) bank.GoldSparkle(where);
    }

    void StepToClear()
    {
        if (clearTarget == null) { phase = Phase.Idle; return; }
        if (mover.Moving) return;
        if (!GoTo(clearTarget.transform.position, OnReachedClear))
        {
            clearTarget = null;
            phase = Phase.Idle;
        }
    }

    void OnReachedClear()
    {
        if (clearTarget == null) { phase = Phase.Idle; return; }
        clearTimer = Mathf.Max(0.01f, clearTarget.clearSeconds);
        phase = Phase.Clearing;
    }

    void StepClearing()
    {
        if (clearTarget == null) { phase = Phase.Idle; return; }
        if (Vector2.Distance(transform.position, clearTarget.transform.position) > harvestReach)
        {
            phase = Phase.ToClear;
            return;
        }
        clearTimer -= Time.deltaTime;
        if (clearTimer > 0f) return;

        var target = clearTarget;
        clearTarget = null;
        clearTimer = 0f;
        if (target != null) Destroy(target.Root);
        phase = Phase.Idle;
    }

    // ---------------------------------------------------------------- repair

    /// <summary>A building may be repaired by this worker while it is alive, finished,
    /// owned by the worker's side and still missing health. Rubble, half-built
    /// structures, neutral scenery and enemy buildings are all invalid.</summary>
    bool CanRepairTarget(Building target)
    {
        if (ReferenceEquals(target, null) || Combatants.IsGone(target)) return false;
        if (target.team != UnitTeam) return false;
        return target.CanRepair;
    }

    void StepToRepair()
    {
        if (!CanRepairTarget(repairTarget)) { FinishRepair(); return; }
        if (mover.Moving) return;
        if (nodeRetry > 0f)
        {
            nodeRetry -= Time.deltaTime;
            if (nodeRetry > 0f) return;
        }
        // Keep the target through an unreachable patch, same as a node: connectivity
        // may return, and the bounded timer stops a path request every frame.
        if (!GoToBuilding(repairTarget, OnReachedRepair))
            nodeRetry = Mathf.Max(0.05f, unreachableRetrySeconds);
    }

    void OnReachedRepair()
    {
        nodeRetry = 0f;
        phase = Phase.Repairing;
    }

    void StepRepair()
    {
        if (!CanRepairTarget(repairTarget)) { FinishRepair(); return; }

        // Safety net, exactly like harvesting: a path that ended on a distant tile must
        // not let the worker repair from across the map. Walk back beside the footprint.
        if (!AdjacentTo(repairTarget)) { nodeRetry = 0f; phase = Phase.ToRepair; return; }

        repairTarget.RepairByWorker(repairRate * Time.deltaTime);
        if (!CanRepairTarget(repairTarget)) FinishRepair();
    }

    /// <summary>Repair done, or the target stopped being valid (full health, destroyed,
    /// half-built): drop the job and stand by. Automatic retasking may then find
    /// economy work again, exactly as before the order.</summary>
    void FinishRepair()
    {
        repairTarget = null;
        nodeRetry = 0f;
        phase = Phase.Idle;
    }

    /// <summary>True when the worker stands within <see cref="repairReach"/> of any
    /// cell of the footprint. Measured against the footprint, not the transform: the
    /// transform sits on the base line, which is off-centre for wider buildings.</summary>
    bool AdjacentTo(Building building)
    {
        var grid = MapGrid.Instance;
        if (grid == null) return false;
        var cells = building.Cells;
        if (cells.Count == 0)
            return Vector2.Distance(transform.position, building.transform.position) <= repairReach;
        for (int i = 0; i < cells.Count; i++)
        {
            if (Vector2.Distance(transform.position, grid.CellCenter(cells[i])) <= repairReach)
                return true;
        }
        return false;
    }

    /// <summary>Path to a walkable tile beside the building's footprint. Mirrors the
    /// tower-garrison approach: the footprint itself is blocked, so every occupied cell
    /// is tried, each destination is capped at an action radius (a wide building must
    /// not make the worker "arrive" on its own distant tile), and the shortest path wins.
    /// Returns false when no adjacent tile is reachable.</summary>
    bool GoToBuilding(Building building, System.Action onArrive)
    {
        PathAttempts++;
        var grid = MapGrid.Instance;
        if (grid == null || ReferenceEquals(building, null)) return false;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
        if (!start.HasValue) return false;

        int region = grid.Region(start.Value);
        List<Vector3Int> bestPath = null;
        for (int i = 0; i < building.Cells.Count; i++)
        {
            var dest = grid.NearestWalkableWithin(start.Value, building.Cells[i], 4, region, 1.6f);
            if (!dest.HasValue) continue;
            var path = grid.FindPath(start.Value, dest.Value);
            if (path != null && (bestPath == null || path.Count < bestPath.Count)) bestPath = path;
        }
        if (bestPath == null) return false;

        mover.SetPath(bestPath, onArrive);
        return true;
    }

    /// <summary>Path to a walkable tile next to the target (the target itself is
    /// usually blocked by its own footprint). Returns false when unreachable.</summary>
    bool GoTo(Vector3 worldPos, System.Action onArrive)
    {
        PathAttempts++;
        var grid = MapGrid.Instance;
        if (grid == null) return false;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
        if (!start.HasValue) return false;

        int region = grid.Region(start.Value);
        // Nearest-walkable-to-the-unit used to hand back the unit's own cell whenever it
        // was already within radius of the node — so the worker "arrived" without moving
        // and harvested from three tiles away. The destination has to be adjacent.
        var dest = grid.NearestWalkableWithin(start.Value, grid.WorldToCell(worldPos), 4, region, 1.6f);
        if (!dest.HasValue) return false;

        var path = grid.FindPath(start.Value, dest.Value);
        if (path == null) return false;

        mover.SetPath(path, onArrive);
        return true;
    }
}
