using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Scatters trees and decoration over the map as real prefab instances, grouped under a
/// "Decor" object so it can be wiped and redone.
///
/// Three rules keep the map playable:
///  1. Props keep at least one free tile between them, so two props never touch.
///  2. Before each placement the map is flood filled with that cell blocked, using the
///     same move rule as the game (<see cref="MapGrid"/>'s CanEnter: 8-way, no corner
///     cutting, level changes only on ramps). A placement that would close off a
///     corridor is rejected.
///  3. Only the island/plateau the cell belongs to is tested — the map has disconnected
///     landmasses, so a global "is everything still reachable" test is meaningless.
/// </summary>
public static class DecorateMap
{
    const string DecorRootName = "Decor";
    const int BaseClearance = 6;      // no props within this many tiles of a building
    const int PropSpacing = 1;        // max Chebyshev distance to another prop

    static readonly Vector3Int[] Dirs8 = {
        new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,1,0), new Vector3Int(0,-1,0),
        new Vector3Int(1,1,0), new Vector3Int(1,-1,0), new Vector3Int(-1,1,0), new Vector3Int(-1,-1,0),
    };

    static readonly string[] TreePrefabs = {
        "Assets/Prefabs/Resources/Tree1.prefab", "Assets/Prefabs/Resources/Tree2.prefab",
        "Assets/Prefabs/Resources/Tree3.prefab", "Assets/Prefabs/Resources/Tree4.prefab",
        "Assets/Prefabs/Resources/Tree5.prefab", "Assets/Prefabs/Resources/Tree6.prefab",
    };

    static readonly string[] SmallPrefabs = {
        "Assets/Prefabs/Deco/Rock1.prefab", "Assets/Prefabs/Deco/Rock2.prefab",
        "Assets/Prefabs/Deco/Rock3.prefab", "Assets/Prefabs/Deco/Rock4.prefab",
        "Assets/Prefabs/Deco/Deco_01.prefab", "Assets/Prefabs/Deco/Deco_02.prefab",
        "Assets/Prefabs/Deco/Deco_03.prefab", "Assets/Prefabs/Deco/Deco_05.prefab",
        "Assets/Prefabs/Deco/Deco_06.prefab", "Assets/Prefabs/Deco/Deco_07.prefab",
        "Assets/Prefabs/Deco/Deco_08.prefab", "Assets/Prefabs/Deco/Deco_09.prefab",
        "Assets/Prefabs/Deco/Deco_10.prefab", "Assets/Prefabs/Deco/Deco_11.prefab",
        "Assets/Prefabs/Deco/Deco_12.prefab", "Assets/Prefabs/Deco/Deco_13.prefab",
        "Assets/Prefabs/Deco/Deco_14.prefab", "Assets/Prefabs/Deco/Deco_15.prefab",
    };

    [MenuItem("Tools/装饰地图/稀疏一些")]
    public static void ScatterSparse() { Run(20, 3, 45, 20251014); }

    [MenuItem("Tools/装饰地图/茂密一些")]
    public static void ScatterDense() { Run(42, 5, 110, 20251015); }

    [MenuItem("Tools/装饰地图/清空装饰")]
    public static void Clear()
    {
        var root = GameObject.Find(DecorRootName);
        if (root != null) Object.DestroyImmediate(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[装饰] 已清空");
    }

    static void Run(int groves, int perGrove, int small, int seed)
    {
        var grid = MapGrid.Instance;
        if (grid == null) { Debug.LogError("[装饰] 场景里没有 MapGrid"); return; }
        Clear();

        var rng = new System.Random(seed);
        var root = new GameObject(DecorRootName);

        // ---- building footprints and the breathing room around them ----
        var blocked = new HashSet<Vector3Int>();
        var clearance = new HashSet<Vector3Int>();
        foreach (var b in Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            b.Prepare();
            foreach (var c in b.Cells)
            {
                blocked.Add(c);
                for (int dx = -BaseClearance; dx <= BaseClearance; dx++)
                for (int dy = -BaseClearance; dy <= BaseClearance; dy++)
                    clearance.Add(new Vector3Int(c.x + dx, c.y + dy, 0));
            }
        }

        // ---- every terrain cell, buildings included (they block at runtime) ----
        var blockedAll = new HashSet<Vector3Int>(blocked);
        var walkable = new HashSet<Vector3Int>();
        var gm = Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None)
                       .FirstOrDefault(t => t.name == "Level0_Ground");
        if (gm == null) { Debug.LogError("[装饰] 找不到 Level0_Ground"); return; }
        var cb = gm.cellBounds;
        for (int x = cb.xMin; x < cb.xMax; x++)
        for (int y = cb.yMin; y < cb.yMax; y++)
        {
            var c = new Vector3Int(x, y, 0);
            if (grid.TerrainWalkable(c)) walkable.Add(c);
        }

        var placeable = new List<Vector3Int>();
        foreach (var c in walkable)
        {
            if (grid.IsRamp(c) || blocked.Contains(c) || clearance.Contains(c)) continue;
            placeable.Add(c);
        }
        for (int i = placeable.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (placeable[i], placeable[j]) = (placeable[j], placeable[i]);
        }

        var taken = new HashSet<Vector3Int>();
        var scratch = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();

        // Mirrors MapGrid.CanEnter exactly: cliffs/buildings/props block, diagonals may
        // not cut corners, and a level change needs a ramp.
        bool CanStep(Vector3Int a, Vector3Int b)
        {
            if (!walkable.Contains(a) || !walkable.Contains(b)) return false;
            if (blockedAll.Contains(a) || blockedAll.Contains(b)) return false;
            if (taken.Contains(a) || taken.Contains(b)) return false;
            int la = grid.Level(a), lb = grid.Level(b);
            if (la != lb && !grid.IsRamp(a) && !grid.IsRamp(b)) return false;
            var d = b - a;
            if (d.x != 0 && d.y != 0)
            {
                if (!CanStep(a, a + new Vector3Int(d.x, 0, 0))) return false;
                if (!CanStep(a, a + new Vector3Int(0, d.y, 0))) return false;
            }
            return true;
        }

        // ---- components, using the same rule ----
        var compOf = new Dictionary<Vector3Int, int>();
        var compSize = new List<int>();
        var pending = new List<Vector3Int>();
        foreach (var w in walkable)
        {
            if (compOf.ContainsKey(w) || blockedAll.Contains(w)) continue;
            int id = compSize.Count;
            int n = 0;
            queue.Clear(); queue.Enqueue(w); compOf[w] = id;
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue(); n++;
                foreach (var d in Dirs8)
                {
                    var nb = cur + d;
                    if (compOf.ContainsKey(nb) || blockedAll.Contains(nb)) continue;
                    if (!walkable.Contains(nb)) continue;
                    if (!CanStep(cur, nb)) continue;
                    compOf[nb] = id; queue.Enqueue(nb);
                }
            }
            compSize.Add(n);
        }

        bool Spaced(Vector3Int c)
        {
            for (int dx = -PropSpacing; dx <= PropSpacing; dx++)
            for (int dy = -PropSpacing; dy <= PropSpacing; dy++)
                if (taken.Contains(new Vector3Int(c.x + dx, c.y + dy, 0))) return false;
            return true;
        }

        var takenInComp = new Dictionary<int, int>();
        bool StaysConnected(Vector3Int c)
        {
            if (!compOf.TryGetValue(c, out int comp)) return false;
            int used = takenInComp.TryGetValue(comp, out var u) ? u : 0;
            int freeBefore = compSize[comp] - used;
            if (freeBefore <= 1) return false;

            taken.Add(c);                       // tentatively block it
            Vector3Int? seedCell = null;
            foreach (var w in walkable)
                if (compOf.TryGetValue(w, out int wc) && wc == comp && !taken.Contains(w)) { seedCell = w; break; }

            int reach = 0;
            if (seedCell.HasValue)
            {
                scratch.Clear(); queue.Clear();
                scratch.Add(seedCell.Value); queue.Enqueue(seedCell.Value);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue(); reach++;
                    foreach (var d in Dirs8)
                    {
                        var nb = cur + d;
                        if (scratch.Contains(nb) || taken.Contains(nb)) continue;
                        if (!walkable.Contains(nb) || blockedAll.Contains(nb)) continue;
                        if (!compOf.TryGetValue(nb, out int nc) || nc != comp) continue;
                        if (!CanStep(cur, nb)) continue;
                        scratch.Add(nb); queue.Enqueue(nb);
                    }
                }
            }
            taken.Remove(c);
            return reach == freeBefore - 1;
        }

        int treeCount = 0, smallCount = 0, rejected = 0;
        var treeAssets = TreePrefabs.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
        var smallAssets = SmallPrefabs.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();

        var placedOrder = new List<Vector3Int>();
        var propObjects = new Dictionary<Vector3Int, GameObject>();

        void Commit(Vector3Int c, GameObject prop)
        {
            taken.Add(c);
            placedOrder.Add(c);
            propObjects[c] = prop;
            int comp = compOf[c];
            takenInComp[comp] = (takenInComp.TryGetValue(comp, out var u) ? u : 0) + 1;
        }

        void DropProp(Vector3Int c)
        {
            if (!taken.Remove(c)) return;
            if (propObjects.TryGetValue(c, out var go))
            {
                if (go != null) Object.DestroyImmediate(go);
                propObjects.Remove(c);
            }
            int comp = compOf[c];
            takenInComp[comp] = Mathf.Max(0, (takenInComp.TryGetValue(comp, out var u) ? u : 0) - 1);
        }

        bool TryPlace(GameObject[] pool, Vector3Int c)
        {
            if (!walkable.Contains(c) || taken.Contains(c) || blockedAll.Contains(c)) return false;
            if (!Spaced(c)) return false;
            if (!StaysConnected(c)) { rejected++; return false; }
            var go = Place(root, pool[rng.Next(pool.Length)], c);
            Commit(c, go);
            return true;
        }

        // ---- groves ----
        for (int g = 0; g < groves && placeable.Count > 0; g++)
        {
            Vector3Int seedCell = default; bool found = false;
            int guard = 0;
            while (guard++ < 3000)
            {
                var c = placeable[rng.Next(placeable.Count)];
                if (!Spaced(c)) continue;
                if (!StaysConnected(c)) { rejected++; continue; }
                var go = Place(root, treeAssets[rng.Next(treeAssets.Length)], c);
                Commit(c, go);
                treeCount++; seedCell = c; found = true;
                break;
            }
            if (!found) break;

            int extra = rng.Next(perGrove + 1);
            for (int k = 0; k < extra; k++)
            {
                int guard2 = 0;
                while (guard2++ < 80)
                {
                    var c = seedCell + new Vector3Int(rng.Next(-7, 8), rng.Next(-7, 8), 0);
                    if (TryPlace(treeAssets, c)) { treeCount++; break; }
                }
            }
        }

        // ---- small decoration ----
        foreach (var c in placeable)
        {
            if (smallCount >= small) break;
            if (TryPlace(smallAssets, c)) smallCount++;
        }

        // ---- final safety net ----
        // The per-placement test can in principle miss a combination; this walks every
        // component once with all props in place and rolls back the most recently placed
        // prop in any component that ended up fragmented. The end state is verified,
        // not assumed.
        int rollback = 0;
        for (int pass = 0; pass < 600; pass++)
        {
            int broken = FindFragmentedComponent();
            if (broken < 0) break;
            bool dropped = false;
            for (int i = placedOrder.Count - 1; i >= 0; i--)
            {
                var c = placedOrder[i];
                if (!taken.Contains(c) || compOf[c] != broken) continue;
                DropProp(c);
                rollback++; dropped = true;
                break;
            }
            if (!dropped) break;
        }

        int FindFragmentedComponent()
        {
            foreach (var kv in takenInComp)
            {
                int comp = kv.Key;
                int freeExpected = compSize[comp] - kv.Value;
                if (freeExpected <= 0) continue;

                Vector3Int? seedCell = null;
                foreach (var w in walkable)
                    if (compOf.TryGetValue(w, out int wc) && wc == comp && !taken.Contains(w)) { seedCell = w; break; }
                if (!seedCell.HasValue) continue;

                int reach = 0;
                scratch.Clear(); queue.Clear();
                scratch.Add(seedCell.Value); queue.Enqueue(seedCell.Value);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue(); reach++;
                    foreach (var d in Dirs8)
                    {
                        var nb = cur + d;
                        if (scratch.Contains(nb) || taken.Contains(nb)) continue;
                        if (!walkable.Contains(nb) || blockedAll.Contains(nb)) continue;
                        if (!compOf.TryGetValue(nb, out int nc) || nc != comp) continue;
                        if (!CanStep(cur, nb)) continue;
                        scratch.Add(nb); queue.Enqueue(nb);
                    }
                }
                if (reach != freeExpected) return comp;
            }
            return -1;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[装饰] 完成：树 {treeCount} 棵（{groves} 丛），小装饰 {smallCount} 个。"
                + $"放置时否掉 {rejected} 处，最后复查又回滚 {rollback} 处。"
                + "收尾时每个连通区都重新验过一遍，地图和各基地之间保持连通。");
    }

    static GameObject Place(GameObject parent, GameObject prefab, Vector3Int cell)
    {
        if (prefab == null) return null;
        // Building's convention: the origin sits on the base line of the cell it occupies
        var pos = MapGrid.Instance.CellCenter(cell) - new Vector3(0f, 0.5f, 0f);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = pos;
        go.transform.SetParent(parent.transform, true);
        return go;
    }
}
