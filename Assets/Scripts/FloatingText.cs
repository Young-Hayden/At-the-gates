using UnityEngine;

/// <summary>
/// The little "+20" that pops out of a drop-off when a worker banks its load.
///
/// Uses a legacy TextMesh rather than a UI Text: it lives in world space, needs no
/// canvas, and a handful of them are cheap enough to just spawn and forget.
/// </summary>
public class FloatingText : MonoBehaviour
{
    public float lifetime = 1.1f;
    public float riseSpeed = 1.2f;
    public float driftX = 0.35f;

    TextMesh mesh;
    float age;
    Color baseColor;

    static Font cachedFont;

    static Font Font()
    {
        if (cachedFont != null) return cachedFont;
        cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return cachedFont;
    }

    public static FloatingText Show(Vector3 worldPos, string text, Color color, float size = 1f)
    {
        var font = Font();
        if (font == null) return null;

        var go = new GameObject("FloatText");
        go.transform.position = worldPos;

        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 56;
        tm.characterSize = 0.055f * size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = font.material;
        mr.sortingOrder = 3000;

        var ft = go.AddComponent<FloatingText>();
        ft.mesh = tm;
        ft.baseColor = color;
        return ft;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Mathf.Max(0.01f, lifetime);

        var p = transform.position;
        p.y += riseSpeed * Time.deltaTime;
        p.x += driftX * Time.deltaTime;
        transform.position = p;

        if (mesh != null)
        {
            // hold full opacity for the first third, then fade out
            float a = Mathf.Clamp01(1f - Mathf.Max(0f, t - 0.33f) / 0.67f);
            mesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
