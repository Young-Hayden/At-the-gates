using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One pooled ground ring for a selected unit's or tower's attack range.
/// Uses a SpriteRenderer with the same default sorting layer/order as the green
/// selection ellipse, so units and trees occlude it exactly like any other ground
/// decal. Ring sprites are generated and cached per radius; the transform scale is
/// the attack radius, while the texture's pixel width is compensated so the line
/// stays a constant ~0.065 world units thick.
/// </summary>
[DisallowMultipleComponent]
public class AttackRangeIndicator : MonoBehaviour
{
    const int TextureSize = 256;
    const float OuterRadiusPixels = 120f;
    const float WorldLineWidth = 0.065f;

    /// <summary>Ground-decal order: above tilemaps (max 2), below every Y-sorted
    /// unit/tree/prop (base 1000) and below the green selection ellipse.</summary>
    public const int GroundSortingOrder = 3;

    static readonly Dictionary<int, Sprite> ringCache = new Dictionary<int, Sprite>();

    public float Radius { get; private set; }
    public bool IsVisible => spriteRenderer != null && spriteRenderer.enabled;

    SpriteRenderer spriteRenderer;

    void Awake()
    {
        // Purely visual: never intercept selection or path clicks, even if a prefab
        // somehow brings a collider.
        var collider2D = GetComponent<Collider2D>();
        if (collider2D != null) collider2D.enabled = false;
        var collider3D = GetComponent<Collider>();
        if (collider3D != null) collider3D.enabled = false;

        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.enabled = false;
    }

    /// <summary>Pool allocation: one inactive ring under the given owner.</summary>
    public static AttackRangeIndicator Create(Transform parent)
    {
        var go = new GameObject("AttackRangeRing");
        if (parent != null) go.transform.SetParent(parent, false);
        return go.AddComponent<AttackRangeIndicator>();
    }

    public void Show(Vector3 center, float radius, Color tint, int sortingOrder)
    {
        if (spriteRenderer == null) return;
        transform.position = center;
        transform.localScale = Vector3.one * radius;
        var sprite = SpriteForRadius(radius);
        if (spriteRenderer.sprite != sprite) spriteRenderer.sprite = sprite;
        spriteRenderer.color = tint;
        spriteRenderer.sortingOrder = sortingOrder;
        spriteRenderer.enabled = true;
        Radius = radius;
    }

    public void Hide()
    {
        if (spriteRenderer != null) spriteRenderer.enabled = false;
    }

    static Sprite SpriteForRadius(float radius)
    {
        radius = Mathf.Max(0.01f, radius);
        int key = Mathf.RoundToInt(radius * 100f);
        if (ringCache.TryGetValue(key, out var cached) && cached != null) return cached;

        float pixelsPerWorld = OuterRadiusPixels / radius;
        float thickness = Mathf.Max(1.5f, WorldLineWidth * pixelsPerWorld);
        float inner = Mathf.Max(0f, OuterRadiusPixels - thickness);
        float center = (TextureSize - 1) * 0.5f;

        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "AttackRangeRing_" + key,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        var pixels = new Color32[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        for (int x = 0; x < TextureSize; x++)
        {
            float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
            // One-pixel feather keeps the Point-filtered circle from looking chunky.
            float alpha = Mathf.Clamp01(OuterRadiusPixels - distance + 0.5f)
                        * Mathf.Clamp01(distance - inner + 0.5f);
            pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f), OuterRadiusPixels);
        sprite.name = "AttackRangeRingSprite_" + key;
        sprite.hideFlags = HideFlags.DontSave;
        ringCache[key] = sprite;
        return sprite;
    }
}
