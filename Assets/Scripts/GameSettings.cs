using UnityEngine;

/// <summary>Small persisted settings store shared by the menu and the RTS camera.
/// Display changes are deliberately ignored in the editor so they cannot resize the
/// user's editor window; standalone players apply them immediately.</summary>
public static class GameSettings
{
    const string ResolutionKey = "rts.resolution";
    const string FullscreenKey = "rts.fullscreen";
    const string EdgePanKey = "rts.edgePan";
    const string PanSpeedKey = "rts.panSpeed";

    public static int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(ResolutionKey, 2);
        set
        {
            PlayerPrefs.SetInt(ResolutionKey, Mathf.Clamp(value, 0, 2));
            PlayerPrefs.Save();
        }
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

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
        get => PlayerPrefs.GetFloat(PanSpeedKey, 18f);
        set
        {
            PlayerPrefs.SetFloat(PanSpeedKey, Mathf.Clamp(value, 6f, 30f));
            PlayerPrefs.Save();
        }
    }

    public static void ApplyDisplay()
    {
        if (Application.isEditor) return;
        int width, height;
        switch (ResolutionIndex)
        {
            case 0: width = 1280; height = 720; break;
            case 1: width = 1600; height = 900; break;
            default: width = 1920; height = 1080; break;
        }
        Screen.SetResolution(width, height, Fullscreen);
    }

    public static void ApplyCamera(RTSCamera camera)
    {
        if (camera == null) return;
        camera.edgePan = EdgePan;
        camera.panSpeed = PanSpeed;
    }

    public static void ResetToDefaults()
    {
        ResolutionIndex = 2;
        Fullscreen = true;
        EdgePan = true;
        PanSpeed = 18f;
    }
}
