using UnityEditor;

/// <summary>Final readability pass; reuses the existing idempotent backup,
/// layout validation and save pipeline. This never rebuilds the HUD.</summary>
public static class ApplyHudReadability20261109
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string BackupPath = ".superpowers/sdd/final-polish/sample-before.scene";
    const string LogPath = ".superpowers/sdd/final-polish/hud-readability-tool.log";

    [MenuItem("Tools/UI/Final HUD Readability 2026-11-09")]
    public static void Apply()
    {
        PolishHUD.Apply(ScenePath, BackupPath, LogPath);
    }
}
