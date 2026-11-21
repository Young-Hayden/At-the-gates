using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Turns a building into a factory: a queue of unit prefabs, each with a cost and a
/// build time. Finished units are placed on a free tile next to the building.
/// </summary>
[RequireComponent(typeof(Building))]
public class ProductionBuilding : MonoBehaviour
{
    [System.Serializable]
    public class Product
    {
        public string label = "新兵";
        public GameObject unitPrefab;
        [Tooltip("生产按钮上的头像。留空则退回单位精灵（框很大，人很小，不推荐）。")]
        public Sprite icon;
        public ResourceCost cost = new ResourceCost(60);
        public float buildSeconds = 4f;
    }

    [Header("能造什么（HUD 按这个列表出按钮）")]
    public Product[] products = new Product[0];

    [Tooltip("排队上限。")]
    public int queueLimit = 5;

    readonly List<int> queue = new List<int>();
    float timer;
    Building building;

    public IReadOnlyList<int> Queue => queue;
    public bool IsBusy => queue.Count > 0;
    public Product Current => queue.Count > 0 ? products[queue[0]] : null;

    /// <summary>Reason produced by the most recent
    /// <see cref="CanQueue(int, bool, out string)"/> call, or null when that call
    /// allowed the order. Each call replaces it. The HUD labels use the call's own
    /// out parameter rather than reading this shared value after a product loop.</summary>
    public string LastRefusalReason { get; private set; }

    /// <summary>True while the queue head is ready but every spawn cell around the
    /// footprint is occupied. The paid order is retained and retried; the HUD uses this
    /// to tell the player why nothing is coming out.</summary>
    public bool SpawnBlocked { get; private set; }

    /// <summary>0..1 progress on the item at the head of the queue.</summary>
    public float Progress
    {
        get
        {
            var p = Current;
            if (p == null || p.buildSeconds <= 0f) return 0f;
            return Mathf.Clamp01(1f - timer / p.buildSeconds);
        }
    }

    [System.Serializable]
    public class SaveState
    {
        public List<int> queue = new List<int>();
        public float remaining;
    }

    public SaveState CaptureSaveState()
    {
        return new SaveState { queue = new List<int>(queue), remaining = timer };
    }

    /// <summary>Restores paid-for orders without charging either bank again.</summary>
    public void RestoreSaveState(SaveState state)
    {
        queue.Clear();
        timer = 0f;
        if (state != null && state.queue != null)
        {
            for (int i = 0; i < state.queue.Count; i++)
            {
                int index = state.queue[i];
                if (index >= 0 && index < products.Length) queue.Add(index);
            }
        }
        if (queue.Count > 0)
            timer = Mathf.Clamp(state.remaining, 0f, Mathf.Max(0f, products[queue[0]].buildSeconds));
        if (building != null) enabled = building.IsAlive && !building.IsUnderConstruction;
        UnitLimits.Invalidate();          // restored orders are already paid for
        Population.Invalidate();
    }

    void Awake()
    {
        building = GetComponent<Building>();
    }

    // ------------------------------------------------------------------- queue

    /// <summary>Index of a product in this factory's list, or -1. Used by the unit shop to
    /// work out which factory can actually make what the player just ordered.</summary>
    public int IndexOfProduct(GameObject unitPrefab)
    {
        if (unitPrefab == null) return -1;
        for (int i = 0; i < products.Length; i++)
            if (products[i].unitPrefab == unitPrefab) return i;
        return -1;
    }

    public bool CanQueue(int index, bool payCost = true)
    {
        return CanQueue(index, payCost, out _);
    }

    /// <summary>Same checks as the two-argument form, plus a player-facing reason
    /// for the refusal.</summary>
    public bool CanQueue(int index, bool payCost, out string reason)
    {
        reason = null;
        LastRefusalReason = null;
        if (index < 0 || index >= products.Length)
        {
            reason = "没有这个产品";
        }
        else if (products[index].unitPrefab == null)
        {
            reason = "产品未配置单位";
        }
        else if (building != null && building.IsUnderConstruction)
        {
            reason = "建造中";
        }
        else if (queue.Count >= queueLimit)
        {
            reason = "队列已满";
        }
        else if (payCost && Economy.Instance != null && !Economy.Instance.CanAfford(products[index].cost))
        {
            reason = "资源不足";
        }
        else if (!UnitLimits.CanProduce(building != null ? building.team : Team.Neutral,
                     products[index].unitPrefab, out reason))
        {
            // The cap service filled in the player-facing reason.
        }
        else if (!Population.CanFit(building != null ? building.team : Team.Neutral,
                     products[index].unitPrefab, out reason))
        {
            // Population sits after the Phase A caps, exactly like the real order
            // of constraints: a capped profession is named before "人口已满".
        }
        else if (Upkeep.IsStarving(building != null ? building.team : Team.Neutral))
        {
            // Upkeep sits last: a starving side that is also at a cap must report the
            // cap, the more specific reason. A fed side is never refused here.
            reason = "食物不足";
        }
        LastRefusalReason = reason;
        return reason == null;
    }

