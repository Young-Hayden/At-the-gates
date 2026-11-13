using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Loads the empty regression scene for each PlayMode test and exposes only the
/// managers and helpers shared by the regression fixtures.
/// </summary>
public abstract class RegressionFixture
{
    protected const string RegressionScenePath = "Assets/Tests/Scenes/RegressionTests.scene";

    const int TestBoundsMin = -32;
    const int TestBoundsSize = 64;

    protected MapGrid Grid { get; private set; }
    protected BuildingPlacer Placer { get; private set; }
    protected Economy Economy { get; private set; }
    protected SelectionManager Selection { get; private set; }

    [UnitySetUp]
    public IEnumerator LoadIsolatedScene()
    {
        Time.timeScale = 1f;
        EditorSceneManager.LoadSceneInPlayMode(
            RegressionScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        Grid = Object.FindFirstObjectByType<MapGrid>();
        Placer = Object.FindFirstObjectByType<BuildingPlacer>();
        Economy = Object.FindFirstObjectByType<Economy>();
        Selection = Object.FindFirstObjectByType<SelectionManager>();

        Assert.IsNotNull(Grid, "Regression scene has no MapGrid.");
        Assert.IsNotNull(Placer, "Regression scene has no BuildingPlacer.");
        Assert.IsNotNull(Economy, "Regression scene has no Economy.");
        Assert.IsNotNull(Selection, "Regression scene has no SelectionManager.");
    }

    [UnityTearDown]
    public IEnumerator RestoreRegressionState()
    {
        Time.timeScale = 1f;
        yield break;
    }

    protected void PaintRect(int xMin, int yMin, int width, int height)
    {
        var tile = ScriptableObject.CreateInstance<Tile>();
        for (int x = xMin; x < xMin + width; x++)
        for (int y = yMin; y < yMin + height; y++)
            Grid.ground0.SetTile(new Vector3Int(x, y, 0), tile);
        Grid.Rescan();
    }

    protected void ClearFloor()
    {
        var layers = new[]
        {
            Grid.ground0, Grid.top1, Grid.cliff1,
            Grid.top2, Grid.cliff2, Grid.rampLayer
        };

        for (int x = TestBoundsMin; x < TestBoundsMin + TestBoundsSize; x++)
        for (int y = TestBoundsMin; y < TestBoundsMin + TestBoundsSize; y++)
        {
            var cell = new Vector3Int(x, y, 0);
            foreach (var layer in layers)
                if (layer != null) layer.SetTile(cell, null);
        }
        Grid.Rescan();
    }

    protected T LoadPrefab<T>(string path) where T : Object
    {
        var prefab = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.IsNotNull(prefab, "Missing regression prefab: " + path);
        return prefab;
    }
}
