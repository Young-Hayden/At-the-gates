using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Win / lose.
///
/// Neither side starts with a base any more — both open with resources and have to put
/// their own command centre down. So "no command centre" cannot mean "defeated": a side
/// only loses once it has HAD one and then lost it. Until then the match simply keeps
/// going, which is what makes the opening build-up part of the game.
///
/// A second defeat path covers the stalemate that rule cannot see: a side that has
/// fielded units or buildings, and now has nothing left alive AND cannot afford even
/// the cheapest building (the player needs no worker to place one, so money alone is
/// a path back), is wiped out. That is what ends the "zero units, zero buildings,
/// zero options" deadlock instead of hanging the match forever.
/// </summary>
public class GameState : MonoBehaviour
{
    public enum Outcome { Playing, Win, Lose }

    public static GameState Instance { get; private set; }

    [Header("玩家阵营")]
    public Team playerTeam = Team.Blue;

    [Tooltip("多久检查一次胜负。")]
    public float checkInterval = 0.5f;

    [Tooltip("无 HUD 时的团灭恢复金线。没有主基地时只有主基地能直接放置；当前 Castle 为 300 金。")]
    public int recoveryGoldFloor = 300;

    public Outcome Result { get; private set; } = Outcome.Playing;
    public bool IsOver => Result != Outcome.Playing;

    [System.Serializable]
    public class SaveState
    {
        public Team playerTeam;
        public Outcome result;
        public List<Team> everHadCentre = new List<Team>();
        public List<Team> everFielded = new List<Team>();
        // Phase B / save version 2: the per-side technology mask. A version-1 file has
        // neither field, so JsonUtility leaves both at 0 — an empty mask, no migration.
        public int blueTechMask;
        public int redTechMask;
        // The line each side was researching when the save was taken: its mask bit
        // (1 / 2 / 4, 0 = idle) and the seconds left. A version-1 file has none of these
        // fields, so they stay 0 and the load has no research running. The saved bank
        // already reflects the cost paid up front, so resuming never charges again.
        public int blueResearchTech;
        public float blueResearchRemaining;
        public int redResearchTech;
        public float redResearchRemaining;
    }

    public SaveState CaptureSaveState()
    {
        return new SaveState
        {
            playerTeam = playerTeam,
            result = Result,
            everHadCentre = new List<Team>(everHadCentre),
            everFielded = new List<Team>(everFielded),
            blueTechMask = Technology.Mask(Team.Blue),
            redTechMask = Technology.Mask(Team.Red),
            blueResearchTech = Technology.ActiveBitOf(Team.Blue),
            blueResearchRemaining = Technology.RemainingOf(Team.Blue),
            redResearchTech = Technology.ActiveBitOf(Team.Red),
            redResearchRemaining = Technology.RemainingOf(Team.Red),
        };
    }

    /// <summary>Applies the saved per-side technology masks and the research each side had
    /// running. The save loader calls this before any unit is restored: a unit saved at its
    /// buffed maximum can only be clamped correctly once the mask (and therefore the
    /// effective maximum) is live.</summary>
    public static void RestoreTechnology(SaveState state)
    {
        if (state == null) return;
        Technology.Restore(Team.Blue, state.blueTechMask);
        Technology.Restore(Team.Red, state.redTechMask);
        // A version-1 payload has no research fields; the zeros clear any stale line.
        Technology.RestoreResearch(Team.Blue, state.blueResearchTech, state.blueResearchRemaining);
        Technology.RestoreResearch(Team.Red, state.redResearchTech, state.redResearchRemaining);
    }

