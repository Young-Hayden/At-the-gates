using UnityEngine;

/// <summary>
/// One pooled world-space ring for a selected unit's or tower's attack range.
/// Drawn with a 96-segment closed <see cref="LineRenderer"/> using a shared
/// material, so a changing selection never allocates per frame. The component
/// deliberately carries no collider: a ring must never intercept a selection
/// click or a move order.
/// </summary>
[DisallowMultipleComponent]
public class AttackRangeIndicator : MonoBehaviour
{
    public float Radius { get; private set; }
    public bool IsVisible => line != null && line.enabled;

    const int Segments = 96;
    const float LineWidth = 0.08f;

    static Material sharedMaterial;

    LineRenderer line;

    void Awake()
    {
        // No collider is ever added; switch off anything a prefab might bring so the
        // ring is purely visual.
        var collider2D = GetComponent<Collider2D>();
        if (collider2D != null) collider2D.enabled = false;
        var collider3D = GetComponent<Collider>();
        if (collider3D != null) collider3D.enabled = false;

        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;              // positions are local to this transform
        line.loop = true;
        line.positionCount = Segments;
        line.startWidth = LineWidth;
        line.endWidth = LineWidth;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = SharedMaterial();
        line.enabled = false;
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
        if (line == null) return;
        transform.position = center;
        line.enabled = true;
        line.startColor = line.endColor = tint;
        line.sortingOrder = sortingOrder;
        if (!Mathf.Approximately(Radius, radius)) Rebuild(radius);
    }

    public void Hide()
    {
        if (line != null) line.enabled = false;
    }

    void Rebuild(float radius)
    {
        Radius = radius;
        if (line == null) return;
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    static Material SharedMaterial()
    {
        if (sharedMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            sharedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
        return sharedMaterial;
    }
}
