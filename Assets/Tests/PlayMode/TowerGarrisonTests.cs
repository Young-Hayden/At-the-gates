using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Task 3 regressions: a garrisoned tower shows the real archer on top of it, the
/// tower fires from the archer's position and animation, and the same archer object
/// is returned to the field when the tower is emptied or lost.
/// </summary>
public class TowerGarrisonTests : RegressionFixture
{
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string ArrowPath = "Assets/Prefabs/Misc/Arrow.prefab";

    // Values the visible garrison must use. The implementation also exposes these
    // as tunable public fields; the assertions pin the agreed defaults.
    static readonly Vector2 ExpectedOffset = new Vector2(0f, 2.0f);
    const int ExpectedSortingBonus = 20;

    // ------------------------------------------------------------ visible archer

    [UnityTest]
    public IEnumerator Garrison_ShowsRealArcherOnTower_AndUntargetsIt()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;                       // let UnitActor.Start build the health bar

        var actor = archer.GetComponent<UnitActor>();
        var sprite = archer.GetComponentInChildren<SpriteRenderer>(true);
        var animator = archer.GetComponentInChildren<Animator>(true);
        var ysort = archer.GetComponentInChildren<YSort>(true);
        var bar = archer.GetComponentInChildren<HealthBar>(true);
        var collider = archer.GetComponent<Collider2D>();
        var mover = archer.GetComponent<UnitMover>();
        var combat = archer.GetComponent<CombatAI>();
        var towerSprite = tower.GetComponent<SpriteRenderer>();

        Assert.IsNotNull(sprite, "The archer prefab must expose a SpriteRenderer.");
        Assert.IsNotNull(animator, "The archer prefab must expose an Animator.");
        Assert.IsNotNull(ysort, "The archer sprite must carry YSort.");
        Assert.IsNotNull(bar, "The control archer should have a health bar.");
        actor.TakeDamage(1);                     // make the bar visible before hiding it
        Assert.IsTrue(bar.gameObject.activeSelf,
            "The hurt control archer should show its health bar.");

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsNotNull(garrison, "The tower prefab must carry TowerGarrison.");
        Assert.IsTrue(garrison.Garrison(actor), "A living friendly archer should garrison.");
        yield return null;
        yield return null;

        Assert.IsTrue(archer.gameObject.activeInHierarchy,
            "The real archer must stay active under the tower.");
        Assert.AreSame(tower.transform, archer.transform.parent,
            "The archer must be parented under the tower.");
        Assert.AreEqual(ExpectedOffset.x, archer.transform.localPosition.x, 0.01f,
            "The archer must sit centred on the tower.");
        Assert.AreEqual(ExpectedOffset.y, archer.transform.localPosition.y, 0.01f,
            "The archer must sit at the tower-top offset.");

        Assert.IsTrue(sprite.enabled, "The archer sprite must stay visible.");
        Assert.IsTrue(animator.enabled, "The archer animator must stay enabled.");
        Assert.IsFalse(actor.enabled, "Unit registration logic must be disabled while garrisoned.");
        Assert.IsFalse(mover.enabled, "Movement must be disabled while garrisoned.");
        Assert.IsFalse(combat.enabled, "CombatAI must be disabled while garrisoned.");
        Assert.IsFalse(collider.enabled, "The archer collider must be disabled while garrisoned.");
        Assert.IsFalse(UnitActor.All.Contains(actor),
            "A garrisoned archer must leave UnitActor.All.");
        Assert.IsFalse(ysort.enabled,
            "YSort must be disabled while the archer is pinned to the tower.");
        Assert.Greater(sprite.sortingOrder, towerSprite.sortingOrder,
            "The archer must draw above the tower sprite.");
        Assert.IsFalse(bar.gameObject.activeSelf,
            "The garrisoned archer's health bar must be hidden.");

        // Direct damage must be rejected while garrisoned; this is the same path an
        // arrow already in flight would take when it reaches the target.
        int healthInside = actor.health;
        actor.TakeDamage(1);
        yield return null;
        Assert.AreEqual(healthInside, actor.health,
            "A garrisoned archer must be immune to damage.");
        Assert.IsFalse(bar.gameObject.activeSelf,
            "Damage must not reveal the garrisoned archer's health bar.");

