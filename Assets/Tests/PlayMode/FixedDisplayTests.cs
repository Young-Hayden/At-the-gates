using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Fixed display task — the game ships at one resolution (2560×1440, exclusive
/// fullscreen window) and the main-menu settings panel exposes no resolution or
/// fullscreen choice. These tests pin both halves: the constants in
/// <see cref="GameSettings"/> and the button state <see cref="MenuSettingsUI"/>
/// builds in Awake. They are expected to fail to compile until the production
/// API exists; that is the RED half of TDD.
/// </summary>
public class FixedDisplayTests
{
    const string ScenePath = "Assets/Scenes/MainMenu.scene";

    [Test]
    public void GameSettings_UsesFixed2560Fullscreen()
    {
        Assert.AreEqual(2560, GameSettings.FixedWidth);
        Assert.AreEqual(1440, GameSettings.FixedHeight);
        Assert.AreEqual("2560×1440", GameSettings.FixedResolutionText);
        Assert.AreEqual(FullScreenMode.FullScreenWindow, GameSettings.FixedFullscreenMode);
    }

    [UnityTest]
    public IEnumerator MainMenuSettings_ShowsOnlyFixedResolutionAndFullscreen()
    {
        EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var ui = Object.FindFirstObjectByType<MenuSettingsUI>();
        Assert.IsNotNull(ui);
        Assert.IsNotNull(ui.resolutionButtons[0]);
        Assert.AreEqual("2560×1440",
            ui.resolutionButtons[0].GetComponentInChildren<Text>().text);
        Assert.IsFalse(ui.resolutionButtons[0].interactable);
        Assert.IsFalse(ui.resolutionButtons[1].gameObject.activeSelf);
        Assert.IsFalse(ui.resolutionButtons[2].gameObject.activeSelf);
        Assert.IsFalse(ui.fullscreenButton.interactable);
        Assert.AreEqual("全屏：固定 2560×1440", ui.fullscreenLabel.text);
    }
}
