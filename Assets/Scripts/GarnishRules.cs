using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared query for cells occupied by non-blocking bushes. Used only by
/// building placement; units may still walk through these cells.</summary>
public static class GarnishRules
{
    static HashSet<Vector3Int> bushCells;
    static int cachedRootId;

    public static void Invalidate()
    {
        bushCells = null;
        cachedRootId = 0;
    }

    public static bool IsBushCell(Vector3Int cell)
    {
        EnsureCache();
        return bushCells != null && bushCells.Contains(cell);
    }

    static void EnsureCache()
    {
        var root = GameObject.Find("Garnish");
        if (root == null)
        {
            bushCells = new HashSet<Vector3Int>();
            cachedRootId = 0;
            return;
        }
        if (bushCells != null && cachedRootId == root.GetInstanceID()) return;

        var grid = MapGrid.Instance;
        if (grid == null)
        {
            bushCells = null;
            cachedRootId = 0;
            return;
        }

        bushCells = new HashSet<Vector3Int>();
        cachedRootId = root.GetInstanceID();
        foreach (Transform child in root.transform)
            if (child.name.StartsWith("Bush")) bushCells.Add(grid.WorldToCell(child.position));
    }
}
