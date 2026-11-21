using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a placed building and reserves its ground footprint in <see cref="MapGrid"/>,
/// so units path around it instead of walking through it.
///
/// Placement convention (matches how the prefabs were built): the transform sits on
/// the building's base line, i.e. position = CellCenter(anchorCell) - (0, 0.5, 0) for a
/// 1-tile-wide building. The footprint covers <see cref="Width"/> tiles horizontally,
/// starting at the tile to the left of the origin, and <see cref="Depth"/> rows upward
/// from the origin's row.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class Building : MonoBehaviour, IDamageable
{
    /// <summary>Every live building, so gatherers can find a drop-off.</summary>
    public static readonly List<Building> All = new List<Building>();

    [Header("占地")]
    [Tooltip("Footprint in tiles. Leave at (0,0) to take the width from the sprite.")]
    public Vector2Int footprint = Vector2Int.zero;

    [Tooltip("How many tile rows up from the base line the building occupies.")]
    public int depth = 1;

    [Tooltip("Off = decorative only, does not block movement (used by the placement ghost).")]
    public bool blocksMovement = true;

    [Header("经济")]
    [Tooltip("建造这座建筑要花的资源。")]
    public ResourceCost cost = new ResourceCost(50);

    [Tooltip("勾上 = 这是卸货点，采集单位会把资源运到这里。")]
    public bool isDropoff;

    [Header("阵营")]
    public Team team = Team.Blue;

    [Header("生命")]
    public int maxHealth = 200;
    public int health = 200;
    [Tooltip("血条挂多高（世界单位）。")]
    public float barHeight = 2.6f;
    [Tooltip("被摧毁后换成这张图，并且不再阻挡、不再生产（留空则整栋消失）。")]
    public Sprite destroyedSprite;
    [Tooltip("这是指挥中心：被摧毁 = 该阵营失败。")]
    public bool isCommandCenter;

    /// <summary>Set on the placement preview. A ghost is a full copy of the prefab, so it
    /// inherits isCommandCenter / isDropoff — and it lands in <see cref="All"/> on the way
    /// in. Left alone that makes the win check see a command centre that does not exist:
    /// click the build button, press Escape, and the game decides your centre was just
    /// destroyed and ends the match.</summary>
    [System.NonSerialized] public bool isGhost;

    [Header("建造时间")]
    [Tooltip("摆下去之后要多久才建好。0 = 立刻建成（编辑器里摆的装饰性建筑用这个）。")]
    public float constructionSeconds = 6f;
    [Tooltip("建造期间显示的图。留空则用本体图压暗显示。")]
    public Sprite constructionSprite;

    /// <summary>True while it was dropped during a match and is still being built.
    /// While it is up the building blocks ground and can be attacked, but does not produce
    /// and does not accept deliveries.</summary>
    public bool IsUnderConstruction { get; private set; }
    public float ConstructionProgress => constructionSeconds <= 0f
        ? 1f : Mathf.Clamp01(1f - constructionTimer / constructionSeconds);

    /// <summary>Set by BuildingPlacer / AIController when they drop it during a match.
    /// Scene-placed scenery never has it, so it never goes through construction.</summary>
    [System.NonSerialized] public bool builtDuringMatch;

    /// <summary>Marks a building as a match project and starts construction immediately.
    /// Starting here rather than waiting for Start() keeps unlock checks correct even
    /// when another action happens in the same frame as placement.</summary>
    public void MarkBuiltDuringMatch()
    {
        builtDuringMatch = true;
        if (constructionSeconds > 0f) BeginConstruction();
    }

    float constructionTimer;
    HealthBar constructionBar;
    SpriteRenderer mainRenderer;
    Sprite finishedSprite;

    public bool IsAlive => health > 0;
    public bool CanDemolish => team != Team.Neutral && !isCommandCenter;

    /// <summary>Health still missing before the building is whole again.</summary>
    public int MissingHealth => Mathf.Max(0, maxHealth - health);

    /// <summary>Price of a full repair: one gold, one wood and one meat per started
    /// 10 missing health. Expression-free of side effects and shared by the player
    /// command and (Task 5) the AI repair pass, so both sides pay the same.</summary>
    public ResourceCost RepairCost
    {
        get
        {
            int units = Mathf.CeilToInt(MissingHealth / 10f);
            return new ResourceCost(units, units, units);
        }
    }

    /// <summary>True when the building could be repaired right now: alive, finished,
    /// owned by a faction and not already at full health. Affordability is the
    /// caller's concern — <see cref="Building"/> must not know about the bank.</summary>
    public bool CanRepair => IsAlive && !IsUnderConstruction && team != Team.Neutral && MissingHealth > 0;

    [System.Serializable]
    public class SaveState
    {
        public Team team;
        public int health;
        public int maxHealth;
        public bool wrecked;
        public bool blocksMovement;
        public bool isDropoff;
        public bool underConstruction;
        public float constructionRemaining;
    }

    public SaveState CaptureSaveState()
    {
        return new SaveState
        {
            team = team,
            health = health,
            maxHealth = maxHealth,
            wrecked = wrecked,
            blocksMovement = blocksMovement,
            isDropoff = isDropoff,
            underConstruction = IsUnderConstruction,
            constructionRemaining = IsUnderConstruction ? constructionTimer : 0f,
        };
    }

    /// <summary>Called one frame after instantiation, when Start has created the bar.
    /// Wrecked objects are restored without the explosion or audio of a live destruction.</summary>
    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        team = state.team;
        maxHealth = Mathf.Max(1, state.maxHealth);
        isDropoff = state.isDropoff;

        if (state.wrecked)
        {
            RestoreWreckedState(state.blocksMovement);
            return;
        }

        blocksMovement = state.blocksMovement;
        if (blocksMovement) Register(); else Unregister();

        health = Mathf.Clamp(state.health, 1, maxHealth);
        if (bar != null) bar.SetRatio((float)health / maxHealth);

        if (state.underConstruction) RestoreConstruction(state.constructionRemaining);
        else if (IsUnderConstruction) FinishConstruction();

        var pb = GetComponent<ProductionBuilding>();
        if (pb != null) pb.enabled = IsAlive && !IsUnderConstruction;
    }

    /// <summary>Recreate permanent rubble: no boom, but the collider and footprint stay.</summary>
    public void RestoreWreckedState(bool keepBlocking)
    {
        health = 0;
        wrecked = true;
        IsUnderConstruction = false;
        isDropoff = false;
        if (bar != null) bar.gameObject.SetActive(false);
        if (constructionBar != null) { Destroy(constructionBar.gameObject); constructionBar = null; }

        blocksMovement = keepBlocking;
        if (blocksMovement) Register(); else Unregister();

        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        var pb = GetComponent<ProductionBuilding>();
        if (pb != null) pb.enabled = false;

        if (destroyedSprite != null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sprite = destroyedSprite;
        }
        All.Remove(this);
    }

    void RestoreConstruction(float remaining)
    {
        builtDuringMatch = true;
        BeginConstruction();
        constructionTimer = Mathf.Clamp(remaining, 0.01f, constructionSeconds);
        if (constructionBar != null) constructionBar.SetProgress(ConstructionProgress);
    }

    // IDamageable — explicit so the property names cannot clash with the fields above
    Team IDamageable.Team => team;
    Transform IDamageable.Transform => transform;

    HealthBar bar;
    bool wrecked;
    float repairPool;
    SpriteRenderer clickVisual;

    readonly List<Vector3Int> cells = new List<Vector3Int>();
    int width = 1;
    int depthRows = 1;
    bool registered;

    /// <summary>Footprint width in tiles.</summary>
    public int Width => width;

    /// <summary>Footprint depth in tiles.</summary>
    public int Depth => depthRows;

    public IReadOnlyList<Vector3Int> Cells => cells;

    void Awake()
    {
        clickVisual = FindLargestClickVisual();
        Prepare();
    }

    void OnEnable()
    {
        if (!isGhost && !All.Contains(this)) All.Add(this);
        Register();
    }

    void OnDisable()
    {
        All.Remove(this);
        Unregister();
    }

    void Start()
    {
        if (wrecked) return;      // damage may land before the first frame in tests or scripts
        if (maxHealth < 1) maxHealth = 1;
        health = Mathf.Clamp(health <= 0 ? maxHealth : health, 1, maxHealth);
        bar = HealthBar.Create(transform, barHeight, Mathf.Max(1.2f, width));
        bar.SetRatio(1f);          // hide it until the building actually takes damage

        // Only what is dropped during a match is a project. Everything the level already
        // contains — trees, rocks, the pre-placed gold mine — is finished scenery, placed
        // by hand, not built. Without this every tree on the map starts "under
        // construction" and renders at reduced alpha forever.
        if (isGhost || !builtDuringMatch || constructionSeconds <= 0f) return;
        BeginConstruction();
    }

    void Update()
    {
        if (!IsUnderConstruction) return;
        constructionTimer -= Time.deltaTime;
        if (constructionBar != null) constructionBar.SetProgress(ConstructionProgress);
        if (constructionTimer > 0f) return;
        FinishConstruction();
    }

    void BeginConstruction()
    {
        if (IsUnderConstruction) return;
        IsUnderConstruction = true;
        constructionTimer = constructionSeconds;

        // Show the half-built art if there is one. We deliberately do NOT dim the renderer
        // colour — mutating renderer.colour and trying to restore it later kept leaving
        // scenery stuck at 55% alpha. The progress bar is the signal; the art is a bonus.
        mainRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (constructionSprite != null && mainRenderer != null)
        {
            finishedSprite = mainRenderer.sprite;
            mainRenderer.sprite = constructionSprite;
        }

        // tucked below the health bar so the two never sit on top of each other
        constructionBar = HealthBar.Create(transform, barHeight * 0.78f, Mathf.Max(1.2f, width));
        constructionBar.SetProgress(0f);

        var pb = GetComponent<ProductionBuilding>();
        if (pb != null) pb.enabled = false;
    }

    void FinishConstruction()
    {
        IsUnderConstruction = false;
        if (constructionBar != null) { Destroy(constructionBar.gameObject); constructionBar = null; }

        if (finishedSprite != null && mainRenderer != null)
        {
            mainRenderer.sprite = finishedSprite;
            finishedSprite = null;
        }

        var pb = GetComponent<ProductionBuilding>();
        if (pb != null) pb.enabled = true;

        Population.Invalidate();      // a finished House may raise this side's capacity
    }

    /// <summary>Turns a live prefab copy into a placement preview and removes every
    /// gameplay reservation it acquired during Awake/OnEnable.</summary>
    public void MakeGhost()
    {
        isGhost = true;
        blocksMovement = false;
        isDropoff = false;
        All.Remove(this);
        Unregister();
    }

    /// <summary>Turn the footprint on or off without destroying the object. Used by
    /// trees: once cut down to a stump they stop blocking, so units can walk over them.</summary>
    public void SetBlocking(bool on)
    {
        if (wrecked || blocksMovement == on) return;
        blocksMovement = on;
        if (on) Register();
        else Unregister();
    }

    // ------------------------------------------------------------------ damage

    public void TakeDamage(int amount)
    {
        if (!IsAlive || wrecked || amount <= 0) return;
        health -= amount;
        if (bar != null) bar.SetRatio((float)health / Mathf.Max(1, maxHealth));
        if (health <= 0) Wreck();
    }

    /// <summary>Free, slow repair applied by a worker standing next to the building.
    /// The health is accumulated as a float because the rate (20 HP/s) brings less than
    /// a point per frame at 60 Hz, and it is clamped to <see cref="maxHealth"/>.
    /// Deliberately free: this path must never touch <see cref="Economy"/> or
    /// <see cref="AIController"/> — those belong to the instant, paid repair.</summary>
    public void RepairByWorker(float amount)
    {
        if (!IsAlive || wrecked || IsUnderConstruction || amount <= 0f) return;
        if (health >= maxHealth) return;

        repairPool += amount;
        int whole = Mathf.FloorToInt(repairPool);
        if (whole <= 0) return;

        repairPool -= whole;
        health = Mathf.Min(maxHealth, health + whole);
        if (bar != null) bar.SetRatio((float)health / Mathf.Max(1, maxHealth));
        if (health >= maxHealth) repairPool = 0f;
    }

    public void Repair(int amount)
    {
        if (!IsAlive) return;
        health = Mathf.Min(maxHealth, health + amount);
        if (bar != null) bar.SetRatio((float)health / Mathf.Max(1, maxHealth));
    }

    /// <summary>Player-requested removal. Command centres and neutral resources are
    /// protected; cost refunds and production queues are handled by SelectionManager.</summary>
    public void Demolish()
    {
        if (!CanDemolish) return;
        // Release the archer while the footprint still blocks, matching Wreck, and
        // before the tower hierarchy is destroyed.
        var garrison = GetComponent<TowerGarrison>();
        if (garrison != null) garrison.Ungarrison();
        isDropoff = false;
        Unregister();
        Destroy(gameObject);
    }

    /// <summary>Destroyed: stop being a factory / drop-off / obstacle, and either turn
    /// into rubble or vanish.</summary>
    void Wreck()
    {
        health = 0;
        wrecked = true;
        var garrison = GetComponent<TowerGarrison>();
        if (garrison != null) garrison.Ungarrison();
        IsUnderConstruction = false;
        if (bar != null) bar.gameObject.SetActive(false);
        if (constructionBar != null) { Destroy(constructionBar.gameObject); constructionBar = null; }

        // a real building goes up with a bang; scenery just quietly stops being in the way
        if (team != Team.Neutral && FxBank.Instance != null)
            FxBank.Instance.Boom(transform.position, Mathf.Clamp(width * 0.5f, 1f, 2f));

        var pb = GetComponent<ProductionBuilding>();
        if (pb != null) pb.enabled = false;
        isDropoff = false;
        Population.Invalidate();      // losing a House may lower this side's capacity

        if (destroyedSprite != null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sprite = destroyedSprite;
            All.Remove(this);
            // Rubble is still a physical obstacle. Keep both the collider and the
            // footprint registration; disabling this component would call OnDisable
            // and silently release the reservation again.
        }
        else
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            Unregister();
            Destroy(gameObject);
        }
    }

    /// <summary>Nearest building flagged as a drop-off. The placement ghost is skipped
    /// (it is not a real building and must never be a haul destination).
    ///
    /// Pass the worker's team: without it, the nearest drop-off wins and a blue worker
    /// will happily haul wood into the red base. Neutral accepts any team's drop-off.
    ///
    /// There is deliberately NO fallback to another team's drop-off. Both sides now open
    /// with no base at all, so "my side has no centre yet" is a normal state — and a
    /// fallback made blue workers set off across the map to unload into the red castle.
    /// A worker with nowhere to go just waits, and delivers the moment a centre goes up.</summary>
    /// <summary>True while this team owns any living command centre, including one
    /// still under construction. Used only for the one-base-per-side rule.</summary>
    public static bool HasLivingCommandCentre(Team team)
    {
        for (int i = 0; i < All.Count; i++)
        {
            var b = All[i];
            if (b == null || b.isGhost || !b.IsAlive) continue;
            if (b.team == team && b.isCommandCenter) return true;
        }
        return false;
    }

    /// <summary>True only when this team owns a living command centre that has
    /// finished construction. Placement ghosts and half-built bases do not unlock
    /// the rest of the tech tree.</summary>
    public static bool HasCompletedCommandCentre(Team team)
    {
        for (int i = 0; i < All.Count; i++)
        {
            var b = All[i];
            if (b == null || b.isGhost || !b.IsAlive || b.IsUnderConstruction) continue;
            if (b.team == team && b.isCommandCenter) return true;
        }
        return false;
    }

    /// <summary>Hit test on the visible body of a live building. The movement
    /// collider is only one tile deep while the art is three to six tiles tall, so a
    /// click on the roof would otherwise miss selection, repair and attack lookups.
    /// Callers pass an accept filter; neutral scenery is deliberately not included by
    /// default so a resource tree's sprite cannot hijack a building click.</summary>
    public static Building FindAtVisualPoint(Vector3 world, System.Func<Building, bool> accept = null)
    {
        Building best = null;
        float bestOrder = float.NegativeInfinity;
        float bestDist = float.MaxValue;

        for (int i = 0; i < All.Count; i++)
        {
            var b = All[i];
            if (b == null || b.isGhost || !b.IsAlive) continue;
            if (accept != null && !accept(b)) continue;

            var sr = b.VisualForHitTest();
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            if (!sr.bounds.Contains(world)) continue;

            float order = sr.sortingOrder;
            float d = (b.transform.position - world).sqrMagnitude;
            if (order > bestOrder + 0.001f ||
                (Mathf.Abs(order - bestOrder) <= 0.001f && d < bestDist))
            {
                best = b;
                bestOrder = order;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>Largest active sprite on this building, excluding runtime health and
    /// construction bars. Cached from Awake (before those bars exist) because the cursor
    /// hit test calls this every frame while an army is selected.</summary>
    SpriteRenderer VisualForHitTest()
    {
        if (clickVisual != null && clickVisual.enabled && clickVisual.sprite != null)
            return clickVisual;
        clickVisual = FindLargestClickVisual();
        return clickVisual;
    }

    SpriteRenderer FindLargestClickVisual()
    {
        SpriteRenderer best = null;
        float bestArea = -1f;
        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var sr = renderers[i];
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            if (!sr.gameObject.activeInHierarchy) continue;
            if (sr.GetComponentInParent<HealthBar>() != null) continue;
            var size = sr.bounds.size;
            float area = size.x * size.y;
            if (area > bestArea) { bestArea = area; best = sr; }
        }
        return best;
    }

    /// <summary>True when a worker at <paramref name="from"/> has a walkable approach
    /// cell beside this building's footprint. Feedback only; the actual repair order
    /// still runs its normal retry loop.</summary>
    public static bool IsApproachableFrom(Vector3 from, Building building)
    {
        if (ReferenceEquals(building, null) || !building.IsAlive || building.IsUnderConstruction)
            return false;
        var grid = MapGrid.Instance;
        if (grid == null) return true;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(from));
        if (!start.HasValue) return false;
        int region = grid.Region(start.Value);
        for (int i = 0; i < building.Cells.Count; i++)
            if (grid.NearestWalkableWithin(start.Value, building.Cells[i], 4, region, 1.6f).HasValue)
                return true;
        return false;
    }

    public static Building FindNearestDropoff(Vector3 from, Team team = Team.Neutral)
    {
        Building best = null;
        float bestD = float.MaxValue;

        for (int i = 0; i < All.Count; i++)
        {
            var b = All[i];
            if (b == null || !b.isDropoff || !b.blocksMovement) continue;
            if (b.IsUnderConstruction) continue;      // half-built: nothing to unload into yet
            if (team != Team.Neutral && b.team != team) continue;

            float d = (b.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    /// <summary>Derives the footprint size AND the occupied cells without touching
    /// MapGrid. Safe to call on a placement ghost, which must not reserve anything.</summary>
    public void Prepare()
    {
        width = footprint.x > 0 ? footprint.x : MeasureWidthFromSprite();
        depthRows = footprint.y > 0 ? footprint.y : Mathf.Max(1, depth);
        ComputeCells();
    }

    int MeasureWidthFromSprite()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return 1;
        return Mathf.Max(1, Mathf.RoundToInt(sr.sprite.bounds.size.x));
    }

    /// <summary>Recomputes the occupied cells from the current transform and updates
    /// the MapGrid reservation. Call this after moving a building.</summary>
    public void Recompute()
    {
        Unregister();
        Prepare();
        ComputeCells();
        Register();
    }

    void ComputeCells()
    {
        // The origin sits on the base line, centred horizontally on the footprint,
        // so the leftmost occupied column starts half a footprint to the left.
        int ax = Mathf.FloorToInt(transform.position.x - width / 2f);
        int ay = Mathf.FloorToInt(transform.position.y);
        cells.Clear();
        for (int i = 0; i < width; i++)
        for (int j = 0; j < depthRows; j++)
            cells.Add(new Vector3Int(ax + i, ay + j, 0));
    }

    void Register()
    {
        if (registered || !blocksMovement) return;
        var grid = MapGrid.Instance;
        if (grid == null) return;
        if (cells.Count == 0) { Prepare(); ComputeCells(); }
        grid.AddBuildingFootprint(cells);
        registered = true;
    }

    void Unregister()
    {
        if (!registered) return;
        var grid = MapGrid.Instance;
        if (grid != null) grid.RemoveBuildingFootprint(cells);
        registered = false;
    }

    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        Prepare();
        int ax = Mathf.FloorToInt(transform.position.x - width / 2f);
        int ay = Mathf.FloorToInt(transform.position.y);
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.6f);
        for (int i = 0; i < width; i++)
        for (int j = 0; j < depthRows; j++)
            Gizmos.DrawWireCube(new Vector3(ax + i + 0.5f, ay + j + 0.5f, 0f),
                                new Vector3(0.9f, 0.9f, 0.1f));
    }
}
