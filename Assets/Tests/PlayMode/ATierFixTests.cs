using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Regressions for the 2026-11-21 A-tier code-only fixes: tall-building hit tests,
/// patrol attack ownership, attack/placement mode, game-over tower shutdown, noncombatant
/// range rings, unreachable manual feedback and save backup fallback.
/// </summary>
public class ATierFixTests : RegressionFixture
{
    const string CastleBluePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";
    const string CastleRedPath = "Assets/Prefabs/Buildings/Knights/Castle_Red.prefab";
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";

    // ------------------------------------------------------------------ A1

    [UnityTest]
    public IEnumerator ClickingTallBuildingArt_SelectsItsBase()
    {
        PaintRect(-8, -4, 20, 12);
        var castle = SpawnBuilding(CastleBluePath, new Vector3Int(0, 0), "Tall selectable castle");
        yield return null;

        Selection.selected.Clear();
        Selection.selectedBuilding = null;
        Vector3 artPoint = castle.transform.position + Vector3.up * 2.5f;
        Vector3 screen = Selection.cam.WorldToScreenPoint(artPoint);

        Selection.HandleLeftMouseInput(artPoint, screen, true, true, false, false);
        Selection.HandleLeftMouseInput(artPoint, screen, false, false, true, false);

        Assert.AreSame(castle.GetComponent<Building>(), Selection.selectedBuilding,
            "A click on the visible castle body must select the building even though its collider is only one tile deep.");
    }

    [UnityTest]
    public IEnumerator ClickingNeutralTreeArt_DoesNotStealBuildingSelection()
    {
        PaintRect(-8, -4, 20, 12);
        var tree = SpawnBuilding(TreePath, new Vector3Int(0, 0), "Tall neutral tree");
        yield return null;

        Selection.selected.Clear();
        Selection.selectedBuilding = null;
        Vector3 artPoint = tree.transform.position + Vector3.up * 2.5f;
        Vector3 screen = Selection.cam.WorldToScreenPoint(artPoint);

        Selection.HandleLeftMouseInput(artPoint, screen, true, true, false, false);
        Selection.HandleLeftMouseInput(artPoint, screen, false, false, true, false);

        Assert.IsNull(Selection.selectedBuilding,
            "Neutral resource art must not hijack a building selection fallback.");
    }

    [UnityTest]
    public IEnumerator FindAt_EnemyTallBuildingArt_FindsTheBuilding()
    {
        PaintRect(-8, -4, 20, 12);
        var castle = SpawnBuilding(CastleRedPath, new Vector3Int(0, 0), "Tall enemy castle");
        yield return null;

        Vector3 artPoint = castle.transform.position + Vector3.up * 2.5f;
        IDamageable found = Combatants.FindAt(artPoint, Team.Blue);
        Assert.AreSame(castle.GetComponent<Building>(), found,
            "Combatants.FindAt must resolve a tall enemy building from its visible body.");
    }

    [UnityTest]
    public IEnumerator RightClickTallDamagedBuildingArt_SendsWorkerToRepair()
    {
        PaintRect(-8, -4, 20, 12);
        var castle = SpawnBuilding(CastleBluePath, new Vector3Int(0, 0), "Tall damaged castle");
        var worker = SpawnUnit(PawnPath, new Vector3Int(-4, 0), "Tall repair worker");
        yield return null;

        var building = castle.GetComponent<Building>();
        building.TakeDamage(10);
        Assert.IsTrue(building.CanRepair);

        Selection.selected.Clear();
        Selection.selectedBuilding = null;
        Selection.selected.Add(worker.GetComponent<UnitMover>());
        Selection.IssueMove(castle.transform.position + Vector3.up * 2.5f);

        Assert.AreSame(building, worker.GetComponent<Gatherer>().RepairTarget,
            "Right-clicking the visible body of a damaged tall building must order a repair.");
    }

    // ------------------------------------------------------------------ A3

    [UnityTest]
    public IEnumerator Patrol_AttackOrder_StopsTheOldPatrolPath()
    {
        PaintRect(-10, -4, 24, 8);
        var warrior = SpawnUnit(WarriorPath, new Vector3Int(0, 0), "Patrol attacker");
        yield return null;
        var ai = warrior.GetComponent<CombatAI>();
        var mover = warrior.GetComponent<UnitMover>();

        ai.OrderPatrol(new Vector3Int(8, 0));
        Assert.IsTrue(mover.Moving, "The control patrol must issue a path.");

        var enemy = SpawnUnit(WarriorPath, new Vector3Int(10, 0), "Patrol target");
        enemy.GetComponent<UnitActor>().team = Team.Red;
        ai.OrderAttack(enemy.GetComponent<UnitActor>());

        Assert.IsFalse(mover.Moving,
            "An explicit attack must stop the old patrol path instead of letting its arrival callback clear the target.");
        Assert.AreSame(enemy.GetComponent<UnitActor>(), ai.Target);
        Assert.IsFalse(ai.IsPatrolling);
    }

    // ------------------------------------------------------------------ A4

