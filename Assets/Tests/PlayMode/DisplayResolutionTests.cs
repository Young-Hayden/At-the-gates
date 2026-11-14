using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Task 3: the game ships at one fixed display mode (2560×1440, fullscreen window,
/// not resizable), but the UI canvases deliberately keep their authored 1920×1080
/// reference so the existing layout scales up by 1.333× and fills the screen as
/// before. These tests pin the player settings plus the unchanged CanvasScaler
/// references in SampleScene, MainMenu, ModeSelect and LoadingScreen.prefab.
/// </summary>
public class DisplayResolutionTests
{
    const string SampleScenePath = "Assets/Scenes/SampleScene.scene";
    const string MainMenuScenePath = "Assets/Scenes/MainMenu.scene";
    const string ModeSelectScenePath = "Assets/Scenes/ModeSelect.scene";
    const string LoadingScreenPrefabPath = "Assets/Resources/LoadingScreen.prefab";

    static readonly Vector2 AuthoredCanvasReference = new Vector2(1920, 1080);

    [Test]
    public void PlayerSettings_Default2560x1440_FixedWindow()
    {
        Assert.AreEqual(2560, PlayerSettings.defaultScreenWidth);
        Assert.AreEqual(1440, PlayerSettings.defaultScreenHeight);
        Assert.AreEqual(FullScreenMode.FullScreenWindow, PlayerSettings.fullScreenMode);
        Assert.IsFalse(PlayerSettings.resizableWindow);
    }

    [UnityTest]
    public IEnumerator SampleScene_CanvasReference_Stays1920x1080()
    {
        EditorSceneManager.LoadSceneInPlayMode(SampleScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        AssertCanvasReferences(SampleScenePath);
    }

    [UnityTest]
    public IEnumerator MainMenu_CanvasReference_Stays1920x1080()
    {
        EditorSceneManager.LoadSceneInPlayMode(MainMenuScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        AssertCanvasReferences(MainMenuScenePath);
    }

    [UnityTest]
    public IEnumerator ModeSelect_CanvasReference_Stays1920x1080()
    {
        EditorSceneManager.LoadSceneInPlayMode(ModeSelectScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        AssertCanvasReferences(ModeSelectScenePath);
    }

    [Test]
    public void LoadingScreenPrefab_CanvasReference_Stays1920x1080()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoadingScreenPrefabPath);
        Assert.IsNotNull(prefab);
        var scaler = prefab.GetComponentInChildren<CanvasScaler>(true);
        Assert.IsNotNull(scaler);
        Assert.AreEqual(AuthoredCanvasReference, scaler.referenceResolution);
    }

    // ---------------------------------------------------------------- helpers

    static void AssertCanvasReferences(string scenePath)
    {
        var scalers = Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None);
        Assert.Greater(scalers.Length, 0,
            "Expected at least one CanvasScaler in " + scenePath);
        foreach (var scaler in scalers)
            Assert.AreEqual(AuthoredCanvasReference, scaler.referenceResolution,
                "CanvasScaler reference resolution in " + scenePath);
    }
}
