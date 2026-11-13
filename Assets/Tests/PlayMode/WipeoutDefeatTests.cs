using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Regression coverage for the wipe-out defeat path: a side that has fielded units or
/// buildings and then loses everything — with no money to place even the cheapest
/// building — loses the match. The same state WITH recovery money must keep playing,
/// because buildings need no worker to be placed.
/// </summary>
public class WipeoutDefeatTests : RegressionFixture
{
    UnitActor CreateActor(string name, Vector3Int cell, Team team)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        go.AddComponent<UnitMover>();
        var actor = go.AddComponent<UnitActor>();
        actor.team = team;
        actor.showHealthBar = false;
        return actor;
    }

    /// <summary>Let GameState evaluate until a predicate holds or real time runs out.</summary>
    IEnumerator WaitFor(System.Func<bool> condition, string what)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsTrue(condition(), "Timed out waiting for: " + what);
    }

    [UnityTest]
    public IEnumerator Wipeout_WithRecoveryMoney_StaysPlaying()
    {
        var gs = GameState.Instance;
        Assert.IsNotNull(gs, "Regression scene must own a GameState.");
        gs.checkInterval = 0.05f;
        Assert.AreEqual(GameState.Outcome.Playing, gs.Result);

        // Both sides field something; the player keeps the default 500 gold — a way back.
        var blue = CreateActor("Wipeout blue", new Vector3Int(-2, 0, 0), Team.Blue);
        CreateActor("Wipeout red", new Vector3Int(-1, 0, 0), Team.Red);

        // Let the fielded-tracking tick register both sides.
        yield return WaitFor(() =>
        {
            var f = typeof(GameState).GetField("everFielded",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var set = (System.Collections.Generic.HashSet<Team>)f.GetValue(gs);
            return set.Contains(Team.Blue) && set.Contains(Team.Red);
        }, "everFielded to register both teams");

        // Blue loses its only unit but can still afford a building: no defeat.
        Object.Destroy(blue.gameObject);

        float deadline = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            Assert.AreEqual(GameState.Outcome.Playing, gs.Result,
                "A side with recovery money must not be declared defeated.");
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator Wipeout_BrokeAndHelpless_IsDefeat()
    {
        var gs = GameState.Instance;
        Assert.IsNotNull(gs, "Regression scene must own a GameState.");
        gs.checkInterval = 0.05f;

        var blue = CreateActor("Doomed blue", new Vector3Int(-2, 0, 0), Team.Blue);
        CreateActor("Watching red", new Vector3Int(-1, 0, 0), Team.Red);

        // Let the fielded-tracking tick register both sides.
        yield return WaitFor(() =>
        {
            var f = typeof(GameState).GetField("everFielded",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var set = (System.Collections.Generic.HashSet<Team>)f.GetValue(gs);
            return set.Contains(Team.Blue) && set.Contains(Team.Red);
        }, "everFielded to register both teams");

        // Blue loses its only unit and cannot afford even the cheapest building.
        Object.Destroy(blue.gameObject);
        Economy.gold = 0;
        Economy.wood = 0;
        Economy.meat = 0;

        yield return WaitFor(() => gs.Result == GameState.Outcome.Lose,
            "broke and helpless blue to be declared defeated");
    }

    [UnityTest]
    public IEnumerator Wipeout_AiBrokeAndHelpless_IsPlayerWin()
    {
        var gs = GameState.Instance;
        Assert.IsNotNull(gs, "Regression scene must own a GameState.");
        gs.checkInterval = 0.05f;

        CreateActor("Surviving blue", new Vector3Int(-2, 0, 0), Team.Blue);
        var red = CreateActor("Doomed red", new Vector3Int(-1, 0, 0), Team.Red);

        // An AI with an empty build order has no way back once wiped.
        var aiObject = new GameObject("Doomed AI");
        var ai = aiObject.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;

        yield return WaitFor(() =>
        {
            var f = typeof(GameState).GetField("everFielded",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var set = (System.Collections.Generic.HashSet<Team>)f.GetValue(gs);
            return set.Contains(Team.Blue) && set.Contains(Team.Red);
        }, "everFielded to register both teams");

        Object.Destroy(red.gameObject);

        yield return WaitFor(() => gs.Result == GameState.Outcome.Win,
            "wiped-out AI side to hand the player the win");
    }
}
