using System.Collections;
using UnityEngine;

/// <summary>
/// Makes a fighter fight on its own: pick the nearest enemy (unit first, then building),
/// walk into range, and hit it. Workers keep working unless something comes close
/// enough to interrupt them.
///
/// A target ordered with <see cref="OrderAttack"/> is kept until it dies, ignoring aggro
/// range — that is what makes right-clicking an enemy an attack order.
/// </summary>
[RequireComponent(typeof(UnitActor), typeof(UnitMover))]
public class CombatAI : MonoBehaviour
{
    [Tooltip("多久重新找一次目标。")]
    public float retargetInterval = 0.5f;
    [Tooltip("自动目标追击失败后，在多久内优先尝试其他目标；窗口内连通区变化会提前重试。")]
    public float autoTargetRetrySeconds = 0.5f;
    [Tooltip("追击时多久重新算一次路（目标换格才算）。")]
    public float repathInterval = 0.4f;
    [Tooltip("工人正在采集时，敌人近到这个距离才会放下手里的活还击。")]
    public float defendRange = 3f;

    UnitActor unit;
    UnitMover mover;
    Gatherer gatherer;
    SpriteRenderer sr;
    Animator anim;

    enum OrderKind { Auto, Move, AttackMove, Garrison, Patrol }

    IDamageable target;
    OrderKind orderKind;
    Vector3Int destinationCell;
    /// <summary>The other end of a patrol: the cell the unit walks back to when it reaches
    /// <see cref="destinationCell"/>.</summary>
    Vector3Int patrolAnchor;
    bool forced;                    // set by an explicit attack order
    bool holdPosition;              // stay put and only engage targets already in weapon range
    float cooldown;
    float retargetTimer;
    float repathTimer;
    Vector3Int lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    IDamageable unreachableTarget;
    Vector3Int unreachableGoal;
    int unreachableVersion;
    float unreachableTimer;
    bool hasAttackTrigger;
    bool hasDirParam;
    bool hasHoldingParam;

    static readonly int DirHash = Animator.StringToHash("Dir");
    static readonly int HoldingHash = Animator.StringToHash("Holding");

    public IDamageable Target => target;
    public bool IsHolding => holdPosition;

    /// <summary>True while the unit is walking a patrol route.</summary>
    public bool IsPatrolling => orderKind == OrderKind.Patrol;

    /// <summary>The endpoint a patrolling unit is walking toward. Exposed for tests and for a
    /// future HUD readout; meaningless when <see cref="IsPatrolling"/> is false.</summary>
    public Vector3Int PatrolTarget => destinationCell;

    /// <summary>Time.time of the last swing. Workers have no Attack state in their
    /// controller — they swing the tool they are carrying — so WorkerLook reads this to
    /// play a chop when the worker fights back.</summary>
    public float LastAttackTime { get; private set; } = -99f;

