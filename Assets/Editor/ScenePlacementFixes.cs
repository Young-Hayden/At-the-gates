using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Targeted, idempotent repair for the audited scene-placement defects.
/// Does not regenerate the map or any decoration group. Every lookup and save is
/// scoped to SampleScene, and only a scene opened by this invocation is closed.</summary>
public static class ScenePlacementFixes
{
    [MenuItem("Tools/场景/修复已审计的落点问题")]
    public static void Apply()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.scene");
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene", OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var grid = roots.SelectMany(g => g.GetComponentsInChildren<MapGrid>(true)).FirstOrDefault();
            if (grid == null) throw new System.InvalidOperationException("MapGrid missing in SampleScene");
            var buildings = roots.SelectMany(g => g.GetComponentsInChildren<Building>(true)).ToArray();
            var occupied = new Dictionary<Vector3Int, int>();
            foreach (var b in buildings)
            {
                b.Prepare();
                foreach (var c in b.Cells) AddOwner(occupied, c);
            }

            var decor = roots.FirstOrDefault(g => g.name == "Decor");
            var decorCells = new HashSet<Vector3Int>();
            if (decor != null)
                foreach (Transform ch in decor.transform) decorCells.Add(grid.WorldToCell(ch.position));

            var garnish = roots.FirstOrDefault(g => g.name == "Garnish");
            if (garnish == null) throw new System.InvalidOperationException("Garnish missing in SampleScene");
            var garnishCells = new HashSet<Vector3Int>();
            foreach (Transform ch in garnish.transform) garnishCells.Add(grid.WorldToCell(ch.position));

            bool Valid(Vector3Int c)
            {
                return grid.TerrainWalkable(c) && !grid.IsRamp(c)
                    && !occupied.ContainsKey(c) && !decorCells.Contains(c) && !garnishCells.Contains(c);
            }

            Vector3 NearestValid(Vector3 original)
            {
                var around = grid.WorldToCell(original);
                for (int r = 0; r <= 4; r++)
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = around + new Vector3Int(dx, dy, 0);
                    if (Valid(c)) return grid.CellCenter(c) - new Vector3(0, 0.5f, 0);
                }
                throw new System.InvalidOperationException("No valid cell within 4 tiles of " + original);
            }

            var redBush = garnish.transform.Cast<Transform>().FirstOrDefault(ch =>
                ch.name == "Bush2" && Vector2.Distance(ch.position, new Vector2(35.5f, 6f)) < 0.01f);
            if (redBush != null)
            {
                var originalCell = grid.WorldToCell(redBush.position);
                garnishCells.Remove(originalCell);
                var next = NearestValid(redBush.position);
                if (next != redBush.position) redBush.position = next;
                garnishCells.Add(grid.WorldToCell(next));
            }

            if (decor != null)
            {
                var deco = decor.transform.Cast<Transform>().FirstOrDefault(ch =>
                    ch.name == "Deco_01" && Vector2.Distance(ch.position, new Vector2(-35.5f, -9f)) < 0.01f);
                if (deco != null)
                {
                    var originalCell = grid.WorldToCell(deco.position);
                    var building = deco.GetComponent<Building>();
                    if (building != null)
                        foreach (var c in building.Cells) RemoveOwner(occupied, c);
                    decorCells.Remove(originalCell);

                    var next = NearestValid(deco.position);
                    if (next != deco.position)
                    {
                        deco.position = next;
                        if (building != null) building.Recompute();
                    }

                    if (building != null)
                        foreach (var c in building.Cells) AddOwner(occupied, c);
                    decorCells.Add(grid.WorldToCell(next));
                }
            }

            int removed = 0;
            foreach (var group in garnish.transform.Cast<Transform>().GroupBy(ch => ch.position).Where(g => g.Count() > 1).ToArray())
            {
                foreach (var duplicate in group.Skip(1).ToArray())
                {
                    Object.DestroyImmediate(duplicate.gameObject);
                    removed++;
                }
            }

            GarnishRules.Invalidate();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[场景落点修复] 已处理红矿草丛与 Deco/Tree 重叠；删除重复水岩 " + removed);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void AddOwner(Dictionary<Vector3Int, int> owners, Vector3Int cell)
    {
        owners[cell] = owners.TryGetValue(cell, out int count) ? count + 1 : 1;
    }

    static void RemoveOwner(Dictionary<Vector3Int, int> owners, Vector3Int cell)
    {
        if (!owners.TryGetValue(cell, out int count)) return;
        if (count <= 1) owners.Remove(cell);
        else owners[cell] = count - 1;
    }
}
