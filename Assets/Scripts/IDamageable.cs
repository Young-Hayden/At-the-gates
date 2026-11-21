using System.Collections.Generic;
using UnityEngine;

/// <summary>Anything that can be hit — units and buildings both implement this so
/// targeting, projectiles and attack orders do not need to care which it is.</summary>
public interface IDamageable
{
    bool IsAlive { get; }
    Team Team { get; }
    Transform Transform { get; }
    void TakeDamage(int amount);
}

public static class Combatants
{
    /// <summary>
    /// True when the reference is null, Unity-destroyed, or at 0 HP.
    ///
    /// A plain `target == null` is NOT enough for an interface reference: Unity's
    /// fake-null only applies to UnityEngine.Object, so a reference to a destroyed
    /// component still compares unequal to null and the next property access throws.
    /// Always use this instead.
    /// </summary>
    public static bool IsGone(IDamageable d)
    {
        if (d == null) return true;
        var c = d as Component;          // both units and buildings are MonoBehaviours
        if (c == null) return true;      // Unity's overloaded == catches destroyed objects
        if (!c.gameObject.activeInHierarchy) return true; // garrisoned or disabled actors are not hit
        // A disabled UnitActor is out of play. TowerGarrison keeps the archer's
        // GameObject and visuals active but disables its UnitActor, so arrows already
        // in flight (and any other direct damage source) must treat it as gone.
        if (c is UnitActor unit && !unit.enabled) return true;
        return !d.IsAlive;
    }

    /// <summary>
    /// Nearest living enemy. Units in range win over buildings, so defenders get
    /// engaged before anything starts demolishing the base.
    /// </summary>
    public static IDamageable FindNearestEnemy(Team myTeam, Vector3 from, float maxRange,
                                               bool includeBuildings = true)
    {
        return FindNearestEnemy(myTeam, maxRange,
            d => (d.Transform.position - from).sqrMagnitude, includeBuildings);
    }

    /// <summary>
    /// Nearest living enemy using a caller-supplied squared-distance metric. This lets
    /// hold-position acquisition use the same building-footprint distance as the attack
    /// itself, without changing the ordinary origin-based aggro behaviour above.
    /// </summary>
    public static IDamageable FindNearestEnemy(Team myTeam, float maxRange,
                                               System.Func<IDamageable, float> distanceSquared,
                                               bool includeBuildings = true,
                                               System.Func<IDamageable, bool> accept = null)
    {
        if (myTeam == Team.Neutral) return null;
        if (distanceSquared == null) throw new System.ArgumentNullException(nameof(distanceSquared));
        float bestD = maxRange * maxRange;

        IDamageable bestUnit = null;
        float unitD = bestD;
        for (int i = 0; i < UnitActor.All.Count; i++)
        {
            var u = UnitActor.All[i];
            if (u == null || !u.IsAlive) continue;
            if (u.team == myTeam || u.team == Team.Neutral) continue;
            if (accept != null && !accept(u)) continue;
            float d = distanceSquared(u);
            if (d < unitD) { unitD = d; bestUnit = u; }
        }
        if (bestUnit != null || !includeBuildings) return bestUnit;

        IDamageable bestBuilding = null;
        float buildingD = bestD;
        for (int i = 0; i < Building.All.Count; i++)
        {
            var b = Building.All[i];
            if (b == null || !b.IsAlive || !b.blocksMovement) continue;
            if (b.team == myTeam || b.team == Team.Neutral) continue;
            if (accept != null && !accept(b)) continue;
            float d = distanceSquared(b);
            if (d < buildingD) { buildingD = d; bestBuilding = b; }
        }
        return bestBuilding;
    }

    /// <summary>
    /// True when something that can actually fight is within <paramref name="maxRange"/>
    /// of <paramref name="from"/>.
    ///
    /// Enemy workers and monks are noncombatants and are deliberately ignored — a fleeing
    /// worker must not stay pinned at its base by the enemy's economy. Enemy buildings
    /// count only while a <see cref="TowerGarrison"/> has an archer in them.
    /// </summary>
    public static bool HasThreat(Team myTeam, Vector3 from, float maxRange)
    {
        if (myTeam == Team.Neutral) return false;
        float maxSqr = maxRange * maxRange;

        for (int i = 0; i < UnitActor.All.Count; i++)
        {
            var u = UnitActor.All[i];
            if (u == null || !u.IsAlive || !u.isCombatant) continue;
            if (!u.gameObject.activeInHierarchy) continue;
            if (u.team == myTeam || u.team == Team.Neutral) continue;
            if ((u.transform.position - from).sqrMagnitude <= maxSqr) return true;
        }

        for (int i = 0; i < Building.All.Count; i++)
        {
            var b = Building.All[i];
            if (b == null || b.isGhost || !b.IsAlive || b.IsUnderConstruction) continue;
            if (b.team == myTeam || b.team == Team.Neutral) continue;
            var garrison = b.GetComponent<TowerGarrison>();
            if (garrison == null || !garrison.HasArcher) continue;
            if ((b.transform.position - from).sqrMagnitude <= maxSqr) return true;
        }
        return false;
    }

    /// <summary>The first thing under a world point of the given enemy team, if any.
    /// Used to turn a right click into an explicit attack order.</summary>
    public static IDamageable FindAt(Vector3 world, Team myTeam)
    {
        var hits = Physics2D.OverlapPointAll(world);
        for (int i = 0; i < hits.Length; i++)
        {
            var u = hits[i].GetComponentInParent<UnitActor>();
            if (u != null && u.IsAlive && TeamOf(u.team, myTeam)) return u;
        }
        for (int i = 0; i < hits.Length; i++)
        {
            var b = hits[i].GetComponentInParent<Building>();
            if (b != null && b.IsAlive && b.blocksMovement && TeamOf(b.team, myTeam)) return b;
        }

        // The physics collider covers the bottom row only; the visible roof of a tall
        // enemy building is still a legitimate focus/attack target.
        return Building.FindAtVisualPoint(world,
            b => b.blocksMovement && TeamOf(b.team, myTeam));
    }

    static bool TeamOf(Team other, Team myTeam)
    {
        return myTeam != Team.Neutral && other != Team.Neutral && other != myTeam;
    }
}
