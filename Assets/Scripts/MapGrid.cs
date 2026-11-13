using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scans the layered tilemaps into a walkability grid, labels connected regions
/// (so islands and unreachable plateaus are first-class, not accidents), and
/// runs A* on a binary min-heap.
///
/// Levels: 0 = low ground, 1/2 = plateaus.
///
/// Two rules govern movement:
///  - Cliff tiles block.
///  - A change of level is only allowed on a RAMP. Everywhere else the edge of a
///    plateau is a wall, so units cannot walk straight up a height difference
///    just because no cliff tile happens to be painted there.
///
/// Ramps come from two places: the rect list below, and anything painted into
/// <see cref="rampLayer"/>. The layer is a logic layer — its art is only a
/// marker, so paint the visible slope art on the terrain tilemaps as usual.
/// </summary>
[ExecuteAlways]
public class MapGrid : MonoBehaviour
{
    static MapGrid instance;

    /// <summary>Resolved lazily: in edit mode a domain reload destroys the old
    /// instance without ever calling Awake on the new one, so a plain cached
    /// static would go null and take every editor tool with it.</summary>
    public static MapGrid Instance
    {
        get
        {
            if (instance == null) instance = FindFirstObjectByType<MapGrid>();
            if (instance != null) instance.EnsureScanned();
            return instance;
        }
    }

    [Header("Tilemap Layers")]
    public Tilemap ground0;
    public Tilemap top1;
    public Tilemap cliff1;
    public Tilemap top2;
    public Tilemap cliff2;

    [Tooltip("Paint cells here to make a height difference climbable. Purely a logic layer; " +
             "the marker art is not the ramp you see in game.")]
    public Tilemap rampLayer;

    [System.Serializable]
    public class Ramp
    {
        public string name = "Ramp";
        public int xMin, xMax, yMin, yMax; // ramp cells themselves (cliff-row cells)
        public int lowLevel = 0;
        public int highLevel = 1;
        public bool Contains(Vector3Int c)
        {
            return c.x >= xMin && c.x <= xMax && c.y >= yMin && c.y <= yMax;
        }
    }

    [Header("Ramps (rect-based, legacy — the ramp Layer above is preferred)")]
    public List<Ramp> ramps = new List<Ramp>();

    [Header("Debug")]
    public bool drawGizmos = true;
    [Tooltip("Tint each disconnected region a different colour in the Scene view.")]
    public bool colourByRegion = true;

    readonly Dictionary<Vector3Int, int> levels = new Dictionary<Vector3Int, int>();
    readonly HashSet<Vector3Int> blockedCells = new HashSet<Vector3Int>();
    readonly Dictionary<Vector3Int, Ramp> rampOf = new Dictionary<Vector3Int, Ramp>();
    readonly HashSet<Vector3Int> rampCells = new HashSet<Vector3Int>();
    readonly Dictionary<Vector3Int, int> levelOf = new Dictionary<Vector3Int, int>();
    readonly Dictionary<Vector3Int, int> regionOf = new Dictionary<Vector3Int, int>();
    /// <summary>Reference-counted cells occupied by placed buildings. Separate from
    /// the tilemap scan so re-scanning terrain never wipes them out. Overlapping
    /// registrations must all be removed before a cell becomes free.</summary>
    readonly Dictionary<Vector3Int, int> dynamicBlockers = new Dictionary<Vector3Int, int>();
    /// <summary>Scratch for <see cref="ApplyBlockingDeltaNoRelabel"/>: the distinct cells a
    /// batch touched and their pre-batch refcounts. Reused, so a step attempt does not
    /// allocate.</summary>
    readonly List<Vector3Int> deltaCells = new List<Vector3Int>();
    readonly List<int> deltaBefore = new List<int>();
    int regionCount;
    bool scanned;

    // --- flat index space for the reachability flood fill ------------------------
    // Rebuilt by Scan(): terrain walkability, level and ramp flags for every cell in
    // the union of the tilemap bounds. CollectReachable copies the walk array and
    // overlays the (cheap, small) dynamic blocker dictionary, so its flood fill runs
    // on plain arrays instead of a handful of dictionary lookups per neighbour.
    BoundsInt reachBounds;
    bool[] reachTerrainWalk;
    int[] reachLevel;
    bool[] reachRamp;
    bool reachCacheReady;

