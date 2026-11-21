using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Why a sheep did or did not take a step. Tests assert on the reason, so a
/// refusal is never confused with "the destination happened to be blocked as well".</summary>
public enum SheepStepResult
{
    Moved,
    Busy,
    GameOver,
    NodeUnavailable,
    WorkerTargeting,
    InvalidStep,
    OutOfRange,
    DestinationBlocked,
    WouldDisconnect,
}

/// <summary>
/// Grazes and wanders a couple of cells around where the sheep started.
///
/// The sheep is not a unit: it is a 2x1 <see cref="Building"/> whose cells are
/// registered in <see cref="MapGrid"/> as dynamic blockers, and it is also a
/// <see cref="ResourceNode"/> the player's workers harvest. Moving it is therefore a
/// map-topology change, not a cosmetic one. The blue-red corridor is a single cell
/// wide at (21,7); a sheep that steps into it stalemates the match. Every step runs
/// the five gates in <see cref="TryStep"/> first, and the connectivity gate simulates
/// the footprint move and undoes it before returning.
///
/// The footprint is re-registered when the transform reaches the new cell centre;
/// moving the transform alone would leave the blocked cells behind.
/// </summary>
[RequireComponent(typeof(Building))]
public class SheepWander : MonoBehaviour
{
    [Header("游荡")]
    [Tooltip("关掉后只保留动画，不再自己选方向（测试用）。")]
    public bool autoWander = true;
    [Tooltip("两次走动之间吃草多久（秒）。")]
    public float idleMinSeconds = 5f;
    public float idleMaxSeconds = 9f;
    [Tooltip("以出生点为圆心、最多走开几格。")]
    public int wanderRadiusCells = 2;
    [Tooltip("每走一格花多少秒。")]
    public float stepSeconds = 0.55f;
    [Tooltip("一次走动连续走几格（1~2）。")]
    public int maxStepsPerWalk = 2;
    [Tooltip("工人离占地多近就算它正在这块地上干活（世界单位）。宁可不走，也不要把工人的目标挪走。")]
    public float workerClearance = 1.6f;

    /// <summary>The footprint's lower-left cell recorded in Start. After a save/load
    /// this is the restored position, not the original spawn.</summary>
    public Vector3Int AnchorCell { get; private set; }

    /// <summary>True from the first frame of a step until the footprint is recomputed
    /// at the destination.</summary>
    public bool IsWalking { get; private set; }

    /// <summary>
    /// 保存中途步伐时回到仍登记在 Building.Cells 中的最后完整占地。
    /// 不推进实时对象，也不把半格 transform 写入存档。
    /// </summary>
    public Vector3 StableSavePosition
    {
        get
        {
            if (!IsWalking || building == null || building.Cells.Count == 0)
                return transform.position;
            int minX = building.Cells.Min(c => c.x);
            int minY = building.Cells.Min(c => c.y);
            return new Vector3(minX + building.Width * 0.5f, minY, transform.position.z);
        }
    }

    /// <summary>The reason for the most recent <see cref="TryStep"/>.</summary>
    public SheepStepResult LastStepResult { get; private set; }

    /// <summary>Every TryStep call since this component was built. A step attempt is the
    /// only place the five gates — and therefore the O(map) region relabel — can run, so
    /// this counter is what makes the "event-driven, not per-frame" claim testable.</summary>
    public int StepAttempts { get; private set; }

    /// <summary>Steps that actually started walking (only a subset of the attempts).</summary>
    public int StepsTaken { get; private set; }

    /// <summary>The footprint's lower-left cell right now.</summary>
    public Vector3Int CurrentCell
    {
        get
        {
            int width = building != null ? Mathf.Max(1, building.Width) : 2;
            return new Vector3Int(
                Mathf.FloorToInt(transform.position.x - width / 2f),
                Mathf.FloorToInt(transform.position.y),
                0);
        }
    }

    static readonly int WalkingHash = Animator.StringToHash("Walking");