    /// <summary>Restoring the "has ever had a centre" set is what stops a save made
    /// during the opening build-up from declaring the match lost on load. Saves written
    /// before everFielded existed simply start tracking from the restore point.</summary>
    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        playerTeam = state.playerTeam;
        Result = state.result;
        everHadCentre.Clear();
        if (state.everHadCentre != null)
            foreach (var team in state.everHadCentre) everHadCentre.Add(team);
        everFielded.Clear();
        if (state.everFielded != null)
            foreach (var team in state.everFielded) everFielded.Add(team);
        RestoreTechnology(state);
    }

    /// <summary>Raised once, when the match ends.</summary>
    public event System.Action<Outcome> GameEnded;

    readonly HashSet<Team> everHadCentre = new HashSet<Team>();
    readonly HashSet<Team> everFielded = new HashSet<Team>();
    float timer;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        // A brand-new match starts with no research. The save loader re-applies the
        // saved mask after the scene has started, so a restore is unaffected by this.
        Technology.Reset();
        // Upkeep has no save fields: a load (and a new match) starts both sides on a
        // full ten-second period with nobody starving. The flag is recomputed by the
        // first tick that fires.
        Upkeep.Reset();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (IsOver) return;
        // Research is a match clock, ticked here so the 45 s line runs without a
        // MonoBehaviour of its own (and tests can drive Technology.Tick directly).
        Technology.Tick(Time.deltaTime);
        // Upkeep is per-team and rules-level, so it rides the match clock too. Both
        // sides are charged from their own bank; tests drive Upkeep.Tick directly.
        Upkeep.Tick(Team.Blue, Time.deltaTime);
        Upkeep.Tick(Team.Red, Time.deltaTime);
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = checkInterval;
        Evaluate();
    }

    static bool HasCentre(Team team)
    {
        foreach (var b in Building.All)
            if (b != null && b.IsAlive && b.isCommandCenter && b.team == team) return true;
        return false;
    }

    void Evaluate()
    {
        // remember any centre that exists right now — that is what arms the lose check
        foreach (var b in Building.All)
            if (b != null && b.IsAlive && b.isCommandCenter) everHadCentre.Add(b.team);

        // remember any side that has ever put anything on the field — that is what
        // arms the wipe-out check. Neutral scenery (trees, mines) arms nobody.
        foreach (var u in UnitActor.All)
            if (u != null && u.IsAlive && u.team != Team.Neutral) everFielded.Add(u.team);
        foreach (var b in Building.All)
            if (b != null && b.IsAlive && b.team != Team.Neutral) everFielded.Add(b.team);

        bool blueOut = (everHadCentre.Contains(Team.Blue) && !HasCentre(Team.Blue)) || WipedOut(Team.Blue);
        bool redOut = (everHadCentre.Contains(Team.Red) && !HasCentre(Team.Red)) || WipedOut(Team.Red);

        if (!blueOut && !redOut) return;

        Outcome outcome;
        if (blueOut && redOut) outcome = playerTeam == Team.Blue ? Outcome.Lose : Outcome.Win;
        else if (blueOut) outcome = playerTeam == Team.Blue ? Outcome.Lose : Outcome.Win;
        else outcome = playerTeam == Team.Blue ? Outcome.Win : Outcome.Lose;
        End(outcome);
    }

    /// <summary>A side that once fielded something and now has no units, no buildings,
    /// and not enough money to place even the cheapest building cannot come back —
    /// that is a defeat, not an opening. Money alone counts as a way back because the
    /// player (and the AI) can place buildings without any worker.</summary>
    bool WipedOut(Team team)
    {
        if (!everFielded.Contains(team)) return false;
        foreach (var u in UnitActor.All)
            if (u != null && u.IsAlive && u.team == team) return false;
        foreach (var b in Building.All)
            if (b != null && b.IsAlive && b.team == team) return false;
        return !CanStillRecover(team);
    }

    bool CanStillRecover(Team team)
    {
        if (team == playerTeam)
        {
            var eco = Economy.Instance;
            if (eco == null) return true;      // cannot prove poverty — never false-positive a defeat

            // Prefer the live build list: the cheapest thing the player could drop
            // right now. Without a HUD (regression scenes) fall back to the floor value.
            var hud = Object.FindFirstObjectByType<HUD>();
            if (hud != null && hud.buildPrefabs != null && hud.buildPrefabs.Length > 0)
            {
                foreach (var p in hud.buildPrefabs)
                {
                    var b = p != null ? p.GetComponent<Building>() : null;
                    if (b == null || !eco.CanAfford(b.cost)) continue;
                    if (b.isCommandCenter)
                    {
                        if (!Building.HasLivingCommandCentre(team)) return true;
                    }
                    else if (Building.HasCompletedCommandCentre(team))
                    {
                        return true;
                    }
                }
                return false;
            }
            return eco.gold >= recoveryGoldFloor;
        }

        var ai = AIController.For(team);
        if (ai == null) return false;          // no brain, no bank, no comeback
        return ai.CanStillRecover();
    }

    public void End(Outcome outcome)
    {
        if (IsOver) return;
        Result = outcome;
        Time.timeScale = 0f;
        foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
            ai.active = false;
        foreach (var ai in Object.FindObjectsByType<CombatAI>(FindObjectsSortMode.None))
            ai.enabled = false;
        foreach (var pb in Object.FindObjectsByType<ProductionBuilding>(FindObjectsSortMode.None))
            pb.enabled = false;
        foreach (var g in Object.FindObjectsByType<Gatherer>(FindObjectsSortMode.None))
            g.enabled = false;
        foreach (var h in Object.FindObjectsByType<Healer>(FindObjectsSortMode.None))
            h.enabled = false;
        foreach (var t in Object.FindObjectsByType<TowerGarrison>(FindObjectsSortMode.None))
            t.enabled = false;
        foreach (var sm in Object.FindObjectsByType<SelectionManager>(FindObjectsSortMode.None))
            sm.enabled = false;
        foreach (var bp in Object.FindObjectsByType<BuildingPlacer>(FindObjectsSortMode.None))
            bp.enabled = false;
        GameEnded?.Invoke(outcome);
        Debug.Log("[GameState] 对局结束: " + outcome);
    }

    /// <summary>Restart helper for a UI button.</summary>
    public void RestartLevel()
    {
        Time.timeScale = 1f;
        LoadingScreen.Load(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        // false return = a load is already in flight; nothing to roll back here
    }
}