    static readonly Vector3Int[] Dirs =
    {
        new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,1,0), new Vector3Int(0,-1,0),
        new Vector3Int(1,1,0), new Vector3Int(1,-1,0), new Vector3Int(-1,1,0), new Vector3Int(-1,-1,0),
    };

    void Awake()
    {
        instance = this;
        Scan();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void OnValidate() { scanned = false; }

    void OnEnable()
    {
        Tilemap.tilemapTileChanged += OnTilemapTileChanged;
    }

    void OnDisable()
    {
        Tilemap.tilemapTileChanged -= OnTilemapTileChanged;
    }

    /// <summary>Painting tiles in the editor does not touch this component, so
    /// without this hook the walkability grid would keep its old scan and the
    /// gizmos would not show newly painted ramps until you pressed Play.</summary>
    void OnTilemapTileChanged(Tilemap tilemap, Tilemap.SyncTile[] syncTiles)
    {
        if (tilemap == null) return;
        if (tilemap != ground0 && tilemap != top1 && tilemap != cliff1 &&
            tilemap != top2 && tilemap != cliff2 && tilemap != rampLayer) return;
        scanned = false;
    }

    [ContextMenu("Rescan map")]
    public void Rescan()
    {
        scanned = false;
        Scan();
    }

    public void Scan()
    {
        if (ground0 == null) return;
        levels.Clear();
        blockedCells.Clear();
        rampOf.Clear();
        rampCells.Clear();
        levelOf.Clear();
        regionOf.Clear();
        regionCount = 0;

        BoundsInt b = ground0.cellBounds;
        Include(top1, ref b);
        Include(top2, ref b);

        for (int x = b.xMin; x < b.xMax; x++)
        for (int y = b.yMin; y < b.yMax; y++)
        {
            var c = new Vector3Int(x, y, 0);
            int lv = -1;
            if (ground0.HasTile(c)) lv = 0;
            if (top1 != null && top1.HasTile(c)) lv = 1;
            if (top2 != null && top2.HasTile(c)) lv = 2;
            if (lv >= 0) levels[c] = lv;
            bool blocked = (cliff1 != null && cliff1.HasTile(c)) || (cliff2 != null && cliff2.HasTile(c));
            if (blocked) blockedCells.Add(c);
        }

        // --- ramps: legacy rects, then the painted layer ---
        foreach (var r in ramps)
            for (int x = r.xMin; x <= r.xMax; x++)
            for (int y = r.yMin; y <= r.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                rampOf[c] = r;
                rampCells.Add(c);
            }

        if (rampLayer != null)
        {
            foreach (var pos in rampLayer.cellBounds.allPositionsWithin)
                if (rampLayer.HasTile(pos)) rampCells.Add(pos);
        }

        // --- folded level lookup (one dictionary hit per query) ---
        foreach (var kv in levels) levelOf[kv.Key] = kv.Value;
        foreach (var kv in rampOf) levelOf[kv.Key] = kv.Value.lowLevel;
        foreach (var c in rampCells)
        {
            if (levelOf.ContainsKey(c)) continue;
            int lowest = int.MaxValue;
            foreach (var d in Dirs)
                if (levels.TryGetValue(c + d, out var lv) && lv < lowest) lowest = lv;
            levelOf[c] = lowest == int.MaxValue ? 0 : lowest;
        }

        LabelRegions();
        BuildReachCache();
        scanned = true;
        RegionVersion++;
    }

    static void Include(Tilemap t, ref BoundsInt b)
    {
        if (t == null) return;
        var cb = t.cellBounds;
        b.xMin = Mathf.Min(b.xMin, cb.xMin); b.xMax = Mathf.Max(b.xMax, cb.xMax);
        b.yMin = Mathf.Min(b.yMin, cb.yMin); b.yMax = Mathf.Max(b.yMax, cb.yMax);
    }

    /// <summary>Flood fill every walkable cell into a connected region using exactly
    /// the same rule as <see cref="CanEnter"/>, so region equality is a sound
    /// reachability test — including the level/ramp rule.</summary>
    void LabelRegions()
    {
        var queue = new Queue<Vector3Int>();
        foreach (var kv in levels)
        {
            var seed = kv.Key;
            if (regionOf.ContainsKey(seed) || !WalkableRaw(seed)) continue;
            FloodFill(seed, queue);
        }
        foreach (var seed in rampCells)
        {
            if (regionOf.ContainsKey(seed)) continue;
            FloodFill(seed, queue);
        }
    }

    void FloodFill(Vector3Int seed, Queue<Vector3Int> queue)
    {
        int id = regionCount++;
        regionOf[seed] = id;
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var d in Dirs)
            {
                var n = cur + d;
                if (regionOf.ContainsKey(n) || !WalkableRaw(n)) continue;
                if (!CanEnter(cur, n)) continue;
                if (d.x != 0 && d.y != 0)
                {
                    if (!CanEnter(cur, cur + new Vector3Int(d.x, 0, 0)) ||
                        !CanEnter(cur, cur + new Vector3Int(0, d.y, 0))) continue;
                }
                regionOf[n] = id;
                queue.Enqueue(n);
            }
        }
    }

    void EnsureScanned() { if (!scanned) Scan(); }

    bool WalkableRaw(Vector3Int c)
    {
        // A building wins over everything, including a ramp underneath it.
        if (dynamicBlockers.ContainsKey(c)) return false;
        if (rampCells.Contains(c)) return true;
        return levels.ContainsKey(c) && !blockedCells.Contains(c);
    }

    /// <summary>Terrain-only test: ignores buildings. Used by placement previews,
    /// which have to ask "is this ground buildable" about cells their own ghost
    /// is not occupying yet.</summary>
    public bool TerrainWalkable(Vector3Int c)
    {
        EnsureScanned();
        if (rampCells.Contains(c)) return true;
        return levels.ContainsKey(c) && !blockedCells.Contains(c);
    }

    public bool Walkable(Vector3Int c)
    {
        EnsureScanned();
        return WalkableRaw(c);
    }

    /// <summary>Level without the lazy-scan check. Scan() itself calls into
    /// CanEnter, which needs the level — going through the public property here
    /// would recurse straight back into Scan().</summary>
    int LevelRaw(Vector3Int c)
    {
        return levelOf.TryGetValue(c, out var lv) ? lv : -1;
    }

    public int Level(Vector3Int c)
    {
        EnsureScanned();
        return LevelRaw(c);
    }

    public bool IsRamp(Vector3Int c)
    {
        EnsureScanned();
        return rampCells.Contains(c);
    }

    /// <summary>Legacy level-based sort order. Superseded by <see cref="YSort"/>,
    /// kept so existing references keep resolving.</summary>
    public int SortOrder(Vector3Int c)
    {
        EnsureScanned();
        int lv = levelOf.TryGetValue(c, out var v) ? v : 0;
        return 3 + (rampCells.Contains(c) ? lv + 1 : lv);
    }

    // ---------------------------------------------------------------- regions

    /// <summary>Connected-region id, or -1 if the cell is not walkable.</summary>
    public int Region(Vector3Int c)
    {
        EnsureScanned();
        return regionOf.TryGetValue(c, out var id) ? id : -1;
    }

    public int RegionCount { get { EnsureScanned(); return regionCount; } }

    /// <summary>Monotonic version of the connected-region labelling. Bumped whenever
    /// terrain is rescanned or building footprints change connectivity, so units can
    /// retry a temporarily unreachable target without probing with an A* every frame.</summary>
    public int RegionVersion { get; private set; }

    /// <summary>True when a unit can actually walk from a to b. O(1).</summary>
    public bool SameRegion(Vector3Int a, Vector3Int b)
    {
        EnsureScanned();
        int ra = Region(a), rb = Region(b);
        return ra >= 0 && ra == rb;
    }

    /// <summary>Cells in this region — the landmass a unit spawns on.</summary>
    public int RegionSize(int region)
    {
        EnsureScanned();
        int n = 0;
        foreach (var kv in regionOf) if (kv.Value == region) n++;
        return n;
    }

    // ------------------------------------------------- dynamic blockers

    /// <summary>True if a building (not terrain) is occupying this cell.</summary>
    public bool IsBlockedByBuilding(Vector3Int c)
    {
        EnsureScanned();
        return dynamicBlockers.ContainsKey(c);
    }

    public void AddBuildingFootprint(IEnumerable<Vector3Int> cells)
    {
        EnsureScanned();
        bool changed = false;
        foreach (var c in cells)
        {
            dynamicBlockers.TryGetValue(c, out int count);
            dynamicBlockers[c] = count + 1;
            if (count == 0) changed = true;       // only 0 -> 1 changes connectivity
        }
        if (changed) RelabelRegions();
    }

    public void RemoveBuildingFootprint(IEnumerable<Vector3Int> cells)
    {
        EnsureScanned();
        bool changed = false;
        foreach (var c in cells)
        {
            if (!dynamicBlockers.TryGetValue(c, out int count)) continue;
            if (count > 1) dynamicBlockers[c] = count - 1;
            else
            {
                dynamicBlockers.Remove(c);        // only 1 -> 0 frees the cell
                changed = true;
            }
        }
        if (changed) RelabelRegions();
    }

    /// <summary>Adjust the refcounted blocker counts for a batch of cells and relabel the
    /// regions <em>once</em> at the end. Equivalent to the matching sequence of
    /// <see cref="RemoveBuildingFootprint"/> / <see cref="AddBuildingFootprint"/> calls,
    /// but costs one relabel instead of one per call. Returns true when any cell crossed
    /// 0 -> 1 or 1 -> 0 (i.e. when a relabel was actually needed).</summary>
    public bool ApplyBlockingDelta(IReadOnlyList<Vector3Int> remove, IReadOnlyList<Vector3Int> add)
    {
        EnsureScanned();
        bool changed = ApplyBlockingDeltaNoRelabel(remove, add);
        if (changed) RelabelRegions();
        return changed;
    }

    /// <summary>Same as <see cref="ApplyBlockingDelta"/> but leaves the region cache
    /// stale. Only for callers that mutate, query reachability with
    /// <see cref="IsReachable"/> (which does not read the cached regions), and then revert
    /// inside the same call stack. Never call <see cref="Region"/> or
    /// <see cref="RegionSize"/> between the apply and the revert.
    ///
    /// The mutation itself is exactly the sequential remove-then-add, including the
    /// clamping rules of the individual calls. The returned flag compares each touched
    /// cell's blocked/unblocked state before and after the batch, so a delta that nets
    /// to zero reports false and the caller can skip the relabel.</summary>
    public bool ApplyBlockingDeltaNoRelabel(IReadOnlyList<Vector3Int> remove, IReadOnlyList<Vector3Int> add)
    {
        EnsureScanned();

        deltaCells.Clear();
        deltaBefore.Clear();
        RecordBefore(remove);
        RecordBefore(add);

        if (remove != null)
        {
            for (int i = 0; i < remove.Count; i++)
            {
                var c = remove[i];
                if (!dynamicBlockers.TryGetValue(c, out int count)) continue;
                if (count > 1) dynamicBlockers[c] = count - 1;
                else dynamicBlockers.Remove(c);
            }
        }
        if (add != null)
        {
            for (int i = 0; i < add.Count; i++)
            {
                var c = add[i];
                dynamicBlockers.TryGetValue(c, out int count);
                dynamicBlockers[c] = count + 1;
            }
        }

        bool changed = false;
        for (int i = 0; i < deltaCells.Count; i++)
        {
            bool wasBlocked = deltaBefore[i] > 0;
            bool isBlocked = dynamicBlockers.TryGetValue(deltaCells[i], out int now) && now > 0;
            if (wasBlocked != isBlocked) changed = true;
        }
        return changed;
    }

    /// <summary>Remembers a touched cell's pre-batch refcount once, for the net-change test.</summary>
    void RecordBefore(IReadOnlyList<Vector3Int> cells)
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            bool seen = false;
            for (int j = 0; j < deltaCells.Count; j++)
                if (deltaCells[j] == c) { seen = true; break; }
            if (seen) continue;
            deltaCells.Add(c);
            dynamicBlockers.TryGetValue(c, out int count);
            deltaBefore.Add(count);
        }
    }

    /// <summary>Read-only reachability between two cells using exactly the adjacency rule
    /// region labelling uses (8 directions, no corner cutting, a ramp for a height change).
    /// It reads <see cref="CanEnter"/> directly and never touches the region cache, so it
    /// is safe to call while the dynamic blockers hold a simulated state.</summary>
    public bool IsReachable(Vector3Int from, Vector3Int to)
    {
        EnsureScanned();
        if (!Walkable(from) || !Walkable(to)) return false;
        if (from == to) return true;
        var reached = new HashSet<Vector3Int>();
        CollectReachable(from, reached);
        return reached.Contains(to);
    }

    /// <summary>One flood fill from <paramref name="from"/> into <paramref name="into"/>,
    /// using the same adjacency rule as region labelling. Read-only: never touches the
    /// region cache. Callers can then test many targets (army starts, approach cells)
    /// against the one reached set.
    ///
    /// Uses the cached flat index space when it exists (the normal case), falling back
    /// to the dictionary-based fill for a start outside the scanned bounds. The two
    /// paths produce the same set; this is the method the connectivity gate leans on,
    /// so it is deliberately allocation-light and dictionary-free in the inner loop.</summary>
    public void CollectReachable(Vector3Int from, HashSet<Vector3Int> into)
    {
        EnsureScanned();
        if (!Walkable(from)) return;
        if (!into.Add(from)) return;

        int width = reachBounds.size.x;
        int height = reachBounds.size.y;
        if (!reachCacheReady || width <= 0 || height <= 0)
        {
            CollectReachableSlow(from, into);
            return;
        }

        int fx = from.x - reachBounds.xMin;
        int fy = from.y - reachBounds.yMin;
        if (fx < 0 || fy < 0 || fx >= width || fy >= height)
        {
            CollectReachableSlow(from, into);
            return;
        }

        int count = width * height;
        var walk = new bool[count];
        System.Array.Copy(reachTerrainWalk, walk, count);
        foreach (var kv in dynamicBlockers)
        {
            int bx = kv.Key.x - reachBounds.xMin;
            int by = kv.Key.y - reachBounds.yMin;
            if (bx < 0 || by < 0 || bx >= width || by >= height) continue;
            walk[by * width + bx] = false;
        }

        var visited = new bool[count];
        var queue = new Queue<int>();
        int start = fy * width + fx;
        visited[start] = true;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % width;
            int cy = cur / width;
            for (int i = 0; i < Dirs.Length; i++)
            {
                var d = Dirs[i];
                int nx = cx + d.x;
                int ny = cy + d.y;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int next = ny * width + nx;
                if (visited[next] || !walk[next] || !ReachEnterable(cur, next, walk)) continue;

                if (d.x != 0 && d.y != 0)
                {
                    int sideX = cy * width + nx;   // (cx + d.x, cy)
                    int sideY = ny * width + cx;   // (cx, cy + d.y)
                    if (!ReachEnterable(cur, sideX, walk) || !ReachEnterable(cur, sideY, walk))
                        continue;
                }

                visited[next] = true;
                queue.Enqueue(next);
                into.Add(new Vector3Int(reachBounds.xMin + nx, reachBounds.yMin + ny, 0));
            }
        }
    }

    /// <summary>Same entry rule as <see cref="CanEnter"/>, on the flat index space.</summary>
    bool ReachEnterable(int fromIndex, int toIndex, bool[] walk)
    {
        if (!walk[fromIndex] || !walk[toIndex]) return false;
        return reachLevel[fromIndex] == reachLevel[toIndex] ||
               reachRamp[fromIndex] || reachRamp[toIndex];
    }

    /// <summary>Dictionary path used only when the start is outside the cached bounds
    /// (or the cache is unavailable). Same rule as the base <see cref="CanEnter"/>.</summary>
    void CollectReachableSlow(Vector3Int from, HashSet<Vector3Int> into)
    {
        var queue = new Queue<Vector3Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            for (int i = 0; i < Dirs.Length; i++)
            {
                var d = Dirs[i];
                var n = cur + d;
                if (!Walkable(n) || !CanEnter(cur, n)) continue;
                if (d.x != 0 && d.y != 0)
                {
                    if (!CanEnter(cur, new Vector3Int(cur.x + d.x, cur.y, 0)) ||
                        !CanEnter(cur, new Vector3Int(cur.x, cur.y + d.y, 0))) continue;
                }
                if (into.Add(n)) queue.Enqueue(n);
            }
        }
    }

    /// <summary>Rebuilds the flat index space used by <see cref="CollectReachable"/>.
    /// Terrain-only: dynamic blockers are overlaid per call, so a building going up or
    /// down does not invalidate this cache.</summary>
    void BuildReachCache()
    {
        var bounds = new BoundsInt();
        bool any = false;
        IncludeReachBounds(ground0, ref bounds, ref any);
        IncludeReachBounds(top1, ref bounds, ref any);
        IncludeReachBounds(top2, ref bounds, ref any);
        IncludeReachBounds(cliff1, ref bounds, ref any);
        IncludeReachBounds(cliff2, ref bounds, ref any);
        IncludeReachBounds(rampLayer, ref bounds, ref any);

        int width = bounds.size.x;
        int height = bounds.size.y;
        reachCacheReady = any && width > 0 && height > 0;
        if (!reachCacheReady)
        {
            reachTerrainWalk = null;
            reachLevel = null;
            reachRamp = null;
            return;
        }

        reachBounds = bounds;
        reachTerrainWalk = new bool[width * height];
        reachLevel = new int[width * height];
        reachRamp = new bool[width * height];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var cell = new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0);
            int i = y * width + x;
            bool ramp = rampCells.Contains(cell);
            reachTerrainWalk[i] = ramp || (levels.ContainsKey(cell) && !blockedCells.Contains(cell));
            reachLevel[i] = LevelRaw(cell);
            reachRamp[i] = ramp;
        }
    }

    static void IncludeReachBounds(Tilemap tilemap, ref BoundsInt bounds, ref bool any)
    {
        if (tilemap == null) return;
        var cb = tilemap.cellBounds;
        if (cb.size.x <= 0 || cb.size.y <= 0) return;
        if (!any)
        {
            bounds = new BoundsInt(cb.xMin, cb.yMin, 0, cb.size.x, cb.size.y, 1);
            any = true;
            return;
        }
        int xMin = Mathf.Min(bounds.xMin, cb.xMin);
        int yMin = Mathf.Min(bounds.yMin, cb.yMin);
        int xMax = Mathf.Max(bounds.xMax, cb.xMax);
        int yMax = Mathf.Max(bounds.yMax, cb.yMax);
        bounds = new BoundsInt(xMin, yMin, 0, xMax - xMin, yMax - yMin, 1);
    }

    /// <summary>Re-run only the connected-region labelling — the terrain scan is
    /// unaffected by buildings, so there is no need to redo it.</summary>
    public void RelabelRegions()
    {
        EnsureScanned();
        regionOf.Clear();
        regionCount = 0;
        LabelRegions();
        RegionVersion++;
    }

    // ------------------------------------------------------------ conversion

    public Vector3Int WorldToCell(Vector3 w) { return ground0.WorldToCell(w); }

    public Vector3 CellCenter(Vector3Int c)
    {
        return ground0.CellToWorld(c) + new Vector3(0.5f, 0.5f, 0f);
    }

    // ------------------------------------------------------------ neighbour

    /// <summary>
    /// Resolves a pathfinding start for a unit that may have ended up inside a
    /// building footprint. A blocked unit walks to the returned neighbouring cell
    /// as the first path waypoint; it is never teleported.
    ///
    /// Only cardinal neighbours are considered, using the same level/ramp rule as
    /// normal movement. A sealed unit therefore fails safely instead of escaping
    /// across water or to an unrelated landmass.
    /// </summary>
    public Vector3Int? ResolveWalkableStart(Vector3Int start)
    {
        EnsureScanned();
        if (WalkableRaw(start)) return start;

        for (int i = 0; i < 4; i++)
        {
            var n = start + Dirs[i];
            if (!WalkableRaw(n)) continue;
            int fromLevel = LevelRaw(start);
            int toLevel = LevelRaw(n);
            if (fromLevel != toLevel && !rampCells.Contains(start) && !rampCells.Contains(n))
                continue;
            return n;
        }
        return null;
    }

    public Vector3Int? NearestWalkable(Vector3Int c, int maxRadius)
    {
        return NearestWalkableInRegion(c, maxRadius, -2);
    }

    /// <summary>Nearest walkable cell, optionally restricted to one region.
    /// Pass region = -2 for "any region". Cells in <paramref name="taken"/> are
    /// skipped, so a group order does not collapse several units onto one tile.</summary>
    public Vector3Int? NearestWalkableInRegion(Vector3Int c, int maxRadius, int region,
                                               HashSet<Vector3Int> taken = null)
    {
        EnsureScanned();
        if (WalkableRaw(c) && (region == -2 || Region(c) == region) &&
            (taken == null || !taken.Contains(c))) return c;
        for (int r = 1; r <= maxRadius; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
            var n = new Vector3Int(c.x + dx, c.y + dy, 0);
            if (!WalkableRaw(n)) continue;
            if (region != -2 && Region(n) != region) continue;
            if (taken != null && taken.Contains(n)) continue;
            return n;
        }
        return null;
    }

    /// <summary>Walkable cell within <paramref name="maxRadius"/> of
    /// <paramref name="around"/> that is closest to <paramref name="from"/>.
    ///
    /// Use this when approaching a multi-tile building. <see cref="NearestWalkableInRegion"/>
    /// returns the first hit in ring order, which for a 5-tile-wide building can be a tile
    /// on the far side — sending the unit on a long walk all the way around it.</summary>
    public Vector3Int? NearestWalkableTo(Vector3Int from, Vector3Int around, int maxRadius, int region,
                                         HashSet<Vector3Int> taken = null)
    {
        EnsureScanned();
        Vector3Int? best = null;
        float bestD = float.MaxValue;
        for (int r = 0; r <= maxRadius; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (r > 0 && Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
            var n = new Vector3Int(around.x + dx, around.y + dy, 0);
            if (!WalkableRaw(n)) continue;
            if (region != -2 && Region(n) != region) continue;
            if (taken != null && taken.Contains(n)) continue;
            int ddx = n.x - from.x, ddy = n.y - from.y;
            float d = ddx * ddx + ddy * ddy;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    /// <summary>
    /// A walkable cell within maxRadius of around that the actor can reach, scored by
    /// distance to the actor so a wide building never hands back its far side.
    ///
    /// actionRadius caps how far the chosen cell may sit from the target in world units.
    /// Without it, whenever the actor is already inside maxRadius, the actor's OWN cell
    /// wins the "closest to the actor" contest — so a worker harvests from three cells
    /// away and a melee fighter "arrives" at its own cell and never closes in.
    /// </summary>
    public Vector3Int? NearestWalkableWithin(Vector3Int from, Vector3Int around, int maxRadius, int region,
                                             float actionRadius, HashSet<Vector3Int> taken = null)
    {
        EnsureScanned();
        Vector3 target = CellCenter(around);
        Vector3Int? best = null;
        float bestD = float.MaxValue;
        for (int r = 0; r <= maxRadius; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (r > 0 && Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
            var n = new Vector3Int(around.x + dx, around.y + dy, 0);
            if (!WalkableRaw(n)) continue;
            if (region != -2 && Region(n) != region) continue;
            if (taken != null && taken.Contains(n)) continue;
            // the whole point: the destination has to be within reach of the target
            if (Vector2.Distance(CellCenter(n), target) > actionRadius) continue;
            int ddx = n.x - from.x, ddy = n.y - from.y;
            float d = ddx * ddx + ddy * ddy;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    // ------------------------------------------------------------ pathfinding

    // Binary min-heap with lazy deletion: stale entries are skipped on pop.
    float[] heapPrio = new float[2048];
    Vector3Int[] heapCell = new Vector3Int[2048];
    int heapCount;

    void HeapPush(float p, Vector3Int c)
    {
        if (heapCount == heapPrio.Length)
        {
            System.Array.Resize(ref heapPrio, heapCount * 2);
            System.Array.Resize(ref heapCell, heapCount * 2);
        }
        int i = heapCount++;
        heapPrio[i] = p; heapCell[i] = c;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (heapPrio[parent] <= heapPrio[i]) break;
            HeapSwap(parent, i); i = parent;
        }
    }

    Vector3Int HeapPop()
    {
        var top = heapCell[0];
        heapCount--;
        if (heapCount > 0)
        {
            heapCell[0] = heapCell[heapCount]; heapPrio[0] = heapPrio[heapCount];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < heapCount && heapPrio[l] < heapPrio[m]) m = l;
                if (r < heapCount && heapPrio[r] < heapPrio[m]) m = r;
                if (m == i) break;
                HeapSwap(m, i); i = m;
            }
        }
        return top;
    }

    void HeapSwap(int a, int b)
    {
        float fp = heapPrio[a]; heapPrio[a] = heapPrio[b]; heapPrio[b] = fp;
        var fc = heapCell[a]; heapCell[a] = heapCell[b]; heapCell[b] = fc;
    }

    /// <summary>Cliff tiles block, and a height difference may only be crossed
    /// on a ramp.</summary>
    bool CanEnter(Vector3Int from, Vector3Int to)
    {
        if (!WalkableRaw(from) || !WalkableRaw(to)) return false;
        int lf = LevelRaw(from), lt = LevelRaw(to);
        if (lf == lt) return true;
        return rampCells.Contains(from) || rampCells.Contains(to);
    }

    /// <summary>
    /// A* from start to goal. Returns null when the order is impossible; check
    /// <see cref="SameRegion"/> first if you want to know that cheaply.
    /// </summary>
    public List<Vector3Int> FindPath(Vector3Int start, Vector3Int goal)
    {
        EnsureScanned();
        if (!WalkableRaw(start)) return null;

        if (!WalkableRaw(goal))
        {
            var g = NearestWalkableInRegion(goal, 4, Region(start));
            if (!g.HasValue) return null;
            goal = g.Value;
        }
        // Islands and separate landmasses: reject before searching anything.
        if (Region(start) != Region(goal)) return null;

        var came = new Dictionary<Vector3Int, Vector3Int>();
        var gScore = new Dictionary<Vector3Int, float> { [start] = 0f };
        var closed = new HashSet<Vector3Int>();

        heapCount = 0;
        HeapPush(Heuristic(start, goal), start);

        while (heapCount > 0)
        {
            var cur = HeapPop();
            if (!closed.Add(cur)) continue;   // stale heap entry
            if (cur == goal)
            {
                var path = new List<Vector3Int> { cur };
                while (came.TryGetValue(cur, out var prev)) { cur = prev; path.Add(cur); }
                path.Reverse();
                return path;
            }
            float gCur = gScore[cur];
            foreach (var d in Dirs)
            {
                var n = cur + d;
                if (closed.Contains(n) || !CanEnter(cur, n)) continue;
                bool diag = d.x != 0 && d.y != 0;
                if (diag) // no corner cutting
                {
                    if (!CanEnter(cur, cur + new Vector3Int(d.x, 0, 0)) ||
                        !CanEnter(cur, cur + new Vector3Int(0, d.y, 0))) continue;
                }
                float ng = gCur + (diag ? 1.4142f : 1f);
                if (!gScore.TryGetValue(n, out var old) || ng < old)
                {
                    gScore[n] = ng;
                    came[n] = cur;
                    HeapPush(ng + Heuristic(n, goal), n);
                }
            }
        }
        return null;
    }

    static float Heuristic(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
        return Mathf.Max(dx, dy) + 0.4142f * Mathf.Min(dx, dy);
    }

    // ---------------------------------------------------------------- gizmos

    void OnDrawGizmos()
    {
        if (!drawGizmos || ground0 == null) return;
        EnsureScanned();
        foreach (var kv in levels)
        {
            var c = kv.Key;
            Vector3 p = CellCenter(c);
            if (blockedCells.Contains(c) && !rampCells.Contains(c))
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.30f);
                Gizmos.DrawCube(p, new Vector3(0.9f, 0.9f, 0.1f));
            }
            else
            {
                Gizmos.color = colourByRegion ? RegionColour(Region(c), kv.Value) : LevelColour(kv.Value);
                Gizmos.DrawCube(p, new Vector3(0.85f, 0.85f, 0.1f));
            }
        }
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.95f);
        foreach (var c in rampCells)
            Gizmos.DrawCube(CellCenter(c), new Vector3(0.5f, 0.5f, 0.2f));

        // building footprints (runtime only — buildings register when the game runs)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.55f);
        foreach (var c in dynamicBlockers.Keys)
            Gizmos.DrawCube(CellCenter(c), new Vector3(0.8f, 0.8f, 0.15f));
    }

    static Color LevelColour(int lv)
    {
        return lv == 0 ? new Color(0f, 1f, 0f, 0.15f)
             : lv == 1 ? new Color(0f, 0.8f, 1f, 0.22f)
             : new Color(0.3f, 0.3f, 1f, 0.28f);
    }

    /// <summary>Hue-rotate per region so islands read as clearly separate blobs.
    /// Every region gets the same alpha — which one is "the mainland" is not
    /// knowable here, and uniform alpha makes the split obvious at a glance.</summary>
    static Color RegionColour(int region, int level)
    {
        if (region < 0) return LevelColour(level);
        float h = Mathf.Repeat(region * 0.618034f, 1f);          // golden-ratio spread
        var c = Color.HSVToRGB(h, 0.9f, 1f);
        c.a = 0.40f;
        return c;
    }
}
