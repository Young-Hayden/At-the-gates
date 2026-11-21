using System.Collections.Generic;
using UnityEngine;

/// <summary>A manually clearable decorative bush. It is not a resource node and gives
/// no resources; it only provides a target for a worker's clear order.</summary>
public class ClearableBush : MonoBehaviour
{
    public static readonly List<ClearableBush> All = new List<ClearableBush>();

    [Tooltip("工人清除这丛草需要多少秒。")]
    public float clearSeconds = 1f;
    public SpriteRenderer visual;

    /// <summary>The prefab root, since this component lives on the Sprite child.</summary>
    public GameObject Root => transform.parent != null ? transform.parent.gameObject : gameObject;

    /// <summary>True when a worker at <paramref name="from"/> has a walkable approach
    /// cell for this bush. Feedback only; the clear order still runs its normal retry.</summary>
    public static bool IsApproachableFrom(Vector3 from, ClearableBush bush, MapGrid grid = null)
    {
        if (bush == null) return false;
        if (grid == null) grid = MapGrid.Instance;
        if (grid == null) return true;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(from));
        if (!start.HasValue) return false;
        int region = grid.Region(start.Value);
        return grid.NearestWalkableWithin(
            start.Value, grid.WorldToCell(bush.transform.position), 4, region, 1.6f).HasValue;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>();
    }

    void OnDisable()
    {
        All.Remove(this);
        GarnishRules.Invalidate();
    }
}
