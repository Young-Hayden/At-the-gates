using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase B population. A side can field 12 population for free and +6 for every
/// completed friendly <c>House</c>, up to 48. Consumption uses the same roster
/// <see cref="UnitLimits"/> already owns — living units, archers locked in towers
/// and paid queue orders — so the two services can never disagree about who is on
/// the field. Only new production is refused (人口已满); existing units above the
/// capacity are left alone.
///
/// Capacity is cached once per frame and per team. Call <see cref="Invalidate"/>
/// after a unit spawns or dies, a queue changes, or a house is completed, wrecked
/// or restored so the current frame sees the change. It clears both this cache and
/// the <see cref="UnitLimits"/> roster cache that <see cref="Used"/> is derived from.
/// </summary>
public static class Population
{
    public const int BaseCapacity = 12;
    public const int HouseBonus = 6;
    public const int MaxCapacity = 48;

    static int cachedFrame = -1;
    static readonly Dictionary<Team, int> capacityCache = new Dictionary<Team, int>();

    public static void Invalidate()
    {
        cachedFrame = -1;
        capacityCache.Clear();
        // Used reads UnitLimits' per-frame roster cache. A population change is a
        // roster change, so that cache must go too or a death this frame is missed.
        UnitLimits.Invalidate();
    }

    /// <summary>12 plus 6 for every completed, living, non-ghost friendly House,
    /// clamped to 48. Houses still being built and rubble contribute nothing.</summary>
    public static int Capacity(Team team)
    {
        if (cachedFrame != Time.frameCount)
        {
            cachedFrame = Time.frameCount;
            capacityCache.Clear();
        }
        if (capacityCache.TryGetValue(team, out int cached)) return cached;

        int houses = 0;
        foreach (var b in Building.All)
        {
            if (b == null || b.isGhost || !b.IsAlive || b.IsUnderConstruction) continue;
            if (b.team != team) continue;
            if (!IsHouse(b)) continue;
            houses++;
        }

        int capacity = Mathf.Min(MaxCapacity, BaseCapacity + houses * HouseBonus);
        capacityCache[team] = capacity;
        return capacity;
    }

    /// <summary>Same test the AI's repair priority uses: the House prefab family
    /// (House_Blue, House_Red and the numbered variants) is recognised by name.</summary>
    static bool IsHouse(Building building) => building.name.Contains("House");

    /// <summary>Population currently occupied: living units, tower garrisons and
    /// paid queue orders, weighted by <see cref="PopOf"/>. Derived from UnitLimits
    /// so the roster is counted in exactly one place.</summary>
    public static int Used(Team team)
    {
        return UnitLimits.Count(team, UnitRole.Worker)  * PopOf(UnitRole.Worker)
             + UnitLimits.Count(team, UnitRole.Archer)  * PopOf(UnitRole.Archer)
             + UnitLimits.Count(team, UnitRole.Monk)    * PopOf(UnitRole.Monk)
             + UnitLimits.Count(team, UnitRole.Warrior) * PopOf(UnitRole.Warrior)
             + UnitLimits.Count(team, UnitRole.Lancer)  * PopOf(UnitRole.Lancer)
             + UnitLimits.Count(team, UnitRole.Other)   * PopOf(UnitRole.Other)
             // An unknown-named fighter is a combatant and occupies a slot too; without
             // this line it would be invisible to population the moment UnitLimits
             // stopped filing it under Other.
             + UnitLimits.Count(team, UnitRole.OtherCombatant) * PopOf(UnitRole.OtherCombatant);
    }

    /// <summary>Population a profession occupies. Heavy infantry costs two;
    /// every other prefab, including unknown ones, costs one.</summary>
    public static int PopOf(UnitRole role)
    {
        switch (role)
        {
            case UnitRole.Warrior:
            case UnitRole.Lancer: return 2;
            default: return 1;
        }
    }

    public static int Cost(GameObject prefab) => PopOf(UnitLimits.RoleOf(prefab));

    /// <summary>Can this side add one more of <paramref name="prefab"/>? Returns the
    /// player-facing refusal in <paramref name="reason"/>, or null when allowed.</summary>
    public static bool CanFit(Team team, GameObject prefab, out string reason)
    {
        reason = null;
        if (prefab == null)
        {
            reason = "没有可生产的单位";
            return false;
        }
        if (Used(team) + Cost(prefab) <= Capacity(team)) return true;
        reason = "人口已满";
        return false;
    }
}
