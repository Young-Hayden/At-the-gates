using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Phase B Task 3: the technology layer.
///
/// Three one-level lines researched at a completed command centre, one at a time and
/// paid up front: Forge (+2 damage), Armour (+15 % max HP) and Gathering (+2 per trip).
/// The effects are computed from the per-team mask, never by editing prefab stats, so a
/// fresh game is unchanged and a bonus can never be applied twice. The mask is the one
/// save-format change in Phase B (version 2, version-1 files load with an empty mask).
/// </summary>
public class TechnologyTests : RegressionFixture
{
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";
    const string RedCastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Red.prefab";
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string RedWarriorPath = "Assets/Prefabs/Units/Warrior_Red.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string RedPawnPath = "Assets/Prefabs/Units/Pawn_Red.prefab";
    const string MonkPath = "Assets/Prefabs/Units/Monk_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";

    // ------------------------------------------------------------ pure tables

    [Test]
    public void Constants_AreTheApprovedNumbers()
    {
        Assert.AreEqual(250, Technology.CostOf(Technology.Tech.Forge).gold);
        Assert.AreEqual(100, Technology.CostOf(Technology.Tech.Forge).wood);
        Assert.AreEqual(250, Technology.CostOf(Technology.Tech.Armour).gold);
        Assert.AreEqual(100, Technology.CostOf(Technology.Tech.Armour).wood);
        Assert.AreEqual(200, Technology.CostOf(Technology.Tech.Gathering).gold);
        Assert.AreEqual(0, Technology.CostOf(Technology.Tech.Gathering).wood);

        foreach (Technology.Tech tech in Enum.GetValues(typeof(Technology.Tech)))
            Assert.AreEqual(45f, Technology.SecondsOf(tech), tech + " must take 45 s.");

        Technology.Reset();
        Assert.AreEqual(0, Technology.DamageBonus(Team.Blue), "No Forge, no damage bonus.");
        Assert.AreEqual(1f, Technology.MaxHealthMultiplier(Team.Blue), 0.0001f);
        Assert.AreEqual(0, Technology.CarryBonus(Team.Blue), "No Gathering, no carry bonus.");

        Technology.Restore(Team.Blue, 7);
        Assert.AreEqual(2, Technology.DamageBonus(Team.Blue), "Forge adds 2 damage.");
        Assert.AreEqual(1.15f, Technology.MaxHealthMultiplier(Team.Blue), 0.0001f);
        Assert.AreEqual(2, Technology.CarryBonus(Team.Blue), "Gathering adds 2 per trip.");
        Assert.AreEqual(0, Technology.DamageBonus(Team.Red), "Bonuses are per team.");
        Technology.Reset();
    }

    // ------------------------------------------------------- begin and refuse

