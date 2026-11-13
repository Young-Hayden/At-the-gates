using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Player-facing regressions for persistent rubble collision and the complete
/// right-click approach / garrison / tower-attack sequence.
/// </summary>
public class RubbleAndGarrisonCommandTests : RegressionFixture
{
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";

    [UnityTest]
    public IEnumerator DestroyedRubble_KeepsFootprintAndColliderBlocking()
    {
        PaintRect(-4, -4, 12, 12);
        var tower = SpawnTower(new Vector3Int(0, 0, 0));
        var building = tower.GetComponent<Building>();
        var collider = tower.GetComponent<Collider2D>();
        var sprite = tower.GetComponent<SpriteRenderer>();
        var cells = new List<Vector3Int>(building.Cells);
        Assert.IsNotNull(building.destroyedSprite, "The control tower must leave rubble.");
        Assert.IsNotEmpty(cells, "The control tower must reserve a footprint.");
        foreach (var cell in cells)
            Assert.IsTrue(Grid.IsBlockedByBuilding(cell),
                "The intact control tower must block its footprint.");

        building.TakeDamage(100000);
        yield return null;

        Assert.IsFalse(building.IsAlive, "The control tower must be destroyed.");
        Assert.AreSame(building.destroyedSprite, sprite.sprite,
            "The control tower must still be showing its rubble sprite.");
        Assert.IsTrue(collider.enabled,
            "Persistent rubble must keep its physical collider enabled.");
        foreach (var cell in cells)
        {
            Assert.IsTrue(Grid.IsBlockedByBuilding(cell),
                "Persistent rubble must retain the building reservation at " + cell + ".");
            Assert.IsFalse(Grid.Walkable(cell),
                "Units must not path through persistent rubble at " + cell + ".");
        }
    }

    [UnityTest]
    public IEnumerator RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3, 0));
        var archer = SpawnArcher(new Vector3Int(-3, 3, 0));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        var mover = archer.GetComponent<UnitMover>();
        mover.speed = 8f;
        Selection.selected.Clear();
        Selection.selected.Add(mover);

        // This is the same public entry point used by the player's right click.
        Selection.IssueMove(tower.transform.position + Vector3.up * 0.5f);

        Assert.IsFalse(garrison.HasArcher,
            "A distant archer must not teleport into the tower on the command frame.");
        Assert.IsNull(archer.transform.parent,
            "A distant archer must remain in the world while approaching.");
        Assert.IsTrue(mover.Moving,
            "The garrison order must give the distant archer a path to the tower.");

        Vector3 outsideAtEntry = archer.transform.position;
        float deadline = Time.time + 5f;
        while (!garrison.HasArcher && Time.time < deadline)
        {
            outsideAtEntry = archer.transform.position;
            yield return null;
        }

        Assert.IsTrue(garrison.HasArcher,
            "The archer must enter the tower after reaching its base.");
        Assert.Less(Vector2.Distance(outsideAtEntry, tower.transform.position), 2.1f,
            "The archer must walk to the tower base before being pinned on top.");
        Assert.AreSame(tower.transform, archer.transform.parent,
            "The same archer must be parented to the tower after arrival.");

        var enemy = SpawnEnemy(tower.transform.position + new Vector3(3f, 0.5f, 0f), 200);
        yield return WaitUntil(() => enemy == null || enemy.health < 200, 3f);
        Assert.IsTrue(enemy == null || enemy.health < 200,
            "After the approach and garrison sequence, the tower archer must damage an enemy in range.");
    }

    [UnityTest]
    public IEnumerator RightClickVisibleTowerTop_DistantArcherWalksToBaseThenGarrisons()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3, 0));
        var archer = SpawnArcher(new Vector3Int(-3, 3, 0));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        var mover = archer.GetComponent<UnitMover>();
        mover.speed = 8f;
        Selection.selected.Clear();
        Selection.selected.Add(mover);

        // The visible tower is about four units tall, while its physical base
        // collider is only one unit tall. Click the upper sprite, as a player does
        // when aiming at the battlements rather than the tower's footprint.
        Vector3 click = tower.transform.position + Vector3.up * 2.5f;
        Assert.IsTrue(tower.GetComponent<SpriteRenderer>().bounds.Contains(click),
            "Control: the command must land on the visible tower sprite.");
        Assert.IsFalse(tower.GetComponent<Collider2D>().OverlapPoint(click),
            "Control: this click must be above the tower's base collider.");

        Selection.IssueMove(click);

        float deadline = Time.time + 5f;
        while (!garrison.HasArcher && Time.time < deadline)
            yield return null;

        Assert.IsTrue(garrison.HasArcher,
            "Clicking the visible tower top must use the same approach/garrison order as clicking its base.");
        Assert.AreSame(tower.transform, archer.transform.parent,
            "The arriving archer must be pinned to the clicked tower.");
    }

    [UnityTest]
    public IEnumerator RightClickOccupiedTower_WithoutAnySelection_ReleasesArcher()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3, 0));
        var archer = SpawnArcher(new Vector3Int(-3, 3, 0));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(archer), "Control: the archer must start inside the tower.");
        Selection.selected.Clear();

        var rightClick = typeof(SelectionManager).GetMethod(
            "HandleRightClickInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(rightClick, "The test must drive the production right-click entry point.");
        rightClick.Invoke(Selection, new object[] { tower.transform.position + Vector3.up * 0.5f, true });

        Assert.IsFalse(garrison.HasArcher,
            "Right-clicking an occupied tower must release its archer even though entering cleared the selection.");
        Assert.IsTrue(archer.enabled, "The released archer's UnitActor must be active again.");
        Assert.IsTrue(UnitActor.All.Contains(archer), "The released archer must return to the unit registry.");
    }

    Building SpawnTower(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(TowerPath);
        var go = UnityEngine.Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = "Rubble/garrison command test tower";
        var building = go.GetComponent<Building>();
        building.Recompute();
        return building;
    }

    UnitActor SpawnArcher(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(ArcherPath);
        var go = UnityEngine.Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        go.name = "Garrison command test archer";
        return go.GetComponent<UnitActor>();
    }

    static UnitActor SpawnEnemy(Vector3 worldPosition, int health)
    {
        var go = new GameObject("Garrison command test enemy");
        go.transform.position = worldPosition;
        var actor = go.AddComponent<UnitActor>();
        actor.team = Team.Red;
        actor.showHealthBar = false;
        actor.maxHealth = health;
        actor.health = health;
        return actor;
    }

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float deadline = Time.time + timeout;
        while (!condition() && Time.time < deadline)
            yield return null;
    }
}