        Assert.AreEqual(ExpectedOffset, garrison.garrisonLocalOffset,
            "garrisonLocalOffset must expose the tower-top offset.");
        Assert.AreEqual(ExpectedSortingBonus, garrison.garrisonSortingBonus,
            "garrisonSortingBonus must expose the sorting lift above the tower.");
    }

    [UnityTest]
    public IEnumerator Garrison_TowerRemainsDamageable()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var building = tower.GetComponent<Building>();
        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(archer.GetComponent<UnitActor>()));
        yield return null;

        int before = building.health;
        building.TakeDamage(10);

        Assert.IsTrue(building.IsAlive, "The tower itself must remain alive while garrisoned.");
        Assert.AreEqual(before - 10, building.health,
            "The tower remains the damageable target; the archer does not take the hit.");
    }

    // ---------------------------------------------------------------- firing

    [UnityTest]
    public IEnumerator EnemyInRange_ProjectileSpawnsAboveTowerAtArcher()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        // Slow the arrow down so the measured position is the spawn point, not a
        // point several frames down its flight path.
        archer.GetComponent<UnitActor>().projectileSpeed = 0.5f;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(archer.GetComponent<UnitActor>()));
        SpawnEnemy(new Vector3Int(0, 3));        // left of the tower, inside 4.5 range

        yield return WaitUntil(() => FindProjectile() != null, 2f);
        var arrow = FindProjectile();
        Assert.IsNotNull(arrow, "An enemy in range must make the tower fire.");

        // The archer stands on the wooden platform inside the battlements. The
        // arrow should leave from that archer, not float above the whole tower.
        float towerPlatform = tower.transform.position.y + garrison.garrisonLocalOffset.y;
        Assert.Greater(arrow.transform.position.y, towerPlatform + 0.4f,
            "The arrow must spawn above the platform at the archer's bow.");
        Assert.Greater(arrow.transform.position.y, archer.transform.position.y - 1f,
            "The arrow must leave from the garrisoned archer, not the tower origin.");
        Assert.AreEqual(archer.transform.position.x, arrow.transform.position.x, 0.6f,
            "The arrow must leave from the archer's position on top of the tower.");
    }

    [UnityTest]
    public IEnumerator EnemyInRange_ArcherPlaysAttackAndFacesTarget()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var animator = archer.GetComponentInChildren<Animator>(true);
        var sprite = archer.GetComponentInChildren<SpriteRenderer>(true);
        Assert.IsNotNull(animator);
        Assert.IsTrue(HasParameter(animator, "Attack"),
            "The archer controller must declare the Attack trigger.");

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(archer.GetComponent<UnitActor>()));
        SpawnEnemy(new Vector3Int(0, 3));        // target is to the archer's left

        yield return WaitUntil(() => FindProjectile() != null, 2f);
        Assert.IsNotNull(FindProjectile(), "An enemy in range must make the tower fire.");

        yield return WaitUntil(
            () => animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), 0.5f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"),
            "Firing must set the archer's Attack trigger.");
        Assert.IsTrue(sprite.flipX,
            "The archer sprite must face the target on its left.");
    }

    // ------------------------------------------------------------ defense range

    [UnityTest]
    public IEnumerator DefenseRange_IsFivePointFive_AndFiresAtFiveButNotSix()
    {
        PaintRect(-10, -8, 28, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;
        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.AreEqual(5.5f, garrison.defenseRange, 0.001f);
        Assert.AreEqual(5.5f, garrison.ActiveRange, 0.001f);
        Assert.IsTrue(garrison.Garrison(archer.GetComponent<UnitActor>()));
        Assert.AreEqual(5.5f, garrison.ActiveRange, 0.001f);

        var farMiss = SpawnEnemy(new Vector3Int(-3, 3));   // distance 6.0
        yield return new WaitForSeconds(0.35f);
        Assert.IsNull(FindProjectile(), "A target at 6.0 must stay outside the 5.5 tower range.");
        UnityEngine.Object.Destroy(farMiss.gameObject);
        yield return null;

        SpawnEnemy(new Vector3Int(-2, 3));                 // distance 5.0
        yield return WaitUntil(() => FindProjectile() != null, 2f);
        Assert.IsNotNull(FindProjectile(), "A target at 5.0 must now be inside the 5.5 range.");
    }

    // ------------------------------------------------------------- ungarrison

    [UnityTest]
    public IEnumerator Ungarrison_RestoresArcherState_AndReturnsItToTheField()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var actor = archer.GetComponent<UnitActor>();
        var sprite = archer.GetComponentInChildren<SpriteRenderer>(true);
        var ysort = archer.GetComponentInChildren<YSort>(true);
        var bar = archer.GetComponentInChildren<HealthBar>(true);
        var collider = archer.GetComponent<Collider2D>();
        var mover = archer.GetComponent<UnitMover>();
        var combat = archer.GetComponent<CombatAI>();
        actor.TakeDamage(1);

        Transform originalParent = archer.transform.parent;
        Vector3 originalScale = archer.transform.localScale;
        Quaternion originalRotation = archer.transform.localRotation;
        int originalSorting = sprite.sortingOrder;
        bool originalBarActive = bar.gameObject.activeSelf;
        bool originalYSortEnabled = ysort.enabled;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(actor));
        yield return null;
        Assert.IsFalse(bar.gameObject.activeSelf, "Control: the bar is hidden inside the tower.");

        garrison.Ungarrison();

        Assert.IsFalse(garrison.HasArcher, "Ungarrison must clear the garrison.");
        Assert.AreSame(originalParent, archer.transform.parent,
            "Ungarrison must restore the original parent.");
        Assert.AreEqual(originalScale, archer.transform.localScale,
            "Ungarrison must restore the original scale.");
        Assert.AreEqual(originalRotation, archer.transform.localRotation,
            "Ungarrison must restore the original rotation.");
        Assert.IsTrue(archer.gameObject.activeInHierarchy,
            "The archer must stay active on the field.");
        Assert.IsTrue(sprite.enabled, "The sprite must stay visible.");
        Assert.AreEqual(originalYSortEnabled, ysort.enabled,
            "Ungarrison must restore the YSort enabled state.");
        Assert.AreEqual(originalSorting, sprite.sortingOrder,
            "Ungarrison must restore the cached sprite sorting order.");
        Assert.AreEqual(originalBarActive, bar.gameObject.activeSelf,
            "Ungarrison must restore the original health-bar state.");

        Assert.IsTrue(actor.enabled, "UnitActor must be enabled again.");
        Assert.IsTrue(mover.enabled, "UnitMover must be enabled again.");
        Assert.IsTrue(combat.enabled, "CombatAI must be enabled again.");
        Assert.IsTrue(collider.enabled, "The collider must be enabled again.");
        Assert.IsTrue(UnitActor.All.Contains(actor),
            "The released archer must be registered in UnitActor.All again.");
        Assert.IsFalse(mover.Moving, "The released archer must not keep walking.");

        var cell = Grid.WorldToCell(archer.transform.position);
        Assert.IsTrue(Grid.Walkable(cell),
            "The released archer must stand on a walkable cell.");
        Assert.Less(Vector3.Distance(archer.transform.position, tower.transform.position), 6f,
            "The released archer must reappear next to its tower.");

        yield return null;                       // one LateUpdate lets YSort take over
        Assert.AreEqual(YSort.OrderFor(archer.transform.position.y), sprite.sortingOrder,
            "YSort must resume driving the sprite order after ungarrison.");
    }

    // ------------------------------------------------------------- invulnerable

    [UnityTest]
    public IEnumerator GarrisonedArcher_IgnoresIncomingArrow_AndIsDamageableAfterRelease()
    {
        PaintRect(-10, -10, 30, 30);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var actor = archer.GetComponent<UnitActor>();
        var sprite = archer.GetComponentInChildren<SpriteRenderer>(true);
        var garrison = tower.GetComponent<TowerGarrison>();
        var arrowPrefab = LoadPrefab<GameObject>(ArrowPath);
        int healthBefore = actor.health;

        // Launch a control arrow at the archer, then garrison it before impact.
        var incoming = Projectile.Spawn(arrowPrefab,
            archer.transform.position + new Vector3(3f, 0.5f, 0f),
            actor, 30, 12f, Color.clear);
        Assert.IsNotNull(incoming, "The control arrow must launch at the archer.");
        Assert.IsTrue(garrison.Garrison(actor));

        yield return WaitUntil(() => incoming == null, 3f);
        Assert.IsTrue(incoming == null,
            "An arrow in flight must fizzle instead of hanging over a garrisoned archer.");
        Assert.AreEqual(healthBefore, actor.health,
            "A garrisoned archer must not take damage from an arrow already in flight.");
        Assert.IsTrue(sprite.enabled,
            "The archer must stay visible and animated while it is immune.");
        Assert.IsFalse(UnitActor.All.Contains(actor),
            "The immune archer must stay out of UnitActor.All.");

        garrison.Ungarrison();
        yield return null;
        Assert.IsTrue(actor.enabled,
            "Damageability must be restored when the archer leaves the tower.");

        int healthAfterRelease = actor.health;
        var second = Projectile.Spawn(arrowPrefab,
            archer.transform.position + new Vector3(3f, 0.5f, 0f),
            actor, 7, 12f, Color.clear);
        Assert.IsNotNull(second, "The released archer must accept a new arrow.");
        yield return WaitUntil(() => actor.health < healthAfterRelease, 2f);
        Assert.Less(actor.health, healthAfterRelease,
            "After ungarrison the same archer must be damageable again.");
    }

    // ------------------------------------------------------------ not a field unit

    [UnityTest]
    public IEnumerator GarrisonedArcher_IsNotSelectable_AndCannotManASecondTower()
    {
        PaintRect(-12, -12, 36, 36);
        var towerA = SpawnTower(new Vector3Int(3, 3));
        var towerB = SpawnTower(new Vector3Int(9, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var actor = archer.GetComponent<UnitActor>();
        var mover = archer.GetComponent<UnitMover>();
        var garrisonA = towerA.GetComponent<TowerGarrison>();
        var garrisonB = towerB.GetComponent<TowerGarrison>();

        // The same static predicates ClickSelect and BoxSelect use.
        var selectable = typeof(SelectionManager).GetMethod("Selectable",
            BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(UnitMover) }, null);
        var isLiving = typeof(SelectionManager).GetMethod("IsLiving",
            BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(UnitMover) }, null);
        Assert.IsNotNull(selectable, "SelectionManager.Selectable(UnitMover) must remain available.");
        Assert.IsNotNull(isLiving, "SelectionManager.IsLiving(UnitMover) must remain available.");

        Assert.IsTrue((bool)selectable.Invoke(null, new object[] { mover }),
            "Control: the field archer must be selectable.");
        Assert.IsTrue((bool)isLiving.Invoke(null, new object[] { mover }),
            "Control: the field archer must count as living.");

        Assert.IsTrue(garrisonA.Garrison(actor));
        yield return null;

        Assert.IsFalse((bool)selectable.Invoke(null, new object[] { mover }),
            "A garrisoned archer must fail the box/click selection predicate.");
        Assert.IsFalse((bool)isLiving.Invoke(null, new object[] { mover }),
            "A garrisoned archer must not receive orders.");

        // A second tower must not claim the same archer or steal it from the first.
        Assert.IsFalse(garrisonB.Garrison(actor),
            "A garrisoned archer must not be garrisoned into a second tower.");
        Assert.IsFalse(garrisonB.HasArcher, "The second tower must stay empty.");
        Assert.IsTrue(garrisonA.HasArcher, "The first tower must keep its archer.");
        Assert.AreSame(towerA.transform, archer.transform.parent,
            "A failed second garrison must not reparent the archer.");

        // A direct move order must ignore the garrisoned archer instead of walking
        // it off the tower and stranding it.
        Vector3 parked = archer.transform.position;
        Selection.selected.Add(mover);
        Selection.IssueMove(Grid.CellCenter(new Vector3Int(-6, 6, 0)));
        yield return null;
        Assert.AreEqual(parked, archer.transform.position,
            "A garrisoned archer must not accept a move order.");
        Assert.IsFalse(mover.Moving, "A garrisoned archer must not start walking.");
        Selection.selected.Clear();

        garrisonA.Ungarrison();
        yield return null;
        Assert.IsTrue((bool)selectable.Invoke(null, new object[] { mover }),
            "The released archer must be selectable again.");
        Assert.IsTrue(UnitActor.All.Contains(actor),
            "The released archer must return to UnitActor.All.");
    }

    // ---------------------------------------------------------- tower loss

    [UnityTest]
    public IEnumerator TowerWreck_LeavesArcherOnWalkableGround_CombatReady()
    {
        PaintRect(-10, -10, 30, 30);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var actor = archer.GetComponent<UnitActor>();
        var collider = archer.GetComponent<Collider2D>();
        var mover = archer.GetComponent<UnitMover>();
        var combat = archer.GetComponent<CombatAI>();
        var building = tower.GetComponent<Building>();
        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(garrison.Garrison(actor));
        yield return null;

        building.TakeDamage(100000);
        yield return null;

        Assert.IsFalse(building.IsAlive, "The tower must be wrecked.");
        Assert.IsFalse(garrison.HasArcher, "A wrecked tower cannot keep its archer.");
        Assert.IsTrue(archer != null, "The archer object must survive the wreck.");
        Assert.IsTrue(archer.gameObject.activeInHierarchy,
            "The archer must be active on the field after the wreck.");
        Assert.IsNull(archer.transform.parent,
            "The archer must be detached from the tower at wreck time.");
        Assert.IsTrue(collider.enabled, "The archer collider must be enabled after the wreck.");
        Assert.IsTrue(actor.enabled, "UnitActor must be enabled after the wreck.");
        Assert.IsTrue(mover.enabled, "UnitMover must be enabled after the wreck.");
        Assert.IsTrue(combat.enabled, "CombatAI must be enabled after the wreck.");
        Assert.IsTrue(UnitActor.All.Contains(actor),
            "The released archer must be registered in UnitActor.All after the wreck.");
        Assert.IsTrue(Grid.Walkable(Grid.WorldToCell(archer.transform.position)),
            "The released archer must land on walkable ground.");

        // Combat is live again: an enemy in range must be shot.
        var enemy = SpawnEnemy(new Vector3Int(0, 3));
        int before = enemy.health;
        yield return WaitUntil(() => enemy == null || enemy.health < before, 3f);
        Assert.IsTrue(enemy == null || enemy.health < before,
            "The freed archer must re-enter combat after its tower falls.");
    }

    [UnityTest]
    public IEnumerator TowerDestroyed_ArcherIsReleasedBeforeTheObjectDisappears()
    {
        PaintRect(-10, -10, 30, 30);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var building = tower.GetComponent<Building>();
        building.destroyedSprite = null;         // force the Destroy path, not the wreck sprite
        var actor = archer.GetComponent<UnitActor>();
        Assert.IsTrue(tower.GetComponent<TowerGarrison>().Garrison(actor));
        yield return null;

        building.TakeDamage(100000);
        yield return null;                       // deferred Destroy happens at frame end

        Assert.IsTrue(tower == null, "The control tower object should be destroyed.");
        Assert.IsTrue(archer != null, "Destroying the tower must not destroy the archer.");
        Assert.IsTrue(archer.gameObject.activeInHierarchy,
            "The archer must remain active after the tower is destroyed.");
        Assert.IsNull(archer.transform.parent,
            "The archer must be reparented before the tower is destroyed.");
        Assert.IsTrue(UnitActor.All.Contains(actor),
            "The archer must be registered in UnitActor.All after the tower is destroyed.");
        Assert.IsTrue(Grid.Walkable(Grid.WorldToCell(archer.transform.position)),
            "The archer must be relocated to walkable ground.");

        // Combat must be live again: an enemy in range takes damage.
        var enemy = SpawnEnemy(new Vector3Int(0, 2));
        int before = enemy.health;
        yield return WaitUntil(() => enemy == null || enemy.health < before, 3f);
        Assert.IsTrue(enemy == null || enemy.health < before,
            "The released archer must re-enter combat after the tower is destroyed.");
    }

    [UnityTest]
    public IEnumerator TowerDemolished_ArcherSurvivesOnWalkableGround()
    {
        PaintRect(-10, -10, 30, 30);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var actor = archer.GetComponent<UnitActor>();
        var building = tower.GetComponent<Building>();
        Assert.IsTrue(tower.GetComponent<TowerGarrison>().Garrison(actor));
        yield return null;

        building.Demolish();
        yield return null;

        Assert.IsTrue(tower == null, "Demolish must remove the tower object.");
        Assert.IsTrue(archer != null,
            "Demolishing a tower must release its archer, not destroy it with the tower.");
        Assert.IsTrue(archer.gameObject.activeInHierarchy,
            "The archer must stay active after the tower is demolished.");
        Assert.IsNull(archer.transform.parent,
            "The archer must be detached from the demolished tower.");
        Assert.IsTrue(actor.enabled, "UnitActor must be enabled after the demolish.");
        Assert.IsTrue(UnitActor.All.Contains(actor),
            "The released archer must be registered in UnitActor.All after the demolish.");
        Assert.IsTrue(Grid.Walkable(Grid.WorldToCell(archer.transform.position)),
            "The archer must be relocated to walkable ground after the demolish.");

        // Combat must be live again: an enemy in range takes damage.
        var enemy = SpawnEnemy(new Vector3Int(0, 2));
        int before = enemy.health;
        yield return WaitUntil(() => enemy == null || enemy.health < before, 3f);
        Assert.IsTrue(enemy == null || enemy.health < before,
            "The released archer must re-enter combat after the tower is demolished.");
    }

    [UnityTest]
    public IEnumerator SceneReload_DoesNotResurrectGarrisonedArcher()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;
        Assert.IsTrue(tower.GetComponent<TowerGarrison>().Garrison(archer.GetComponent<UnitActor>()));
        yield return null;

        var reload = EditorSceneManager.LoadSceneInPlayMode(
            RegressionScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        Assert.IsTrue(reload.IsValid(), "The reload scene handle must be valid.");

        const float ReloadTimeout = 15f;
        float deadline = Time.realtimeSinceStartup + ReloadTimeout;
        bool settled = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            settled = reload.isLoaded && tower == null && archer == null &&
                      UnitActor.All.Count == 0;
            if (settled) break;
            yield return null;
        }

        Assert.IsTrue(settled,
            "The scene reload did not settle. Loaded=" + reload.isLoaded +
            ", tower alive=" + (tower != null) +
            ", archer alive=" + (archer != null) +
            ", units=" + UnitActor.All.Count + ".");
        Assert.AreEqual(0, UnitActor.All.Count,
            "A garrisoned archer must not be resurrected into the reloaded scene.");
    }

    // -------------------------------------------------------- construction safety

    [UnityTest]
    public IEnumerator UnderConstructionTower_RejectsNormalGarrison_AndRestoreCannotFireEarly()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        tower.MarkBuiltDuringMatch();
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;
        Assert.IsTrue(tower.IsUnderConstruction,
            "Control: a tower dropped during the match must start under construction.");

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsFalse(garrison.Garrison(archer),
            "A half-built tower must reject the normal garrison order.");
        Assert.IsFalse(garrison.HasArcher,
            "A rejected normal order must not leave an archer in the tower.");

        // Old saves may have garrisoned the tower before construction finished.
        Assert.IsTrue(garrison.RestoreGarrison(archer),
            "Save restoration must be able to restore a garrison on a half-built tower.");
        Assert.IsTrue(garrison.HasArcher,
            "The legacy restore path must keep the garrison relation.");

        var bar = archer.GetComponentInChildren<HealthBar>(true);
        Assert.IsNotNull(bar, "The started archer must have its health bar before garrison restoration.");
        bar.gameObject.SetActive(true);
        var update = typeof(TowerGarrison).GetMethod(
            "Update", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(update, "The test must drive the same private TowerGarrison.Update entry point.");
        update.Invoke(garrison, null);
        Assert.IsFalse(bar.gameObject.activeSelf,
            "Construction early-return must still hide a re-shown garrison health bar.");

        SpawnEnemy(new Vector3Int(0, 3));
        yield return new WaitForSeconds(0.25f);
        Assert.IsNull(FindProjectile(),
            "A tower under construction must not fire even when restored from a legacy save.");

        typeof(Building).GetMethod("FinishConstruction",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tower, null);
        yield return WaitUntil(() => FindProjectile() != null, 2f);
        Assert.IsNotNull(FindProjectile(),
            "After construction finishes the restored archer must man the tower and fire.");
    }

    [UnityTest]
    public IEnumerator UnderConstructionTower_PlayerGarrisonOrder_IsConsumedWithoutPathOrGarrison()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = SpawnTower(new Vector3Int(3, 3));
        tower.MarkBuiltDuringMatch();
        var archer = SpawnArcher(new Vector3Int(-3, -3));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        var mover = archer.GetComponent<UnitMover>();
        Assert.IsTrue(tower.IsUnderConstruction,
            "Control: a tower dropped during the match must start under construction.");
        Assert.IsNotNull(mover, "The archer prefab must carry UnitMover.");

        Selection.selected.Clear();
        Selection.selected.Add(mover);
        Vector3 start = archer.transform.position;

        // This is the same public entry point used by the player's right click.
        Selection.IssueMove(tower.transform.position + Vector3.up * 0.5f);

        Assert.IsFalse(garrison.HasArcher,
            "The player's garrison command must not man a half-built tower.");
        Assert.IsFalse(mover.Moving,
            "A rejected garrison command must be consumed instead of falling through to a move path.");
        var marker = GameObject.Find("BlockedMarker");
        Assert.IsNotNull(marker, "The rejected garrison command must show the blocked marker.");
        Assert.IsTrue(marker.activeSelf);
        Assert.IsTrue(Selection.selected.Contains(mover),
            "The rejected garrison command must not clear the selection.");

        yield return null;
        yield return null;
        Assert.IsFalse(garrison.HasArcher,
            "No delayed arrival callback may garrison the archer during construction.");
        Assert.IsFalse(mover.Moving,
            "No approach path may be created for a half-built tower.");
        Assert.Less(Vector3.Distance(start, archer.transform.position), 0.01f,
            "The archer must not start walking toward a half-built tower.");
        Selection.selected.Clear();
    }

    // ---------------------------------------------------------------- helpers

    Building SpawnTower(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(TowerPath);
        var go = UnityEngine.Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = "Test tower";
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, "The tower prefab must carry a Building.");
        building.Recompute();
        return building;
    }

    UnitActor SpawnArcher(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(ArcherPath);
        var go = UnityEngine.Object.Instantiate(
            prefab, Grid.CellCenter(cell), Quaternion.identity);
        go.name = "Test archer";
        return go.GetComponent<UnitActor>();
    }

    static UnitActor SpawnEnemy(Vector3Int cell)
    {
        var go = new GameObject("Test enemy");
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        var actor = go.AddComponent<UnitActor>();
        actor.team = Team.Red;
        actor.showHealthBar = false;
        actor.maxHealth = 100;
        actor.health = 100;
        actor.isCombatant = true;
        return actor;
    }

    static Projectile FindProjectile()
    {
        return UnityEngine.Object.FindFirstObjectByType<Projectile>();
    }

    static bool HasParameter(Animator animator, string parameter)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (var p in animator.parameters)
            if (p.name == parameter) return true;
        return false;
    }

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float deadline = Time.time + timeout;
        while (!condition() && Time.time < deadline)
            yield return null;
    }
}
