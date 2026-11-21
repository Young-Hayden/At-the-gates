using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A harvestable deposit — gold mine, sheep, wood pile. A <see cref="Gatherer"/>
/// walks to it, takes <see cref="perTrip"/> per trip and hauls it to a drop-off.
/// </summary>
public class ResourceNode : MonoBehaviour
{
    /// <summary>Every node in the scene, so a gatherer can ask for the nearest one.</summary>
    public static readonly List<ResourceNode> All = new List<ResourceNode>();

    [Header("资源")]
    public ResourceKind kind = ResourceKind.Gold;
    public int amount = 1500;
    public int maxAmount = 1500;

    [Header("采集")]
    [Tooltip("每次采集动作花多少秒。")]
    public float harvestSeconds = 1.2f;
    [Tooltip("一次带走多少。单位负重上限到不了这个数就按负重算。")]
    public int perTrip = 20;

    [Header("外观（留空则不切换）")]
    public SpriteRenderer visual;
    [Tooltip("剩余比例 > 66% 时用这张")]
    public Sprite spriteFull;
    [Tooltip("剩余比例 33%~66% 时用这张")]
    public Sprite spriteHalf;
    [Tooltip("剩余比例 < 33% 时用这张")]
    public Sprite spriteEmpty;
    [Tooltip("树的摇摆动画。采空露出树桩时停掉它——不停的话动画每帧都在改写精灵，" +
             "树桩会被盖回去。矿脉这类没有动画的留空即可。")]
    public Animator swayAnimator;

    [Header("联动")]
    [Tooltip("采空时停止阻挡、长回来时恢复。树的树桩可以踩过去，矿脉留空着。")]
    public Building blockingBuilding;

    [Header("重生")]
    [Tooltip("采空后多久长回来（秒）。0 = 不重生（矿脉采完就没了）。")]
    public float regrowSeconds = 0f;

    public bool Depleted => amount <= 0;
    public float FillRatio => maxAmount <= 0 ? 0f : Mathf.Clamp01((float)amount / maxAmount);

    [System.Serializable]
    public class SaveState
    {
        public int amount;
        public int maxAmount;
        public bool regrowing;
        public float regrowTimer;
    }

    public SaveState CaptureSaveState()
    {
        return new SaveState
        {
            amount = amount,
            maxAmount = maxAmount,
            regrowing = regrowing,
            regrowTimer = regrowTimer,
        };
    }

