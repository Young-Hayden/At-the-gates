using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase B meat upkeep: the army's appetite. Every ten seconds each side pays one
/// meat for every living combat unit, out of its own bank — the player's
/// <see cref="Economy"/> or the matching <see cref="AIController"/>. Workers, monks
/// and buildings cost nothing.
///
/// A side that cannot pay the full amount becomes <see cref="IsStarving">Starving</see>
/// until it can. That is a soft, fully recoverable penalty: production is refused with
/// 食物不足 (after the Phase A caps and population, so the most specific reason wins),
/// monks stop healing, the bank is charged only what it has and never goes negative,
/// and no unit loses health. The unpaid remainder is forgiven, not booked as debt.
///
/// Combatant here is the same <see cref="UnitActor.isCombatant"/> flag the rest of the
/// gameplay uses (technology bonuses, aggro, AI attack waves) — monks and workers are
/// authored as non-combatants. It is deliberately NOT <see cref="UnitLimits.TotalCombat"/>:
/// that is a cap ledger whose job is to give monks a roster slot, not to decide who eats.
///
/// Nothing here is saved. <see cref="Reset"/> restarts the clock (GameState does it in
/// Awake, so a load or a new match begins with a full period), and the flag is
/// recomputed from the bank by the next tick. Time is advanced through
/// <see cref="Tick(Team, float)"/> with an explicit delta so tests never wait ten
/// real seconds; GameState.Update drives it with Time.deltaTime for both sides.
/// </summary>
public static class Upkeep
{
    public const float PeriodSeconds = 10f;
    public const int MeatPerUnit = 1;

    /// <summary>Seconds left before each side's next charge.</summary>
    static readonly Dictionary<Team, float> timers = new Dictionary<Team, float>();

    /// <summary>The side's flag, consumed by production, the healer and the HUD. It is
    /// cleared by a tick that can pay and immediately by a meat delivery.</summary>
    static readonly HashSet<Team> starving = new HashSet<Team>();

    /// <summary>The side whose bank is <see cref="Economy"/>; everyone else is an AI
    /// ledger. Mirrors <see cref="Gatherer"/>'s crediting rule.</summary>
    public static Team PlayerTeam =>
        GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;

    /// <summary>Forgets both clocks and both flags. Called when a new match starts and
    /// when a save is loaded, which is what makes upkeep have no persistent state.</summary>
    public static void Reset()
    {
        timers.Clear();
        starving.Clear();
    }

    /// <summary>Living combat units of one side. Tower garrisons count: a garrisoned
    /// archer's UnitActor is disabled and absent from <see cref="UnitActor.All"/>, but
    /// it is still alive and still eating. Queued orders do not count — they are not
    /// alive yet.</summary>
    public static int UnitCount(Team team)
    {
        if (team == Team.Neutral) return 0;

        int count = 0;
        for (int i = 0; i < UnitActor.All.Count; i++)
        {
            var unit = UnitActor.All[i];
            if (unit == null || !unit.IsAlive || unit.team != team) continue;
            if (!unit.isCombatant) continue;
            count++;
        }

        foreach (var b in Building.All)
        {
            if (b == null || !b.IsAlive) continue;
            var garrison = b.GetComponent<TowerGarrison>();
            if (garrison == null || !garrison.HasArcher) continue;
            var archer = garrison.Archer;
            if (archer == null || !archer.IsAlive || archer.team != team) continue;
            if (archer.isCombatant) count++;
        }
        return count;
    }

    /// <summary>Meat owed at the next tick: one per living combat unit.</summary>
    public static int AmountDue(Team team) => UnitCount(team) * MeatPerUnit;

    /// <summary>The flag production, the healer and the HUD read.</summary>
    public static bool IsStarving(Team team) => starving.Contains(team);

    /// <summary>Advances one side's clock by <paramref name="deltaTime"/>. When the
    /// period elapses, charges what the bank can afford and sets or clears the flag from
    /// the result. One charge per call: a long frame cannot burn several periods at once,
    /// matching the other countdowns in the game.</summary>
    public static void Tick(Team team, float deltaTime)
    {
        if (team == Team.Neutral) return;

        float remaining;
        if (!timers.TryGetValue(team, out remaining)) remaining = PeriodSeconds;
        remaining -= Mathf.Max(0f, deltaTime);
        if (remaining > 0f)
        {
            timers[team] = remaining;
            return;
        }

        timers[team] = PeriodSeconds;
        Charge(team);
    }

    /// <summary>A successful meat delivery ends a famine at once, so a worker hauling
    /// meat home does not have to wait for the next ten-second tick. Wired from
    /// <see cref="Economy.Add"/> and <see cref="AIController.Add"/>.</summary>
    public static void NoteMeatReceived(Team team)
    {
        if (starving.Contains(team) && MeatOf(team) > 0) starving.Remove(team);
    }

    static void Charge(Team team)
    {
        int due = AmountDue(team);
        if (due <= 0)
        {
            // An empty army owes nothing: a side with no combatants is never starving.
            starving.Remove(team);
            return;
        }

        int paid = SpendMeat(team, due);
        if (paid < due) starving.Add(team);
        else starving.Remove(team);
    }

    /// <summary>Reads whichever bank owns the team, exactly like Gatherer.Credit.</summary>
    static int MeatOf(Team team)
    {
        if (team == PlayerTeam)
            return Economy.Instance != null ? Economy.Instance.meat : 0;
        var ai = AIController.For(team);
        return ai != null ? ai.Meat : 0;
    }

    static int SpendMeat(Team team, int amount)
    {
        if (amount <= 0) return 0;
        if (team == PlayerTeam)
            return Economy.Instance != null ? Economy.Instance.SpendMeat(amount) : 0;
        var ai = AIController.For(team);
        return ai != null ? ai.SpendMeat(amount) : 0;
    }
}