    void Awake()
    {
        unit = GetComponent<UnitActor>();
        mover = GetComponent<UnitMover>();
        gatherer = GetComponent<Gatherer>();
        sr = GetComponentInChildren<SpriteRenderer>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var p in anim.parameters)
            {
                if (p.name == "Attack") hasAttackTrigger = true;
                if (p.name == "Dir") hasDirParam = true;
                if (p.name == "Holding") hasHoldingParam = true;
            }
    }

    /// <summary>Sector index for a directional attack set.
    /// 0 = right, 1 = up-right, 2 = up, 3 = down-right, 4 = down. The left-facing
    /// three share these clips and are mirrored by flipping the sprite.</summary>
    public static int DirIndex(Vector3 from, Vector3 to)
    {
        Vector2 d = (to - from);
        if (d.sqrMagnitude < 0.0001f) return 0;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        if (ang > 90f) ang = 180f - ang;          // mirror the left half onto the right
        else if (ang < -90f) ang = -180f - ang;
        if (ang > 67.5f) return 2;
        if (ang > 22.5f) return 1;
        if (ang > -22.5f) return 0;
        if (ang > -67.5f) return 3;
        return 4;
    }

    public void OrderMove(Vector3Int destination) { SetTemporaryOrder(OrderKind.Move, destination); }
    public void OrderAttackMove(Vector3Int destination) { SetTemporaryOrder(OrderKind.AttackMove, destination); }
    public void OrderGarrison() { SetTemporaryOrder(OrderKind.Garrison, default); }

    /// <summary>
    /// Walk back and forth between where the unit stands now and <paramref name="point"/>,
    /// fighting whatever it meets on the way. Workers keep their economic orders, exactly like
    /// <see cref="OrderHold"/>. Any other move or attack order cancels the route.
    /// </summary>
    public void OrderPatrol(Vector3Int point)
    {
        if (gatherer != null) return;
        var grid = MapGrid.Instance;
        Vector3Int here = point;
        if (grid != null)
        {
            var cell = grid.WorldToCell(transform.position);
            var start = grid.ResolveWalkableStart(cell);
            here = start ?? cell;
        }
        if (here == point) return;      // patrolling nowhere would ping-pong in place forever
        patrolAnchor = here;
        SetTemporaryOrder(OrderKind.Patrol, point);
        if (!ResumeDestination(ArrivePatrol)) CompleteOrder();
    }

    /// <summary>Reached one end of the patrol: turn around. A different order that took over
    /// meanwhile wins, so a stale arrival callback just finishes that order.</summary>
    void ArrivePatrol()
    {
        if (orderKind != OrderKind.Patrol)
        {
            CompleteOrder();
            return;
        }
        var arrived = destinationCell;
        destinationCell = patrolAnchor;
        patrolAnchor = arrived;
        lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        repathTimer = 0f;
        if (!ResumeDestination(ArrivePatrol)) CompleteOrder();
    }

    public void CompleteOrder()
    {
        orderKind = OrderKind.Auto;
        destinationCell = default;
        forced = false;
        target = null;
        holdPosition = false;
        lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        ClearUnreachable();
    }

    void SetTemporaryOrder(OrderKind kind, Vector3Int destination)
    {
        orderKind = kind;
        destinationCell = destination;
        holdPosition = false;
        forced = false;
        target = null;
        lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        repathTimer = 0f;
        retargetTimer = 0f;
        ClearUnreachable();
    }

    /// <summary>Right-click an enemy: chase and kill it, ignoring the usual aggro range.</summary>
    public void OrderAttack(IDamageable t)
    {
        // A temporary move/attack-move/garrison path may still be walking toward its old
        // destination with an arrival callback that calls CompleteOrder() or garrisons an
        // archer. An explicit attack now owns the unit, so cancel that path first; otherwise
        // the late callback would clear this forced target or garrison on arrival.
        if (orderKind == OrderKind.Move || orderKind == OrderKind.AttackMove || orderKind == OrderKind.Garrison)
            mover.Stop();

        orderKind = OrderKind.Auto;
        destinationCell = default;
        holdPosition = false;
        target = t;
        forced = t != null;
        lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        repathTimer = 0f;
        retargetTimer = retargetInterval;
        ClearUnreachable();
    }

    /// <summary>Hold the current ground: do not chase, but keep fighting enemies
    /// that are already inside weapon range. Workers keep their economic orders.</summary>
    public void OrderHold()
    {
        if (gatherer != null) return;
        orderKind = OrderKind.Auto;
        destinationCell = default;
        holdPosition = true;
        forced = false;
        target = null;
        lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        repathTimer = 0f;
        retargetTimer = 0f;
        ClearUnreachable();
        mover.Stop();
    }

    /// <summary>Drop any attack or temporary movement order.</summary>
    public void ClearOrder()
    {
        CompleteOrder();
    }

    void Update()
    {
        if (!unit.IsAlive || !unit.isCombatant) return;
        if (hasHoldingParam) anim.SetBool(HoldingHash, holdPosition);
        float dt = Time.deltaTime;
        if (unreachableTimer > 0f) unreachableTimer -= dt;

        // Exactly once per frame, unconditionally.
        //
        // This used to be decremented here AND again further down, and the second one
        // only ran when the unit had a target — so everything that was actually fighting
        // reloaded at double speed while everything else ran at the authored rate. The
        // inspector numbers described a different game from the one being played.
        if (cooldown > 0f) cooldown -= dt;

        if (orderKind == OrderKind.Move || orderKind == OrderKind.Garrison)
        {
            if (mover.Moving) return;
            CompleteOrder(); // Arrival may already have done this; an aborted path must not freeze combat.
        }

        if (!holdPosition && gatherer != null && gatherer.IsWorking && !forced)
        {
            var threat = Combatants.FindNearestEnemy(unit.team, defendRange,
                d => (d.Transform.position - transform.position).sqrMagnitude,
                includeBuildings: true, accept: AcceptAutomaticTarget);
            if (threat == null) return;
            target = threat;
            forced = false;
            lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        }

        // Must run BEFORE anything touches target.Transform: an interface reference to a
        // destroyed unit is still non-null, so `target == null` would miss it and the
        // next property access would throw.
        if (Combatants.IsGone(target))
        {
            bool hadTarget = target != null;
            forced = false;
            target = null;
            if (hadTarget) ClearUnreachable();
        }
        if (unreachableTarget != null && Combatants.IsGone(unreachableTarget))
            ClearUnreachable();

        bool lost = target == null ||
                    (!forced && (holdPosition
                        ? DistanceTo(target) > unit.attackRange
                        : Vector2.Distance(transform.position, target.Transform.position) > unit.aggroRange));
        if (lost)
        {
            retargetTimer -= dt;
            if (retargetTimer <= 0f)
            {
                retargetTimer = retargetInterval;
                target = holdPosition
                    ? Combatants.FindNearestEnemy(unit.team, unit.attackRange, DistanceSquaredTo,
                        includeBuildings: true, accept: AcceptAutomaticTarget)
                    : Combatants.FindNearestEnemy(unit.team, unit.aggroRange,
                        d => (d.Transform.position - transform.position).sqrMagnitude,
                        includeBuildings: true, accept: AcceptAutomaticTarget);
                if (target != null) lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
            }
        }
        if ((orderKind == OrderKind.AttackMove || orderKind == OrderKind.Patrol) && target == null)
        {
            if (mover.Moving) return;
            bool resumed = orderKind == OrderKind.Patrol
                ? ResumeDestination(ArrivePatrol)
                : ResumeDestination();
            if (!resumed) CompleteOrder();
            else return;
        }
        if (target == null) return;

        float dist = DistanceTo(target);
        if (dist > unit.attackRange)
        {
            if (holdPosition)
            {
                if (mover.Moving) mover.Stop();
                return;
            }
            repathTimer -= dt;
            if (repathTimer <= 0f)
            {
                repathTimer = repathInterval;
                Chase();
            }
            return;
        }

        // in range — stop and swing
        if (mover.Moving) mover.Stop();
        Face();
        if (cooldown > 0f) return;
        cooldown = unit.attackCooldown;
        Attack();
    }

    /// <summary>Re-path to <see cref="destinationCell"/>. <paramref name="onArrive"/> defaults
    /// to finishing the order; a patrol passes its own turn-around callback instead.</summary>
    bool ResumeDestination(System.Action onArrive = null)
    {
        var grid = MapGrid.Instance;
        if (grid == null) return false;
        var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
        if (!start.HasValue) return false;
        var path = grid.FindPath(start.Value, destinationCell);
        if (path == null) return false;
        mover.SetPath(path, onArrive != null ? onArrive : (System.Action)CompleteOrder);
        return true;
    }

    /// <summary>Distance to the nearest occupied tile for buildings, to the origin for units.</summary>
    float DistanceTo(IDamageable t)
    {
        var grid = MapGrid.Instance;
        if (t is Building b && grid != null && b.Cells.Count > 0)
        {
            float best = float.MaxValue;
            Vector2 me = transform.position;
            for (int i = 0; i < b.Cells.Count; i++)
            {
                float d = ((Vector2)grid.CellCenter(b.Cells[i]) - me).sqrMagnitude;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }
        return Vector2.Distance(transform.position, t.Transform.position);
    }

    float DistanceSquaredTo(IDamageable t)
    {
        float d = DistanceTo(t);
        return d * d;
    }

    bool AcceptAutomaticTarget(IDamageable candidate)
    {
        if (unreachableTimer <= 0f || candidate != unreachableTarget) return true;
        var grid = MapGrid.Instance;
        if (grid == null) return true;
        // A region relabel means the map itself may now connect the old goal, so the
        // target is worth another attempt before the short retry window expires.
        if (grid.RegionVersion != unreachableVersion) return true;
        // The same target may be retried as soon as its unit cell or nearest building
        // footprint cell changes; an unchanged goal waits out the short retry window.
        return GoalCell(candidate, grid) != unreachableGoal;
    }

    void ClearUnreachable()
    {
        unreachableTarget = null;
        unreachableGoal = default;
        unreachableVersion = 0;
        unreachableTimer = 0f;
    }

    void FailChase(Vector3Int goal)
    {
        if (forced) return; // explicit attacks keep their target and retry on repathInterval
        unreachableTarget = target;
        unreachableGoal = goal;
        var grid = MapGrid.Instance;
        unreachableVersion = grid != null ? grid.RegionVersion : 0;
        unreachableTimer = Mathf.Max(0.01f, autoTargetRetrySeconds);
        target = null;
        retargetTimer = 0f; // the next Update may immediately select a reachable alternate
        lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    }

    Vector3Int GoalCell(IDamageable t, MapGrid grid)
    {
        if (t is Building b && b.Cells.Count > 0)
            return NearestCell(b.Cells, transform.position, grid);
        return grid.WorldToCell(t.Transform.position);
    }

    void Chase()
    {
        if (holdPosition) return;
        var grid = MapGrid.Instance;
        if (grid == null || target == null) return;

        Vector3Int goal = GoalCell(target, grid);
        if (goal == lastGoalCell && mover.Moving) return;    // already on the way
        lastGoalCell = goal;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
        if (!start.HasValue) { FailChase(goal); return; }
        int region = grid.Region(start.Value);
        // The destination must be within the attack range of the target — not merely
        // anywhere in its neighbourhood. Without that cap, a unit already close to the
        // target "arrives" at its own cell and never actually closes in.
        var dest = grid.NearestWalkableWithin(start.Value, goal, 3, region, Mathf.Max(unit.attackRange, 1.2f));
        if (!dest.HasValue) { FailChase(goal); return; }
        var path = grid.FindPath(start.Value, dest.Value);
        if (path == null) { FailChase(goal); return; }
        mover.SetPath(path);
    }

    static Vector3Int NearestCell(System.Collections.Generic.IReadOnlyList<Vector3Int> cells, Vector3 from, MapGrid grid)
    {
        Vector3Int best = cells[0];
        float bestD = float.MaxValue;
        for (int i = 0; i < cells.Count; i++)
        {
            float d = ((Vector2)grid.CellCenter(cells[i]) - (Vector2)from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = cells[i]; }
        }
        return best;
    }

    void Face()
    {
        if (sr == null || target == null) return;
        sr.flipX = target.Transform.position.x < transform.position.x;
    }

    void Attack()
    {
        Face();
        LastAttackTime = Time.time;
        if (hasDirParam && target != null) anim.SetInteger(DirHash, DirIndex(transform.position, target.Transform.position));
        if (hasAttackTrigger) anim.SetTrigger("Attack");

        if (unit.projectile != null)
        {
            Color tint = unit.team == Team.Red ? new Color(1f, 0.75f, 0.75f) : Color.white;
            Projectile.Spawn(unit.projectile, transform.position + Vector3.up * 0.9f,
                             target, unit.EffectiveAttackDamage, unit.projectileSpeed, tint);
        }
        else
        {
            var hitTarget = target;
            if (unit.attackHitDelay <= 0f) ApplyMelee(hitTarget);
            else StartCoroutine(DelayedMelee(hitTarget, unit.attackHitDelay));
        }
    }

    IEnumerator DelayedMelee(IDamageable hitTarget, float delay)
    {
        yield return new WaitForSeconds(delay);
        ApplyMelee(hitTarget);
    }

    void ApplyMelee(IDamageable hitTarget)
    {
        if (!unit.IsAlive || Combatants.IsGone(hitTarget)) return;
        hitTarget.TakeDamage(unit.EffectiveAttackDamage);
        var bank = FxBank.Instance;
        if (bank != null && !Combatants.IsGone(hitTarget))
            bank.Hit(hitTarget.Transform.position);
    }
}