    /// <summary>Restores remaining yield and an in-progress regrow timer.</summary>
    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        maxAmount = Mathf.Max(1, state.maxAmount);
        amount = Mathf.Clamp(state.amount, 0, maxAmount);
        regrowing = state.regrowing && regrowSeconds > 0f && amount <= 0;
        regrowTimer = regrowing ? Mathf.Clamp(state.regrowTimer, 0f, regrowSeconds) : 0f;
        if (blockingBuilding != null) blockingBuilding.SetBlocking(!Depleted);
        RefreshVisual();
    }

    /// <summary>True while a chopped-down node is waiting to grow back.</summary>
    public bool IsRegrowing => regrowing;
    public float RegrowProgress => !regrowing || regrowSeconds <= 0f
        ? 0f : Mathf.Clamp01(1f - regrowTimer / regrowSeconds);

    float regrowTimer;
    bool regrowing;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        if (!regrowing) return;
        regrowTimer -= Time.deltaTime;
        if (regrowTimer > 0f) return;

        // A building placed over the stump/depleted animal must not be overlapped by
        // the regrown footprint. Defer the regrow until the cell is free again; a
        // permanent building simply leaves the node depleted.
        if (blockingBuilding != null && FootprintOccupiedByOtherBuilding())
        {
            regrowTimer = Mathf.Max(1f, regrowSeconds * 0.1f);
            return;
        }

        regrowing = false;
        amount = maxAmount;
        if (blockingBuilding != null) blockingBuilding.SetBlocking(true);
        RefreshVisual();
    }

    bool FootprintOccupiedByOtherBuilding()
    {
        var grid = MapGrid.Instance;
        if (grid == null || blockingBuilding == null) return false;
        var cells = blockingBuilding.Cells;
        for (int i = 0; i < cells.Count; i++)
            if (grid.IsBlockedByBuilding(cells[i])) return true;
        return false;
    }

    void Awake()
    {
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>();
        if (maxAmount < amount) maxAmount = amount;
        RefreshVisual();
    }

    /// <summary>Takes up to <paramref name="want"/>, returns how much was actually taken.</summary>
    public int Harvest(int want)
    {
        if (want <= 0 || amount <= 0) return 0;
        int taken = Mathf.Min(want, amount);
        amount -= taken;
        if (amount <= 0 && regrowSeconds > 0f)
        {
            regrowing = true;
            regrowTimer = regrowSeconds;
            if (blockingBuilding != null) blockingBuilding.SetBlocking(false);
        }
        RefreshVisual();
        return taken;
    }

    public void RefreshVisual()
    {
        if (visual == null) return;
        float r = FillRatio;
        Sprite want = r > 0.66f ? spriteFull : r > 0.33f ? spriteHalf : spriteEmpty;
        // A living tree sways; a stump does not. Pause the animator before touching the
        // sprite, or its next frame would overwrite the stump we are about to show.
        bool stump = spriteEmpty != null && want == spriteEmpty;
        if (swayAnimator != null) swayAnimator.enabled = !stump;
        if (want != null && visual.sprite != want) visual.sprite = want;
    }

    /// <summary>Nearest live node this actor can actually reach.
    ///
    /// Distance alone can select a deposit across water or on another plateau, so the
    /// worker's walkable start and connected region are resolved first and the node is
    /// accepted only when an approach cell exists inside that region. Shared by
    /// <see cref="Gatherer"/> auto-assign and <see cref="AIController"/> dispatch so the
    /// two selection rules cannot drift apart.</summary>
    public static ResourceNode FindNearestReachable(Vector3 from, float maxDistance,
                                                    MapGrid grid = null,
                                                    System.Func<ResourceNode, bool> accept = null)
    {
        if (grid == null) grid = MapGrid.Instance;
        if (grid == null) return null;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(from));
        if (!start.HasValue) return null;
        int region = grid.Region(start.Value);

        ResourceNode best = null;
        float bestD = maxDistance * maxDistance;
        for (int i = 0; i < All.Count; i++)
        {
            var candidate = All[i];
            if (candidate == null || candidate.Depleted) continue;
            if (accept != null && !accept(candidate)) continue;
            float d = (candidate.transform.position - from).sqrMagnitude;
            if (d >= bestD) continue;
            var approach = grid.NearestWalkableWithin(
                start.Value, grid.WorldToCell(candidate.transform.position), 4, region, 1.6f);
            if (!approach.HasValue) continue;
            best = candidate;
            bestD = d;
        }
        return best;
    }

    /// <summary>True when a worker standing at <paramref name="from"/> has a walkable
    /// approach cell for this node, using the same rule as auto-assignment. Used only
    /// to give a manual order visible feedback; the order itself is still retried.</summary>
    public static bool IsApproachableFrom(Vector3 from, ResourceNode node, MapGrid grid = null)
    {
        if (node == null || node.Depleted) return false;
        if (grid == null) grid = MapGrid.Instance;
        if (grid == null) return true;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(from));
        if (!start.HasValue) return false;
        int region = grid.Region(start.Value);
        return grid.NearestWalkableWithin(
            start.Value, grid.WorldToCell(node.transform.position), 4, region, 1.6f).HasValue;
    }

    /// <summary>Nearest node that still has something in it, preferring the same
    /// resource kind. Returns null when there is nothing left to harvest.</summary>
    public static ResourceNode FindNearest(Vector3 from, ResourceKind? kind = null, float maxDistance = float.MaxValue)
    {
        ResourceNode best = null;
        float bestD = maxDistance * maxDistance;
        for (int i = 0; i < All.Count; i++)
        {
            var n = All[i];
            if (n == null || n.Depleted) continue;
            if (kind.HasValue && n.kind != kind.Value) continue;
            float d = (n.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = kind == ResourceKind.Gold ? Color.yellow
                     : kind == ResourceKind.Wood ? new Color(0.6f, 0.4f, 0.2f)
                     : new Color(1f, 0.5f, 0.6f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(1f, 1f, 0f));
    }
}