    /// <summary>0..1 on the item this factory is building right now, or -1 when idle.</summary>
    public float ProgressOf(GameObject unitPrefab)
    {
        if (Queue.Count == 0) return -1f;
        if (products[queue[0]].unitPrefab != unitPrefab) return -1f;
        return Progress;
    }

    public bool TryQueue(int index, bool payCost = true)
    {
        if (!CanQueue(index, payCost)) return false;
        if (payCost && Economy.Instance != null && !Economy.Instance.TrySpend(products[index].cost)) return false;

        queue.Add(index);
        if (queue.Count == 1) timer = products[index].buildSeconds;   // start the timer
        UnitLimits.Invalidate();          // the new order now occupies a roster slot
        Population.Invalidate();
        return true;
    }

    /// <summary>Refunds and removes the last thing queued.</summary>
    public void CancelLast()
    {
        if (queue.Count == 0) return;
        int i = queue[queue.Count - 1];
        queue.RemoveAt(queue.Count - 1);
        var cost = products[i].cost;
        var owner = building != null ? building.team : Team.Neutral;
        var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
        if (owner == player || owner == Team.Neutral)
        {
            if (Economy.Instance != null) Economy.Instance.Refund(cost);
        }
        else
        {
            var ai = AIController.For(owner);
            if (ai != null) ai.Refund(cost);
        }
        UnitLimits.Invalidate();          // the cancelled order frees its roster slot
        Population.Invalidate();
        if (queue.Count == 0) SpawnBlocked = false;
    }

    // ------------------------------------------------------------------ runtime

    void Update()
    {
        if (queue.Count == 0)
        {
            SpawnBlocked = false;
            return;
        }
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        int index = queue[0];
        if (!Spawn(index))
        {
            // Keep the paid-for item at the head of the queue and retry without charging again.
            SpawnBlocked = true;
            timer = 0.5f;
            return;
        }
        SpawnBlocked = false;
        queue.RemoveAt(0);
        Population.Invalidate();          // the order left the queue as a living unit
        if (queue.Count > 0) timer = products[queue[0]].buildSeconds;
    }

    bool Spawn(int index)
    {
        var prefab = products[index].unitPrefab;
        if (prefab == null) return true;
        var cell = FindSpawnCell();
        if (!cell.HasValue) return false;                // blocked in — keep the order and retry
        var go = Instantiate(prefab, MapGrid.Instance.CellCenter(cell.Value), Quaternion.identity);
        go.name = prefab.name;
        // drop anything the factory unit inherited that it should not have
        var g = go.GetComponent<Gatherer>();
        if (g != null) g.enabled = true;

        // a puff of dust so a unit appearing out of a building reads as "just built"
        var bank = FxBank.Instance;
        if (bank != null) bank.Dust(go.transform.position);
        return true;
    }

    /// <summary>First walkable, unit-free tile in the ring around the footprint,
    /// preferring the tiles in front (below) the building.</summary>
    Vector3Int? FindSpawnCell()
    {
        var grid = MapGrid.Instance;
        if (grid == null || building == null || building.Cells.Count == 0) return null;

        int minX = building.Cells.Min(c => c.x), maxX = building.Cells.Max(c => c.x);
        int minY = building.Cells.Min(c => c.y), maxY = building.Cells.Max(c => c.y);

        Vector3Int? fallback = null;
        for (int r = 1; r <= 5; r++)
        {
            for (int x = minX - r; x <= maxX + r; x++)
            for (int y = minY - r; y <= maxY + r; y++)
            {
                // ring only — skip the interior
                if (x > minX - r && x < maxX + r && y > minY - r && y < maxY + r) continue;
                var c = new Vector3Int(x, y, 0);
                if (!grid.Walkable(c)) continue;
                if (UnitAt(c)) continue;
                if (y < minY) return c;               // in front of the building: first choice
                fallback ??= c;
            }
            if (fallback.HasValue) return fallback;
        }
        return fallback;
    }

    static bool UnitAt(Vector3Int cell)
    {
        var grid = MapGrid.Instance;
        foreach (var u in UnitActor.All)
        {
            if (u == null || !u.IsAlive) continue;
            if (grid.WorldToCell(u.transform.position) == cell) return true;
        }
        return false;
    }
}