    static readonly Vector3Int[] CardinalDirs =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
    };

    Building building;
    ResourceNode node;
    Animator animator;

    readonly List<Vector3Int> destinationCells = new List<Vector3Int>();
    readonly List<Vector3Int> ownCells = new List<Vector3Int>();
    readonly List<Vector3Int> previousCells = new List<Vector3Int>();

    float idleTimer;
    Vector3 walkFrom;
    Vector3 walkTo;
    float walkElapsed;
    Vector3Int goalCell;
    int stepsRemaining;

    void Awake()
    {
        building = GetComponent<Building>();
        node = GetComponent<ResourceNode>();
        animator = GetComponentInChildren<Animator>(true);
    }

    void Start()
    {
        AnchorCell = CurrentCell;
        idleTimer = NextIdleSeconds();
    }

    /// <summary>Restarts the grazing period now, with the current idle settings. Used by
    /// the performance harness to switch cadence without waiting out the old timer.</summary>
    public void RestartIdle()
    {
        idleTimer = NextIdleSeconds();
    }

    /// <summary>
    /// Event-driven on purpose. An idle frame only subtracts from the idle timer; a
    /// walking frame only advances the interpolation. The five gates — including the
    /// O(map) connectivity relabel — run inside <see cref="TryStep"/>, which this loop
    /// reaches once per 5-9 s idle period, never per frame. Nothing here allocates.
    /// </summary>
    void Update()
    {
        if (IsWalking)
        {
            AdvanceWalk(Time.deltaTime);
            return;
        }
        if (!autoWander) return;

        idleTimer -= Time.deltaTime;
        if (idleTimer > 0f) return;
        idleTimer = NextIdleSeconds();
        StartRandomWalk();                 // the gates run once, inside TryStep
    }

    // ------------------------------------------------------------------ gates

    /// <summary>
    /// Tries to move the footprint one cardinal cell to <paramref name="destination"/>,
    /// which is the new lower-left cell of the 2x1 footprint.
    ///
    /// Runs the gates in the brief's order: game over / node depleted, a worker on the
    /// node, one-cell and range validity, destination cells, then the connectivity
    /// simulation. Returns the reason instead of a bool so tests can tell "refused by
    /// the cut-vertex gate" from "refused because a tree is in the way".
    /// </summary>
    public SheepStepResult TryStep(Vector3Int destination)
    {
        StepAttempts++;
        LastStepResult = EvaluateStep(destination);
        return LastStepResult;
    }

    SheepStepResult EvaluateStep(Vector3Int destination)
    {
        if (IsWalking) return SheepStepResult.Busy;
        if (building == null || node == null) return SheepStepResult.NodeUnavailable;

        // Gate 1: the match is over, or this sheep is currently a stump.
        if (GameState.Instance != null && GameState.Instance.IsOver) return SheepStepResult.GameOver;
        if (node.Depleted || node.IsRegrowing) return SheepStepResult.NodeUnavailable;

        // Gate 2: any worker en route to or harvesting this node, or standing on it.
        if (WorkerNearby()) return SheepStepResult.WorkerTargeting;

        var current = CurrentCell;
        int dx = destination.x - current.x;
        int dy = destination.y - current.y;
        if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1) return SheepStepResult.InvalidStep;

        int radius = Mathf.Max(1, wanderRadiusCells);
        if (Mathf.Max(Mathf.Abs(destination.x - AnchorCell.x),
                      Mathf.Abs(destination.y - AnchorCell.y)) > radius)
            return SheepStepResult.OutOfRange;

        destinationCells.Clear();
        destinationCells.Add(destination);
        destinationCells.Add(new Vector3Int(destination.x + 1, destination.y, 0));

        // Gate 3: terrain, other buildings, units.
        if (!DestinationClear()) return SheepStepResult.DestinationBlocked;

        // Gate 4: the map must stay connected for the blue and red armies.
        if (!ConnectivityPreserved()) return SheepStepResult.WouldDisconnect;

        // Gate 5.
        BeginStep(destination);
        return SheepStepResult.Moved;
    }

    /// <summary>A worker <em>targeting</em> this node counts even while it is still
    /// walking over — moving the node under a path in flight is how a worker gets
    /// stuck on an unreachable cell. Standing next to the footprint counts too.
    /// Only ever called from a step attempt, never from Update.</summary>
    bool WorkerNearby()
    {
        var grid = MapGrid.Instance;
        for (int i = 0; i < Gatherer.All.Count; i++)
        {
            var worker = Gatherer.All[i];
            if (worker == null) continue;
            if (node != null && worker.TargetNode == node) return true;
            if (grid == null) continue;

            var cells = building.Cells;
            for (int c = 0; c < cells.Count; c++)
                if (Vector2.Distance(worker.transform.position, grid.CellCenter(cells[c])) <= workerClearance)
                    return true;
        }
        return false;
    }

    /// <summary>Gate 3. The footprint overlaps its old cells by one on every cardinal
    /// step, so a cell this sheep already owns is not "occupied by another building".</summary>
    bool DestinationClear()
    {
        var grid = MapGrid.Instance;
        if (grid == null) return false;

        ownCells.Clear();
        ownCells.AddRange(building.Cells);

        for (int i = 0; i < destinationCells.Count; i++)
        {
            var cell = destinationCells[i];
            if (!grid.TerrainWalkable(cell)) return false;
            if (grid.IsRamp(cell)) return false;      // sheep must not occupy a ramp
            if (!IsOwnCell(cell) && grid.IsBlockedByBuilding(cell)) return false;
            if (UnitAt(grid, cell)) return false;
        }
        return true;
    }

    bool IsOwnCell(Vector3Int cell)
    {
        for (int i = 0; i < ownCells.Count; i++)
            if (ownCells[i] == cell) return true;
        return false;
    }

    static bool UnitAt(MapGrid grid, Vector3Int cell)
    {
        for (int i = 0; i < UnitActor.All.Count; i++)
        {
            var unit = UnitActor.All[i];
            if (unit == null || !unit.IsAlive) continue;
            if (grid.WorldToCell(unit.transform.position) == cell) return true;
        }
        return false;
    }

    /// <summary>Gate 4. Temporarily swap the footprint on the live grid, ask whether
    /// the blue and red armies still share a region and whether this sheep is still
    /// harvest-reachable from it, then put the old footprint back in a finally so a
    /// refusal can never silently eat two blocked cells.</summary>
    bool ConnectivityPreserved()
    {
        var grid = MapGrid.Instance;
        if (grid == null) return false;

        ownCells.Clear();
        ownCells.AddRange(building.Cells);
        if (ownCells.Count == 0) return false;

        // Simulate the move WITHOUT relabelling the whole map, then revert in the same
        // call stack. The reachability queries below read CanEnter directly, so they never
        // consult the (deliberately stale) region cache. This is what turns six O(map)
        // relabels per step into zero.
        grid.ApplyBlockingDeltaNoRelabel(ownCells, destinationCells);
        try
        {
            var reached = new HashSet<Vector3Int>();
            bool haveArmy = false;

            for (int i = 0; i < UnitActor.All.Count; i++)
            {
                var unit = UnitActor.All[i];
                if (unit == null || !unit.IsAlive) continue;
                if (unit.team != Team.Blue && unit.team != Team.Red) continue;

                var cell = grid.WorldToCell(unit.transform.position);
                Vector3Int? start = grid.Walkable(cell) ? cell : grid.ResolveWalkableStart(cell);
                if (!start.HasValue) return false;

                if (!haveArmy)
                {
                    grid.CollectReachable(start.Value, reached);   // one flood fill
                    haveArmy = true;
                }
                else if (!reached.Contains(start.Value))
                {
                    return false;      // an army start was split off
                }
            }

            // No starting army in this scene (isolated fixtures): nothing to split.
            if (!haveArmy) return true;

            // The sheep must still be approachable from that side: at least one walkable
            // cardinal neighbour of its new footprint has to be in the reached set.
            for (int i = 0; i < destinationCells.Count; i++)
            {
                var c = destinationCells[i];
                for (int d = 0; d < CardinalDirs.Length; d++)
                {
                    var n = c + CardinalDirs[d];
                    if (grid.Walkable(n) && reached.Contains(n)) return true;
                }
            }
            return false;
        }
        finally
        {
            grid.ApplyBlockingDeltaNoRelabel(destinationCells, ownCells);
        }
    }

    // --------------------------------------------------------------- movement

    void BeginStep(Vector3Int destination)
    {
        StepsTaken++;
        IsWalking = true;
        walkFrom = transform.position;
        walkTo = new Vector3(destination.x + 1f, destination.y, transform.position.z);
        walkElapsed = 0f;
        SetWalkingAnimation(true);
    }

    void AdvanceWalk(float deltaTime)
    {
        walkElapsed += deltaTime;
        float duration = Mathf.Max(0f, stepSeconds);
        float t = duration <= 0f ? 1f : Mathf.Clamp01(walkElapsed / duration);
        transform.position = Vector3.Lerp(walkFrom, walkTo, t);
        if (t >= 1f) FinishStep();
    }

    void FinishStep()
    {
        transform.position = walkTo;          // land exactly on the cell centre line
        IsWalking = false;
        RecomputeFootprint();
        SetWalkingAnimation(false);

        if (stepsRemaining > 0)
        {
            stepsRemaining--;
            var direction = DirectionToward(CurrentCell, goalCell);
            if (direction != Vector3Int.zero &&
                TryStep(CurrentCell + direction) == SheepStepResult.Moved)
                return;
            stepsRemaining = 0;
        }
        idleTimer = NextIdleSeconds();
    }

    /// <summary>
    /// Moves the Building's registered footprint to the current transform with ONE
    /// region relabel. <see cref="Building.Recompute"/> runs Unregister + Register, each
    /// of which relabels the whole map; here <see cref="Building.Prepare"/> refreshes
    /// the Building's own cell list first, and one batched delta moves the registration.
    /// The resulting state is the same one Recompute would reach. If the building does
    /// not look registered (an unexpected state), it falls back to Recompute so the
    /// private bookkeeping is never guessed when it matters.
    /// </summary>
    void RecomputeFootprint()
    {
        if (building == null) return;

        var grid = MapGrid.Instance;
        if (grid == null) { building.Recompute(); return; }

        previousCells.Clear();
        previousCells.AddRange(building.Cells);
        building.Prepare();                    // new cells, no MapGrid calls

        // Depleted mid-walk: SetBlocking(false) already released the old footprint.
        if (!building.blocksMovement) return;

        if (previousCells.Count > 0 && grid.IsBlockedByBuilding(previousCells[0]))
        {
            grid.ApplyBlockingDelta(previousCells, building.Cells);
            return;
        }
        building.Recompute();                  // unexpected state: let the Building reconcile
    }

    void StartRandomWalk()
    {
        int radius = Mathf.Max(1, wanderRadiusCells);

        // Re-pick a few times rather than burning the whole idle period when the first
        // roll lands on the cell the sheep already occupies (no direction to walk).
        goalCell = CurrentCell;
        for (int attempt = 0; attempt < 4 && goalCell == CurrentCell; attempt++)
        {
            goalCell = AnchorCell + new Vector3Int(
                Random.Range(-radius, radius + 1),
                Random.Range(-radius, radius + 1),
                0);
        }
        if (goalCell == CurrentCell) return;

        var direction = DirectionToward(CurrentCell, goalCell);
        if (direction == Vector3Int.zero) return;

        int planned = Random.Range(1, Mathf.Max(1, maxStepsPerWalk) + 1);
        if (TryStep(CurrentCell + direction) == SheepStepResult.Moved)
            stepsRemaining = planned - 1;
    }

    static Vector3Int DirectionToward(Vector3Int from, Vector3Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if (dx == 0 && dy == 0) return Vector3Int.zero;
        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return new Vector3Int(System.Math.Sign(dx), 0, 0);
        return new Vector3Int(0, System.Math.Sign(dy), 0);
    }

    float NextIdleSeconds()
    {
        float low = Mathf.Max(0.1f, idleMinSeconds);
        float high = Mathf.Max(low, idleMaxSeconds);
        return Random.Range(low, high);
    }

    void SetWalkingAnimation(bool walking)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetBool(WalkingHash, walking);
    }
}
