using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Ghost-preview building placement: the ghost snaps to the tile grid, tints green
/// or red depending on whether the ground is buildable, and drops the real prefab
/// on left click. Right click or Escape cancels.
///
/// Snapping puts the footprint on whole tiles, which means the origin does NOT land
/// on a cell centre when the building is an even number of tiles wide — that is what
/// keeps a 2-tile house covering exactly two tiles instead of straddling three.
/// </summary>
public class BuildingPlacer : MonoBehaviour
{
    public static BuildingPlacer Instance { get; private set; }

    [Header("调试用：运行中按 1..9 直接进入对应预制体的摆放模式")]
    public GameObject[] quickBuild = new GameObject[0];

    [Header("预览外观")]
    public Color validTint = new Color(0.45f, 1f, 0.45f, 0.72f);
    public Color invalidTint = new Color(1f, 0.32f, 0.32f, 0.72f);
    public int ghostSortingOrder = 5000;

    [Header("按键")]
    public KeyCode cancelKey = KeyCode.Escape;

    public bool IsPlacing => source != null && ghost != null;

    /// <summary>Set when the last attempted placement was refused — handy for a HUD message.</summary>
    public string LastRefusalReason { get; private set; }

    /// <summary>Frame of the latest cancel, used to consume that frame's world input.</summary>
    public int LastCancelFrame { get; private set; } = -1;

    GameObject source;
    GameObject ghost;
    Building ghostBuilding;
    Camera cam;
    SpriteRenderer[] ghostRenderers;
    readonly List<Vector3Int> ghostCells = new List<Vector3Int>();
    bool lastValid;
    Vector3Int lastCheckedCell = new Vector3Int(int.MinValue, int.MinValue, 0);

    void Awake()
    {
        Instance = this;
        cam = Camera.main;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (ghost != null) Destroy(ghost);
    }

    // ------------------------------------------------------------------ API

    /// <summary>Start placing this prefab. Safe to call from a build menu.</summary>
    public void Begin(GameObject prefab)
    {
        if (prefab == null) return;
        Cancel();

        // The build ghost owns the mouse while it is up — an armed attack order left
        // over from the selection panel would otherwise linger underneath and spring
        // back to life the moment the ghost is cancelled.
        var sm = FindFirstObjectByType<SelectionManager>();
        if (sm != null) sm.DisarmAttack();

        source = prefab;
        ghost = Instantiate(prefab);
        ghost.name = "__Ghost_" + prefab.name;

        ghostBuilding = ghost.GetComponent<Building>();
        if (ghostBuilding == null)
        {
            Debug.LogWarning("[BuildingPlacer] " + prefab.name + " has no Building component; using a 1x1 footprint.");
            ghostBuilding = ghost.AddComponent<Building>();
        }
        // Instantiate runs Awake/OnEnable before this code gets control, so the copy may
        // already have joined Building.All and reserved cells. MakeGhost explicitly rolls
        // back all of those real-building side effects while retaining command-centre flags
        // needed by Evaluate's one-base rule.
        ghostBuilding.MakeGhost();
        ghostBuilding.Prepare();

        // A ghost is a full copy of the prefab, so every behaviour on it is live. Anything
        // that scans by component instead of through Building.All — the unit shop's factory
        // list, the AI's production loop, the resource finder — would otherwise treat the
        // preview as a finished building. Shutting the whole set down here is one fix for
        // the whole class, instead of remembering to filter ghosts at every call site.
        foreach (var c in ghost.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
        foreach (var a in ghost.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb is Building) continue;   // Building stays on for the footprint check
            mb.enabled = false;
        }
        // disabling a component does not call OnDisable, so the registry entry is still there
        var ghostNode = ghost.GetComponent<ResourceNode>();
        if (ghostNode != null) ResourceNode.All.Remove(ghostNode);

        ghostRenderers = ghost.GetComponentsInChildren<SpriteRenderer>();
        foreach (var r in ghostRenderers) r.sortingOrder = ghostSortingOrder;

        ghostCells.Clear();
        for (int i = 0; i < ghostBuilding.Width; i++)
            ghostCells.Add(new Vector3Int(i, 0, 0));

        lastCheckedCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        LastRefusalReason = null;
    }

    public void Cancel()
    {
        LastCancelFrame = Time.frameCount;
        if (ghost != null) { Destroy(ghost); ghost = null; }
        source = null;
        ghostBuilding = null;
        ghostRenderers = null;
        ghostCells.Clear();
    }

    // --------------------------------------------------------------- runtime

    void Update()
    {
        // timeScale = 0 does not stop Update or input: without this gate a paused
        // player could still place buildings and spend resources.
        if (PauseMenu.IsAnyOpen) return;

        HandleQuickKeys();

        if (!IsPlacing) return;

        if (Input.GetKeyDown(cancelKey))
        {
            Cancel();
            return;
        }
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 mw = cam.ScreenToWorldPoint(Input.mousePosition);
        mw.z = 0f;
        SnapGhostTo(mw);
        RefreshValidation();

        if (Input.GetMouseButtonDown(1)) { Cancel(); return; }

        if (Input.GetMouseButtonDown(0) && lastValid) Place();
    }

