using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// End-to-end HUD wiring check for the reported "attack button does nothing" case:
/// select a real scene soldier, press the real Attack button listener, then confirm
/// the enemy under the cursor and assert the soldier's CombatAI target.
/// </summary>
public class HudAttackWiringTests
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";

    [UnityTest]
    public IEnumerator HudAttackButton_ArmsAndFocusesEnemy()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return null;

        var selection = Object.FindFirstObjectByType<SelectionManager>();
        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(selection, "SampleScene has no SelectionManager.");
        Assert.IsNotNull(hud, "SampleScene has no HUD.");
        Assert.IsNotNull(hud.attackButton, "HUD attackButton is not wired.");

        var units = Object.FindObjectsByType<UnitActor>(FindObjectsSortMode.None);
        var soldier = units.FirstOrDefault(u => u != null && u.IsAlive && u.team == Team.Blue && u.isCombatant);
        var enemy = units.FirstOrDefault(u => u != null && u.IsAlive && u.team == Team.Red && u.isCombatant);
        Assert.IsNotNull(soldier, "No blue combat unit to select in SampleScene.");
        Assert.IsNotNull(enemy, "No red combat unit to attack in SampleScene.");

        selection.selected.Clear();
        selection.selectedBuilding = null;
        selection.selected.Add(soldier.GetComponent<UnitMover>());
        yield return null;
        yield return null;

        Assert.IsTrue(hud.attackButton.gameObject.activeInHierarchy,
            "The attack button must be visible for a living combat selection.");
        Assert.IsTrue(hud.attackButton.interactable);

        hud.attackButton.onClick.Invoke();
        Assert.IsTrue(selection.AttackArmed,
            "Clicking the HUD Attack button must arm the attack mode.");

        selection.HandleAttackArmedInput(enemy.transform.position, true, false);

        var combat = soldier.GetComponent<CombatAI>();
        Assert.IsNotNull(combat);
        Assert.AreSame(enemy, combat.Target,
            "Confirming an enemy through the armed attack path must set the soldier's target.");
        Assert.IsFalse(selection.AttackArmed, "Confirming must spend the attack mode.");
    }
}