    [UnityTest]
    public IEnumerator BeginResearch_PaysTheCostOnce_AndExposesProgress()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "A completed command centre must accept the first research. " + reason);
        Assert.IsNull(reason, "An accepted research must not report a refusal.");
        Assert.AreEqual(750, Economy.gold, "Forge costs 250 gold up front.");
        Assert.AreEqual(400, Economy.wood, "Forge costs 100 wood up front.");
        Assert.IsTrue(Technology.ActiveTech.HasValue);
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTech.Value);
        Assert.AreEqual(0f, Technology.Progress, 0.001f);

        Technology.Tick(20f);
        Assert.AreEqual(20f / 45f, Technology.Progress, 0.01f,
            "Progress must be the fraction of the 45 s already spent.");
        Assert.IsFalse(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "The tech must not exist before its time is up.");

        Technology.Tick(25f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "The tech completes when its 45 s elapse.");
        Assert.IsFalse(Technology.ActiveTech.HasValue,
            "A finished research must free the slot.");
        Assert.AreEqual(750, Economy.gold, "The cost must be charged exactly once.");
        Assert.AreEqual(400, Economy.wood, "The cost must be charged exactly once.");
    }

    [UnityTest]
    public IEnumerator BeginResearch_WithoutCommandCentre_IsRefused()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        yield return null;

        Assert.IsFalse(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "Research without a command centre must be refused.");
        StringAssert.Contains("指挥中心", reason,
            "The refusal must name the missing command centre.");
        Assert.AreEqual(1000, Economy.gold, "A refusal must not spend gold.");
        Assert.AreEqual(500, Economy.wood, "A refusal must not spend wood.");
        Assert.IsFalse(Technology.ActiveTech.HasValue, "A refusal must not start a research.");
    }

    [UnityTest]
    public IEnumerator BeginResearch_UnderConstructionCommandCentre_IsRefused()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        var castle = CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue,
            underConstruction: true);
        yield return null;
        Assert.IsTrue(castle.IsUnderConstruction,
            "Control: the castle must really be half-built.");
        Assert.IsTrue(Building.HasLivingCommandCentre(Team.Blue),
            "Control: the half-built castle still counts as a living centre.");

        Assert.IsFalse(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "A half-built command centre must not unlock research.");
        StringAssert.Contains("指挥中心", reason);
        Assert.AreEqual(1000, Economy.gold, "A refusal must not spend gold.");

        castle.TakeDamage(1000000);
        yield return null;
        Assert.IsFalse(castle.IsAlive, "Control: the wrecked castle must be gone.");
        Assert.IsFalse(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out _),
            "A wrecked command centre must not unlock research either.");
    }

    [UnityTest]
    public IEnumerator BeginResearch_WhileAnotherIsRunning_IsRefused()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out _));
        int gold = Economy.gold;
        int wood = Economy.wood;

        Assert.IsFalse(Technology.BeginResearch(Team.Blue, Technology.Tech.Armour, out string reason),
            "A second research must be refused while one is running.");
        StringAssert.Contains("研究", reason,
            "The refusal must explain that a research is already running.");
        Assert.AreEqual(gold, Economy.gold, "The refused second order must not spend gold.");
        Assert.AreEqual(wood, Economy.wood, "The refused second order must not spend wood.");
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTech.Value,
            "The running research must be the one that survives the refusal.");
    }

    [UnityTest]
    public IEnumerator BeginResearch_Unaffordable_IsRefusedWithoutSpending()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 100;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        yield return null;

        Assert.IsFalse(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "An unaffordable research must be refused.");
        StringAssert.Contains("资源不足", reason,
            "The refusal must use the same 资源不足 wording as the production path.");
        Assert.AreEqual(100, Economy.gold, "An unaffordable order must not spend the little gold it has.");
        Assert.AreEqual(500, Economy.wood);
        Assert.IsFalse(Technology.ActiveTech.HasValue,
            "An unaffordable order must not occupy the research slot.");
    }

    [UnityTest]
    public IEnumerator BeginResearch_UsesTheCallerSuppliedBank()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        int economyGold = Economy.gold;
        int economyWood = Economy.wood;
        CreateCastle(RedCastlePath, new Vector3Int(0, 0, 0), Team.Red);
        yield return null;

        var charges = new List<ResourceCost>();
        bool Pay(ResourceCost cost)
        {
            charges.Add(cost);
            return true;
        }

        Assert.IsTrue(Technology.BeginResearch(Team.Red, Technology.Tech.Gathering, Pay, out string reason),
            "The caller's own bank must be enough; the player's Economy is not involved. " + reason);
        Assert.AreEqual(1, charges.Count, "The cost must be charged exactly once.");
        Assert.AreEqual(200, charges[0].gold, "Gathering costs 200 gold.");
        Assert.AreEqual(0, charges[0].wood, "Gathering costs no wood.");
        Assert.AreEqual(economyGold, Economy.gold,
            "Researching for another side must not touch the player's bank.");
        Assert.AreEqual(economyWood, Economy.wood,
            "Researching for another side must not touch the player's bank.");
        Assert.IsTrue(Technology.Has(Team.Red, Technology.Tech.Gathering) == false);
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Red, Technology.Tech.Gathering));
        Assert.IsFalse(Technology.Has(Team.Blue, Technology.Tech.Gathering),
            "A per-team mask must not leak across sides.");
    }

    [UnityTest]
    public IEnumerator Research_CompletesOnlyAfterFortyFiveSeconds()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out _));
        Technology.Tick(44f);
        Assert.IsFalse(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "Research must not finish one second early.");
        Assert.IsTrue(Technology.ActiveTech.HasValue);
        Technology.Tick(1f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "Research must finish once 45 s have elapsed.");
    }

    [UnityTest]
    public IEnumerator SameTech_CannotBeResearchedTwice()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 2000;
        Economy.wood = 1000;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge));

        int gold = Economy.gold;
        int wood = Economy.wood;
        Assert.IsFalse(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "An already researched tech must not be researched again.");
        StringAssert.Contains("已研究", reason);
        Assert.AreEqual(gold, Economy.gold, "The refused repeat must not spend gold.");
        Assert.AreEqual(wood, Economy.wood, "The refused repeat must not spend wood.");
        Assert.IsFalse(Technology.ActiveTech.HasValue);
    }

    // -------------------------------------------------------------- effects

    [UnityTest]
    public IEnumerator Forge_AddsTwoDamage_ToExistingAndLaterUnits_WithoutTouchingPrefabStats()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var existing = CreateWarrior(WarriorPath, new Vector3Int(-6, 0, 0), Team.Blue, "Existing warrior");
        yield return null;

        Assert.AreEqual(16, existing.attackDamage, "Control: the warrior prefab deals 16.");
        Assert.AreEqual(16, existing.EffectiveAttackDamage,
            "Control: with no tech the effective damage equals the prefab value.");

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge), "Forge must be researched.");

        Assert.AreEqual(18, existing.EffectiveAttackDamage,
            "Forge must add exactly 2 to a unit that already existed.");
        Assert.AreEqual(16, existing.attackDamage,
            "The prefab-authored damage field must stay the source of truth.");

        var later = CreateWarrior(WarriorPath, new Vector3Int(6, 0, 0), Team.Blue, "Later warrior");
        yield return null;
        Assert.AreEqual(18, later.EffectiveAttackDamage,
            "A unit produced after the research must also deal +2.");
        Assert.AreEqual(16, later.attackDamage,
            "A later unit must not have its authored value overwritten.");

        var prefabActor = LoadPrefab<GameObject>(WarriorPath).GetComponent<UnitActor>();
        Assert.IsNotNull(prefabActor);
        Assert.AreEqual(16, prefabActor.attackDamage,
            "Forge must never edit the prefab asset.");

        var enemy = CreateDummy("Forge target", new Vector3Int(6, 3, 0), Team.Red, 200);
        ApplyMelee(existing.GetComponent<CombatAI>(), enemy);
        Assert.AreEqual(182, enemy.health,
            "The real melee path must deal the buffed 18 damage, not the authored 16.");
    }

    [UnityTest]
    public IEnumerator Armour_RaisesMaxHealthOnce_ForExistingAndLaterUnits()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var existing = CreateWarrior(WarriorPath, new Vector3Int(-6, 0, 0), Team.Blue, "Existing warrior");
        var hurt = CreateWarrior(WarriorPath, new Vector3Int(-12, 0, 0), Team.Blue, "Hurt warrior");
        yield return null;
        hurt.health = 55;

        Assert.AreEqual(110, existing.EffectiveMaxHealth, "Control: base max health is 110.");
        Assert.AreEqual(110, existing.health, "Control: the warrior starts at full health.");

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Armour, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Armour), "Armour must be researched.");

        Assert.AreEqual(127, existing.EffectiveMaxHealth,
            "Armour must raise max health by 15 % once: 110 -> 127.");
        Assert.AreEqual(127, existing.health,
            "The extra max health is granted to a unit that existed when the research finished.");
        Assert.AreEqual(72, hurt.health,
            "A damaged unit keeps its missing health: 55 + 17 = 72.");
        Assert.AreEqual(110, existing.maxHealth,
            "The authored max health field must stay the source of truth.");

        // The non-compounding proof is the unit produced below: it is computed from the
        // same mask and must be 127, never 146. (Re-calling Complete() here would not
        // probe anything — it no-ops with no line running.)

        var later = CreateWarrior(WarriorPath, new Vector3Int(6, 0, 0), Team.Blue, "Later warrior");
        yield return null;
        Assert.AreEqual(127, later.EffectiveMaxHealth,
            "A unit produced later must be at base x 1.15 (127).");
        Assert.AreNotEqual(146, later.EffectiveMaxHealth,
            "A later unit must never be at base x 1.15 x 1.15 (146).");
        Assert.AreEqual(127, later.health, "A freshly produced unit must spawn at full buffed health.");

        var red = CreateWarrior(RedWarriorPath, new Vector3Int(12, 0, 0), Team.Red, "Red warrior");
        yield return null;
        Assert.AreEqual(110, red.EffectiveMaxHealth,
            "A blue research must not buff the enemy's units.");
    }

    [UnityTest]
    public IEnumerator Armour_DoesNotBuffNonCombatants_AndTheHealerLeavesThemAlone()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var warrior = CreateWarrior(WarriorPath, new Vector3Int(-10, 0, 0), Team.Blue, "Armour warrior");
        var pawn = CreateWorker(PawnPath, new Vector3Int(-6, 6, 0), Team.Blue)
            .GetComponent<UnitActor>();
        var monk = CreateMonk(MonkPath, new Vector3Int(-2, 6, 0), Team.Blue, "Armour monk");
        var healer = monk.GetComponent<Healer>();
        yield return null;

        Assert.AreEqual(70, pawn.maxHealth, "Control: the pawn prefab has 70 max health.");
        Assert.AreEqual(55, monk.maxHealth, "Control: the monk prefab has 55 max health.");
        Assert.AreEqual(70, pawn.EffectiveMaxHealth, "Control: with no tech the pawn reads 70.");
        Assert.AreEqual(55, monk.EffectiveMaxHealth, "Control: with no tech the monk reads 55.");
        Assert.IsNull(healer.Patient, "Control: nobody is wounded before Armour.");

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Armour, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Armour), "Armour must be researched.");

        Assert.AreEqual(127, warrior.EffectiveMaxHealth,
            "Control: a combatant still gets 110 -> 127.");
        Assert.AreEqual(127, warrior.health, "Control: the combatant is topped up.");

        Assert.AreEqual(70, pawn.EffectiveMaxHealth,
            "Armour must not raise a worker's maximum: the pawn stays 70, not 81.");
        Assert.AreEqual(70, pawn.health, "An existing worker keeps its full 70 health.");
        Assert.AreEqual(pawn.maxHealth, pawn.EffectiveMaxHealth,
            "The healer's topped-up test must see the pawn as full health.");
        Assert.AreEqual(55, monk.EffectiveMaxHealth,
            "Armour must not raise a monk's maximum either: 55, not 63.");
        Assert.AreEqual(55, monk.health, "The monk stays at its full 55 health.");

        var laterPawn = CreateWorker(PawnPath, new Vector3Int(-14, 6, 0), Team.Blue)
            .GetComponent<UnitActor>();
        yield return null;
        Assert.AreEqual(70, laterPawn.EffectiveMaxHealth,
            "A worker produced after Armour must not be born buffed.");
        Assert.AreEqual(70, laterPawn.health,
            "A worker produced after Armour must spawn at 70/70, not 81/81.");

        // The healer seeks the worst health ratio in range. With the bug every worker
        // read 70/81 and became a permanent patient; nobody here is wounded.
        yield return null;
        Assert.IsNull(healer.Patient,
            "A full-health non-combatant must not be picked as a patient under Armour.");
    }

    [UnityTest]
    public IEnumerator Forge_ReachesAGarrisonedArchersTowerFire()
    {
        PaintRect(-24, -12, 48, 24);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var tower = SpawnTower(new Vector3Int(9, 6));
        var archer = SpawnArcher(new Vector3Int(-9, 0));
        yield return null;

        Assert.AreEqual(12, archer.attackDamage, "Control: the archer prefab deals 12.");

        // Man the tower BEFORE Forge completes, so a garrison-time damage snapshot
        // would be the unbuffed 12 and the test can tell the two implementations apart.
        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsNotNull(garrison, "The tower prefab must carry TowerGarrison.");
        Assert.IsTrue(garrison.Garrison(archer),
            "The archer must man the tower before Forge is researched.");

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "Forge must be startable at the completed castle. " + reason);
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge), "Forge must be researched.");
        Assert.AreEqual(14, archer.EffectiveAttackDamage,
            "Control: the garrisoned archer's effective damage is 12 + 2.");

        var enemy = CreateDummy("Tower Forge target", new Vector3Int(9, 9, 0), Team.Red, 100);
        int before = enemy.health;
        yield return WaitUntil(() => enemy == null || enemy.health < before, 3f);

        Assert.IsNotNull(enemy, "The tower's arrow must not destroy or lose the target.");
        Assert.AreEqual(before - 14, enemy.health,
            "Holding Forge, the tower must apply the archer's current 14 damage, " +
            "not the 12 captured when the archer manned it.");
    }

    [UnityTest]
    public IEnumerator Gathering_AddsTwoPerTrip_ForTheResearchedTeamOnly()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var blueNode = CreateNode("Blue node", new Vector3Int(-12, -6, 0));
        var redNode = CreateNode("Red node", new Vector3Int(-12, 6, 0));
        var blue = CreateWorker(PawnPath, new Vector3Int(-16, -6, 0), Team.Blue);
        var red = CreateWorker(RedPawnPath, new Vector3Int(-16, 6, 0), Team.Red);
        yield return null;

        Assert.AreEqual(20, blue.PerTripAmount(blueNode),
            "Control: without Gathering a worker takes the node's 20 per trip.");
        Assert.AreEqual(20, blueNode.perTrip);

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Gathering, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Gathering));

        Assert.AreEqual(22, blue.PerTripAmount(blueNode),
            "Gathering adds exactly 2 to the per-trip amount.");
        Assert.AreEqual(20, red.PerTripAmount(redNode),
            "The enemy team must keep the base per-trip amount.");

        blue.Assign(blueNode);
        red.Assign(redNode);
        yield return WaitUntil(() => blue.Carrying > 0, 8f);
        int blueLoad = blue.Carrying;
        blue.enabled = false;          // freeze before the delivery, so the load can be read
        yield return WaitUntil(() => red.Carrying > 0, 8f);
        int redLoad = red.Carrying;
        red.enabled = false;

        Assert.AreEqual(22, blueLoad,
            "A buffed worker must actually carry 22 per trip (base 20).");
        Assert.AreEqual(20, redLoad,
            "An unbuffed worker must still carry 20 per trip.");
    }

    // ----------------------------------------------------------------- save

    [UnityTest]
    public IEnumerator Save_RoundTripsTheTechMask_AndTheBuffedUnits()
    {
        PaintRect(-24, -12, 48, 24);
        Technology.Reset();
        Economy.gold = 2000;
        Economy.wood = 1000;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var warrior = CreateWarrior(WarriorPath, new Vector3Int(-8, 0, 0), Team.Blue, "Tech warrior");
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Armour, out _));
        Technology.Tick(45f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge));
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Armour));
        Assert.AreEqual(127, warrior.health, "Control: the warrior is at full buffed health.");

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "Capture failed: " + captureError);
        Assert.AreEqual(2, SaveGameService.Version, "This task is the version-2 save change.");
        Assert.AreEqual(2, snapshot.version, "A captured snapshot must be written as version 2.");
        Assert.AreEqual(Technology.Mask(Team.Blue), snapshot.gameState.blueTechMask,
            "The snapshot must carry the player's technology mask.");
        Assert.AreEqual(0, snapshot.gameState.redTechMask,
            "The enemy mask stays zero until Task 6.");

        string path = Path.Combine(Application.temporaryCachePath, "technology_roundtrip.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError), writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError), readError);

        // Simulate a fresh process: the live mask must not be what the restore reads.
        Technology.Reset();
        Assert.IsFalse(Technology.Has(Team.Blue, Technology.Tech.Forge));

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "Restore failed: " + restoreError);

        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "The researched Forge must survive the round trip.");
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Armour),
            "The researched Armour must survive the round trip.");
        Assert.AreEqual(0, Technology.Mask(Team.Red), "The enemy mask must still be empty.");

        var restoredWarrior = GameObject.Find("Tech warrior");
        Assert.IsNotNull(restoredWarrior, "The saved warrior must be recreated.");
        var actor = restoredWarrior.GetComponent<UnitActor>();
        Assert.AreEqual(18, actor.EffectiveAttackDamage, "The restored warrior keeps the Forge bonus.");
        Assert.AreEqual(127, actor.EffectiveMaxHealth, "The restored warrior keeps the Armour bonus.");
        Assert.AreEqual(127, actor.health,
            "Buffed health above the base maximum must survive the round trip.");
    }

    [Test]
    public void RestoreResearch_WithNoTimeLeft_CompletesImmediately()
    {
        Technology.Reset();
        Assert.AreEqual(0, Technology.ActiveBitOf(Team.Blue), "Control: the side is idle.");

        Technology.RestoreResearch(Team.Blue, Technology.Bit(Technology.Tech.Gathering), 0f);

        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Gathering),
            "A restored line whose time already ran out must complete immediately.");
        Assert.IsFalse(Technology.ActiveTechOf(Team.Blue).HasValue,
            "A completed line must not stay active.");
        Assert.AreEqual(0f, Technology.RemainingOf(Team.Blue), "A completed line has no time left.");

        // A zero bit is the encoding for "nothing was running" and must clear a stale line.
        Technology.RestoreResearch(Team.Blue, Technology.Bit(Technology.Tech.Forge), 20f);
        Assert.IsTrue(Technology.ActiveTechOf(Team.Blue).HasValue, "Control: a line is running.");
        Technology.RestoreResearch(Team.Blue, 0, 0f);
        Assert.IsFalse(Technology.ActiveTechOf(Team.Blue).HasValue,
            "A zero bit must clear any stale running line.");
        Technology.Reset();
    }

    [UnityTest]
    public IEnumerator ResearchInProgress_SurvivesSaveAndLoad_WithoutPayingTwice()
    {
        PaintRect(-24, -12, 48, 24);
        Technology.Reset();
        Economy.gold = 2000;
        Economy.wood = 1000;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Forge, out string reason),
            "Forge must be startable at the completed castle. " + reason);
        Technology.Tick(20f);

        int paidGold = Economy.gold;
        int paidWood = Economy.wood;
        Assert.AreEqual(1750, paidGold, "Control: Forge was paid up front.");
        Assert.AreEqual(900, paidWood, "Control: Forge was paid up front.");
        float remainingBefore = Technology.RemainingOf(Team.Blue);
        Assert.That(remainingBefore, Is.InRange(24f, 25.5f),
            "Control: 25 s of the 45 s line are left.");

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "Capture failed: " + captureError);
        Assert.AreEqual(Technology.Bit(Technology.Tech.Forge), snapshot.gameState.blueResearchTech,
            "The snapshot must carry the running line's mask bit.");
        Assert.That(snapshot.gameState.blueResearchRemaining, Is.InRange(24f, 25.5f),
            "The snapshot must carry the seconds left on the line.");
        Assert.AreEqual(0, snapshot.gameState.redResearchTech,
            "The enemy had nothing running.");

        string path = Path.Combine(Application.temporaryCachePath, "technology_research_in_progress.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError), writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError), readError);
        Assert.AreEqual(Technology.Bit(Technology.Tech.Forge), loaded.gameState.blueResearchTech,
            "The running line must survive serialisation.");

        // Simulate a fresh process: the live line must not be what the restore reads.
        Technology.Reset();
        Assert.IsFalse(Technology.ActiveTechOf(Team.Blue).HasValue,
            "Control: a reset clears the line before the load.");

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "Restore failed: " + restoreError);

        Assert.IsTrue(Technology.ActiveTechOf(Team.Blue).HasValue,
            "Research that was running when the save was taken must still be running.");
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Blue).Value);
        Assert.IsFalse(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "A half-finished line must not come back as already researched.");
        float remainingAfter = Technology.RemainingOf(Team.Blue);
        Assert.That(remainingAfter, Is.InRange(20f, 25.5f),
            "The restored line must resume with its saved remaining time, not restart at 45 s or finish.");
        Assert.AreEqual(paidGold, Economy.gold,
            "Loading must not charge the research a second time.");
        Assert.AreEqual(paidWood, Economy.wood,
            "Loading must not charge the research a second time.");

        // It must finish normally, once, and cost nothing more.
        Technology.Tick(remainingAfter + 0.5f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Forge),
            "The restored line must complete when its remaining time elapses.");
        Assert.IsFalse(Technology.ActiveTechOf(Team.Blue).HasValue,
            "A completed line must free the slot.");
        Assert.AreEqual(paidGold, Economy.gold,
            "Completing the restored line must not charge it again.");
        Assert.AreEqual(paidWood, Economy.wood,
            "Completing the restored line must not charge it again.");
    }

    [UnityTest]
    public IEnumerator VersionOnePayload_LoadsWithAnEmptyMask_AndNoDataLoss()
    {
        PaintRect(-24, -12, 48, 24);
        Technology.Reset();
        Economy.gold = 640;
        Economy.wood = 210;
        CreateCastle(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var warrior = CreateWarrior(WarriorPath, new Vector3Int(-8, 0, 0), Team.Blue, "Legacy warrior");
        yield return null;
        warrior.health = 64;

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "Capture failed: " + captureError);
        string v2Path = Path.Combine(Application.temporaryCachePath, "technology_v2_payload.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, v2Path, out string writeError), writeError);

        // Turn the real, current payload into the shape a version-1 writer produced:
        // version 1 and no technology mask fields at all.
        string v2Json = File.ReadAllText(v2Path);
        StringAssert.Contains("\"version\": 2", v2Json, "Control: the live writer must emit version 2.");
        string v1Json = ToVersionOne(v2Json);
        StringAssert.Contains("\"version\": 1", v1Json);
        StringAssert.DoesNotContain("TechMask", v1Json,
            "A version-1 payload must not contain the new field.");
        StringAssert.DoesNotContain("Research", v1Json,
            "A version-1 payload must not contain the in-progress research fields.");
        StringAssert.Contains("Legacy warrior", v1Json,
            "Control: the payload must really contain the match, not an empty document.");

        string v1Path = Path.Combine(Application.temporaryCachePath, "technology_v1_payload.json");
        File.WriteAllText(v1Path, v1Json);
        Assert.IsTrue(SaveGameService.TryReadFromPath(v1Path, out var loaded, out string readError),
            "A version-1 save must load: " + readError);
        Assert.AreEqual(1, loaded.version);

        // A stale mask and a stale running line from the previous match must both be
        // cleared by the version-1 load, which carries neither field. The line is set
        // before the mask so the mask's own bits do not refuse the restart.
        Technology.Restore(Team.Blue, 0);
        Technology.Restore(Team.Red, 0);
        Technology.RestoreResearch(Team.Blue, Technology.Bit(Technology.Tech.Forge), 30f);
        Technology.Restore(Team.Blue, 7);
        Technology.Restore(Team.Red, 7);
        Assert.AreEqual(7, Technology.Mask(Team.Blue), "Control: the stale mask is present.");
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Blue).Value,
            "Control: a stale research line is running.");

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "A version-1 save must restore without an error: " + restoreError);
        Assert.AreEqual(0, Technology.Mask(Team.Blue),
            "A version-1 save must load with an empty technology mask.");
        Assert.AreEqual(0, Technology.Mask(Team.Red),
            "A version-1 save must load with an empty technology mask.");
        Assert.IsFalse(Technology.ActiveTechOf(Team.Blue).HasValue,
            "A version-1 save must load with no research running.");
        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "A version-1 save must load with no research running for either side.");
        Assert.AreEqual(0f, Technology.RemainingOf(Team.Blue),
            "A version-1 save has no remaining research time.");

        Assert.AreEqual(640, Economy.gold, "The version-1 payload must not lose the bank.");
        Assert.AreEqual(210, Economy.wood);

        var restoredWarrior = GameObject.Find("Legacy warrior");
        Assert.IsNotNull(restoredWarrior, "The version-1 payload must not lose its units.");
        var actor = restoredWarrior.GetComponent<UnitActor>();
        Assert.AreEqual(64, actor.health, "The version-1 payload must not lose unit health.");
        Assert.AreEqual(16, actor.EffectiveAttackDamage, "A version-1 unit gets no Forge bonus.");
        Assert.AreEqual(110, actor.EffectiveMaxHealth, "A version-1 unit gets no Armour bonus.");
    }

    // --------------------------------------------------------------- helpers

    /// <summary>Rewrites a real version-2 document into what the version-1 writer
    /// produced: the version number drops to 1 and the technology fields (mask and
    /// in-progress research), which did not exist then, are removed. Pretty-printed
    /// JsonUtility output puts one member per line, so dropping the lines and fixing
    /// the now-trailing comma is enough to produce a genuinely old-shaped file.</summary>
    static string ToVersionOne(string json)
    {
        var kept = new List<string>();
        foreach (var line in json.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("\"blueTechMask\"")) continue;
            if (trimmed.StartsWith("\"redTechMask\"")) continue;
            if (trimmed.StartsWith("\"blueResearchTech\"")) continue;
            if (trimmed.StartsWith("\"blueResearchRemaining\"")) continue;
            if (trimmed.StartsWith("\"redResearchTech\"")) continue;
            if (trimmed.StartsWith("\"redResearchRemaining\"")) continue;
            kept.Add(line);
        }
        string stripped = string.Join("\n", kept);
        stripped = stripped.Replace("\"version\": " + SaveGameService.Version, "\"version\": 1");
        return Regex.Replace(stripped, @",(\s*)\}", "$1}");
    }

    static void ApplyMelee(CombatAI ai, IDamageable target)
    {
        Assert.IsNotNull(ai, "The attacker must carry a CombatAI.");
        var method = typeof(CombatAI).GetMethod(
            "ApplyMelee", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method, "The test must drive the same private CombatAI.ApplyMelee entry point.");
        method.Invoke(ai, new object[] { target });
    }

    Building CreateCastle(string path, Vector3Int cell, Team team, bool underConstruction = false)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = UnityEngine.Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, path + " must carry a Building.");
        building.team = team;
        building.Recompute();
        if (underConstruction) building.MarkBuiltDuringMatch();
        return building;
    }

    UnitActor CreateWarrior(string path, Vector3Int cell, Team team, string name)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = UnityEngine.Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        go.name = name;
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, path + " must carry a UnitActor.");
        actor.team = team;
        actor.showHealthBar = false;
        return actor;
    }

    static UnitActor CreateDummy(string name, Vector3Int cell, Team team, int health)
    {
        var go = new GameObject(name);
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        var actor = go.AddComponent<UnitActor>();
        actor.team = team;
        actor.showHealthBar = false;
        actor.maxHealth = health;
        actor.health = health;
        actor.isCombatant = true;
        return actor;
    }

    Gatherer CreateWorker(string path, Vector3Int cell, Team team)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = UnityEngine.Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var gatherer = go.GetComponent<Gatherer>();
        Assert.IsNotNull(gatherer, path + " must carry a Gatherer.");
        var actor = go.GetComponent<UnitActor>();
        actor.team = team;
        actor.showHealthBar = false;
        gatherer.autoStart = false;
        gatherer.retaskSeconds = 0f;
        gatherer.carryCapacity = 20;
        go.GetComponent<UnitMover>().speed = 20f;
        return gatherer;
    }

    UnitActor CreateMonk(string path, Vector3Int cell, Team team, string name)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = UnityEngine.Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        go.name = name;
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, path + " must carry a UnitActor.");
        Assert.IsNotNull(go.GetComponent<Healer>(), path + " must carry a Healer.");
        actor.team = team;
        actor.showHealthBar = false;
        return actor;
    }

    Building SpawnTower(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(TowerPath);
        var go = UnityEngine.Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = "Test tower";
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, "The tower prefab must carry a Building.");
        building.team = Team.Blue;
        building.Recompute();
        return building;
    }

    UnitActor SpawnArcher(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(ArcherPath);
        var go = UnityEngine.Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        go.name = "Test archer";
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, ArcherPath + " must carry a UnitActor.");
        actor.team = Team.Blue;
        actor.showHealthBar = false;
        return actor;
    }

    static ResourceNode CreateNode(string name, Vector3Int cell)
    {
        var go = new GameObject(name);
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        var node = go.AddComponent<ResourceNode>();
        node.kind = ResourceKind.Wood;
        node.amount = 100;
        node.maxAmount = 100;
        node.perTrip = 20;
        node.harvestSeconds = 0.05f;
        return node;
    }

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float deadline = Time.time + timeout;
        while (!condition() && Time.time < deadline)
            yield return null;
    }
}
