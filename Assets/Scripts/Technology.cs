using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase B technology layer: three single-level lines researched at a completed
/// friendly command centre, one at a time per side, cost paid up front.
///
/// The researched state is a per-team bit mask. The mask plus any line still in
/// progress (the mask bit of the active line and its remaining seconds) is what the
/// save persists, so a save taken mid-research resumes instead of burning the cost the
/// player already paid. Effects are *computed* from the mask by the gameplay code
/// (<see cref="UnitActor.EffectiveAttackDamage"/>, <see cref="UnitActor.EffectiveMaxHealth"/>,
/// <see cref="Gatherer.PerTripAmount"/>); no prefab asset or authored stat field is ever
/// edited, so a fresh game is unchanged and a bonus cannot be applied twice. That is
/// what makes Armour structurally unable to compound: a multiplier read from the mask
/// is the same 1.15 for a unit that existed before the research and one produced after.
///
/// Call <see cref="Reset"/> when a new match starts (GameState does this in Awake) and
/// <see cref="Restore"/> when a save is loaded. <see cref="Tick"/> is driven by
/// GameState.Update; tests drive it directly with an explicit delta.
/// </summary>
public static class Technology
{
    public enum Tech { Forge = 0, Armour = 1, Gathering = 2 }

    public const float ResearchSeconds = 45f;

    public const int ForgeDamageBonus = 2;
    public const int ArmourBonusPercent = 15;
    public const int GatheringCarryBonus = 2;

    static readonly Dictionary<Team, int> masks = new Dictionary<Team, int>();
    static readonly Dictionary<Team, Tech> active = new Dictionary<Team, Tech>();
    static readonly Dictionary<Team, float> remaining = new Dictionary<Team, float>();

    /// <summary>The side whose research the HUD and the player path drive.</summary>
    public static Team PlayerTeam =>
        GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;

    public static int Bit(Tech tech) => 1 << (int)tech;

    /// <summary>Every bit this version understands. Unknown bits in a loaded mask are
    /// preserved rather than stripped so a future line cannot be silently lost.</summary>
    public static int AllBits => Bit(Tech.Forge) | Bit(Tech.Armour) | Bit(Tech.Gathering);

    public static int Mask(Team team) => masks.TryGetValue(team, out int mask) ? mask : 0;

    public static bool Has(Team team, Tech tech) => (Mask(team) & Bit(tech)) != 0;

    /// <summary>Replaces a side's mask — used by the save loader (and only there).</summary>
    public static void Restore(Team team, int mask)
    {
        masks[team] = mask;
    }

    /// <summary>Forgets every side's research and any research in progress. Called when a
    /// new match starts, so a previous match's techs cannot leak into a fresh game.</summary>
    public static void Reset()
    {
        masks.Clear();
        active.Clear();
        remaining.Clear();
    }

    // ---------------------------------------------------------------- research

    /// <summary>The line this side is researching right now, or null.</summary>
    public static Tech? ActiveTech => ActiveTechOf(PlayerTeam);
    public static Tech? ActiveTechOf(Team team) =>
        active.TryGetValue(team, out Tech tech) ? (Tech?)tech : null;

    /// <summary>The mask bit (1 / 2 / 4) of the line this side is researching, or 0 when
    /// idle. Bit-based rather than the enum index so 0 unambiguously means "no research"
    /// — <see cref="Tech.Forge"/> is enum value 0 — and the save field uses exactly the
    /// same encoding as <see cref="Mask"/>.</summary>
    public static int ActiveBitOf(Team team) =>
        active.TryGetValue(team, out Tech tech) ? Bit(tech) : 0;

    /// <summary>Seconds left on this side's running research, or 0 when idle.</summary>
    public static float RemainingOf(Team team) =>
        active.ContainsKey(team) && remaining.TryGetValue(team, out float left) ? left : 0f;

    /// <summary>Resumes a line captured in a save. <paramref name="bit"/> is the mask bit
    /// <see cref="ActiveBitOf"/> reported (0 = the save had nothing running, which clears
    /// any stale line from the previous match). The saved bank already reflects the cost
    /// paid up front, so this deliberately never charges again. A line whose remaining
    /// time has run out completes immediately; an already-researched line is not
    /// restarted, and an unknown bit is ignored so a newer save cannot corrupt this one.</summary>
    public static void RestoreResearch(Team team, int bit, float remainingSeconds)
    {
        active.Remove(team);
        remaining.Remove(team);
        if (bit == 0) return;
        Tech? tech = FromBit(bit);
        if (tech == null || Has(team, tech.Value)) return;
        active[team] = tech.Value;
        remaining[team] = Mathf.Clamp(remainingSeconds, 0f, SecondsOf(tech.Value));
        if (remaining[team] <= 0f) Complete(team);
    }

    /// <summary>Inverse of <see cref="Bit"/>. Kept next to it so the two cannot drift:
    /// the switch values are the enum's declared bits.</summary>
    static Tech? FromBit(int bit)
    {
        switch (bit)
        {
            case 1: return Tech.Forge;
            case 2: return Tech.Armour;
            case 4: return Tech.Gathering;
            default: return null;
        }
    }

    /// <summary>0..1 progress of the side's running research (0 when idle).</summary>
    public static float Progress => ProgressOf(PlayerTeam);

