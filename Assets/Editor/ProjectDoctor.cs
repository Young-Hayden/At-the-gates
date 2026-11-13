using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Severity of one Doctor finding.</summary>
public enum ProjectDoctorSeverity
{
    Error,
    Warning,
}

/// <summary>Fixed report categories. The integer value is also the report order.</summary>
public enum ProjectDoctorIssueKind
{
    Footprint = 0,
    Terrain = 1,
    Ramp = 2,
    Duplicate = 3,
    Manager = 4,
    MissingRef = 5,
    Texture = 6,
    Animation = 7,
}

/// <summary>One read-only finding. The text is already in the stable report format.</summary>
public sealed class ProjectDoctorIssue
{
    public ProjectDoctorIssueKind Kind { get; private set; }
    public ProjectDoctorSeverity Severity { get; private set; }
    public string Text { get; private set; }

    internal ProjectDoctorIssue(ProjectDoctorIssueKind kind, ProjectDoctorSeverity severity, string text)
    {
        Kind = kind;
        Severity = severity;
        Text = text;
    }
}

/// <summary>
/// Read-only project audit. It never repairs, saves, dirties, reimports, or changes
/// Build Settings. Called by the Tools/Project Doctor/Run Audit menu and by tests.
/// </summary>
public static class ProjectDoctor
{
    public const string MenuPath = "Tools/Project Doctor/Run Audit";
    public const string ReportRelativePath = "Logs/ProjectDoctor.txt";

    /// <summary>Manager components that must exist at most once in the active scene.</summary>
    static readonly Type[] SingletonManagerTypes =
    {
        typeof(MapGrid),
        typeof(Economy),
        typeof(GameState),
        typeof(BuildingPlacer),
        typeof(SelectionManager),
        typeof(AIController),
        typeof(HUD),
        typeof(CursorManager),
        typeof(FxBank),
        typeof(RTSCamera),
        typeof(Minimap),
    };

    public sealed class Report
    {
        public List<ProjectDoctorIssue> Issues { get; private set; }
        public string ScenePath { get; private set; }
        public string ReportPath { get; private set; }

