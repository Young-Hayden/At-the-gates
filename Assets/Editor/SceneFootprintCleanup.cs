using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Targeted, one-shot repair for blocking buildings whose root cell lies outside
/// their own computed footprint.
///
/// Policy (fixed by the task): each offending object is aligned to the centre of
/// its CURRENT root cell, never to the current computed footprint. If that root
/// cell is not legal ground — not TerrainWalkable, a ramp, or occupied by another
/// building after accounting for candidates already moved — the object is left
/// untouched and reported. There is deliberately no nearest-cell fallback.
///
/// Scope is the saved SampleScene only. The map is not regenerated, decoration
/// groups are not reordered or culled, and no other scene is touched.
/// </summary>
public static class SceneFootprintCleanup
{
    public const string ScenePath = "Assets/Scenes/SampleScene.scene";
    public const string MenuPath = "Tools/Project Doctor/Align Scene Roots To Current Cells";
    public const string ReportRelativePath = "Logs/FootprintCleanup.txt";
    public const string BackupRelativeDirectory = "Backups";
    const string BackupPrefix = "SampleScene-before-footprint-cleanup-";

    public sealed class Result
    {
        public string ScenePath { get; internal set; }
        public string ReportPath { get; internal set; }
        public string BackupPath { get; internal set; }
        public List<string> Moved { get; internal set; }
        /// <summary>Per-object skips only — one line per object that was not moved.</summary>
        public List<string> Skipped { get; internal set; }
        /// <summary>Scene/tool messages (open/save/warnings/fatal), kept out of
        /// <see cref="SkippedCount"/> so a rerun's no-op count stays unambiguous.</summary>
        public List<string> Messages { get; internal set; }
        public int MovedCount { get { return Moved.Count; } }
        public int SkippedCount { get { return Skipped.Count; } }
        public int MessageCount { get { return Messages.Count; } }
    }

