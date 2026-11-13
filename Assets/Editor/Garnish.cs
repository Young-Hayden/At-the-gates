using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// The cosmetic layer: bushes, foam, rocks in the water and drifting clouds.
///
/// Everything here is deliberately non-blocking — no <see cref="Building"/>, no collider,
/// nothing that touches <see cref="MapGrid"/>. That is the whole point: the blocking
/// pass (<see cref="DecorateMap"/>) has to prove the map stays connected, and it can only
/// afford to do that for props that change the walkable graph. Garnish costs nothing,
/// so it can be sprinkled freely.
/// </summary>
public static class Garnish
{
    const string RootName = "Garnish";
    const string PrefabDir = "Assets/Prefabs/Garnish";

    [MenuItem("Tools/装饰地图/点缀层：灌木·水岩·云")]
    public static void BuildDense() { Run(140, 130, 7, 20251016); }

    [MenuItem("Tools/装饰地图/点缀层（少量）")]
    public static void BuildLight() { Run(60, 55, 4, 20251017); }

    [MenuItem("Tools/装饰地图/清空点缀层")]
    public static void Clear()
    {
        var root = GameObject.Find(RootName);
        if (root != null) Object.DestroyImmediate(root);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[点缀] 已清空");
    }

    // ------------------------------------------------------------------ prefabs

    static Sprite LoadSprite(string png) =>
        AssetDatabase.LoadAllAssetsAtPath(png).OfType<Sprite>().FirstOrDefault();

    /// <summary>
    /// One static prop. The sprite goes on a child raised from its actual bounds, so the
    /// prefab's origin is the point where the prop meets the ground — the same
    /// convention buildings and trees use. Sprite pivots are not always centred.
    /// </summary>
    static GameObject MakeProp(string name, string png, float scale, int order, int ysortOffset)
    {
        string path = PrefabDir + "/" + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var spr = LoadSprite(png);
        if (spr == null) { Debug.LogWarning("[点缀] 找不到精灵：" + png); return null; }

        var root = new GameObject(name);
        var child = new GameObject("Sprite");
        child.transform.SetParent(root.transform, false);

        float lift = -spr.bounds.min.y * scale;
        child.transform.localPosition = new Vector3(0f, lift, 0f);
        child.transform.localScale = Vector3.one * scale;

        var sr = child.AddComponent<SpriteRenderer>();
        sr.sprite = spr;
        sr.sortingOrder = order;

        // YSort reads its own transform's Y, which is above the ground point; folding
        // that lift back into baseOrder makes it sort by the ground line again.
        if (order >= 1000)
        {
            var ys = child.AddComponent<YSort>();
            ys.baseOrder = order + Mathf.RoundToInt(lift * 10f);
            ys.offset = ysortOffset;
        }

        var go = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return go;
    }

    static GameObject MakeCloud(string name, string png)
    {
        string path = PrefabDir + "/" + name + ".prefab";
        var spr = LoadSprite(png);
        if (spr == null) return null;

        // rebuilt every run: the tint and order are the whole point of this prefab
        AssetDatabase.DeleteAsset(path);

        var root = new GameObject(name);
        var sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = spr;
        sr.sortingOrder = 6000;
        // low alpha on purpose — a cloud must never hide what the player is doing
        sr.color = new Color(1f, 1f, 1f, 0.42f);
        var drift = root.AddComponent<CloudDrift>();
        drift.speed = 0.3f;

        var go = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return go;
    }

