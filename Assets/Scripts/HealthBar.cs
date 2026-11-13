using UnityEngine;

/// <summary>
/// The little bar that floats above a damaged thing. Built from two 1x1 sprites so it
/// needs no art, and it hides itself at full health to keep the map readable.
/// Attach it as a child of the unit/building root (never of the sprite child, which
/// gets flipped).
/// </summary>
public class HealthBar : MonoBehaviour
{
    static Sprite whiteSprite;

    Transform bg;
    Transform fill;
    SpriteRenderer fillSr;
    float width = 0.9f;

    public static HealthBar Create(Transform parent, float height, float width = 0.9f)
    {
        var go = new GameObject("HealthBar");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        var hb = go.AddComponent<HealthBar>();
        hb.width = width;
        hb.Build();
        return hb;
    }

    static Sprite White()
    {
        if (whiteSprite != null) return whiteSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return whiteSprite;
    }

    void Build()
    {
        // the bar must never be sorted with the world, or buildings would cover it
        bg = Quad("BG", new Color(0.08f, 0.06f, 0.09f, 0.85f), width + 0.08f, 0.17f, 3000);
        fill = Quad("Fill", Color.green, width, 0.10f, 3001);
        fillSr = fill.GetComponent<SpriteRenderer>();
    }

    Transform Quad(string name, Color color, float w, float h, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(w, h, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = White();
        sr.color = color;
        sr.sortingOrder = order;
        return go.transform;
    }

    public void SetRatio(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        gameObject.SetActive(ratio < 1f);
        if (fill == null) return;
        fill.localScale = new Vector3(width * ratio, 0.10f, 1f);
        fill.localPosition = new Vector3(-width * (1f - ratio) * 0.5f, 0f, 0f);
        fillSr.color = ratio > 0.5f ? new Color(0.35f, 0.9f, 0.35f)
                     : ratio > 0.25f ? new Color(0.95f, 0.8f, 0.25f)
                     : new Color(0.95f, 0.3f, 0.3f);
    }

    /// <summary>Bars on big things need to be wider, and read from further away.</summary>
    public void SetWidth(float w)
    {
        width = w;
        if (bg != null) bg.localScale = new Vector3(w + 0.08f, 0.17f, 1f);
    }

    /// <summary>Same widget, one fixed colour — used for construction progress instead of
    /// health, so the bar does not read as "this building is damaged".</summary>
    public Color progressColor = new Color(0.45f, 0.75f, 1f);

    public void SetProgress(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        gameObject.SetActive(ratio < 1f);
        if (fill == null) return;
        fill.localScale = new Vector3(width * ratio, 0.10f, 1f);
        fill.localPosition = new Vector3(-width * (1f - ratio) * 0.5f, 0f, 0f);
        fillSr.color = progressColor;
    }
}