    [MenuItem(MenuPath)]
    public static void RunMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[FootprintCleanup] Leave PlayMode before running the cleanup.");
            return;
        }

        Run();
    }

    /// <summary>Runs the repair and writes the text report. Returns the outcome so
    /// tests and callers can inspect the exact moved/skipped lists.</summary>
    public static Result Run()
    {
        string reportPath = Path.GetFullPath(Path.Combine(
            Path.Combine(Application.dataPath, ".."), ReportRelativePath));
        var result = new Result
        {
            ScenePath = ScenePath,
            ReportPath = reportPath,
            BackupPath = null,
            Moved = new List<string>(),
            Skipped = new List<string>(),
            Messages = new List<string>(),
        };

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        Scene previousActive = SceneManager.GetActiveScene();
        bool saveAttempted = false;

        try
        {
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                result.Messages.Add("<scene> | could not open " + ScenePath);
                return result;
            }

            bool wasDirty = scene.isDirty;
            var roots = scene.GetRootGameObjects();
            MapGrid grid = roots
                .SelectMany(r => r.GetComponentsInChildren<MapGrid>(true))
                .Where(g => g != null)
                .OrderBy(g => PathOf(g.transform), StringComparer.Ordinal)
                .FirstOrDefault();

            if (grid == null || grid.ground0 == null)
            {
                result.Messages.Add("<scene> | MapGrid (or its ground0 tilemap) is missing; nothing moved");
                return result;
            }

            // Only blocking buildings participate, exactly as the Doctor audits them.
            var buildings = roots
                .SelectMany(r => r.GetComponentsInChildren<Building>(true))
                .Where(b => b != null && b.blocksMovement)
                .ToArray();

            // One preparation pass so Width/Depth/Cells are coherent for every owner.
            var candidates = new List<Building>();
            foreach (var building in buildings)
            {
                building.Prepare();
                Vector3Int rootCell = grid.WorldToCell(building.transform.position);
                if (!ContainsCell(building.Cells, rootCell))
                    candidates.Add(building);
            }

            // Occupancy is ref-counted because a cell can in principle carry more
            // than one building; every registration must leave before it is free.
            // The owner path travels with each registration so a refusal can name
            // exactly which building occupies the target cell.
            var owners = new Dictionary<Vector3Int, List<string>>();
            foreach (var building in buildings)
            {
                string ownerKey = Describe(building);
                foreach (var cell in building.Cells)
                    AddOwner(owners, cell, ownerKey);
            }

            // Fixed processing order: instanceID. No spatial or name-based sorting.
            candidates.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

            foreach (var building in candidates)
            {
                string objectPath = PathOf(building.transform);
                string ownerKey = Describe(building);
                int instanceId = building.GetInstanceID();
                Vector3Int rootCell = grid.WorldToCell(building.transform.position);
                Vector3 oldPosition = building.transform.position;
                var oldCells = new List<Vector3Int>(building.Cells);

                bool ownCellsRemoved = false;
                bool moved = false;
                try
                {
                    if (building.Width != 1 || building.Depth != 1)
                    {
                        result.Skipped.Add(FormatSkip(objectPath, instanceId, rootCell,
                            "footprint is " + building.Width + "x" + building.Depth +
                            "; only 1x1 objects are moved by this policy"));
                        continue;
                    }

                    // This object is a candidate that would move, so its own current
                    // cells must not count as a collision.
                    foreach (var cell in oldCells) RemoveOwner(owners, cell, ownerKey);
                    ownCellsRemoved = true;

                    string reason;
                    if (!IsTargetLegal(grid, owners, rootCell, out reason))
                    {
                        foreach (var cell in oldCells) AddOwner(owners, cell, ownerKey);
                        ownCellsRemoved = false;
                        result.Skipped.Add(FormatSkip(objectPath, instanceId, rootCell, reason));
                        continue;
                    }

                    Vector3 newPosition = grid.CellCenter(rootCell) - new Vector3(0f, 0.5f, 0f);
                    building.transform.position = newPosition;
                    moved = true;
                    building.Recompute();

                    bool recomputeOk = ContainsCell(building.Cells, rootCell);
                    foreach (var cell in building.Cells)
                    {
                        if (!grid.TerrainWalkable(cell) || grid.IsRamp(cell))
                        {
                            recomputeOk = false;
                            break;
                        }
                    }

                    if (!recomputeOk)
                    {
                        // Reject the move atomically: restore the old transform and
                        // its reservation before recording the skip.
                        building.transform.position = oldPosition;
                        building.Recompute();
                        moved = false;
                        foreach (var cell in oldCells) AddOwner(owners, cell, ownerKey);
                        ownCellsRemoved = false;
                        result.Skipped.Add(FormatSkip(objectPath, instanceId, rootCell,
                            "post-move verification failed; object restored to its original position"));
                        continue;
                    }

                    foreach (var cell in building.Cells) AddOwner(owners, cell, ownerKey);
                    ownCellsRemoved = false;
                    result.Moved.Add(
                        "[Move] " + objectPath +
                        " | instanceID " + instanceId.ToString(CultureInfo.InvariantCulture) +
                        " | root cell " + FormatCell(rootCell) +
                        " | position " + FormatPosition(oldPosition) + " -> " + FormatPosition(newPosition) +
                        " | cells " + FormatCells(building.Cells));
                }
                catch (Exception e)
                {
                    // Leave the object and the occupancy map consistent: a failed
                    // candidate must end where it started.
                    if (ownCellsRemoved)
                    {
                        if (moved)
                        {
                            try
                            {
                                building.transform.position = oldPosition;
                                building.Recompute();
                            }
                            catch (Exception restoreError)
                            {
                                result.Messages.Add("<restore> | " + objectPath + " | " +
                                    restoreError.GetType().Name + ": " + restoreError.Message);
                            }
                        }
                        foreach (var cell in oldCells) AddOwner(owners, cell, ownerKey);
                        ownCellsRemoved = false;
                    }
                    result.Skipped.Add(FormatSkip(objectPath, instanceId, rootCell,
                        "exception: " + e.Message));
                }
            }

            if (result.MovedCount > 0)
            {
                result.BackupPath = CreateBackup();
                saveAttempted = true;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    result.Messages.Add("<save> | SaveScene returned false for " + ScenePath);
                }
                else if (scene.isDirty)
                {
                    result.Messages.Add("<save> | scene still reports dirty after save");
                }
            }
            else
            {
                // No move: do not save, do not create a backup, do not dirty the scene.
                result.Messages.Add("<save> | skipped: no object was moved");
            }

            if (wasDirty)
            {
                result.Messages.Add("<scene> | warning: scene was already dirty before this run; " +
                    "the backup reflects the last on-disk version");
            }
        }
        catch (Exception e)
        {
            result.Messages.Add("<fatal> | " + e.GetType().Name + ": " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            WriteReport(result);
            LogSummary(result);

            if (openedHere && scene.IsValid() && scene.isLoaded)
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        if (saveAttempted && result.MovedCount > 0)
            Debug.Log("[FootprintCleanup] SampleScene saved.");

        return result;
    }

    static bool IsTargetLegal(MapGrid grid, Dictionary<Vector3Int, List<string>> owners,
                              Vector3Int cell, out string reason)
    {
        if (!grid.TerrainWalkable(cell))
        {
            reason = "target cell is not TerrainWalkable";
            return false;
        }
        if (grid.IsRamp(cell))
        {
            reason = "target cell is a ramp";
            return false;
        }
        if (owners.TryGetValue(cell, out var occupants) && occupants.Count > 0)
        {
            reason = "target cell is occupied by " + occupants[occupants.Count - 1];
            return false;
        }
        reason = null;
        return true;
    }

    static string CreateBackup()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string backupDir = Path.Combine(projectRoot, BackupRelativeDirectory);
        Directory.CreateDirectory(backupDir);

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(backupDir, BackupPrefix + stamp + ".scene");
        int suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(backupDir, BackupPrefix + stamp + "-" + suffix + ".scene");
            suffix++;
        }

        string source = Path.Combine(projectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar));
        File.Copy(source, path, false);
        return path;
    }

    static void WriteReport(Result result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Scene Footprint Cleanup");
        sb.AppendLine("Scene: " + result.ScenePath);
        sb.AppendLine("Menu: " + MenuPath);
        sb.AppendLine("Backup: " + (string.IsNullOrEmpty(result.BackupPath) ? "<none>" : result.BackupPath));
        sb.AppendLine("Moved: " + result.MovedCount.ToString(CultureInfo.InvariantCulture) +
                      "  ObjectSkips: " + result.SkippedCount.ToString(CultureInfo.InvariantCulture) +
                      "  ToolMessages: " + result.MessageCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
        foreach (var line in result.Moved) sb.AppendLine(line);
        foreach (var line in result.Skipped) sb.AppendLine(line);
        foreach (var line in result.Messages) sb.AppendLine(line);

        string directory = Path.GetDirectoryName(result.ReportPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(result.ReportPath, sb.ToString(), new UTF8Encoding(false));
    }

    static void LogSummary(Result result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "[FootprintCleanup] Moved={0} ObjectSkips={1} ToolMessages={2} | backup: {3} | report: {4}",
            result.MovedCount, result.SkippedCount, result.MessageCount,
            string.IsNullOrEmpty(result.BackupPath) ? "<none>" : result.BackupPath,
            result.ReportPath));
        foreach (var line in result.Moved) sb.AppendLine(line);
        foreach (var line in result.Skipped) sb.AppendLine(line);
        foreach (var line in result.Messages) sb.AppendLine(line);
        Debug.Log(sb.ToString().TrimEnd());
    }

    // ---------------------------------------------------------------- helpers

    static bool ContainsCell(IReadOnlyList<Vector3Int> cells, Vector3Int cell)
    {
        if (cells == null) return false;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i] == cell) return true;
        return false;
    }

    static void AddOwner(Dictionary<Vector3Int, List<string>> owners, Vector3Int cell, string owner)
    {
        if (!owners.TryGetValue(cell, out var list))
        {
            list = new List<string>();
            owners[cell] = list;
        }
        list.Add(owner);
    }

    static void RemoveOwner(Dictionary<Vector3Int, List<string>> owners, Vector3Int cell, string owner)
    {
        if (!owners.TryGetValue(cell, out var list)) return;
        list.Remove(owner);
        if (list.Count == 0) owners.Remove(cell);
    }

    static string Describe(Building building)
    {
        return PathOf(building.transform) +
               " (instanceID " + building.GetInstanceID().ToString(CultureInfo.InvariantCulture) + ")";
    }

    static string FormatSkip(string objectPath, int instanceId, Vector3Int rootCell, string reason)
    {
        return "[Skip] " + objectPath +
               " | instanceID " + instanceId.ToString(CultureInfo.InvariantCulture) +
               " | root cell " + FormatCell(rootCell) +
               " | " + reason;
    }

    static string PathOf(Transform transform)
    {
        if (transform == null) return "<null>";
        var parts = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    static string FormatCell(Vector3Int cell)
    {
        return "(" + cell.x.ToString(CultureInfo.InvariantCulture) + ", " +
               cell.y.ToString(CultureInfo.InvariantCulture) + ")";
    }

    static string FormatCells(IReadOnlyList<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return "<none>";
        var parts = new string[cells.Count];
        for (int i = 0; i < cells.Count; i++) parts[i] = FormatCell(cells[i]);
        return string.Join(", ", parts);
    }

    static string FormatPosition(Vector3 position)
    {
        return "(" + position.x.ToString("0.###", CultureInfo.InvariantCulture) + ", " +
               position.y.ToString("0.###", CultureInfo.InvariantCulture) + ", " +
               position.z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }
}
