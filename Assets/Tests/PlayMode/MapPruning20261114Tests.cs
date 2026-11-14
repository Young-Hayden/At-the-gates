using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MapPruning20261114Tests : RegressionFixture
{
    [UnityTest]
    public IEnumerator ShippedMap_HasFiftyBushes_AndLeavesOtherGarnishUntouched()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var root = GameObject.Find("Garnish");
        Assert.IsNotNull(root);
        var children = root.transform.Cast<Transform>().ToArray();
        Assert.AreEqual(50, children.Count(t => t.name.StartsWith("Bush")));
        Assert.AreEqual(94, children.Count(t => t.name.StartsWith("WaterRock")));
        Assert.AreEqual(28, children.Count(t => t.name.StartsWith("SeaRock")));
        Assert.AreEqual(7, children.Count(t => t.name.StartsWith("Cloud")));
    }

    [UnityTest]
    public IEnumerator ShippedMap_OffsetTreeMatchesExistingBlocker_AndRecoveryFloorIsCastleCost()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var tree = Object.FindObjectsByType<Building>(FindObjectsSortMode.None)
            .SingleOrDefault(b => b.name == "Tree1"
                && Mathf.Abs(b.transform.position.y - 18.44f) < 0.001f
                && Mathf.Abs(b.transform.position.x - 3.50f) < 0.001f);
        Assert.IsNotNull(tree, "目标 Tree1 必须移动到其原有逻辑占地上。");
        Assert.AreEqual(new Vector3Int(3, 18, 0), tree.Cells[0]);
        Assert.IsTrue(MapGrid.Instance.IsBlockedByBuilding(new Vector3Int(3, 18, 0)));
        Assert.AreEqual(300, GameState.Instance.recoveryGoldFloor);
    }
}