    static void EnsurePrefabDir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Garnish"))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Garnish");
            AssetDatabase.Refresh();
        }
    }

    // ------------------------------------------------------------------ scatter

    static void Run(int bushes, int waterRocks, int clouds, int seed)
    {
        var grid = MapGrid.Instance;
        if (grid == null) { Debug.LogError("[点缀] 场景里没有 MapGrid"); return; }

        EnsurePrefabDir();

        // ---- prefabs ----
        var bushPool = new[]{
            MakeProp("Bush1", "Assets/Art/Deco/Bushes/Bushe1.png", 0.62f, 1000, -8),
            MakeProp("Bush2", "Assets/Art/Deco/Bushes/Bushe2.png", 0.62f, 1000, -8),
            MakeProp("Bush3", "Assets/Art/Deco/Bushes/Bushe3.png", 0.62f, 1000, -8),
            MakeProp("Bush4", "Assets/Art/Deco/Bushes/Bushe4.png", 0.62f, 1000, -8),
        }.Where(x => x != null).ToArray();

        var rockPool = new[]{
            MakeProp("WaterRock1", "Assets/Art/Deco/WaterRocks/Water Rocks_01.png", 1f, 6, 0),
            MakeProp("WaterRock2", "Assets/Art/Deco/WaterRocks/Water Rocks_02.png", 1f, 6, 0),
            MakeProp("WaterRock3", "Assets/Art/Deco/WaterRocks/Water Rocks_03.png", 1f, 6, 0),
            MakeProp("WaterRock4", "Assets/Art/Deco/WaterRocks/Water Rocks_04.png", 1f, 6, 0),
        }.Where(x => x != null).ToArray();

        var bigRockPool = new[]{
            MakeProp("SeaRock1", "Assets/Art/Terrain/Water/Rocks_01.png", 0.9f, 7, 0),
            MakeProp("SeaRock2", "Assets/Art/Terrain/Water/Rocks_02.png", 0.9f, 7, 0),
            MakeProp("SeaRock3", "Assets/Art/Terrain/Water/Rocks_03.png", 0.9f, 7, 0),
            MakeProp("SeaRock4", "Assets/Art/Terrain/Water/Rocks_04.png", 0.9f, 7, 0),
        }.Where(x => x != null).ToArray();

        var cloudPool = Enumerable.Range(1, 8)
            .Select(i => MakeCloud("Cloud" + i.ToString("D2"), "Assets/Art/Deco/Clouds/Clouds_" + i.ToString("D2") + ".png"))
            .Where(x => x != null).ToArray();

        if (bushPool.Length == 0 || rockPool.Length == 0)
        { Debug.LogError("[点缀] 精灵加载失败，中止"); return; }

        // Foam.png is a water surface tile, not a shoreline splash — sprinkled sparsely
        // it renders as flat pale squares. Dropped in favour of more water rocks.
        AssetDatabase.DeleteAsset(PrefabDir + "/Foam.prefab");

        Clear();
        var rng = new System.Random(seed);
        var root = new GameObject(RootName);

        // ---- terrain lookup ----
        var tilemaps = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)
                            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        var land = new HashSet<Vector3Int>();
        var water = new HashSet<Vector3Int>();
        void Collect(string tmName, HashSet<Vector3Int> into)
        {
            if (!tilemaps.TryGetValue(tmName, out var tm)) return;
            var cb = tm.cellBounds;
            for (int x = cb.xMin; x < cb.xMax; x++)
            for (int y = cb.yMin; y < cb.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (tm.HasTile(c)) into.Add(c);
            }
        }
        Collect("Level0_Ground", land);
        Collect("Level1_Top", land);
        Collect("Level2_Top", land);
        Collect("Water1", water);
        Collect("Water2", water);

        if (land.Count == 0 || water.Count == 0)
        { Debug.LogError("[点缀] 地形扫描为空（land=" + land.Count + " water=" + water.Count + "）"); return; }

        // ---- occupied: buildings and the blocking decor ----
        var occupied = new HashSet<Vector3Int>();
        foreach (var b in Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            b.Prepare();
            foreach (var c in b.Cells) occupied.Add(c);
        }
        var decorRoot = GameObject.Find("Decor");
        if (decorRoot != null)
            foreach (Transform ch in decorRoot.transform)
            {
                var c = grid.WorldToCell(ch.position);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    occupied.Add(new Vector3Int(c.x + dx, c.y + dy, 0));
            }

        // The water tilemaps carry a tile under every land tile too (all 1466 of them),
        // so "has a water tile" on its own would scatter rocks across the grass. Open
        // sea is the water that no land tile covers.
        var waterList = water.Where(c => !land.Contains(c)).ToList();
        // Only open, walkable ground may grow a bush. land.Contains() alone is not
        // enough: a cell can carry a plateau-top tile AND a cliff tile at once, and
        // those are the walls bushes were showing up on. TerrainWalkable excludes the
        // cliff/blocked cells, so nothing sprouts out of a rock face any more.
        var landList = land.Where(c => grid.TerrainWalkable(c)).ToList();
        for (int i = waterList.Count - 1; i > 0; i--)
        { int j = rng.Next(i + 1); (waterList[i], waterList[j]) = (waterList[j], waterList[i]); }

        // shore = open sea touching land — where rocks look like they broke off the coast
        var shore = waterList.Where(c => Neighbours(c).Any(n => land.Contains(n))).ToList();

        var mapCb = tilemaps.TryGetValue("Level0_Ground", out var gm0) ? gm0.cellBounds : new BoundsInt();
        float left = mapCb.xMin - 8f, right = mapCb.xMax + 8f;
        float top = mapCb.yMax;
        int placedBush = 0, placedRock = 0, placedCloud = 0;

        // ---- bushes: clumps on grass, never on a building or a blocking prop ----
        int guard = 0;
        while (placedBush < bushes && guard++ < 20000)
        {
            var seedCell = landList[rng.Next(landList.Count)];
            if (occupied.Contains(seedCell) || !grid.TerrainWalkable(seedCell)) continue;
            int clump = 1 + rng.Next(3);
            for (int k = 0; k < clump && placedBush < bushes; k++)
            {
                var c = seedCell + new Vector3Int(rng.Next(-1, 2), rng.Next(-1, 2), 0);
                if (!grid.TerrainWalkable(c) || occupied.Contains(c)) continue;
                Place(root, bushPool[rng.Next(bushPool.Length)], c, 0f);
                occupied.Add(c);
                placedBush++;
            }
        }

        // ---- water rocks, biased to the shoreline ----
        guard = 0;
        while (placedRock < waterRocks && guard++ < 20000)
        {
            var c = (rng.Next(100) < 70 && shore.Count > 0)
                  ? shore[rng.Next(shore.Count)]
                  : waterList[rng.Next(waterList.Count)];
            if (occupied.Contains(c)) continue;
            var pool = rng.Next(100) < 25 && bigRockPool.Length > 0 ? bigRockPool : rockPool;
            Place(root, pool[rng.Next(pool.Length)], c, 0f);
            occupied.Add(c);
            placedRock++;
        }

        // ---- clouds: a thin band just above the tallest land, so they are actually
        // on screen when the camera is framed on the map, but never over the action ----
        float span = right - left;
        float skyLow = top + 2f, skyHigh = top + 6f;
        foreach (var _ in cloudPool.Take(clouds))
        {
            var cloud = cloudPool[rng.Next(cloudPool.Length)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(cloud);
            go.transform.position = new Vector3(
                left + (float)rng.NextDouble() * span,
                skyLow + (float)rng.NextDouble() * (skyHigh - skyLow),
                0f);
            go.transform.localScale = Vector3.one * (0.45f + (float)rng.NextDouble() * 0.4f);
            go.transform.SetParent(root.transform, true);
            var d = go.GetComponent<CloudDrift>();
            if (d != null) { d.minX = left - 12f; d.maxX = right + 12f; d.speed = 0.22f + (float)rng.NextDouble() * 0.3f; }
            placedCloud++;
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[点缀] 完成：灌木 {placedBush} 丛，水岩 {placedRock} 块，云 {placedCloud} 朵。"
                + $"全部不阻挡通行，MapGrid 未改动。陆地 {land.Count} 格，纯水 {waterList.Count} 格，其中岸边 {shore.Count} 格。");
    }

    static IEnumerable<Vector3Int> Neighbours(Vector3Int c)
    {
        yield return c + new Vector3Int(1, 0, 0);
        yield return c + new Vector3Int(-1, 0, 0);
        yield return c + new Vector3Int(0, 1, 0);
        yield return c + new Vector3Int(0, -1, 0);
        yield return c + new Vector3Int(1, 1, 0);
        yield return c + new Vector3Int(1, -1, 0);
        yield return c + new Vector3Int(-1, 1, 0);
        yield return c + new Vector3Int(-1, -1, 0);
    }

    /// <summary>Garnish sits on the ground point of its cell, like every other prop.</summary>
    static GameObject Place(GameObject parent, GameObject prefab, Vector3Int cell, float zRot)
    {
        if (prefab == null) return null;
        var pos = MapGrid.Instance.CellCenter(cell) - new Vector3(0f, 0.5f, 0f);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, 0f, zRot);
        go.transform.SetParent(parent.transform, true);
        return go;
    }
}
