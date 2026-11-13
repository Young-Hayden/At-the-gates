using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Phase A Task 3: adds the three neutral meat sheep to the centre of
/// SampleScene, once and idempotently. Every lookup and the save are scoped to
/// SampleScene; a scene opened by this invocation is the only one it closes.
///
/// Placement follows the controller amendments: each sheep is a 2x1 footprint
/// whose origin sits on the grid line at (nominal.x + 1, nominal.y), so the
/// blocked cells are exactly (nominal.x, nominal.y) and (nominal.x + 1, nominal.y)
/// and the 2-unit-wide sprite spans the same two cells.
///
/// Before saving the tool validates that the six cells are pairwise distinct,
/// are free of other buildings (registered or scene-serialized), are walkable
/// terrain, and share one connected region with every blue and red starting
/// unit. Any failure destroys what this invocation created and throws without
/// touching the scene file.</summary>
public static class AddCentralMeat
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string PrefabPath = "Assets/Prefabs/Resources/HappySheep_Idle.prefab";
    const string LogRelativePath = ".superpowers/sdd/phase-a/task-3-tool.log";

    static readonly Vector3Int[] NominalCells =
    {
        new Vector3Int(18, 3, 0),
        new Vector3Int(19, 5, 0),
        new Vector3Int(22, 7, 0),
    };

    [MenuItem("Tools/阶段A/添加中央羊群")]
    public static void Add()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[中央羊群] Leave PlayMode before adding the central sheep.");
            return;
        }

        var log = new List<string>();
        try
        {
            Run(log);
        }
        finally
        {
            WriteLog(log);
        }
    }

    static void Run(List<string> log)
    {
        var scene = FindSampleScene();
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedHere = true;
            log.Add("Opened " + ScenePath + " additively.");
        }
        else
        {
            log.Add("SampleScene was already open; operating on the loaded scene.");
        }

        try
        {
            var roots = scene.GetRootGameObjects();
            var grid = roots.SelectMany(g => g.GetComponentsInChildren<MapGrid>(true)).FirstOrDefault();
            if (grid == null) throw new System.InvalidOperationException("MapGrid missing in SampleScene.");

            var created = new List<GameObject>();
            try
            {
                var sheep = ResolveSheep(scene, roots, created, log);
                Validate(sheep, grid, scene, log);
                BackupSceneFile(log);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new System.InvalidOperationException("Could not save " + ScenePath);
                log.Add("Saved " + ScenePath + ".");
                Debug.Log("[中央羊群] 三只中央羊群已就位并通过校验，SampleScene 已保存。详见 " + LogRelativePath);
            }
            catch
            {
                foreach (var go in created)
                    if (go != null) Object.DestroyImmediate(go);
                if (created.Count > 0)
                    log.Add("Rolled back " + created.Count + " instance(s) created by this invocation; scene NOT saved.");
                throw;
            }
        }
        finally
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>Looks for an already-loaded SampleScene before opening anything:
    /// opening an already-open scene additively would load a second copy of the
    /// whole map into the hierarchy.</summary>
    static Scene FindSampleScene()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var candidate = EditorSceneManager.GetSceneAt(i);
            if (candidate.path == ScenePath && candidate.isLoaded)
                return candidate;
        }
        return default;
    }

    /// <summary>Returns the three sheep buildings, creating only the ones whose
    /// names are not in the scene yet (idempotent re-runs).</summary>
    static Building[] ResolveSheep(Scene scene, GameObject[] roots, List<GameObject> created, List<string> log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new System.InvalidOperationException("Missing prefab: " + PrefabPath);

        var byName = roots
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name.StartsWith("Sheep_Meat_"))
            .GroupBy(t => t.name)
            .ToDictionary(g => g.Key, g => g.First().gameObject);

        var result = new Building[NominalCells.Length];
        for (int i = 0; i < NominalCells.Length; i++)
        {
            string name = "Sheep_Meat_" + (i + 1);
            if (byName.TryGetValue(name, out var existing))
            {
                // Converge an already-present sheep to the desired state instead of
                // blindly skipping it, so a re-run after a cell ruling relocates it.
                var existingBuilding = existing.GetComponent<Building>();
                var existingNode = existing.GetComponent<ResourceNode>();
                if (existingBuilding == null || existingNode == null)
                    throw new System.InvalidOperationException(name + " lost its Building/ResourceNode.");
                var wanted = new Vector3(NominalCells[i].x + 1f, NominalCells[i].y, 0f);
                bool moved = existing.transform.position != wanted;
                if (moved) existing.transform.position = wanted;
                existingBuilding.footprint = new Vector2Int(2, 1);
                existingBuilding.team = Team.Neutral;
                existingNode.regrowSeconds = 120f;
                existingNode.blockingBuilding = existingBuilding;
                existingBuilding.Prepare();
                result[i] = existingBuilding;
                log.Add(name + (moved
                    ? " existed at the wrong position; relocated to " + wanted + "."
                    : " already in place; fields converged."));
                continue;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            created.Add(go);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.name = name;
            // Amendment A2: origin on the grid line at the right edge of the nominal cell.
            go.transform.position = new Vector3(NominalCells[i].x + 1f, NominalCells[i].y, 0f);

            var building = go.GetComponent<Building>();
            var node = go.GetComponent<ResourceNode>();
            if (building == null || node == null)
                throw new System.InvalidOperationException(name + ": prefab lost Building/ResourceNode.");

            building.footprint = new Vector2Int(2, 1);   // pinned, not sprite-measured
            building.team = Team.Neutral;
            node.regrowSeconds = 120f;
            node.blockingBuilding = building;            // depleted sheep stop blocking
            building.Prepare();

            result[i] = building;
            log.Add(name + " created at " + go.transform.position + " for nominal cell " + NominalCells[i] + ".");
        }

        for (int i = 0; i < result.Length; i++)
            if (result[i] == null)
                throw new System.InvalidOperationException("Sheep_Meat_" + (i + 1) + " has no Building component.");
        return result;
    }

    static void Validate(Building[] sheep, MapGrid grid, Scene scene, List<string> log)
    {
        // 1. Each sheep blocks exactly its nominal pair, and the six cells are distinct.
        var six = new List<Vector3Int>();
        for (int i = 0; i < sheep.Length; i++)
        {
            sheep[i].Prepare();
            var expected = new[]
            {
                NominalCells[i],
                NominalCells[i] + new Vector3Int(1, 0, 0),
            };
            if (!sheep[i].Cells.SequenceEqual(expected))
                throw new System.InvalidOperationException(
                    "Sheep_Meat_" + (i + 1) + " footprint resolved to [" +
                    string.Join(", ", sheep[i].Cells) + "], expected [" +
                    string.Join(", ", expected) + "].");
            six.AddRange(expected);
        }
        if (six.Distinct().Count() != six.Count)
            throw new System.InvalidOperationException("The six sheep cells are not pairwise distinct: " +
                                                         string.Join(", ", six));

        // 2. Terrain is walkable and no OTHER building holds any of the cells —
        //    neither a registered dynamic blocker nor a scene-serialized footprint
        //    (scene buildings only register at runtime, so scan them directly).
        var otherCells = new Dictionary<Vector3Int, string>();
        foreach (var b in scene.GetRootGameObjects()
                     .SelectMany(g => g.GetComponentsInChildren<Building>(true)))
        {
            if (sheep.Contains(b)) continue;
            b.Prepare();
            foreach (var c in b.Cells)
                otherCells[c] = b.name;
        }
        foreach (var c in six)
        {
            if (!grid.TerrainWalkable(c))
                throw new System.InvalidOperationException(c + " is not walkable terrain.");
            if (grid.IsBlockedByBuilding(c))
                throw new System.InvalidOperationException(c + " is already registered as blocked by a building.");
            if (otherCells.TryGetValue(c, out var owner))
                throw new System.InvalidOperationException(c + " is already occupied by scene building " + owner + ".");
        }

        // 3. One shared region for the six cells and every blue/red starting unit.
        int region = grid.Region(six[0]);
        if (region < 0)
            throw new System.InvalidOperationException(six[0] + " has no walkable region.");
        foreach (var c in six.Skip(1))
            if (grid.Region(c) != region)
                throw new System.InvalidOperationException(
                    "Sheep cell " + c + " is in region " + grid.Region(c) + ", expected " + region + ".");

        var units = Object.FindObjectsByType<UnitActor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(u => u.gameObject.scene == scene && (u.team == Team.Blue || u.team == Team.Red))
            .ToArray();
        if (!units.Any(u => u.team == Team.Blue))
            throw new System.InvalidOperationException("No blue starting units found in SampleScene.");
        if (!units.Any(u => u.team == Team.Red))
            throw new System.InvalidOperationException("No red starting units found in SampleScene.");
        foreach (var u in units)
        {
            var cell = grid.WorldToCell(u.transform.position);
            if (grid.Region(cell) != region)
                throw new System.InvalidOperationException(
                    "Starting unit " + u.name + " at " + cell + " is in region " +
                    grid.Region(cell) + ", but the sheep are in region " + region + ".");
        }

        // 4. Runtime-connectivity guard. In edit mode no Building registers its
        //    footprint, so steps 2-3 see terrain only. At play time every
        //    blocksMovement building (trees, decor, the sheep) reserves its cells,
        //    which can narrow a corridor to a single cut vertex — and a sheep placed
        //    on that vertex walls one base off from the other. Simulate the runtime
        //    reservation here and require the two starting armies to remain in one
        //    connected region, and every sheep to stay harvest-reachable from it
        //    (§A5, as amended by §A1b); roll back the simulation afterwards.
        var blockers = scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Building>(true))
            .Where(b => b.blocksMovement)
            .ToArray();
        foreach (var b in blockers) { b.Prepare(); grid.AddBuildingFootprint(b.Cells); }
        try
        {
            var blueStart = grid.ResolveWalkableStart(
                grid.WorldToCell(units.First(u => u.team == Team.Blue).transform.position));
            var redStart = grid.ResolveWalkableStart(
                grid.WorldToCell(units.First(u => u.team == Team.Red).transform.position));
            if (!blueStart.HasValue || !redStart.HasValue)
                throw new System.InvalidOperationException("A starting unit is sealed inside blockers.");
            int blueRegion = grid.Region(blueStart.Value);
            int redRegion = grid.Region(redStart.Value);
            if (blueRegion < 0 || blueRegion != redRegion)
                throw new System.InvalidOperationException(
                    "The sheep seal a cut vertex: with all footprints reserved the blue base (region " +
                    blueRegion + ") and red base (region " + redRegion +
                    ") are no longer connected. Move the sheep off the corridor.");

            // Harvest reachability from the shared region, using the same probe
            // Gatherer dispatch (ResourceNode.FindNearestReachable) relies on.
            for (int i = 0; i < sheep.Length; i++)
            {
                var nodeCell = grid.WorldToCell(sheep[i].transform.position);
                var approach = grid.NearestWalkableWithin(
                    blueStart.Value, nodeCell, 4, blueRegion, 1.6f);
                if (!approach.HasValue)
                    throw new System.InvalidOperationException(
                        "Sheep_Meat_" + (i + 1) + " at " + nodeCell +
                        " is not harvest-reachable from the shared starting region " +
                        blueRegion + ".");
            }

            log.Add("Runtime-connectivity guard OK: blue and red stay in one region (" +
                    blueRegion + ", size " + grid.RegionSize(blueRegion) +
                    ") and all three sheep are harvest-reachable, with all footprints reserved.");
        }
        finally
        {
            foreach (var b in blockers) grid.RemoveBuildingFootprint(b.Cells);
        }

        log.Add("Validation OK: 6 distinct cells, no other building, terrain walkable.");
        log.Add("Region " + region + " (size " + grid.RegionSize(region) + ") shared by all sheep and " +
                units.Length + " blue/red starting units (" +
                units.Count(u => u.team == Team.Blue) + " blue, " +
                units.Count(u => u.team == Team.Red) + " red).");
        Debug.Log("[中央羊群] 校验通过：region=" + region + " size=" + grid.RegionSize(region) +
                  " 起始单位=" + units.Length);
    }

    static void BackupSceneFile(List<string> log)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string backup = Path.Combine(projectRoot,
            ".superpowers/sdd/phase-a/task-3-scene-before.scene".Replace('/', Path.DirectorySeparatorChar));
        string sceneFile = Path.Combine(projectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(backup))
        {
            log.Add("Backup already exists, left untouched: " + backup);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        File.Copy(sceneFile, backup, false);
        log.Add("Backed up scene file to " + backup);
    }

    static void WriteLog(List<string> lines)
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(
                Path.Combine(Application.dataPath, ".."),
                LogRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[中央羊群] Could not write " + LogRelativePath + ": " + e.Message);
        }
    }
}
