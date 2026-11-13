using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cycles a sliced idle sheet on a UI Image. Used by the loading screen's unit
/// portraits; runs on unscaled time so the animation keeps moving while the game
/// is paused (the pause menu is one of the callers that can trigger a load).
/// </summary>
public class LoadingUnitAnim : MonoBehaviour
{
    public Image image;
    public Sprite[] frames = new Sprite[0];
    public float fps = 6f;
    [Tooltip("起始相位偏移，几个单位摆在一起时不至于同手同脚。")]
    public float phaseOffset;

    void Awake()
    {
        if (image == null) image = GetComponent<Image>();
    }

    void Update()
    {
        if (image == null || frames == null || frames.Length == 0) return;
        int i = Mathf.FloorToInt(Time.unscaledTime * fps + phaseOffset) % frames.Length;
        if (frames[i] != null && image.sprite != frames[i]) image.sprite = frames[i];
    }
}