    public static float ProgressOf(Team team)
    {
        if (!active.TryGetValue(team, out Tech tech)) return 0f;
        float total = SecondsOf(tech);
        if (total <= 0f) return 1f;
        remaining.TryGetValue(team, out float left);
        return Mathf.Clamp01(1f - left / total);
    }

    public static ResourceCost CostOf(Tech tech)
    {
        switch (tech)
        {
            case Tech.Forge:     return new ResourceCost(250, 100);
            case Tech.Armour:    return new ResourceCost(250, 100);
            case Tech.Gathering: return new ResourceCost(200);
            default:             return new ResourceCost(0);
        }
    }

    public static float SecondsOf(Tech tech) => ResearchSeconds;

    /// <summary>Player path: pays from <see cref="Economy"/>. The AI (Task 6) calls the
    /// overload below with its own bank instead, so this one is never used for it.</summary>
    public static bool BeginResearch(Team team, Tech tech, out string reason)
    {
        var economy = Economy.Instance;
        System.Func<ResourceCost, bool> pay = economy != null ? (System.Func<ResourceCost, bool>)economy.TrySpend : null;
        return BeginResearch(team, tech, pay, out reason);
    }

    /// <summary>
    /// Starts one research for <paramref name="team"/>. Returns false and fills
    /// <paramref name="reason"/> with a player-facing refusal when the side has already
    /// researched it, is already researching, has no completed command centre, or cannot
    /// pay. <paramref name="pay"/> must check and spend atomically: it returns true only
    /// when the full cost was taken, so a refusal never charges anything. Research is
    /// per team — one side's running line does not block another side.
    /// </summary>
    public static bool BeginResearch(Team team, Tech tech, System.Func<ResourceCost, bool> pay,
                                     out string reason)
    {
        reason = null;
        if (Has(team, tech))
        {
            reason = "已研究";
            return false;
        }
        if (active.ContainsKey(team))
        {
            reason = "已有研究在进行";
            return false;
        }
        if (!Building.HasCompletedCommandCentre(team))
        {
            reason = "需要完成的指挥中心";
            return false;
        }
        if (pay == null || !pay(CostOf(tech)))
        {
            reason = "资源不足";
            return false;
        }

        active[team] = tech;
        remaining[team] = SecondsOf(tech);
        return true;
    }

    /// <summary>Advances every running research. The caller supplies the delta so tests
    /// can drive the 45 s without waiting in real time.</summary>
    public static void Tick(float deltaTime)
    {
        if (deltaTime <= 0f || active.Count == 0) return;
        var teams = new List<Team>(active.Keys);
        for (int i = 0; i < teams.Count; i++)
        {
            Team team = teams[i];
            if (!active.ContainsKey(team)) continue;
            remaining[team] -= deltaTime;
            if (remaining[team] <= 0f) Complete(team);
        }
    }

    /// <summary>Finishes the player's running research now (the HUD/test entry point).</summary>
    public static void Complete() => Complete(PlayerTeam);

    /// <summary>Finishes a side's running research and applies its effect. A no-op when
    /// that side has nothing running, so it can never re-apply a completed line.</summary>
    public static void Complete(Team team)
    {
        if (!active.TryGetValue(team, out Tech tech)) return;
        active.Remove(team);
        remaining.Remove(team);

        bool firstTime = !Has(team, tech);
        masks[team] = Mask(team) | Bit(tech);

        // Armour raises the maximum of units that already exist. They keep their current
        // health plus the new head-room, so a full-health unit stays full and a damaged
        // one keeps exactly the damage it had. Units produced later are born full at the
        // computed maximum, and restored units carry their saved health.
        if (firstTime && tech == Tech.Armour) GrantArmourHealth(team);
    }

    static void GrantArmourHealth(Team team)
    {
        var units = new List<UnitActor>(UnitActor.All);

        // A garrisoned archer's UnitActor is disabled, so it is absent from UnitActor.All.
        // Without this it would leave the tower at 110/127 — it never received the
        // head-room the rest of the army got when Armour landed.
        foreach (var b in Building.All)
        {
            if (b == null || !b.IsAlive || b.team != team) continue;
            var garrison = b.GetComponent<TowerGarrison>();
            if (garrison == null || !garrison.HasArcher) continue;
            var archer = garrison.Archer;
            if (archer != null && archer.IsAlive && archer.team == team && !units.Contains(archer))
                units.Add(archer);
        }

        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit == null || !unit.IsAlive || unit.team != team || !unit.isCombatant) continue;
            int gained = unit.EffectiveMaxHealth - unit.maxHealth;   // the mask is already set
            if (gained > 0) unit.GrantMaxHealthIncrease(gained);
        }
    }

    // ---------------------------------------------------------------- effects

    public static int DamageBonus(Team team) => Has(team, Tech.Forge) ? ForgeDamageBonus : 0;

    public static float MaxHealthMultiplier(Team team) =>
        Has(team, Tech.Armour) ? 1f + ArmourBonusPercent / 100f : 1f;

    /// <summary>The integer form of the Armour multiplier. UnitActor computes health
    /// with this rather than a float multiply so the rounded maximum is stable.</summary>
    public static int MaxHealthBonusPercent(Team team) =>
        Has(team, Tech.Armour) ? ArmourBonusPercent : 0;

    public static int CarryBonus(Team team) => Has(team, Tech.Gathering) ? GatheringCarryBonus : 0;
}
