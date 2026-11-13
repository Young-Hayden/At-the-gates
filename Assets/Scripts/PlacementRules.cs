using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ground-occupancy validation shared by player and AI building placement.
/// Economy, command-centre unlocks, and AI-only spacing remain with their callers.
/// </summary>
public static class PlacementRules
{
    public static bool ValidateCells(MapGrid grid, IReadOnlyList<Vector3Int> cells,
                                     bool checkUnits, out string reason)
    {
        reason = null;
        if (grid == null) { reason = "场景里没有 MapGrid"; return false; }
        if (cells == null || cells.Count == 0) { reason = "建筑没有占地"; return false; }

        int level = -1;
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (!grid.TerrainWalkable(c)) { reason = "地面不可建（悬崖 / 水 / 没有地面瓦片）"; return false; }
            if (grid.IsRamp(c)) { reason = "不能建在斜坡上"; return false; }
            if (GarnishRules.IsBushCell(c)) { reason = "草丛中不能建造"; return false; }
            if (grid.IsBlockedByBuilding(c)) { reason = "已经被别的建筑占了"; return false; }

            int l = grid.Level(c);
            if (level < 0) level = l;
            else if (l != level) { reason = "横跨了两个高度层"; return false; }
        }

        if (checkUnits)
        {
            var occupied = new HashSet<Vector3Int>(cells);
            foreach (var u in Object.FindObjectsByType<UnitMover>(FindObjectsSortMode.None))
            {
                if (u == null) continue;
                if (occupied.Contains(grid.WorldToCell(u.transform.position)))
                { reason = "有单位站在上面"; return false; }
            }
        }
        return true;
    }
}
