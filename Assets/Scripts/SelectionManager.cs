using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Left-click select, drag-box multi-select, right-click move order.
/// Selection rings and the "blocked" marker are generated at runtime (no art needed).
///
/// Move orders are region aware: a right-click that no selected unit can actually
/// reach is refused loudly instead of being silently dropped.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public Camera cam;

    [Tooltip("Layers that can be clicked to select. Widen/narrow once unit/building layers exist.")]
    public LayerMask selectableLayers = ~0;

    [Tooltip("Show a red X where a move order could not be carried out.")]
    public bool showBlockedMarker = true;

    public readonly List<UnitMover> selected = new List<UnitMover>();

    /// <summary>Ctrl+1..9 stores the current unit selection, a bare 1..9 recalls it.
    /// Session state only: it is deliberately not saved, because a load rebuilds every
    /// unit and the stored references would all be dead objects.</summary>
    readonly Dictionary<int, List<UnitMover>> controlGroups = new Dictionary<int, List<UnitMover>>();

    /// <summary>Set when a building (rather than a unit) is under the cursor. The HUD
    /// reads this to show that building's production buttons.</summary>
    public Building selectedBuilding;

    /// <summary>Attack-command mode (the HUD attack button): the next left-click is an
    /// attack order — an enemy gets focused, open ground becomes an attack-move.
    /// Right-click or ESC cancels. Mirrored by CursorManager, which shows the crossed
    /// swords while this is on.</summary>
    public bool AttackArmed { get; private set; }

    /// <summary>Time.frameCount of the last disarm. PauseMenu reads this so one ESC
    /// never both cancels the attack order and opens the pause menu on the same press.</summary>
    public int LastDisarmFrame { get; private set; } = -1;

    /// <summary>Arm the attack command. Refused (stays a no-op) when nothing in the
    /// current selection can actually fight — arming a box of workers would swallow
    /// their next click and produce nothing.</summary>
    public void ArmAttack()
    {
        if (!HasCombatSelection()) return;

        // Clicking the attack button while a building ghost is up used to be a silent
        // no-op (the ghost owns the mouse). The player's intent is clear: leave
        // placement and aim an attack. Cancel the ghost first.
        if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing)
            BuildingPlacer.Instance.Cancel();

        AttackArmed = true;
        attackArmedFrame = Time.frameCount;
        attackPressStartedOnWorld = false;
        dragging = false;
        leftPressStartedOnWorld = false;
    }

    public void DisarmAttack()
    {
        if (!AttackArmed) return;
        AttackArmed = false;
        LastDisarmFrame = Time.frameCount;
    }

    /// <summary>The armed frame's input as parameters — same pattern as
    /// <see cref="HandleRightClickInput"/>, so regression tests drive the exact code
    /// Update runs without faking OS input. Returns true while the mode owns the frame
    /// (armed mode swallows world clicks even when nothing was clicked yet).</summary>
    public bool HandleAttackArmedInput(Vector3 mw, bool confirmClick, bool cancel)
    {
        if (!AttackArmed) return false;
        if (cancel)
        {
            DisarmAttack();
            return true;
        }
        if (confirmClick)
        {
            // Enemies take priority over everything else under the cursor — an
            // attack click next to a gold mine must attack, not gather.
            var enemy = Combatants.FindAt(mw, SelectionTeam());
            if (enemy == null || !OrderAttack(enemy)) IssueGroundMove(mw, attackMove: true);
            DisarmAttack();
        }
        return true;
    }

    public bool HasCombatSelection()
    {
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (!IsLiving(u)) continue;
            var a = u.GetComponent<UnitActor>();
            if (a != null && a.isCombatant) return true;
        }
        return false;
    }

    Sprite ringSprite;
    Sprite blockedSprite;
    Texture2D white;
    readonly List<GameObject> rings = new List<GameObject>();
    readonly List<AttackRangeIndicator> rangeIndicators = new List<AttackRangeIndicator>();
    readonly List<Marker> markers = new List<Marker>();
    readonly List<Collider2D> overlapBuf = new List<Collider2D>(32);
    ContactFilter2D selectFilter;
    Vector2 dragStart;
    bool dragging;
    bool leftPressStartedOnWorld;
    bool attackPressStartedOnWorld;
    int attackArmedFrame = -1;

    const float MarkerLifetime = 0.7f;
    const float DragThreshold = 8f;

    static readonly Color FriendlyRangeTint = new Color(0.35f, 0.65f, 1f, 0.85f);
    static readonly Color EnemyRangeTint = new Color(1f, 0.35f, 0.35f, 0.85f);
    static readonly Color NeutralRangeTint = new Color(1f, 0.82f, 0.25f, 0.85f);

    class Marker
    {
        public GameObject go;
        public SpriteRenderer sr;
        public float timer;
    }

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        ringSprite = MakeRingSprite();
        blockedSprite = MakeXSprite();
        white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        white.SetPixel(0, 0, Color.white);
        white.Apply();

        selectFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = selectableLayers,
            useTriggers = false,
            useDepth = false,
        };
    }

    void OnValidate()
    {
        selectFilter.useLayerMask = true;
        selectFilter.layerMask = selectableLayers;
        selectFilter.useTriggers = false;
        selectFilter.useDepth = false;
    }

    static Sprite MakeRingSprite()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var c = new Vector2(s / 2f - 0.5f, s / 2f - 0.5f);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c);
            bool on = d >= 25f && d <= 29f;
            tex.SetPixel(x, y, on ? new Color(0.2f, 1f, 0.2f, 0.9f) : Color.clear);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 64f);
    }

    /// <summary>Red X drawn over a refused order — this is what turns a silent
    /// no-op into readable feedback.</summary>
    static Sprite MakeXSprite()
    {
        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            bool inRange = x > 3 && x < s - 4 && y > 3 && y < s - 4;
            bool diag = Mathf.Abs(x - y) <= 1 || Mathf.Abs((s - 1 - x) - y) <= 1;
            tex.SetPixel(x, y, inRange && diag ? Color.white : Color.clear);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 32f);
    }

    bool PointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    bool ConsumePlacementWorldInput()
    {
        var placer = BuildingPlacer.Instance;
        if (placer == null || (!placer.IsPlacing && placer.LastCancelFrame != Time.frameCount))
            return false;
        dragging = false;
        leftPressStartedOnWorld = false;
        UpdateRings();
        return true;
    }

    void Update()
    {
        if (cam == null) return;

        TickMarkers(Time.deltaTime);

        // The pause menu freezes timeScale, but Update and Input keep running — without
        // this gate a paused game still takes move/attack/demolish orders. Markers keep
        // ticking above; selection state simply holds until the menu closes.
        if (PauseMenu.IsAnyOpen)
        {
            dragging = false;
            leftPressStartedOnWorld = false;
            UpdateRings();
            return;
        }

        // Units can die between clicks. Prune before anything else reads the list —
        // a corpse delayed by corpseSeconds is still a non-null Unity object.
        if (selected.Count > 0) selected.RemoveAll(u => !IsLiving(u));
        if (selectedBuilding != null && !selectedBuilding.IsAlive) selectedBuilding = null;

        // While the building ghost is up it owns the mouse — otherwise a click would
        // both drop a building and re-issue move orders. The cancel frame is consumed
        // too, so the same right-click cannot become a move order after closing the ghost.
        if (ConsumePlacementWorldInput()) return;

        Vector2 screen = Input.mousePosition;
        Vector3 mw = cam.ScreenToWorldPoint(screen);
        mw.z = 0f;
        bool leftDown = Input.GetMouseButtonDown(0);
        bool leftHeld = Input.GetMouseButton(0);
        bool leftUp = Input.GetMouseButtonUp(0);
        bool overUI = PointerOverUI();

        // Keyboard shortcuts are world commands, not pointer commands. They must keep
        // working with the cursor parked on the HUD (very common at the bottom bars).
        if (!AttackArmed)
        {
            if (Input.GetKeyDown(KeyCode.Delete)) DemolishSelected();
            if (Input.GetKeyDown(KeyCode.H) && selected.Count > 0)
                OrderHold();
            if (Input.GetKeyDown(KeyCode.A)) ArmAttack();
            HandleControlGroupKeys();
        }

        // Attack-command mode owns the mouse until it is spent or cancelled. No
        // box-select, no plain select, no Delete/H shortcuts while it is up.
        if (AttackArmed)
        {
            dragging = false;
            leftPressStartedOnWorld = false;
            if (!HasCombatSelection()) DisarmAttack();      // the fighters died mid-aim

            // Only a click that both starts and ends in the world can confirm. This
            // keeps the attack button's own release (or a release after dragging off
            // the HUD) from being consumed as the attack click.
            if (leftDown && !overUI) attackPressStartedOnWorld = true;
            bool confirm = leftUp && attackPressStartedOnWorld &&
                           Time.frameCount != attackArmedFrame;
            if (leftUp) attackPressStartedOnWorld = false;

            // Cancellation must also work while the pointer is over the HUD: the
            // attack button itself is on the HUD, so the cursor is normally there
            // right after arming. Confirming with a left click over UI remains blocked.
            bool cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1);
            if (overUI)
            {
                if (cancel) DisarmAttack();
                UpdateRings();
                return;
            }

            HandleAttackArmedInput(mw, confirm, cancel);
            UpdateRings();
            return;
        }

        // UI frames still pass the real button edges through the left-mouse path so
        // a press that began on HUD cannot leave a stale world-origin press behind.
        if (overUI)
        {
            HandleLeftMouseInput(mw, screen, leftDown, leftHeld, leftUp, true);
            UpdateRings();
            return;
        }

        HandleLeftMouseInput(mw, screen, leftDown, leftHeld, leftUp, false);
        HandleRightClickInput(mw, Input.GetMouseButtonDown(1));

        UpdateRings();
    }

    // The pointer state and both coordinate spaces are parameters so deterministic
    // regression tests drive the exact same left-click path as Update, without OS input.
    public void HandleLeftMouseInput(Vector3 world, Vector2 screen,
        bool down, bool held, bool up, bool overUI)
    {
        if (down)
        {
            leftPressStartedOnWorld = !overUI;
            dragging = false;
        }

        if (overUI || !leftPressStartedOnWorld)
        {
            dragging = false;
            if (up) leftPressStartedOnWorld = false;
            return;
        }

        if (down) dragStart = screen;
        if (held && !dragging && Vector2.Distance(dragStart, screen) > DragThreshold)
            dragging = true;

        if (up)
        {
            if (dragging) BoxSelect(screen);
            else ClickSelect(world);
            dragging = false;
            leftPressStartedOnWorld = false;
        }
    }

    // The button state and world point are parameters so deterministic regression
    // coverage drives the exact same right-click path as Update, without OS input.
    void HandleRightClickInput(Vector3 mw, bool rightClickDown)
    {
        if (!rightClickDown || ConsumePlacementWorldInput()) return;
        // Shift+right-click is the patrol order: same click, different intent, and no extra
        // modal mode to steal the next click if the player changes their mind.
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (selected.Count == 0)
        {
            // Entering a tower removes its archer from the selection. An occupied tower
            // must nevertheless remain right-clickable so the same archer can come out.
            if (!shift)
            {
                var occupied = FindTowerAt(mw);
                if (occupied != null && occupied.HasArcher) TryGarrison(occupied);
            }
            return;
        }
        if (shift) IssuePatrol(mw);
        else IssueMove(mw);
    }

    void ClickSelect(Vector3 mw)
    {
        selected.Clear();
        selectedBuilding = null;
        int n = Physics2D.OverlapPoint((Vector2)mw, selectFilter, overlapBuf);

        // a unit under the cursor wins over anything else it overlaps
        for (int i = 0; i < n; i++)
        {
            var u = overlapBuf[i].GetComponentInParent<UnitMover>();
            if (u != null && Selectable(u)) { selected.Add(u); return; }
        }
        for (int i = 0; i < n; i++)
        {
            var b = overlapBuf[i].GetComponentInParent<Building>();
            if (b != null && Selectable(b)) { selectedBuilding = b; return; }
        }

        // Physics only covers the one-tile-deep movement footprint. A click on the
        // visible roof of a tall building must still select its base.
        var visualBuilding = Building.FindAtVisualPoint(mw,
            b => b.team != Team.Neutral && Selectable(b));
        if (visualBuilding != null) selectedBuilding = visualBuilding;
    }

    void BoxSelect(Vector2 releaseScreen)
    {
        selected.Clear();
        selectedBuilding = null;
        Vector2 a = dragStart, b = releaseScreen;
        Rect r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                 Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        foreach (var u in FindObjectsByType<UnitMover>(FindObjectsSortMode.None))
        {
            if (!Selectable(u)) continue;
            Vector3 sp = cam.WorldToScreenPoint(u.transform.position);
            if (r.Contains(sp)) selected.Add(u);
        }
    }

    // ------------------------------------------------------------- what may be picked

    /// <summary>Only the player's own units and buildings, plus anything unowned (trees,
    /// gold mines).
    ///
    /// Without this the player could box-select enemy troops and then order them around,
    /// and — worse — click an enemy factory. The production panel drives whatever
    /// <c>selectedBuilding</c> points at, so a red barracks would queue red units and bill
    /// the blue stockpile for them.</summary>
    static bool Selectable(UnitMover u)
    {
        if (u == null) return false;
        var a = u.GetComponent<UnitActor>();
        if (a == null) return true;              // not a faction unit, just a body
        // A disabled UnitActor is garrisoned (or otherwise out of play): it must not
        // be box-selected, click-selected, or handed orders.
        return a.IsAlive && a.enabled && Owned(a.team);
    }

    /// <summary>True while the unit may still receive orders. A corpse kept around for
    /// <c>corpseSeconds</c> is a live Unity object with a dead actor, so a plain
    /// <c>== null</c> check is not enough. A garrisoned archer keeps its GameObject
    /// active, so its disabled UnitActor must also count as not living.</summary>
    static bool IsLiving(UnitMover u)
    {
        if (u == null) return false;
        var a = u.GetComponent<UnitActor>();
        return a == null || (a.IsAlive && a.enabled);
    }

    static bool Selectable(Building b)
    {
        if (b == null || !b.IsAlive) return false;
        return Owned(b.team);
    }

    static bool Owned(Team team)
    {
        if (team == Team.Neutral) return true;
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        return team == player;
    }

    /// <summary>One offset per unit index, spiralling outwards and never repeating,
    /// so a group of any size ends up on distinct tiles.</summary>
    public static Vector2Int SpiralOffset(int index)
    {
        if (index <= 0) return Vector2Int.zero;
        int ring = Mathf.CeilToInt((Mathf.Sqrt(index + 1f) - 1f) * 0.5f);
        ring = Mathf.Max(ring, 1);
        while (index < (2 * ring - 1) * (2 * ring - 1)) ring--;      // guard against rounding
        int ringStart = (2 * ring - 1) * (2 * ring - 1);
        int t = index - ringStart;
        int side = t / (2 * ring);
        int k = t % (2 * ring);
        switch (side)
        {
            case 0:  return new Vector2Int(-ring + k, -ring);
            case 1:  return new Vector2Int(ring, -ring + k);
            case 2:  return new Vector2Int(ring - k, ring);
            default: return new Vector2Int(-ring, ring - k);
        }
    }

    public void IssueMove(Vector3 mw)
    {
        // Right-clicking a resource deposit sends any workers in the selection to
        // harvest it. If nobody can gather, fall through to a normal move order.
        var node = FindResourceNodeAt(mw);
        if (node != null && AssignGatherers(node))
        {
            // Gathering consumes the workers' part of a mixed order, not the soldiers'.
            // If an enemy shares the deposit, fighters still receive the explicit attack.
            var contestedEnemy = Combatants.FindAt(mw, SelectionTeam());
            if (contestedEnemy != null) OrderAttack(contestedEnemy);
            return;
        }

        // Right-clicking a decorative bush sends workers to clear it for building.
        var bush = FindClearableBushAt(mw);
        if (bush != null && AssignClearers(bush)) return;

        // Right-clicking a tower garrisons one archer, or returns the current archer.
        var tower = FindTowerAt(mw);
        if (tower != null && TryGarrison(tower)) return;

        // Right-clicking an enemy is an attack order for every soldier in the selection.
        var enemy = Combatants.FindAt(mw, SelectionTeam());
        if (enemy != null && OrderAttack(enemy)) return;

        // Right-clicking a damaged friendly building sends any workers in the selection
        // to repair it for free. Full-health, half-built, wrecked and neutral buildings
        // are not repair targets, so the click falls through to a plain move; enemy
        // buildings were already turned into an attack order above.
        var repair = FindRepairableBuildingAt(mw);
        if (repair != null && AssignRepairers(repair)) return;

        IssueGroundMove(mw, attackMove: false);
    }

    void IssueGroundMove(Vector3 mw, bool attackMove)
    {
        var grid = MapGrid.Instance;
        if (grid == null) return;

        Vector3Int clicked = grid.WorldToCell(mw);
        int moved = 0, refused = 0;
        var claimed = new HashSet<Vector3Int>();   // one unit per tile

        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (u == null) continue;
            // A direct IssueMove call (tests, scripted commands) may run before
            // SelectionManager.Update has pruned a corpse from the list.
            if (!IsLiving(u)) continue;

            var ai = u.GetComponent<CombatAI>();
            if (attackMove)
            {
                // Attack-move is a combat order: workers keep their assignments and
                // non-combatants such as monks are not pulled into the attack.
                var actor = u.GetComponent<UnitActor>();
                if (ai == null || actor == null || !actor.IsAlive || !actor.isCombatant) continue;
            }

            var start = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
            if (!start.HasValue) { refused++; continue; }

            // Snap to a tile that is reachable *from this unit*, not merely nearby —
            // clicking an island or across water no longer silently does nothing.
            // Cells already claimed by an earlier unit in this order are skipped,
            // so a group never stacks two units on one tile.
            int region = grid.Region(start.Value);
            var off = SpiralOffset(i);
            var want = clicked + new Vector3Int(off.x, off.y, 0);
            var dest = grid.NearestWalkableInRegion(want, 4, region, claimed);
            if (!dest.HasValue) { refused++; continue; }

            var path = grid.FindPath(start.Value, dest.Value);
            if (path != null)
            {
                if (attackMove)
                {
                    ai.OrderAttackMove(dest.Value);
                }
                else
                {
                    // An ordinary move cancels economic work and any previous attack order.
                    // A worker keeps its cargo and becomes manually idle so automatic
                    // retasking cannot immediately override the player's command.
                    var gatherer = u.GetComponent<Gatherer>();
                    if (gatherer != null) gatherer.StopWork();
                    var healer = u.GetComponent<Healer>();
                    if (healer != null) healer.OrderMove();
                    if (ai != null) ai.OrderMove(dest.Value);
                }

                if (ai != null) u.SetPath(path, ai.CompleteOrder);
                else u.SetPath(path);
                claimed.Add(dest.Value);
                moved++;
            }
            else refused++;
        }

        if (refused > 0 && showBlockedMarker)
            ShowBlockedMarker(mw);
    }

    /// <summary>
    /// Shift+right-click: send the selected fighters patrolling between where they stand and
    /// the clicked cell. Workers keep their economic orders and monks stay with the army, the
    /// same rule the H key uses. Each unit gets its own reachable endpoint near the click, so a
    /// group does not pile onto one tile.
    /// </summary>
    public void IssuePatrol(Vector3 mw)
    {
        var grid = MapGrid.Instance;
        if (grid == null) return;

        Vector3Int clicked = grid.WorldToCell(mw);
        var claimed = new HashSet<Vector3Int>();
        int issued = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (u == null || !IsLiving(u)) continue;

            var actor = u.GetComponent<UnitActor>();
            var ai = u.GetComponent<CombatAI>();
            if (actor == null || ai == null || !actor.IsAlive || !actor.isCombatant) continue;

            var start = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
            if (!start.HasValue) continue;
            int region = grid.Region(start.Value);
            var off = SpiralOffset(i);
            var want = clicked + new Vector3Int(off.x, off.y, 0);
            var dest = grid.NearestWalkableInRegion(want, 4, region, claimed);
            if (!dest.HasValue) continue;
            if (dest.Value == start.Value) continue;   // already standing on the far end
            claimed.Add(dest.Value);
            ai.OrderPatrol(dest.Value);
            issued++;
        }

        if (issued == 0 && showBlockedMarker)
            ShowBlockedMarker(mw);
    }

    /// <summary>A resource deposit under the cursor, if any. Tested against the
    /// sprite's own bounds so clicking anywhere on a big gold mine counts.</summary>
    static ResourceNode FindResourceNodeAt(Vector3 world)
    {
        ResourceNode nearest = null;
        float nearestD = 4f;                       // 2 tiles, as a fallback
        for (int i = 0; i < ResourceNode.All.Count; i++)
        {
            var n = ResourceNode.All[i];
            if (n == null) continue;
            var sr = n.visual != null ? n.visual : n.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var b = sr.bounds;
                b.Expand(0.25f);
                if (b.Contains(world)) return n;
            }
            float d = (n.transform.position - world).sqrMagnitude;
            if (d < nearestD) { nearestD = d; nearest = n; }
        }
        return nearest;
    }

    /// <summary>True when at least one selected unit could take the job.</summary>
    bool AssignGatherers(ResourceNode node)
    {
        int assigned = 0;
        bool anyReachable = false;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (!IsLiving(u)) continue;
            var g = u.GetComponent<Gatherer>();
            if (g == null) continue;               // soldiers cannot gather
            g.Assign(node);
            if (node != null && ResourceNode.IsApproachableFrom(u.transform.position, node))
                anyReachable = true;
            assigned++;
        }
        if (assigned > 0 && node != null && !anyReachable)
            ShowBlockedMarker(node.transform.position);
        return assigned > 0;
    }

    /// <summary>True when at least one selected worker accepted the repair order.
    /// Soldiers fall through to a normal move, exactly like the resource-node rule.</summary>
    bool AssignRepairers(Building building)
    {
        int assigned = 0;
        bool anyReachable = false;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (!IsLiving(u)) continue;
            var g = u.GetComponent<Gatherer>();
            if (g == null) continue;               // soldiers cannot repair
            g.AssignRepair(building);
            if (building != null && Building.IsApproachableFrom(u.transform.position, building))
                anyReachable = true;
            assigned++;
        }
        if (assigned > 0 && building != null && !anyReachable)
            ShowBlockedMarker(building.transform.position);
        return assigned > 0;
    }

    /// <summary>A damaged friendly building under the cursor. Everything that cannot
    /// be repaired is skipped so the click falls through to a plain move.</summary>
    static Building FindRepairableBuildingAt(Vector3 world)
    {
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        var hits = Physics2D.OverlapPointAll(world);
        for (int i = 0; i < hits.Length; i++)
        {
            var b = hits[i].GetComponentInParent<Building>();
            if (b == null || b.isGhost) continue;
            if (b.team != player || !b.CanRepair) continue;
            return b;
        }

        // The collider only covers the bottom row; right-clicking the visible body
        // of a tall damaged building must still route workers to repair it.
        return Building.FindAtVisualPoint(world,
            b => b.team == player && b.CanRepair);
    }

    /// <summary>Find a clearable bush under the cursor, using its sprite bounds first
    /// and a small distance fallback for the thin edge of the art.</summary>
    static ClearableBush FindClearableBushAt(Vector3 world)
    {
        ClearableBush nearest = null;
        float nearestD = 4f;
        for (int i = 0; i < ClearableBush.All.Count; i++)
        {
            var bush = ClearableBush.All[i];
            if (bush == null) continue;
            var sr = bush.visual != null ? bush.visual : bush.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var b = sr.bounds;
                b.Expand(0.25f);
                if (b.Contains(world)) return bush;
            }
            float d = (bush.transform.position - world).sqrMagnitude;
            if (d < nearestD) { nearestD = d; nearest = bush; }
        }
        return nearest;
    }

    /// <summary>True when at least one selected worker accepted the clear order.</summary>
    bool AssignClearers(ClearableBush bush)
    {
        int assigned = 0;
        bool anyReachable = false;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (!IsLiving(u)) continue;
            var g = u.GetComponent<Gatherer>();
            if (g == null) continue;
            g.AssignClear(bush);
            if (bush != null && ClearableBush.IsApproachableFrom(u.transform.position, bush))
                anyReachable = true;
            assigned++;
        }
        if (assigned > 0 && bush != null && !anyReachable)
            ShowBlockedMarker(bush.transform.position);
        return assigned > 0;
    }

    /// <summary>Find a tower garrison under the cursor. The collider covers the
    /// footprint for movement, so also accept the visible tower sprite above it.</summary>
    static TowerGarrison FindTowerAt(Vector3 world)
    {
        var hits = Physics2D.OverlapPointAll(world);
        for (int i = 0; i < hits.Length; i++)
        {
            var tower = hits[i].GetComponentInParent<TowerGarrison>();
            if (tower != null) return tower;
        }

        var towers = FindObjectsByType<TowerGarrison>(FindObjectsSortMode.None);
        for (int i = 0; i < towers.Length; i++)
        {
            var sprite = towers[i].GetComponent<SpriteRenderer>();
            if (sprite != null && sprite.enabled && sprite.sprite != null && sprite.bounds.Contains(world))
                return towers[i];
        }
        return null;
    }

    /// <summary>Enter or leave a tower. Empty towers accept exactly one selected archer.</summary>
    bool TryGarrison(TowerGarrison tower)
    {
        if (tower == null) return false;
        var building = tower.GetComponent<Building>();
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;

        // Invalid tower targets are consumed here instead of falling through to a move
        // order. In particular a half-built tower must never send the archer on an
        // approach path that could only fail on arrival.
        if (building == null || building.isGhost || !building.IsAlive)
        {
            ShowBlockedMarker(tower.transform.position);
            return true;
        }
        if (building.team != player) return false;
        if (building.IsUnderConstruction)
        {
            ShowBlockedMarker(tower.transform.position);
            return true;
        }
        if (tower.HasArcher)
        {
            tower.Ungarrison();
            selected.Clear();
            return true;
        }
        if (selected.Count != 1 || !IsLiving(selected[0])) return false;
        var mover = selected[0];
        var archer = mover.GetComponent<UnitActor>();
        if (archer == null || archer.projectile == null || archer.team != building.team) return false;

        var grid = MapGrid.Instance;
        if (grid == null) return false;
        var start = grid.ResolveWalkableStart(grid.WorldToCell(mover.transform.position));
        if (!start.HasValue)
        {
            ShowBlockedMarker(tower.transform.position);
            return true;
        }

        // Approach the nearest reachable tile next to any part of the footprint.
        // The action-radius cap excludes the archer's own distant tile, so a wide
        // tower cannot make the order complete before the archer actually arrives.
        int region = grid.Region(start.Value);
        List<Vector3Int> bestPath = null;
        for (int i = 0; i < building.Cells.Count; i++)
        {
            var dest = grid.NearestWalkableWithin(start.Value, building.Cells[i], 2, region, 1.75f);
            if (!dest.HasValue) continue;
            var path = grid.FindPath(start.Value, dest.Value);
            if (path != null && (bestPath == null || path.Count < bestPath.Count)) bestPath = path;
        }
        if (bestPath == null)
        {
            ShowBlockedMarker(tower.transform.position);
            return true;
        }

        // As with an ordinary move, a new path cancels this callback. Only the
        // arrival callback may pin the archer to the tower, so distant orders never teleport.
        var gatherer = mover.GetComponent<Gatherer>();
        if (gatherer != null) gatherer.StopWork();
        var healer = mover.GetComponent<Healer>();
        if (healer != null) healer.OrderMove();
        var ai = mover.GetComponent<CombatAI>();
        if (ai != null) ai.OrderGarrison();
        mover.SetPath(bestPath, () =>
        {
            if (tower == null || building == null || archer == null || !building.IsAlive)
            {
                if (ai != null) ai.CompleteOrder();
                return;
            }
            if (tower.Garrison(archer)) selected.Remove(mover);
            else if (ai != null) ai.CompleteOrder();
        });
        return true;
    }

    /// <summary>The side the current selection belongs to.</summary>
    Team SelectionTeam()
    {
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (!IsLiving(u)) continue;
            var a = u.GetComponent<UnitActor>();
            if (a != null) return a.team;
        }
        return Team.Neutral;
    }

    /// <summary>True when at least one selected unit can fight the given enemy.</summary>
    bool OrderAttack(IDamageable enemy)
    {
        int ordered = 0;
        bool anyReachable = false;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (!IsLiving(u)) continue;
            var ai = u.GetComponent<CombatAI>();
            if (ai == null) continue;
            var a = u.GetComponent<UnitActor>();
            if (a != null && !a.isCombatant) continue;
            ai.OrderAttack(enemy);
            if (CanEngage(u, enemy)) anyReachable = true;
            ordered++;
        }
        if (ordered > 0 && enemy != null && !anyReachable)
            ShowBlockedMarker(enemy.Transform.position);
        return ordered > 0;
    }

    /// <summary>Whether this unit can actually bring the target into weapon range:
    /// already in range, or a walkable approach cell exists inside its region. Used
    /// only for the red-X feedback; the forced attack itself is still retained.</summary>
    static bool CanEngage(UnitMover u, IDamageable target)
    {
        if (u == null || target == null) return false;
        var actor = u.GetComponent<UnitActor>();
        if (actor == null) return false;
        var grid = MapGrid.Instance;
        if (grid == null) return true;

        float range = Mathf.Max(0.1f, actor.attackRange);
        Vector2 me = u.transform.position;

        if (target is Building building && building != null && building.Cells.Count > 0)
        {
            for (int i = 0; i < building.Cells.Count; i++)
                if (Vector2.Distance(me, grid.CellCenter(building.Cells[i])) <= range)
                    return true;

            var start = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
            if (!start.HasValue) return false;
            int region = grid.Region(start.Value);
            for (int i = 0; i < building.Cells.Count; i++)
                if (grid.NearestWalkableWithin(start.Value, building.Cells[i], 4, region, range).HasValue)
                    return true;
            return false;
        }

        if (Vector2.Distance(me, target.Transform.position) <= range) return true;
        var unitStart = grid.ResolveWalkableStart(grid.WorldToCell(u.transform.position));
        if (!unitStart.HasValue) return false;
        int unitRegion = grid.Region(unitStart.Value);
        var goal = grid.WorldToCell(target.Transform.position);
        return grid.NearestWalkableWithin(unitStart.Value, goal, 4, unitRegion, range).HasValue;
    }

    /// <summary>
    /// Ctrl+digit stores the current unit selection; a bare digit recalls it. Returns true
    /// when the key did something, so callers can treat it as consumed. Recalling prunes dead
    /// units first, and a group that has died out is dropped rather than kept as a trap.
    /// </summary>
    public bool HandleControlGroupKey(int digit, bool ctrl)
    {
        if (digit < 1 || digit > 9) return false;

        if (ctrl)
        {
            var units = new List<UnitMover>();
            for (int i = 0; i < selected.Count; i++)
                if (IsLiving(selected[i])) units.Add(selected[i]);
            // Ctrl+N with nothing selected keeps the old group: assigning an empty selection
            // would silently wipe a group the player just built.
            if (units.Count == 0) return false;
            controlGroups[digit] = units;
            return true;
        }

        if (!controlGroups.TryGetValue(digit, out var group)) return false;
        group.RemoveAll(u => !IsLiving(u));
        if (group.Count == 0)
        {
            controlGroups.Remove(digit);
            return false;
        }

        selected.Clear();
        selected.AddRange(group);
        selectedBuilding = null;
        UpdateRings();
        return true;
    }

    /// <summary>Digit keys, with Ctrl meaning "assign". Runs only while the attack command is
    /// not armed and the building ghost is not up, so it can never steal those modes' input.</summary>
    void HandleControlGroupKeys()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        for (int d = 1; d <= 9; d++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + d))) continue;
            HandleControlGroupKey(d, ctrl);
            return;
        }
    }

    /// <summary>H: soldiers hold their ground. Workers are ignored by CombatAI.OrderHold,
    /// so a mixed box selection does not abandon its gathering orders.</summary>
    void OrderHold()
    {
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (u == null) continue;
            var ai = u.GetComponent<CombatAI>();
            var a = u.GetComponent<UnitActor>();
            if (ai == null || a == null || !a.IsAlive || !a.isCombatant) continue;
            ai.OrderHold();
        }
    }

    /// <summary>Spend the player's stockpile to restore the selected building to full
    /// health. Every rejection returns before touching the bank, and the cost is
    /// computed once — recomputing after the repair would see zero missing health.</summary>
    public void RepairSelected()
    {
        var b = selectedBuilding;
        if (b == null) return;
        if (!b.CanRepair) return;

        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        if (b.team != player) return;

        var cost = b.RepairCost;
        if (Economy.Instance == null || !Economy.Instance.TrySpend(cost)) return;

        b.Repair(b.MissingHealth);
    }

    /// <summary>Delete: refund queued products in full, then half the building cost,
    /// and remove the selected building. Command centres and neutral resources are excluded.</summary>
    public void DemolishSelected()
    {
        var b = selectedBuilding;
        if (b == null || !b.IsAlive || !b.CanDemolish) return;
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        if (b.team != player) return;

        var production = b.GetComponent<ProductionBuilding>();
        if (production != null)
            while (production.Queue.Count > 0) production.CancelLast();

        if (Economy.Instance != null)
            Economy.Instance.Refund(new ResourceCost(b.cost.gold / 2, b.cost.wood / 2, b.cost.meat / 2));

        selectedBuilding = null;
        b.Demolish();
    }

    void ShowBlockedMarker(Vector3 worldPos)
    {
        Marker m = null;
        for (int i = 0; i < markers.Count; i++)
            if (!markers[i].go.activeSelf) { m = markers[i]; break; }

        if (m == null)
        {
            var go = new GameObject("BlockedMarker");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = blockedSprite;
            sr.color = new Color(1f, 0.25f, 0.2f, 1f);
            m = new Marker { go = go, sr = sr };
            markers.Add(m);
        }

        worldPos.z = 0f;
        m.go.transform.position = worldPos;
        m.sr.sortingOrder = YSort.OrderFor(worldPos.y) + 5;   // always above the units
        m.timer = MarkerLifetime;
        m.go.SetActive(true);
    }

    void TickMarkers(float dt)
    {
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (!m.go.activeSelf) continue;
            m.timer -= dt;
            if (m.timer <= 0f) { m.go.SetActive(false); continue; }
            var c = m.sr.color;
            c.a = Mathf.Clamp01(m.timer / MarkerLifetime);
            m.sr.color = c;
        }
    }

    void UpdateRings()
    {
        selected.RemoveAll(u => u == null);
        while (rings.Count < selected.Count)
        {
            var go = new GameObject("SelectionRing");
            go.AddComponent<SpriteRenderer>().sprite = ringSprite;
            go.transform.localScale = new Vector3(1.1f, 0.8f, 1f); // squashed ellipse under the feet
            rings.Add(go);
        }
        for (int i = 0; i < rings.Count; i++)
        {
            bool on = i < selected.Count;
            rings[i].SetActive(on);
            if (!on) continue;
            var u = selected[i];
            var pos = u.transform.position;
            rings[i].transform.position = pos;
            // Just behind the unit it belongs to, and Y-sorted like everything else.
            rings[i].GetComponent<SpriteRenderer>().sortingOrder = YSort.OrderFor(pos.y) - 1;
        }
        UpdateAttackRangeRings();
    }

    /// <summary>Rings behind the selection ellipses: one per selected living unit
    /// (its own attack range) and one for a selected finished tower (its active
    /// range). The pool only grows when the selection needs more rings and hides
    /// the surplus instead of destroying it, so a changing selection never
    /// allocates per frame.</summary>
    void UpdateAttackRangeRings()
    {
        var building = selectedBuilding;
        var tower = building != null ? building.GetComponent<TowerGarrison>() : null;
        bool showTowerRing = tower != null && building.IsAlive &&
                             !building.IsUnderConstruction && tower.HasArcher;

        // Size the pool to what this frame needs, growing at most once per frame.
        int needed = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (u == null || !IsLiving(u)) continue;
            var actor = u.GetComponent<UnitActor>();
            if (actor != null && actor.isCombatant) needed++;
        }
        if (showTowerRing) needed++;
        if (rangeIndicators.Count < needed)
        {
            int grow = needed - rangeIndicators.Count;
            for (int i = 0; i < grow; i++)
                rangeIndicators.Add(AttackRangeIndicator.Create(transform));
        }

        int used = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            var u = selected[i];
            if (u == null || !IsLiving(u)) continue;
            var actor = u.GetComponent<UnitActor>();
            if (actor == null || !actor.isCombatant) continue;
            ShowRangeRing(used++, u.transform.position,
                Mathf.Max(0.1f, actor.attackRange), RangeTint(actor.team));
        }
        if (showTowerRing)
        {
            ShowRangeRing(used++, building.transform.position,
                Mathf.Max(0.1f, tower.ActiveRange), RangeTint(building.team));
        }

        for (int i = used; i < rangeIndicators.Count; i++)
        {
            var ring = rangeIndicators[i];
            if (ring != null && ring.IsVisible) ring.Hide();
        }
    }

    void ShowRangeRing(int index, Vector3 center, float radius, Color tint)
    {
        var ring = rangeIndicators[index];
        if (ring == null)
        {
            ring = AttackRangeIndicator.Create(transform);
            rangeIndicators[index] = ring;
        }
        ring.Show(center, radius, tint, AttackRangeIndicator.GroundSortingOrder);
    }

    static Color RangeTint(Team team)
    {
        switch (team)
        {
            case Team.Blue: return FriendlyRangeTint;
            case Team.Red: return EnemyRangeTint;
            default: return NeutralRangeTint;
        }
    }

    void OnGUI()
    {
        if (!dragging) return;
        Vector2 a = dragStart, b = Input.mousePosition;
        Rect r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                                 Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
        var prev = GUI.color;
        GUI.color = new Color(0.2f, 1f, 0.2f, 0.15f);
        GUI.DrawTexture(r, white);
        GUI.color = new Color(0.2f, 1f, 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), white);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), white);
        GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), white);
        GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), white);
        GUI.color = prev;
    }
}