    /// <summary>Drop the current prefab at a world point as if the mouse were there.
    /// Returns false and fills <see cref="LastRefusalReason"/> when the ground is not
    /// buildable — this is the entry point an AI or a scripted layout would use.</summary>
    public bool TryPlaceAt(Vector3 worldPos)
    {
        if (!IsPlacing) return false;
        worldPos.z = 0f;
        SnapGhostTo(worldPos);
        lastCheckedCell = new Vector3Int(int.MinValue, int.MinValue, 0);   // force a re-check
        RefreshValidation();
        return Place();
    }

    /// <summary>Re-tests the ghost's current cells and tints it accordingly. Skipped
    /// when the ghost has not changed cell, so the unit scan is not per-frame.</summary>
    void RefreshValidation()
    {
        Vector3Int cell = MapGrid.Instance != null
            ? MapGrid.Instance.WorldToCell(ghost.transform.position)
            : new Vector3Int(int.MinValue, int.MinValue, 0);
        if (cell == lastCheckedCell) return;
        lastCheckedCell = cell;
        lastValid = Evaluate(ghostBuilding, ghostCells, out var reason);
        LastRefusalReason = reason;
        var tint = lastValid ? validTint : invalidTint;
        foreach (var r in ghostRenderers) if (r != null) r.color = tint;
    }

    public bool TryQuickBuild(int index)
    {
        if (quickBuild == null || index < 0 || index >= quickBuild.Length) return false;
        if (quickBuild[index] == null) return false;
        Begin(quickBuild[index]);
        return true;
    }

    void HandleQuickKeys()
    {
        if (quickBuild == null) return;
        for (int i = 0; i < quickBuild.Length && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) && TryQuickBuild(i)) return;
        }
    }

    /// <summary>Puts the footprint on whole tiles. The origin ends up on a cell centre
    /// only when the building is an odd number of tiles wide.</summary>
    void SnapGhostTo(Vector3 mouseWorld)
    {
        int w = Mathf.Max(1, ghostBuilding.Width);
        float px = Mathf.Floor(mouseWorld.x - w / 2f) + w / 2f;
        float py = Mathf.Floor(mouseWorld.y);
        ghost.transform.position = new Vector3(px, py, 0f);

        int ax = Mathf.FloorToInt(px - w / 2f);
        int ay = Mathf.FloorToInt(py);
        int d = Mathf.Max(1, ghostBuilding.Depth);
        ghostCells.Clear();
        for (int i = 0; i < w; i++)
        for (int j = 0; j < d; j++)
            ghostCells.Add(new Vector3Int(ax + i, ay + j, 0));
    }

    bool Evaluate(Building b, List<Vector3Int> cells, out string reason)
    {
        reason = null;
        var grid = MapGrid.Instance;
        if (grid == null) { reason = "场景里没有 MapGrid"; return false; }

        if (b != null && !b.cost.IsZero && Economy.Instance != null && !Economy.Instance.CanAfford(b.cost))
        {
            reason = "资源不够（需要 " + b.cost + "）";
            return false;
        }

        // The command centre is the base, not another barracks: one per side.
        if (b != null && b.isCommandCenter && HasCommandCentre())
        {
            reason = "指挥中心只能建一座";
            return false;
        }

        if (b != null && !b.isCommandCenter &&
            !Building.HasCompletedCommandCentre(GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue))
        {
            reason = "需要先建成主基地";
            return false;
        }

        return PlacementRules.ValidateCells(grid, cells, checkUnits: true, out reason);
    }

    /// <summary>True when the player's side has a living, completed command centre.
    /// Placement ghosts and half-built bases are excluded.</summary>
    static bool HasCommandCentre()
    {
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        return Building.HasLivingCommandCentre(player);
    }

    bool Place()
    {
        if (!IsPlacing) return false;

        // The mouse path can arrive here with a cached green preview. Revalidate at
        // the commit boundary so a newly occupied cell cannot spend or instantiate.
        lastValid = Evaluate(ghostBuilding, ghostCells, out var reason);
        LastRefusalReason = reason;
        if (!lastValid) return false;

        var template = source.GetComponent<Building>();
        if (template != null && !template.cost.IsZero && Economy.Instance != null
            && !Economy.Instance.TrySpend(template.cost))
        {
            LastRefusalReason = "资源不够（需要 " + template.cost + "）";
            return false;
        }

        Vector3 pos = ghost.transform.position;
        var go = Instantiate(source, pos, Quaternion.identity);
        go.name = source.name;

        var b = go.GetComponent<Building>();
        if (b != null)
        {
            b.blocksMovement = true;
            b.MarkBuiltDuringMatch();       // it is a project, not level scenery
            b.Recompute();                 // reserve the footprint now that it is positioned
        }
        LastRefusalReason = null;
        lastCheckedCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        // stay in placement mode so a whole row can be laid down; Escape / right click exits
        return true;
    }
}
