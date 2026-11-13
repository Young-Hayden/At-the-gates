using System.Collections.Generic;
using UnityEngine;

public enum UnitRole { Worker, Warrior, Lancer, Archer, Monk, Other, OtherCombatant }

/// <summary>
/// Phase A army caps. A side's roster is everything it can still field: living
/// units, archers locked inside towers, and paid queue orders that have not spawned
/// yet. Workers have their own cap; every other profession also spends from one
/// shared combat allowance, so both a per-role and a total refusal are possible.
///
/// Counts are cached until <see cref="Invalidate"/> reports a roster change.
/// Call <see cref="Invalidate"/> immediately after creating, removing or enqueueing
/// units outside the normal lifecycle/production paths so the current frame sees it.
/// </summary>
public static class UnitLimits
{
    public const int WorkerCap = 6;
    public const int WarriorCap = 8;
    public const int LancerCap = 5;
    public const int ArcherCap = 5;
    public const int MonkCap = 4;
    public const int CombatCap = 14;

    static readonly Dictionary<Team, Dictionary<UnitRole, int>> cache =
        new Dictionary<Team, Dictionary<UnitRole, int>>();

    public static void Invalidate()
    {
        cache.Clear();
    }

    /// <summary>Profession of a unit prefab. Matching is by prefab name because the
    /// prefabs do not carry a profession field of their own. An unknown name still has to
    /// be classified by whether it fights: a future combatant must spend from the shared
    /// allowance even when nobody updated this table.</summary>
    public static UnitRole RoleOf(GameObject prefab)
    {
        if (prefab == null) return UnitRole.Other;
        string n = prefab.name;
        if (n.Contains("Pawn")) return UnitRole.Worker;
        if (n.Contains("Warrior")) return UnitRole.Warrior;
        if (n.Contains("Lancer")) return UnitRole.Lancer;
        if (n.Contains("Archer")) return UnitRole.Archer;
        if (n.Contains("Monk")) return UnitRole.Monk;
        return IsCombatant(prefab) ? UnitRole.OtherCombatant : UnitRole.Other;
    }

    public static int Count(Team team, UnitRole role)
    {
        var counts = Counts(team);
        return counts.TryGetValue(role, out int n) ? n : 0;
    }

    /// <summary>Warriors, lancers, archers, monks and any unknown-named fighter, combined.
    /// Workers are excluded.</summary>
    public static int TotalCombat(Team team)
    {
        var counts = Counts(team);
        return Get(counts, UnitRole.Warrior)
             + Get(counts, UnitRole.Lancer)
             + Get(counts, UnitRole.Archer)
             + Get(counts, UnitRole.Monk)
             + Get(counts, UnitRole.OtherCombatant);
    }

    /// <summary>Can this side add one more of <paramref name="prefab"/>? Returns the
    /// player-facing refusal in <paramref name="reason"/>, or null when allowed.</summary>
    public static bool CanProduce(Team team, GameObject prefab, out string reason)
    {
        reason = null;
        if (prefab == null)
        {
            reason = "没有可生产的单位";
            return false;
        }

        var role = RoleOf(prefab);
        int count = Count(team, role);

        if (role == UnitRole.Worker)
        {
            if (count >= WorkerCap)
            {
                reason = "工人已达上限";
                return false;
            }
            return true;
        }

        // Unknown non-fighters (a future worker variant) are outside the army ledger;
        // unknown fighters arrive here as OtherCombatant and face the shared allowance.
        if (role == UnitRole.Other) return true;

        if (role != UnitRole.Other && count >= CapFor(role))
        {
            reason = RoleName(role) + "已达上限";
            return false;
        }
        if (TotalCombat(team) >= CombatCap)
        {
            reason = "总战斗单位已达上限";
            return false;
        }
        return true;
    }

    static bool IsCombatant(GameObject prefab)
    {
        var actor = prefab.GetComponent<UnitActor>();
        return actor != null && actor.isCombatant;
    }

    static int CapFor(UnitRole role)
    {
        switch (role)
        {
            case UnitRole.Worker: return WorkerCap;
            case UnitRole.Warrior: return WarriorCap;
            case UnitRole.Lancer: return LancerCap;
            case UnitRole.Archer: return ArcherCap;
            case UnitRole.Monk: return MonkCap;
            default: return int.MaxValue;
        }
    }

    static string RoleName(UnitRole role)
    {
        switch (role)
        {
            case UnitRole.Worker: return "工人";
            case UnitRole.Warrior: return "战士";
            case UnitRole.Lancer: return "枪兵";
            case UnitRole.Archer: return "弓手";
            case UnitRole.Monk: return "僧侣";
            default: return "单位";
        }
    }

    static Dictionary<UnitRole, int> Counts(Team team)
    {
        if (cache.TryGetValue(team, out var counts)) return counts;

        counts = new Dictionary<UnitRole, int>();

        foreach (var u in UnitActor.All)
        {
            if (u == null || !u.IsAlive || u.team != team) continue;
            Add(counts, RoleOf(u.gameObject));
        }

        // A garrisoned archer's UnitActor is disabled, so it is absent from
        // UnitActor.All. The tower still holds it on the roster.
        foreach (var b in Building.All)
        {
            if (b == null || !b.IsAlive) continue;
            var garrison = b.GetComponent<TowerGarrison>();
            if (garrison == null || !garrison.HasArcher) continue;
            var archer = garrison.Archer;
            if (archer != null && archer.IsAlive && archer.team == team)
                Add(counts, RoleOf(archer.gameObject));
        }

        // Queued orders are counted from this single component scan. They are
        // deliberately NOT also read from Building.All: that would count every
        // queue twice. FindObjectsInactive.Include covers factories restored from
        // a save whose GameObject is still disabled or under construction.
        foreach (var pb in Object.FindObjectsByType<ProductionBuilding>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (pb == null) continue;
            var b = pb.GetComponent<Building>();
            if (b == null || b.isGhost || !b.IsAlive || b.team != team) continue;
            var products = pb.products;
            if (products == null) continue;
            foreach (int index in pb.Queue)
            {
                if (index < 0 || index >= products.Length) continue;
                Add(counts, RoleOf(products[index].unitPrefab));
            }
        }

        cache[team] = counts;
        return counts;
    }

    static int Get(Dictionary<UnitRole, int> counts, UnitRole role)
    {
        return counts.TryGetValue(role, out int n) ? n : 0;
    }

    static void Add(Dictionary<UnitRole, int> counts, UnitRole role)
    {
        counts.TryGetValue(role, out int n);
        counts[role] = n + 1;
    }
}
