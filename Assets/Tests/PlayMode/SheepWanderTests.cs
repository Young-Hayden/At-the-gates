using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Presentation Task P1: the three central meat sheep graze and wander within two
/// cells of where they started.
///
/// The hard constraint these tests exist for: a sheep is a 2x1 <see cref="Building"/>
/// registered in <see cref="MapGrid"/>'s dynamic blockers, and the blue-red corridor
/// is exactly one cell wide at (21,7). A sheep that steps into it stalemates the
/// match, so every step has to pass the connectivity gate. The cut-vertex test below
/// is the one that must fail if that gate is removed (see the task report).
///
/// Movement is driven through <see cref="SheepWander.TryStep"/> rather than the
/// 5-9 s random idle timer, so the tests are deterministic.
/// </summary>
public class SheepWanderTests : RegressionFixture
{
    const string SampleScenePath = "Assets/Scenes/SampleScene.scene";
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string SheepPrefabPath = "Assets/Prefabs/Resources/HappySheep_Idle.prefab";

    static readonly Vector3Int Corridor = new Vector3Int(21, 7, 0);

    IEnumerator LoadSampleScene()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            SampleScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
    }

    static ResourceNode FindSheep(string name)
    {
        return Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)
            .FirstOrDefault(n => n.kind == ResourceKind.Meat && n.name == name);
    }

    static MapGrid FindGrid()
    {
        var grid = Object.FindFirstObjectByType<MapGrid>();
        Assert.IsNotNull(grid, "SampleScene must carry a MapGrid.");
        return grid;
    }

    static int TeamRegion(MapGrid grid, Team team)
    {
        var unit = Object.FindObjectsByType<UnitActor>(FindObjectsSortMode.None)
            .FirstOrDefault(u => u.team == team && u.IsAlive);
        Assert.IsNotNull(unit, "SampleScene must carry a living " + team + " unit.");
        int region = grid.Region(grid.WorldToCell(unit.transform.position));
        Assert.GreaterOrEqual(region, 0, team + " unit must stand on walkable ground.");
        return region;
    }

    /// <summary>Completes whatever step the sheep started. Step seconds are set to 0
    /// by the callers that use this, so one frame is enough.</summary>
    static IEnumerator CompleteStep(SheepWander wander)
    {
        float deadline = Time.realtimeSinceStartup + 2f;
        while (wander.IsWalking && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsFalse(wander.IsWalking, "The step did not complete within 2 s.");
    }

    static IEnumerator WaitUntil(System.Func<bool> condition, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
    }

    [UnityTest]
    public IEnumerator Step_IsRefused_WhileAWorkerTargetsTheNode_AndAllowedOnceItClears()
    {
        yield return LoadSampleScene();

        var grid = FindGrid();
        var sheep = FindSheep("Sheep_Meat_1");
        Assert.IsNotNull(sheep, "Sheep_Meat_1 must exist.");
        var wander = sheep.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;
        wander.stepSeconds = 0f;

        var node = sheep;
        var current = wander.CurrentCell;
        var target = current + Vector3Int.right;

        // No worker assigned yet: the same step is accepted, which proves the refusal
        // below is caused by the gatherer and not by the destination.
        Assert.AreEqual(SheepStepResult.Moved, wander.TryStep(target),
            "An unclaimed sheep must be able to take a free step.");
        yield return CompleteStep(wander);

        // Put the worker back and walk it toward the new position.
        wander.stepSeconds = 0f;
        var pawn = Object.Instantiate(
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PawnPath),
            new Vector3(15.5f, 1.5f, 0f), Quaternion.identity);
        pawn.name = "SheepWanderTestWorker";
        yield return null;
        var gatherer = pawn.GetComponent<Gatherer>();
        Assert.IsNotNull(gatherer, "Pawn_Blue must carry a Gatherer.");
        gatherer.Assign(node);
        Assert.AreSame(node, gatherer.TargetNode, "The worker must be assigned to the sheep node.");

        var before = wander.CurrentCell;
        Assert.AreEqual(SheepStepResult.WorkerTargeting, wander.TryStep(before + Vector3Int.left),
            "A step must be refused while a worker is en route to (or harvesting) this node.");
        Assert.AreEqual(before, wander.CurrentCell, "A refused step must not move the sheep.");

        // Clearing the order is what the brief means by "the worker clears".
        gatherer.StopWork();
        Assert.IsNull(gatherer.TargetNode, "StopWork must clear the gatherer's target.");
        yield return null;

        Assert.AreEqual(SheepStepResult.Moved, wander.TryStep(before + Vector3Int.left),
            "Once no worker targets the node, the same step must be allowed again.");
        yield return CompleteStep(wander);
    }

    [UnityTest]
    public IEnumerator Step_IsRefused_WhenDestinationCellsAreOccupied()
    {
        yield return LoadSampleScene();

        var sheep = FindSheep("Sheep_Meat_1");
        Assert.IsNotNull(sheep, "Sheep_Meat_1 must exist.");
        var wander = sheep.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;
        wander.stepSeconds = 0f;

        var current = wander.CurrentCell;
        var target = current + Vector3Int.left;

        // A 1x1 tree on the destination's outer cell. A building blocks regardless of team.
        var tree = Object.Instantiate(
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TreePath),
            new Vector3(target.x + 0.5f, target.y, 0f), Quaternion.identity);
        tree.name = "SheepWanderTestTree";
        yield return null;

        var grid = FindGrid();
        Assert.IsTrue(grid.IsBlockedByBuilding(target),
            "The test tree must own the destination cell " + target + ".");

        Assert.AreEqual(SheepStepResult.DestinationBlocked, wander.TryStep(target),
            "A step into a cell occupied by another building must be refused.");
        Assert.AreEqual(current, wander.CurrentCell, "A refused step must not move the sheep.");
        Assert.IsFalse(wander.IsWalking, "A refused step must not start walking.");
    }

    /// <summary>
    /// The cut vertex, deliberately decoupled from the level layout. The test spawns
    /// its own 2x1 sheep on (22,7)+(23,7) — immediately right of the one-cell corridor
    /// at (21,7) — and steps it left. That would put the footprint on (21,7)+(22,7)
    /// and split the blue and red starting regions.
    ///
    /// This used to rely on the scene's Sheep_Meat_3 standing there; the user-approved
    /// relocation of that sheep to (22,3) is exactly why the guard now builds its own
    /// subject instead of trusting the level.
    /// </summary>
    [UnityTest]
    public IEnumerator CutVertexCorridor_StepIntoCorridor_IsRefused_AndRegionsStayWhole()
    {
        yield return LoadSampleScene();

        var grid = FindGrid();
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SheepPrefabPath);
        Assert.IsNotNull(prefab, "Missing sheep prefab: " + SheepPrefabPath);

        // Origin (23,7) => the 2x1 footprint occupies (22,7)+(23,7).
        var spawn = Object.Instantiate(prefab, new Vector3(23f, 7f, 0f), Quaternion.identity);
        spawn.name = "CutVertexTestSheep";
        yield return null;

        var sheep = spawn.GetComponent<ResourceNode>();
        Assert.IsNotNull(sheep, "The spawned prefab must carry a ResourceNode.");
        var wander = spawn.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;
        wander.stepSeconds = 0f;

        var building = spawn.GetComponent<Building>();
        Assert.IsNotNull(building, "The spawned sheep must carry a Building.");
        Assert.AreEqual(new Vector3Int(22, 7, 0), wander.CurrentCell,
            "The test sheep must start on (22,7)+(23,7).");
        Assert.IsTrue(grid.TerrainWalkable(Corridor), "The corridor cell must be walkable terrain.");
        Assert.IsFalse(grid.IsBlockedByBuilding(Corridor), "The corridor must start unblocked.");
        Assert.IsTrue(grid.IsBlockedByBuilding(new Vector3Int(22, 7, 0)),
            "The test sheep must have registered its footprint before the attempt.");

        var beforeCells = building.Cells.ToArray();
        var beforePosition = spawn.transform.position;
        int blue = TeamRegion(grid, Team.Blue);
        int red = TeamRegion(grid, Team.Red);
        Assert.AreEqual(blue, red, "Precondition: the two starting armies share one region.");
        int regionVersion = grid.RegionVersion;

        Assert.AreEqual(SheepStepResult.WouldDisconnect,
            wander.TryStep(Corridor),
            "Stepping into the one-cell corridor must be refused by the connectivity gate.");

        // A refused step must leave no trace — not the transform, not the footprint,
        // and not the temporary destination footprint the gate added for its check.
        Assert.IsFalse(wander.IsWalking, "A refused step must not start walking.");
        Assert.AreEqual(beforePosition, spawn.transform.position, "A refused step must not move the sheep.");
        CollectionAssert.AreEquivalent(beforeCells, building.Cells.ToArray(),
            "A refused step must leave the sheep's footprint where it was.");
        Assert.IsTrue(grid.TerrainWalkable(Corridor), "The corridor must still be walkable terrain.");
        Assert.IsFalse(grid.IsBlockedByBuilding(Corridor),
            "The connectivity gate must remove its simulated destination footprint before returning.");
        Assert.IsTrue(grid.Walkable(Corridor), "The corridor must still be walkable after the refusal.");
        Assert.AreEqual(blue, TeamRegion(grid, Team.Blue), "Blue must stay in its region.");
        Assert.AreEqual(red, TeamRegion(grid, Team.Red), "Red must stay in its region.");
        Assert.AreEqual(TeamRegion(grid, Team.Blue), TeamRegion(grid, Team.Red),
            "Blue and red must still share one region after the refused step.");
        Assert.AreEqual(regionVersion, grid.RegionVersion,
            "The connectivity gate must not relabel the map: it restores the blocker state, " +
            "so the region cache is still valid and RegionVersion must not move.");
    }

    /// <summary>
    /// The user-approved relocation put Sheep_Meat_3 on (22,3)+(23,3). This proves the
    /// new home is actually usable — at least one cardinal step is accepted — and that a
    /// step back returns the sheep to its anchor cell, so the two-cell range is intact.
    /// </summary>
    [UnityTest]
    public IEnumerator RelocatedSheep_CanTakeAtLeastOneCardinalStep_FromItsNewHome()
    {
        yield return LoadSampleScene();

        var sheep = FindSheep("Sheep_Meat_3");
        Assert.IsNotNull(sheep, "Sheep_Meat_3 must exist.");
        var wander = sheep.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;
        wander.stepSeconds = 0f;

        var home = wander.CurrentCell;
        Assert.AreEqual(new Vector3Int(22, 3, 0), home,
            "Sheep_Meat_3 must stand on (22,3)+(23,3) after the relocation.");

        var directions = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
        var results = new SheepStepResult[directions.Length];
        bool moved = false;

        for (int i = 0; i < directions.Length; i++)
        {
            var from = wander.CurrentCell;
            var result = wander.TryStep(from + directions[i]);
            results[i] = result;
            if (result != SheepStepResult.Moved) continue;

            moved = true;
            yield return CompleteStep(wander);
            Assert.AreEqual(from + directions[i], wander.CurrentCell,
                "An accepted step must land on the destination cell.");

            // Return home so every direction is probed from the same pair.
            Assert.AreEqual(SheepStepResult.Moved, wander.TryStep(from),
                "The step back to the home cell must be accepted.");
            yield return CompleteStep(wander);
            Assert.AreEqual(home, wander.CurrentCell, "The sheep must be back at its start cell.");
        }

        Debug.Log("[SheepRelocate] home=" + home
                  + " R=" + results[0] + " L=" + results[1]
                  + " U=" + results[2] + " D=" + results[3]);

        Assert.IsTrue(moved,
            "The relocated sheep must have at least one legal cardinal step: R=" + results[0]
            + " L=" + results[1] + " U=" + results[2] + " D=" + results[3] + ".");
    }

    /// <summary>
    /// Pins the batched API's semantics: one <see cref="MapGrid.ApplyBlockingDelta"/> call
    /// must leave exactly the blocker state that the matching sequence of individual
    /// <see cref="MapGrid.RemoveBuildingFootprint"/> / <see cref="MapGrid.AddBuildingFootprint"/>
    /// calls leaves — same Walkable/Region per cell and same RegionSize per region.
    /// A net-zero delta must report no change and must not relabel.
    /// </summary>
    [UnityTest]
    public IEnumerator BatchedBlockingDelta_MatchesIndividualFootprintCalls()
    {
        yield return LoadSampleScene();

        var grid = FindGrid();

        // Open ground on row y=1, far from every sheep, unit and building.
        var a = new Vector3Int(16, 1, 0);
        var b = new Vector3Int(17, 1, 0);
        var c = new Vector3Int(18, 1, 0);
        foreach (var cell in new[] { a, b, c })
        {
            Assert.IsTrue(grid.TerrainWalkable(cell), cell + " must be walkable terrain.");
            Assert.IsFalse(grid.IsBlockedByBuilding(cell), cell + " must start unblocked.");
        }

        var baseline = SnapshotRegions(grid);

        // Put a 2-cell blocker down so the delta really removes something.
        grid.AddBuildingFootprint(new[] { a, b });
        var withBoth = SnapshotRegions(grid);

        // Batched: free a, block c.
        Assert.IsTrue(grid.ApplyBlockingDelta(new[] { a }, new[] { c }),
            "A batch that crosses a 0<->1 boundary must report a change.");
        var batched = SnapshotRegions(grid);

        // Revert the batch; the labelling must come back to the withBoth state.
        Assert.IsTrue(grid.ApplyBlockingDelta(new[] { c }, new[] { a }),
            "The inverse batch must report a change.");
        AssertSameRegions(withBoth, SnapshotRegions(grid), "After the batched revert");

        // The same logical move through the two individual calls.
        grid.RemoveBuildingFootprint(new[] { a });
        grid.AddBuildingFootprint(new[] { c });
        var individual = SnapshotRegions(grid);
        AssertSameRegions(batched, individual,
            "One batched delta must leave the same labelling as the matching individual calls");
        AssertSameRegionSizes(grid, batched);

        // Net-zero deltas are free. c is blocked at this point: removing and re-adding it
        // in the same batch leaves the same blocker state, reports no change and must not
        // bump RegionVersion.
        int version = grid.RegionVersion;
        Assert.IsTrue(grid.IsBlockedByBuilding(c), "Precondition: c must be blocked here.");
        Assert.IsFalse(grid.ApplyBlockingDelta(new Vector3Int[0], new Vector3Int[0]),
            "An empty delta must report no change.");
        Assert.IsFalse(grid.ApplyBlockingDelta(new[] { c }, new[] { c }),
            "A blocked cell removed and added in the same batch nets to zero.");
        Assert.AreEqual(version, grid.RegionVersion,
            "A net-zero delta must not relabel the regions.");

        // Cleanup: the individual sequence's net effect from the baseline is +b (the
        // initial add) and +c (the individual add); a was freed by the individual remove.
        // Undo those two, and the baseline labelling must return exactly.
        grid.RemoveBuildingFootprint(new[] { c });
        grid.RemoveBuildingFootprint(new[] { b });
        AssertSameRegions(baseline, SnapshotRegions(grid), "Cleanup");
    }

    static Dictionary<Vector3Int, int> SnapshotRegions(MapGrid grid)
    {
        var cells = new HashSet<Vector3Int>();
        AddBounds(cells, grid.ground0);
        AddBounds(cells, grid.top1);
        AddBounds(cells, grid.top2);

        var snapshot = new Dictionary<Vector3Int, int>(cells.Count);
        foreach (var cell in cells) snapshot[cell] = grid.Region(cell);
        return snapshot;
    }

    static void AddBounds(HashSet<Vector3Int> cells, UnityEngine.Tilemaps.Tilemap tilemap)
    {
        if (tilemap == null) return;
        foreach (var pos in tilemap.cellBounds.allPositionsWithin)
            cells.Add(new Vector3Int(pos.x, pos.y, 0));
    }

    static void AssertSameRegions(Dictionary<Vector3Int, int> expected,
                                  Dictionary<Vector3Int, int> actual, string context)
    {
        Assert.AreEqual(expected.Count, actual.Count, context + ": sampled cell count");
        foreach (var kv in expected)
        {
            Assert.IsTrue(actual.TryGetValue(kv.Key, out int got), context + ": missing cell " + kv.Key);
            Assert.AreEqual(kv.Value, got, context + ": region of " + kv.Key);
        }
    }

    static void AssertSameRegionSizes(MapGrid grid, Dictionary<Vector3Int, int> snapshot)
    {
        var seen = new HashSet<int>();
        var counts = new Dictionary<int, int>();
        foreach (var kv in snapshot)
        {
            if (kv.Value < 0) continue;
            counts.TryGetValue(kv.Value, out int count);
            counts[kv.Value] = count + 1;
        }
        foreach (var kv in counts)
        {
            Assert.AreEqual(kv.Value, grid.RegionSize(kv.Key),
                "RegionSize must match the snapshot for region " + kv.Key);
        }
    }

    [UnityTest]
    public IEnumerator Sheep_StaysWithinTwoCellsOfAnchor_OverManySteps()
    {
        yield return LoadSampleScene();

        var sheep = FindSheep("Sheep_Meat_1");
        Assert.IsNotNull(sheep, "Sheep_Meat_1 must exist.");
        var wander = sheep.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;
        wander.stepSeconds = 0f;

        var anchor = wander.AnchorCell;
        Assert.AreEqual(anchor, wander.CurrentCell, "A fresh sheep's anchor is its own cell.");

        int moves = 0;
        // Right to the edge, left across the whole range, then back and across again:
        // 14 accepted steps and four refused boundary steps.
        for (int i = 0; i < 2; i++)
            yield return StepOnce(wander, Vector3Int.right, anchor, () => moves++);
        AssertEdge(wander, anchor, +2, Vector3Int.right);

        for (int i = 0; i < 4; i++)
            yield return StepOnce(wander, Vector3Int.left, anchor, () => moves++);
        AssertEdge(wander, anchor, -2, Vector3Int.left);

        for (int i = 0; i < 4; i++)
            yield return StepOnce(wander, Vector3Int.right, anchor, () => moves++);
        AssertEdge(wander, anchor, +2, Vector3Int.right);

        for (int i = 0; i < 4; i++)
            yield return StepOnce(wander, Vector3Int.left, anchor, () => moves++);
        AssertEdge(wander, anchor, -2, Vector3Int.left);

        Assert.GreaterOrEqual(moves, 12, "The drive must actually exercise many steps.");
        Assert.AreEqual(anchor, wander.AnchorCell, "The anchor never moves when the sheep does.");
        AssertWithinAnchor(wander, anchor);
    }

    static IEnumerator StepOnce(SheepWander wander, Vector3Int direction, Vector3Int anchor,
                                System.Action onMoved)
    {
        var from = wander.CurrentCell;
        Assert.AreEqual(SheepStepResult.Moved, wander.TryStep(from + direction),
            "A free step inside the wander range must be accepted.");
        yield return CompleteStep(wander);
        onMoved?.Invoke();
        AssertWithinAnchor(wander, anchor);

        // Gate 5: the grid footprint must follow the sheep. Without this, deleting the
        // re-registration would leave a trail of permanently blocked cells and the whole
        // suite would still pass, because every other check reads transform-derived state.
        var grid = MapGrid.Instance;
        Assert.IsNotNull(grid, "The scene must carry a MapGrid.");
        var dest = from + direction;
        var destRight = new Vector3Int(dest.x + 1, dest.y, 0);
        var fromRight = new Vector3Int(from.x + 1, from.y, 0);
        Assert.IsTrue(grid.IsBlockedByBuilding(dest),
            "The cell the sheep moved onto must be registered as blocked: " + dest);
        Assert.IsTrue(grid.IsBlockedByBuilding(destRight),
            "The sheep's right-hand cell must be registered as blocked: " + destRight);

        // A cell the sheep no longer covers must be released, unless the new footprint
        // still covers it (the two footprints overlap by one cell on a cardinal step).
        if (from != destRight)
            Assert.IsFalse(grid.IsBlockedByBuilding(from),
                "The vacated cell must no longer be blocked by the sheep: " + from);
        if (fromRight != dest && fromRight != destRight)
            Assert.IsFalse(grid.IsBlockedByBuilding(fromRight),
                "The vacated right cell must no longer be blocked by the sheep: " + fromRight);
    }

    static void AssertEdge(SheepWander wander, Vector3Int anchor, int offset, Vector3Int beyond)
    {
        var edge = wander.CurrentCell;
        Assert.AreEqual(anchor.x + offset, edge.x,
            "The sheep must have reached the edge of its range.");
        Assert.AreEqual(SheepStepResult.OutOfRange, wander.TryStep(edge + beyond),
            "A step that would leave the two-cell range must be refused.");
        Assert.AreEqual(edge, wander.CurrentCell,
            "The refused out-of-range step must not move the sheep.");
    }

    static void AssertWithinAnchor(SheepWander wander, Vector3Int anchor)
    {
        var cell = wander.CurrentCell;
        Assert.LessOrEqual(Mathf.Max(Mathf.Abs(cell.x - anchor.x), Mathf.Abs(cell.y - anchor.y)), 2,
            "The sheep left its two-cell wander range: anchor " + anchor + ", cell " + cell + ".");
    }

    /// <summary>
    /// The anchor is captured in Start from the (restored) position, so a load resumes
    /// the range around where the sheep actually is — not around its original spawn.
    /// </summary>
    [UnityTest]
    public IEnumerator SaveLoad_RestoredAnchor_IsTheRestoredPosition_NotTheOriginal()
    {
        yield return LoadSampleScene();

        var sheep = FindSheep("Sheep_Meat_1");
        Assert.IsNotNull(sheep, "Sheep_Meat_1 must exist.");
        var wander = sheep.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;
        wander.stepSeconds = 0f;

        var originalAnchor = wander.AnchorCell;
        var moved = wander.CurrentCell + Vector3Int.right;
        Assert.AreEqual(SheepStepResult.Moved, wander.TryStep(moved),
            "The control step must be accepted.");
        yield return CompleteStep(wander);
        Assert.AreEqual(moved, wander.CurrentCell, "The sheep must have moved one cell right.");
        Assert.AreEqual(originalAnchor, wander.AnchorCell,
            "Moving must not move the anchor within a session.");

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "SaveGameService capture failed: " + captureError);
        string path = Path.Combine(
            Application.temporaryCachePath, "at_the_gates_sheep_wander_roundtrip.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError),
            "SaveToPath failed: " + writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError),
            "TryReadFromPath failed: " + readError);

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "SaveGameService restore failed: " + restoreError);
        yield return null;      // let the rebuilt object's Start run

        var after = FindSheep("Sheep_Meat_1");
        Assert.IsNotNull(after, "The restore must rebuild Sheep_Meat_1.");
        var afterWander = after.GetComponent<SheepWander>();
        Assert.IsNotNull(afterWander, "The restored sheep must carry SheepWander (it must live on the prefab).");

        var restoredCell = afterWander.CurrentCell;
        Assert.AreEqual(moved, restoredCell, "The sheep must come back at the saved position.");
        Assert.AreEqual(restoredCell, afterWander.AnchorCell,
            "After a load the anchor must be the restored cell, not the original spawn.");
        Assert.AreNotEqual(originalAnchor, afterWander.AnchorCell,
            "The restored anchor must differ from the original spawn anchor.");

        try { File.Delete(path); } catch (IOException) { }
        try { File.Delete(path + ".tmp"); } catch (IOException) { }
    }

    [UnityTest]
    public IEnumerator Animator_SwitchesToWalk_WhileStepping_AndBackToIdle()
    {
        yield return LoadSampleScene();

        var sheep = FindSheep("Sheep_Meat_1");
        Assert.IsNotNull(sheep, "Sheep_Meat_1 must exist.");
        var wander = sheep.GetComponent<SheepWander>();
        Assert.IsNotNull(wander, "HappySheep_Idle.prefab must carry SheepWander.");
        wander.autoWander = false;

        var animator = sheep.GetComponent<Animator>();
        Assert.IsNotNull(animator, "The sheep prefab must carry an Animator.");
        Assert.IsNotNull(animator.runtimeAnimatorController,
            "The sheep prefab's Animator must point at the sheep controller.");
        var renderer = sheep.GetComponent<SpriteRenderer>();
        Assert.IsNotNull(renderer, "The sheep must carry a SpriteRenderer.");

        yield return WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), 2f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "The sheep must start in the Idle (grazing) state.");
        Assert.IsNotNull(renderer.sprite, "The grazing animator must be driving a sprite.");
        StringAssert.StartsWith("HappySheep_Idle_", renderer.sprite.name,
            "Idle must play the grazing sheet. The sprite name is the binding-path proof: " +
            "an empty path only animates if the Animator sits on the SpriteRenderer's object.");

        var current = wander.CurrentCell;
        Assert.AreEqual(SheepStepResult.Moved, wander.TryStep(current + Vector3Int.right),
            "The control step must be accepted.");

        yield return WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), 1f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"),
            "The animator must switch to Walk while the sheep is stepping.");
        Assert.IsNotNull(renderer.sprite, "The walking animator must be driving a sprite.");
        StringAssert.StartsWith("HappySheep_Bouncing_", renderer.sprite.name,
            "Walk must play the bouncing sheet, read back from the live renderer.");

        yield return CompleteStep(wander);
        yield return WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), 1f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "The animator must return to Idle once the step ends.");
        StringAssert.StartsWith("HappySheep_Idle_", renderer.sprite.name,
            "The grazing sheet must be playing again after the walk.");
        Assert.AreEqual(current.x + 1, wander.CurrentCell.x, "The sheep must have moved one cell right.");
    }

    // -------------------------------------------------------------- performance

    sealed class FrameStats
    {
        public float AverageMs;
        public float MaxMs;
        public int Frames;
    }

    /// <summary>Samples the real frame duration once per frame. The first 20 frames of
    /// each window are excluded from the statistics so the enable/disable transition and
    /// any editor hitch from the previous tool call do not dominate the average.</summary>
    static IEnumerator SampleFrames(float seconds, FrameStats stats)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        float total = 0f;
        float max = 0f;
        int frames = 0;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (frames >= 20)
            {
                total += ms;
                if (ms > max) max = ms;
            }
            frames++;
        }
        stats.Frames = frames;
        stats.AverageMs = total / Mathf.Max(1, frames - 20);
        stats.MaxMs = max;
    }

    /// <summary>
    /// The wander loop must be event-driven: idle frames only tick a timer, the five
    /// gates and the O(map) region relabel run once per step attempt. This measures the
    /// frame cost against a wander-disabled baseline and times the two map-touching
    /// halves of one step (the connectivity gate inside TryStep, and Building.Recompute
    /// on arrival). The numbers are logged as [SheepPerf] and quoted in the task report.
    /// </summary>
    [UnityTest]
    public IEnumerator Performance_WanderIsEventDriven_AndStepMapCostIsMeasured()
    {
        yield return LoadSampleScene();

        var sheep = Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)
            .Where(n => n.kind == ResourceKind.Meat)
            .OrderBy(n => n.name)
            .ToArray();
        Assert.AreEqual(3, sheep.Length, "SampleScene must carry the three central sheep.");

        var wanders = new SheepWander[sheep.Length];
        for (int i = 0; i < sheep.Length; i++)
        {
            wanders[i] = sheep[i].GetComponent<SheepWander>();
            Assert.IsNotNull(wanders[i], sheep[i].name + " must carry SheepWander.");
        }

        // Baseline: the wander logic is present but idle (autoWander = false); animators,
        // gatherers and AI keep running, so the delta is attributable to wandering alone.
        for (int i = 0; i < wanders.Length; i++)
        {
            wanders[i].autoWander = false;
            wanders[i].RestartIdle();
        }
        var baseline = new FrameStats();
        yield return SampleFrames(6f, baseline);

        // Production configuration: defaults, all three sheep wandering.
        for (int i = 0; i < wanders.Length; i++) wanders[i].autoWander = true;
        int attemptsBefore = 0, stepsBefore = 0;
        for (int i = 0; i < wanders.Length; i++)
        {
            attemptsBefore += wanders[i].StepAttempts;
            stepsBefore += wanders[i].StepsTaken;
        }

        var wandering = new FrameStats();
        yield return SampleFrames(10f, wandering);

        int attempts = 0, steps = 0;
        for (int i = 0; i < wanders.Length; i++)
        {
            attempts += wanders[i].StepAttempts - attemptsBefore;
            steps += wanders[i].StepsTaken - stepsBefore;
        }

        // Accelerated: 0.5-1 s idle so several accepted steps (and their arrival
        // Recompute) land inside a short sample. This is a stress window, not the
        // shipped rate — it is the only way to sample the per-step spike in 6 s.
        for (int i = 0; i < wanders.Length; i++)
        {
            wanders[i].idleMinSeconds = 0.5f;
            wanders[i].idleMaxSeconds = 1f;
            wanders[i].RestartIdle();      // the old 5-9 s timer must not delay the window
        }
        int accelAttemptsBefore = 0, accelStepsBefore = 0;
        for (int i = 0; i < wanders.Length; i++)
        {
            accelAttemptsBefore += wanders[i].StepAttempts;
            accelStepsBefore += wanders[i].StepsTaken;
        }
        var accelerated = new FrameStats();
        yield return SampleFrames(6f, accelerated);

        int accelAttemptsAfter = 0, accelStepsAfter = 0;
        for (int i = 0; i < wanders.Length; i++)
        {
            accelAttemptsAfter += wanders[i].StepAttempts;
            accelStepsAfter += wanders[i].StepsTaken;
        }
        int fastAttempts = accelAttemptsAfter - accelAttemptsBefore;
        int fastSteps = accelStepsAfter - accelStepsBefore;

        // Time one accepted step's map work: the gate inside TryStep, and the one
        // batched relabel the arrival path uses (RecomputeFootprint). Recompute is timed
        // only as the fallback reference; it is two relabels.
        // Measure on a purpose-spawned sheep in open ground: after ~25 s of simulation a
        // worker can wander into a scene sheep's next cell, which made this sample flaky
        // once. Row y=1 is open and away from every unit and building.
        var measurePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SheepPrefabPath);
        Assert.IsNotNull(measurePrefab, "Missing sheep prefab: " + SheepPrefabPath);
        var measure = Object.Instantiate(measurePrefab, new Vector3(16f, 1f, 0f), Quaternion.identity);
        measure.name = "PerfMeasureSheep";
        yield return null;

        var walker = measure.GetComponent<SheepWander>();
        Assert.IsNotNull(walker, "The spawned sheep must carry SheepWander.");
        walker.autoWander = false;
        walker.stepSeconds = 0f;
        var building = measure.GetComponent<Building>();
        var grid = FindGrid();
        var arrivalA = new Vector3Int(21, 1, 0);
        var arrivalB = new Vector3Int(22, 1, 0);

        float tryTotal = 0f, arrivalTotal = 0f, recomputeTotal = 0f;
        const int samples = 4;
        for (int i = 0; i < samples; i++)
        {
            var current = walker.CurrentCell;
            var direction = current.x - walker.AnchorCell.x > 0 ? Vector3Int.left : Vector3Int.right;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = walker.TryStep(current + direction);
            stopwatch.Stop();
            Assert.AreEqual(SheepStepResult.Moved, result,
                "The measured step must be accepted (step " + i + ", direction " + direction + ").");
            tryTotal += (float)stopwatch.Elapsed.TotalMilliseconds;
            yield return CompleteStep(walker);

            stopwatch.Restart();
            Assert.IsTrue(grid.ApplyBlockingDelta(new[] { arrivalA }, new[] { arrivalB }),
                "The measured arrival delta must relabel.");
            stopwatch.Stop();
            arrivalTotal += (float)stopwatch.Elapsed.TotalMilliseconds;
            grid.ApplyBlockingDelta(new[] { arrivalB }, new[] { arrivalA });   // revert, untimed

            stopwatch.Restart();
            building.Recompute();
            stopwatch.Stop();
            recomputeTotal += (float)stopwatch.Elapsed.TotalMilliseconds;
        }
        float tryMs = tryTotal / samples;
        float arrivalMs = arrivalTotal / samples;
        float recomputeMs = recomputeTotal / samples;

        Debug.Log("[SheepPerf] baseline avg=" + baseline.AverageMs.ToString("F3") + "ms max="
                  + baseline.MaxMs.ToString("F3") + "ms frames=" + baseline.Frames
                  + " | wandering avg=" + wandering.AverageMs.ToString("F3") + "ms max="
                  + wandering.MaxMs.ToString("F3") + "ms frames=" + wandering.Frames
                  + " attempts=" + attempts + " steps=" + steps
                  + " | accelerated avg=" + accelerated.AverageMs.ToString("F3") + "ms max="
                  + accelerated.MaxMs.ToString("F3") + "ms frames=" + accelerated.Frames
                  + " attempts=" + fastAttempts + " steps=" + fastSteps
                  + " | TryStep avg=" + tryMs.ToString("F3") + "ms"
                  + " | arrival delta avg=" + arrivalMs.ToString("F3") + "ms"
                  + " | Recompute(fallback) avg=" + recomputeMs.ToString("F3") + "ms"
                  + " | map cost/step=" + (tryMs + arrivalMs).ToString("F3") + "ms");

        // Event-driven: three sheep, 10 s at 5-9 s idle — a handful of attempts, not per frame.
        Assert.GreaterOrEqual(attempts, 3, "Each sheep must attempt at least one step in 10 s.");
        Assert.Less(attempts, Mathf.Max(12, wandering.Frames / 10),
            "The five gates must run once per step attempt, not per frame.");

        // The accelerated window must actually produce accepted steps, or the per-step
        // spike this test exists for was never sampled.
        Assert.GreaterOrEqual(fastAttempts, 6, "The accelerated window must attempt many steps.");
        Assert.Greater(fastSteps, 0, "The accelerated window must produce accepted steps.");
        Assert.Less(fastAttempts, accelerated.Frames, "Even accelerated, wander is per-event, not per-frame.");

        // No measurable per-frame cost from wandering.
        Assert.LessOrEqual(wandering.AverageMs, baseline.AverageMs + 5f,
            "Wandering added more than 5 ms per frame on average.");

        // One step's map work: the gate is a read-only flood fill and the arrival is one
        // batched relabel. The old design was four + two relabels (~31 ms).
        Assert.Less(tryMs, 10f, "TryStep (gate + reachability) exceeded 10 ms.");
        Assert.Less(arrivalMs, 15f, "One arrival relabel exceeded 15 ms.");
        Assert.Less(tryMs + arrivalMs, 12f,
            "Per accepted step map work exceeded 12 ms (target: <=10 ms).");
        Assert.Less(recomputeMs, 25f, "Building.Recompute (fallback path) exceeded 25 ms.");
    }
}
