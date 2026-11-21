using UnityEngine;

/// <summary>Small persisted settings store shared by the menu and the RTS camera.
/// The game ships at one fixed display mode: 2560×1440 in a borderless fullscreen
/// window. Only edge panning and pan speed remain user-configurable. Display
/// changes are deliberately ignored in the editor so they cannot resize the
/// user's editor window; standalone players apply them immediately.</summary>
public static class GameSettings
{
    const string EdgePanKey = "rts.edgePan";
    const string PanSpeedKey = "rts.panSpeed";

    public const int FixedWidth = 2560;
    public const int FixedHeight = 1440;
    public const string FixedResolutionText = "2560×1440";
    public const FullScreenMode FixedFullscreenMode = FullScreenMode.FullScreenWindow;

    public static bool EdgePan
    {
        get => PlayerPrefs.GetInt(EdgePanKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(EdgePanKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static float PanSpeed
    {
        get => PlayerPrefs.GetFloat(PanSpeedKey, 15f);
        set
        {
            PlayerPrefs.SetFloat(PanSpeedKey, Mathf.Clamp(value, 6f, 30f));
            PlayerPrefs.Save();
        }
    }

    public static void ApplyDisplay()
    {
        if (Application.isEditor) return;
        Screen.SetResolution(FixedWidth, FixedHeight, FixedFullscreenMode);
    }

    public static void ApplyCamera(RTSCamera camera)
    {
        if (camera == null) return;
        camera.edgePan = EdgePan;
        camera.panSpeed = PanSpeed;
    }

    public static void ResetToDefaults()
    {
        EdgePan = true;
        PanSpeed = 15f;
    }
}