        public int ErrorCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == ProjectDoctorSeverity.Error) n++;
                return n;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == ProjectDoctorSeverity.Warning) n++;
                return n;
            }
        }

        internal Report(string scenePath, string reportPath)
        {
            Issues = new List<ProjectDoctorIssue>();
            ScenePath = scenePath;
            ReportPath = reportPath;
        }

        internal void Add(ProjectDoctorIssueKind kind, ProjectDoctorSeverity severity, string text)
        {
            Issues.Add(new ProjectDoctorIssue(kind, severity, text));
        }

        public int CountOf(ProjectDoctorIssueKind kind)
        {
            int n = 0;
            for (int i = 0; i < Issues.Count; i++)
                if (Issues[i].Kind == kind) n++;
            return n;
        }

        internal void SortIssues()
        {
            Issues.Sort((a, b) =>
            {
                int c = ((int)a.Kind).CompareTo((int)b.Kind);
                return c != 0 ? c : string.CompareOrdinal(a.Text, b.Text);
            });
        }
    }

    [MenuItem(MenuPath)]
    public static void RunAuditMenu()
    {
        RunAudit();
    }

    /// <summary>Runs every read-only check, writes the grouped text report, and logs
    /// a concise summary. Returns the findings for callers that need them.</summary>
    public static Report RunAudit()
    {
        var scene = SceneManager.GetActiveScene();
        string scenePath = scene.IsValid() && !string.IsNullOrEmpty(scene.path)
            ? scene.path
            : "<unsaved active scene>";
        string reportPath = Path.GetFullPath(Path.Combine(
            Path.Combine(Application.dataPath, ".."), ReportRelativePath));

        var report = new Report(scenePath, reportPath);

        // Each audit family is isolated: one unexpected failure still leaves a
        // complete, written report and a concise console summary.
        try
        {
            AuditActiveScene(report, scene);
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.Manager, ProjectDoctorSeverity.Error,
                "[Manager] <scene audit> | scene audit failed: " + e.Message);
        }

        try
        {
            AuditPrefabs(report);
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                "[MissingRef] <prefab scan> | prefab audit failed: " + e.Message);
        }

        try
        {
            AuditWorldTextures(report);
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.Texture, ProjectDoctorSeverity.Error,
                "[Texture] <texture scan> | texture audit failed: " + e.Message);
        }

        report.SortIssues();
        WriteReport(report);

        Debug.Log(string.Format(
            "[ProjectDoctor] Audit complete. Errors={0} Warnings={1} | Footprint={2} Terrain={3} Ramp={4} Duplicate={5} Manager={6} MissingRef={7} Texture={8} Animation={9} | report: {10}",
            report.ErrorCount, report.WarningCount,
            report.CountOf(ProjectDoctorIssueKind.Footprint),
            report.CountOf(ProjectDoctorIssueKind.Terrain),
            report.CountOf(ProjectDoctorIssueKind.Ramp),
            report.CountOf(ProjectDoctorIssueKind.Duplicate),
            report.CountOf(ProjectDoctorIssueKind.Manager),
            report.CountOf(ProjectDoctorIssueKind.MissingRef),
            report.CountOf(ProjectDoctorIssueKind.Texture),
            report.CountOf(ProjectDoctorIssueKind.Animation),
            report.ReportPath));

        return report;
    }

    // ---------------------------------------------------------------- scene

    static void AuditActiveScene(Report report, Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            report.Add(ProjectDoctorIssueKind.Manager, ProjectDoctorSeverity.Error,
                "[Manager] active scene is not loaded; scene checks skipped");
            return;
        }

        var roots = scene.GetRootGameObjects();
        CheckDuplicateManagers(report, roots);

        // MainMenu and ModeSelect are not gameplay scenes and legitimately have no
        // MapGrid; a missing gameplay grid is an error, a menu scene's absence is
        // only advisory.
        bool gameplayScene = !IsNonGameplayScene(scene);
        ProjectDoctorSeverity missingGridSeverity = gameplayScene
            ? ProjectDoctorSeverity.Error
            : ProjectDoctorSeverity.Warning;

        var grids = roots
            .SelectMany(r => r.GetComponentsInChildren<MapGrid>(true))
            .OrderBy(g => PathOf(g.transform), StringComparer.Ordinal)
            .ToArray();

        if (grids.Length == 0)
        {
            report.Add(ProjectDoctorIssueKind.Manager, missingGridSeverity,
                "[Manager] MapGrid | count 0 | " +
                (gameplayScene ? "gameplay scene is missing its MapGrid" : "non-gameplay scene") +
                "; footprint, terrain and ramp checks skipped");
            return;
        }

        MapGrid grid = grids[0];
        if (grid.ground0 == null)
        {
            report.Add(ProjectDoctorIssueKind.Manager, missingGridSeverity,
                "[Manager] " + PathOf(grid.transform) + " | ground0 is not assigned; footprint, terrain and ramp checks skipped");
            return;
        }

        var buildings = roots
            .SelectMany(r => r.GetComponentsInChildren<Building>(true))
            .Where(b => b != null && b.blocksMovement)
            .ToArray();

        var owners = new Dictionary<Vector3Int, List<Building>>();

        foreach (var building in buildings)
        {
            string objectPath = PathOf(building.transform);
            try
            {
                building.Prepare();
            }
            catch (Exception e)
            {
                report.Add(ProjectDoctorIssueKind.Footprint, ProjectDoctorSeverity.Error,
                    "[Footprint] " + objectPath + " | root cell ? | computed cells ? | footprint calculation failed: " + e.Message);
                continue;
            }

            try
            {
                Vector3Int rootCell = grid.WorldToCell(building.transform.position);
                bool rootInside = false;
                for (int i = 0; i < building.Cells.Count; i++)
                {
                    if (building.Cells[i] == rootCell)
                    {
                        rootInside = true;
                        break;
                    }
                }

                if (!rootInside)
                {
                    report.Add(ProjectDoctorIssueKind.Footprint, ProjectDoctorSeverity.Error,
                        "[Footprint] " + objectPath +
                        " | root cell " + FormatCell(rootCell) +
                        " | computed cells " + FormatCells(building.Cells) +
                        " | root cell is outside the computed footprint");
                }

                for (int i = 0; i < building.Cells.Count; i++)
                {
                    Vector3Int cell = building.Cells[i];

                    if (!grid.TerrainWalkable(cell))
                    {
                        report.Add(ProjectDoctorIssueKind.Terrain, ProjectDoctorSeverity.Error,
                            "[Terrain] " + objectPath + " | cell " + FormatCell(cell) + " | TerrainWalkable=false");
                    }

                    if (grid.IsRamp(cell))
                    {
                        report.Add(ProjectDoctorIssueKind.Ramp, ProjectDoctorSeverity.Error,
                            "[Ramp] " + objectPath + " | cell " + FormatCell(cell) + " | blocking footprint occupies a ramp cell");
                    }

                    List<Building> list;
                    if (!owners.TryGetValue(cell, out list))
                    {
                        list = new List<Building>();
                        owners.Add(cell, list);
                    }
                    list.Add(building);
                }
            }
            catch (Exception e)
            {
                report.Add(ProjectDoctorIssueKind.Footprint, ProjectDoctorSeverity.Error,
                    "[Footprint] " + objectPath + " | <footprint scan> | footprint audit failed: " + e.Message);
            }
        }

        var duplicateCells = owners
            .Where(kv => kv.Value.Count > 1)
            .OrderBy(kv => kv.Key.x)
            .ThenBy(kv => kv.Key.y)
            .ThenBy(kv => kv.Key.z)
            .ToArray();

        foreach (var kv in duplicateCells)
        {
            var paths = kv.Value
                .Select(b => PathOf(b.transform))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            report.Add(ProjectDoctorIssueKind.Duplicate, ProjectDoctorSeverity.Error,
                "[Duplicate] cell " + FormatCell(kv.Key) + " | " + string.Join(" | ", paths));
        }
    }

    static void CheckDuplicateManagers(Report report, GameObject[] roots)
    {
        foreach (var type in SingletonManagerTypes)
        {
            var components = roots
                .SelectMany(r => r.GetComponentsInChildren(type, true))
                .Where(c => c != null)
                .OrderBy(c => PathOf(c.transform), StringComparer.Ordinal)
                .ToArray();

            if (components.Length <= 1) continue;

            report.Add(ProjectDoctorIssueKind.Manager, ProjectDoctorSeverity.Error,
                "[Manager] " + type.Name + " | count " + components.Length + " | " +
                string.Join(" | ", components.Select(c => PathOf(c.transform)).ToArray()));
        }
    }

    // --------------------------------------------------------------- prefabs

    static void AuditPrefabs(Report report)
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        var paths = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        foreach (var path in paths)
        {
            try
            {
                AuditPrefab(report, path);
            }
            catch (Exception e)
            {
                report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                    "[MissingRef] " + path + " | <prefab> | prefab audit failed: " + e.Message);
            }
        }
    }

    static void AuditPrefab(Report report, string path)
    {
        GameObject prefab = null;
        try
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                "[MissingRef] " + path + " | <asset> | could not load prefab: " + e.Message);
            return;
        }

        if (prefab == null)
        {
            report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                "[MissingRef] " + path + " | <asset> | could not load prefab");
            return;
        }

        var transforms = prefab
            .GetComponentsInChildren<Transform>(true)
            .OrderBy(t => PathOf(t), StringComparer.Ordinal)
            .ToArray();

        foreach (var transform in transforms)
        {
            string objectPath = PathOf(transform);

            int missingScripts;
            try
            {
                missingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            }
            catch
            {
                missingScripts = 0;
            }

            // Fallback for a component slot that GetComponents exposes as null.
            var components = transform.GetComponents<Component>();
            int nullSlots = components.Count(c => c == null);
            if (nullSlots > missingScripts) missingScripts = nullSlots;

            if (missingScripts > 0)
            {
                report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                    "[MissingRef] " + path + " | " + objectPath + " | Missing Script (" + missingScripts + ")");
            }

            foreach (var component in components)
            {
                if (component == null) continue;
                AuditBrokenReferences(report, path, objectPath, component);
            }
        }

        try
        {
            AuditAnimatorBindings(report, path, prefab);
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.Animation, ProjectDoctorSeverity.Error,
                "[Animation] " + path + " | <animator scan> | animator audit failed: " + e.Message);
        }
    }

    /// <summary>Unity-owned serialized reference slots. These are not user fields;
    /// a null value here is engine bookkeeping, not a broken project reference.</summary>
    static bool IsUnityInternalReference(string propertyPath)
    {
        switch (propertyPath)
        {
            case "m_GameObject":
            case "m_Script":
            case "m_CorrespondingSourceObject":
            case "m_PrefabInstance":
            case "m_PrefabAsset":
            case "m_PrefabParentObject":
            case "m_PrefabInternal":
                return true;
            default:
                return false;
        }
    }

    static void AuditBrokenReferences(Report report, string assetPath, string objectPath, Component component)
    {
        SerializedObject serialized;
        try
        {
            serialized = new SerializedObject(component);
        }
        catch
        {
            return;
        }

        SerializedProperty property = serialized.GetIterator();
        try
        {
            // Next(true), not NextVisible(true): hidden serialized fields are still
            // real references and must be audited. Unity's own bookkeeping paths are
            // skipped by the narrow guard below; the missing-vs-optional rule stays.
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (IsUnityInternalReference(property.propertyPath)) continue;

                // An intentionally unassigned optional field has instanceID 0 and a
                // null value. A genuinely broken reference has a non-zero instance ID
                // whose target no longer resolves. Only the latter is reported.
                if (property.objectReferenceValue != null) continue;
                if (property.objectReferenceInstanceIDValue == 0) continue;

                report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                    "[MissingRef] " + assetPath + " | " + objectPath + " (" + component.GetType().Name + ")" +
                    " | " + property.propertyPath + " | broken object reference");
            }
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.MissingRef, ProjectDoctorSeverity.Error,
                "[MissingRef] " + assetPath + " | " + objectPath + " (" + component.GetType().Name + ")" +
                " | <serialized properties> | reference scan failed: " + e.Message);
        }
    }

    // -------------------------------------------------------------- animation

    static void AuditAnimatorBindings(Report report, string assetPath, GameObject prefab)
    {
        Animator[] animators;
        try
        {
            animators = prefab
                .GetComponentsInChildren<Animator>(true)
                .OrderBy(a => PathOf(a.transform), StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.Animation, ProjectDoctorSeverity.Error,
                "[Animation] " + assetPath + " | <animator scan> | animator discovery failed: " + e.Message);
            return;
        }

        foreach (var animator in animators)
        {
            try
            {
                AuditAnimator(report, assetPath, animator);
            }
            catch (Exception e)
            {
                report.Add(ProjectDoctorIssueKind.Animation, ProjectDoctorSeverity.Error,
                    "[Animation] " + assetPath + " | " + PathOf(animator.transform) +
                    " | <animator> | animator audit failed: " + e.Message);
            }
        }
    }

    static void AuditAnimator(Report report, string assetPath, Animator animator)
    {
        var controller = animator.runtimeAnimatorController;
        if (controller == null) return;

        AnimationClip[] clips;
        try
        {
            clips = controller.animationClips
                .Where(c => c != null)
                .Distinct()
                .OrderBy(c => c.name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception e)
        {
            report.Add(ProjectDoctorIssueKind.Animation, ProjectDoctorSeverity.Error,
                "[Animation] " + assetPath + " | " + PathOf(animator.transform) +
                " | <clips> | clip discovery failed: " + e.Message);
            return;
        }

        foreach (var clip in clips)
        {
            List<EditorCurveBinding> bindings;
            try
            {
                bindings = new List<EditorCurveBinding>();
                bindings.AddRange(AnimationUtility.GetCurveBindings(clip));
                bindings.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            }
            catch (Exception e)
            {
                report.Add(ProjectDoctorIssueKind.Animation, ProjectDoctorSeverity.Error,
                    "[Animation] " + assetPath + " | " + PathOf(animator.transform) + " | " + clip.name +
                    " | <bindings> | binding scan failed: " + e.Message);
                continue;
            }

            bindings.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.path, b.path);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.type == null ? "" : a.type.FullName, b.type == null ? "" : b.type.FullName);
                return c != 0 ? c : string.CompareOrdinal(a.propertyName, b.propertyName);
            });

            foreach (var binding in bindings)
            {
                string error;
                try
                {
                    error = ValidateBinding(animator, binding);
                }
                catch (Exception e)
                {
                    error = "binding validation failed: " + e.Message;
                }
                if (error == null) continue;

                report.Add(ProjectDoctorIssueKind.Animation, ProjectDoctorSeverity.Error,
                    "[Animation] " + assetPath + " | " + PathOf(animator.transform) + " | " + clip.name +
                    " | " + (string.IsNullOrEmpty(binding.path) ? "<self>" : binding.path) + " | " + error);
            }
        }
    }

    /// <summary>Binding paths are relative to the object carrying the Animator, not to
    /// the prefab root. Resolve with Transform.Find from the Animator's own transform.</summary>
    static string ValidateBinding(Animator animator, EditorCurveBinding binding)
    {
        Transform target = string.IsNullOrEmpty(binding.path)
            ? animator.transform
            : animator.transform.Find(binding.path);

        if (target == null) return "target transform not found";
        if (binding.type == null) return "binding component type could not be resolved";
        if (binding.type == typeof(GameObject)) return null;
        if (target.GetComponent(binding.type) == null)
            return "target component " + binding.type.Name + " not found";
        return null;
    }

    // --------------------------------------------------------------- textures

    static void AuditWorldTextures(Report report)
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" });
        var paths = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Where(p => !IsUnderUI(p))
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        foreach (var path in paths)
        {
            try
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    report.Add(ProjectDoctorIssueKind.Texture, ProjectDoctorSeverity.Error,
                        "[Texture] " + path + " | no TextureImporter | expected PPU 64, FilterMode.Point");
                    continue;
                }

                float ppu = importer.spritePixelsPerUnit;
                FilterMode filter = importer.filterMode;
                if (Mathf.Approximately(ppu, 64f) && filter == FilterMode.Point) continue;

                report.Add(ProjectDoctorIssueKind.Texture, ProjectDoctorSeverity.Error,
                    "[Texture] " + path + " | PPU " + ppu.ToString("0.###", CultureInfo.InvariantCulture) +
                    ", FilterMode." + filter + " | expected PPU 64, FilterMode.Point");
            }
            catch (Exception e)
            {
                report.Add(ProjectDoctorIssueKind.Texture, ProjectDoctorSeverity.Error,
                    "[Texture] " + path + " | <import settings> | texture audit failed: " + e.Message);
            }
        }
    }

    static bool IsUnderUI(string assetPath)
    {
        return assetPath.StartsWith("Assets/Art/UI/", StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------- output

    static void WriteReport(Report report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Project Doctor");
        sb.AppendLine("Scene: " + report.ScenePath);
        sb.AppendLine("Errors: " + report.ErrorCount + "  Warnings: " + report.WarningCount);
        sb.AppendLine("Summary: " + string.Join(" ", new[]
        {
            "Footprint=" + report.CountOf(ProjectDoctorIssueKind.Footprint),
            "Terrain=" + report.CountOf(ProjectDoctorIssueKind.Terrain),
            "Ramp=" + report.CountOf(ProjectDoctorIssueKind.Ramp),
            "Duplicate=" + report.CountOf(ProjectDoctorIssueKind.Duplicate),
            "Manager=" + report.CountOf(ProjectDoctorIssueKind.Manager),
            "MissingRef=" + report.CountOf(ProjectDoctorIssueKind.MissingRef),
            "Texture=" + report.CountOf(ProjectDoctorIssueKind.Texture),
            "Animation=" + report.CountOf(ProjectDoctorIssueKind.Animation),
        }));

        if (report.Issues.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("No issues found.");
        }
        else
        {
            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (i == 0 || report.Issues[i].Kind != report.Issues[i - 1].Kind)
                    sb.AppendLine();
                sb.AppendLine(report.Issues[i].Text);
            }
        }

        string directory = Path.GetDirectoryName(report.ReportPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(report.ReportPath, sb.ToString(), new UTF8Encoding(false));
    }

    // ---------------------------------------------------------------- helpers

    static bool IsNonGameplayScene(Scene scene)
    {
        string name = scene.name;
        return string.Equals(name, "MainMenu", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "ModeSelect", StringComparison.OrdinalIgnoreCase);
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
        return "(" + cell.x + ", " + cell.y + ")";
    }

    static string FormatCells(IReadOnlyList<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return "<none>";
        var parts = new string[cells.Count];
        for (int i = 0; i < cells.Count; i++) parts[i] = FormatCell(cells[i]);
        return string.Join(", ", parts);
    }
}
