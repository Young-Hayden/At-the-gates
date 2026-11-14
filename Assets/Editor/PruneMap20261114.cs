using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot, deterministic reduction of the shipped Garnish bush layer.
/// Preview is read-only. Apply only deletes the selected scene instances, moves
/// the approved Tree1, changes the local recovery floor, and saves SampleScene once.
/// </summary>
public static class PruneMap20261114
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";

    const int RequiredBushCount = 99;
    const int RequiredRemovalCount = 49;
    const int RetainedBushCount = 50;
    const int RequiredWaterRockCount = 94;
    const int RequiredSeaRockCount = 28;
    const int RequiredCloudCount = 7;

    static readonly Vector3 TreeBaseline = new Vector3(4.37f, 18.44f, 0f);
    static readonly Vector3 TreeTarget = new Vector3(3.50f, 18.44f, 0f);

    sealed class Plan
    {
        public Scene Scene;
        public List<Transform> Bushes;
        public List<Transform> Candidates;
        public Transform Tree;
        public GameState GameState;
        public Vector3 BlueCentre;
        public Vector3 RedCentre;
    }

    sealed class BinGroup
    {
        public Vector2Int Bin;
        public List<Transform> Items;
        public int Quota;
        public double Remainder;
    }

    public static void Preview()
    {
        Plan plan = BuildPlan();
        Debug.Log(FormatPlan(plan));
    }

    public static void Apply()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave PlayMode before pruning.");

        // BuildPlan repeats every baseline guard and recomputes the same selection
        // immediately before any mutation. The external byte backup is a prerequisite.
        Plan plan = BuildPlan();
        Debug.Log(FormatPlan(plan));

        foreach (Transform candidate in plan.Candidates)
            UnityEngine.Object.DestroyImmediate(candidate.gameObject);

        plan.Tree.position = TreeTarget;
        plan.GameState.recoveryGoldFloor = 300;

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(plan.Scene);
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(plan.Scene))
            throw new InvalidOperationException("Failed to save SampleScene after pruning.");
    }

    static Plan BuildPlan()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (SceneManager.sceneCount != 1 || !scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            throw new InvalidOperationException(
                "SampleScene must be the only loaded scene and the active scene.");

        var roots = scene.GetRootGameObjects();
        GameObject garnish = roots.SingleOrDefault(g => g.name == "Garnish");
        if (garnish == null)
            throw new InvalidOperationException("Garnish missing in SampleScene.");

        var children = garnish.transform.Cast<Transform>().ToArray();
        var bushes = children.Where(t => t.name.StartsWith("Bush", StringComparison.Ordinal)).ToList();
        int waterRocks = children.Count(t => t.name.StartsWith("WaterRock", StringComparison.Ordinal));
        int seaRocks = children.Count(t => t.name.StartsWith("SeaRock", StringComparison.Ordinal));
        int clouds = children.Count(t => t.name.StartsWith("Cloud", StringComparison.Ordinal));

        Transform tree = roots.SelectMany(g => g.GetComponentsInChildren<Building>(true))
            .SingleOrDefault(b => b.name == "Tree1"
                && Vector3.Distance(b.transform.position, TreeBaseline) <= 0.001f)?.transform;
        if (tree == null || Vector3.Distance(tree.position, TreeBaseline) > 0.001f)
            throw new InvalidOperationException("Target Tree1 not at approved baseline.");

        var gameState = roots.SelectMany(g => g.GetComponentsInChildren<GameState>(true))
            .SingleOrDefault();
        if (gameState == null || gameState.recoveryGoldFloor != 50)
            throw new InvalidOperationException("Unexpected recovery floor.");

        if (waterRocks != RequiredWaterRockCount || seaRocks != RequiredSeaRockCount
            || clouds != RequiredCloudCount)
            throw new InvalidOperationException("Non-target garnish changed.");

        // Read actual scene objects, including inactive ones, rather than UnitActor.All.
        // Dead and neutral actors do not define either initial centre.
        var units = UnityEngine.Object.FindObjectsByType<UnitActor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(u => u != null && u.gameObject.scene == scene && u.IsAlive
                && (u.team == Team.Blue || u.team == Team.Red))
            .ToArray();
        Vector3 blueCentre = CentreOf(units, Team.Blue);
        Vector3 redCentre = CentreOf(units, Team.Red);

        List<Transform> candidates = SelectForRemoval(bushes, blueCentre, redCentre);
        if (bushes.Count != RequiredBushCount || candidates.Count != RequiredRemovalCount
            || bushes.Count - candidates.Count != RetainedBushCount)
            throw new InvalidOperationException("Unexpected bush/candidate count.");

        return new Plan
        {
            Scene = scene,
            Bushes = bushes,
            Candidates = candidates,
            Tree = tree,
            GameState = gameState,
            BlueCentre = blueCentre,
            RedCentre = redCentre,
        };
    }

    static Vector3 CentreOf(IEnumerable<UnitActor> units, Team team)
    {
        // Stable input order makes floating-point summation reproducible without
        // introducing instance IDs or any scene-hierarchy ordering dependency.
        var side = units.Where(u => u.team == team)
            .OrderBy(u => u.transform.position.x)
            .ThenBy(u => u.transform.position.y)
            .ThenBy(u => u.transform.position.z)
            .ThenBy(u => u.name, StringComparer.Ordinal)
            .ToArray();
        if (side.Length == 0)
            throw new InvalidOperationException("No live " + team + " starting units found in SampleScene.");

        Vector3 sum = Vector3.zero;
        foreach (UnitActor unit in side) sum += unit.transform.position;
        return sum / side.Length;
    }

    static Vector2Int BinOf(Vector3 p) => new Vector2Int(
        Mathf.FloorToInt(p.x / 10f), Mathf.FloorToInt(p.y / 10f));

    static float RemovalScore(Transform candidate, Vector3 blue, Vector3 red,
        IEnumerable<Transform> all)
    {
        float spawnDistance = Mathf.Min(
            Vector2.Distance(candidate.position, blue),
            Vector2.Distance(candidate.position, red));
        int neighbours = all.Count(other => other != candidate
            && Vector2.Distance(candidate.position, other.position) <= 2.01f);
        // 越小越先删：先初始建设区，再密集丛；坐标只用于最后稳定排序。
        return spawnDistance - neighbours * 1000f;
    }

    static List<Transform> SelectForRemoval(IReadOnlyList<Transform> bushes,
        Vector3 blue, Vector3 red)
    {
        var groups = bushes.GroupBy(b => BinOf(b.position))
            .Select(g => new BinGroup
            {
                Bin = g.Key,
                Items = g.ToList(),
            })
            .ToList();

        foreach (BinGroup group in groups)
        {
            double exact = group.Items.Count * (double)RequiredRemovalCount / RequiredBushCount;
            group.Quota = (int)Math.Floor(exact);
            group.Remainder = exact - group.Quota;
        }

        int remaining = RequiredRemovalCount - groups.Sum(g => g.Quota);
        if (remaining < 0 || remaining > groups.Count)
            throw new InvalidOperationException("Invalid largest-remainder quota total.");

        foreach (BinGroup group in groups
            .OrderByDescending(g => g.Remainder)
            .ThenBy(g => g.Bin.x)
            .ThenBy(g => g.Bin.y)
            .Take(remaining))
        {
            group.Quota++;
        }

        if (groups.Sum(g => g.Quota) != RequiredRemovalCount)
            throw new InvalidOperationException("Largest-remainder quotas do not total 49.");

        var selected = new List<Transform>(RequiredRemovalCount);
        foreach (BinGroup group in groups.OrderBy(g => g.Bin.x).ThenBy(g => g.Bin.y))
        {
            selected.AddRange(group.Items
                .OrderBy(candidate => RemovalScore(candidate, blue, red, bushes))
                .ThenBy(candidate => candidate.position.x)
                .ThenBy(candidate => candidate.position.y)
                .ThenBy(candidate => candidate.position.z)
                .ThenBy(candidate => candidate.name, StringComparer.Ordinal)
                .Take(group.Quota));
        }
        return selected;
    }

    static string FormatPlan(Plan plan)
    {
        var text = new StringBuilder();
        text.AppendLine("bush=" + plan.Bushes.Count.ToString(CultureInfo.InvariantCulture)
            + " remove=" + plan.Candidates.Count.ToString(CultureInfo.InvariantCulture)
            + " remain=" + (plan.Bushes.Count - plan.Candidates.Count).ToString(CultureInfo.InvariantCulture)
            + " WaterRock=" + RequiredWaterRockCount.ToString(CultureInfo.InvariantCulture)
            + " SeaRock=" + RequiredSeaRockCount.ToString(CultureInfo.InvariantCulture)
            + " Cloud=" + RequiredCloudCount.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("blueCentre=" + FormatPosition(plan.BlueCentre)
            + " redCentre=" + FormatPosition(plan.RedCentre));
        foreach (Transform candidate in plan.Candidates)
        {
            text.AppendLine("REMOVE " + FormatBin(BinOf(candidate.position))
                + " " + candidate.name + " " + FormatPosition(candidate.position));
        }
        text.AppendLine("TREE before=" + FormatPosition(plan.Tree.position)
            + " after=" + FormatPosition(TreeTarget) + " blocker=(3,18)");
        text.Append("recoveryGoldFloor before="
            + plan.GameState.recoveryGoldFloor.ToString(CultureInfo.InvariantCulture)
            + " after=" + 300.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }

    static string FormatBin(Vector2Int bin) => "("
        + bin.x.ToString(CultureInfo.InvariantCulture) + ","
        + bin.y.ToString(CultureInfo.InvariantCulture) + ")";

    static string FormatPosition(Vector3 p) => "("
        + p.x.ToString("R", CultureInfo.InvariantCulture) + ","
        + p.y.ToString("R", CultureInfo.InvariantCulture) + ","
        + p.z.ToString("R", CultureInfo.InvariantCulture) + ")";
}