    [UnityTest]
    public IEnumerator ArmAttack_WhilePlacingBuilding_CancelsPlacementAndArms()
    {
        PaintRect(-8, -4, 20, 12);
        var warrior = SpawnUnit(WarriorPath, new Vector3Int(0, 0), "Mode fighter");
        yield return null;

        Selection.selected.Clear();
        Selection.selected.Add(warrior.GetComponent<UnitMover>());
        Placer.Begin(LoadPrefab<GameObject>(CastleBluePath));
        Assert.IsTrue(Placer.IsPlacing);

        Selection.ArmAttack();
        Assert.IsFalse(Placer.IsPlacing,
            "Arming an attack must leave building placement instead of silently doing nothing.");
        Assert.IsTrue(Selection.AttackArmed);

        Selection.DisarmAttack();
        yield return null;
    }

    // ------------------------------------------------------------------ A5

    [UnityTest]
    public IEnumerator GameOver_LeavesGarrisonedArcherCombatDisabled()
    {
        PaintRect(-8, -4, 20, 12);
        var tower = SpawnBuilding(TowerPath, new Vector3Int(0, 0), "Game over tower");
        var archer = SpawnUnit(ArcherPath, new Vector3Int(-2, 0), "Game over archer");
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(archer.GetComponent<UnitActor>()));

        var gameState = Object.FindFirstObjectByType<GameState>();
        Assert.IsNotNull(gameState);
        gameState.End(GameState.Outcome.Win);

        var combat = archer.GetComponent<CombatAI>();
        Assert.IsNotNull(combat);
        Assert.IsFalse(combat.enabled,
            "Ungarrisoning during End must not leave CombatAI enabled after the blanket shutdown.");
        Assert.AreEqual(0f, Time.timeScale);
    }

    // ------------------------------------------------------------------ A7

    [UnityTest]
    public IEnumerator SelectedWorker_ShowsNoAttackRangeRing()
    {
        PaintRect(-8, -4, 20, 12);
        var worker = SpawnUnit(PawnPath, new Vector3Int(0, 0), "Ringless worker");
        yield return null;

        Selection.selected.Clear();
        Selection.selectedBuilding = null;
        Selection.selected.Add(worker.GetComponent<UnitMover>());
        InvokeUpdateRings();
        yield return null;

        Assert.IsFalse(Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None)
                .Any(r => r.IsVisible),
            "A noncombatant worker must not advertise an attack range.");
    }

    // ------------------------------------------------------------------ A8

    [UnityTest]
    public IEnumerator UnreachableManualGather_ShowsBlockedMarker_ButStillAssigns()
    {
        PaintRect(-4, -4, 8, 8);
        var worker = SpawnUnit(PawnPath, new Vector3Int(0, 0), "Unreachable gatherer");
        yield return null;

        Selection.selected.Clear();
        Selection.selected.Add(worker.GetComponent<UnitMover>());

        var nodeGo = new GameObject("Unreachable node");
        nodeGo.transform.position = Grid.CellCenter(new Vector3Int(20, 0));
        var node = nodeGo.AddComponent<ResourceNode>();
        node.kind = ResourceKind.Wood;
        node.amount = 100;
        node.maxAmount = 100;

        var assign = typeof(SelectionManager).GetMethod(
            "AssignGatherers", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(assign);
        bool accepted = (bool)assign.Invoke(Selection, new object[] { node });

        Assert.IsTrue(accepted, "The manual order must still be accepted and retried.");
        Assert.AreSame(node, worker.GetComponent<Gatherer>().TargetNode);
        Assert.IsNotNull(GameObject.Find("BlockedMarker"),
            "An unreachable manual gather must show the same red-X feedback as a refused move.");
    }

    // ------------------------------------------------------------------ A9

    [UnityTest]
    public IEnumerator SaveBackup_LoadsBakWhenPrimaryIsCorrupt()
    {
        string path = Path.Combine(Application.temporaryCachePath, "atg_a_tier_backup.json");
        string backup = path + ".bak";
        try
        {
            PaintRect(-2, -2, 5, 5);
            var data = SaveGameService.CaptureSnapshot(out string captureError);
            Assert.IsNotNull(data, captureError);

            data.economy.gold = 111;
            Assert.IsTrue(SaveGameService.SaveToPath(data, path, out string firstError), firstError);
            data.economy.gold = 222;
            Assert.IsTrue(SaveGameService.SaveToPath(data, path, out string secondError), secondError);
            Assert.IsTrue(File.Exists(backup), "The second write must keep a .bak.");

            File.WriteAllText(path, "{ not valid json");
            Assert.IsTrue(SaveGameService.TryReadWithBackup(path, out var loaded, out string readError),
                readError);
            Assert.AreEqual(111, loaded.economy.gold,
                "The backup must contain the previous valid save.");
            Assert.IsTrue(SaveGameService.LastReadUsedBackup);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(backup)) File.Delete(backup);
        }
        yield return null;
    }

    // ------------------------------------------------------------------ helpers

    GameObject SpawnBuilding(string path, Vector3Int cell, string name)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab,
            Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        var building = go.GetComponent<Building>();
        if (building != null) building.Recompute();
        return go;
    }

    GameObject SpawnUnit(string path, Vector3Int cell, string name)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        go.name = name;
        UnitLimits.Invalidate();
        Population.Invalidate();
        return go;
    }

    void InvokeUpdateRings()
    {
        typeof(SelectionManager).GetMethod("UpdateRings",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(Selection, null);
    }
}
