using UnityEngine;

/// <summary>
/// Draws this sprite at an order derived from its world Y, so things further down
/// the screen (closer to the camera) draw in front. This is how the RTS gets
/// correct overlap between units, buildings and props.
///
/// Only sprite sorting order is used: URP 2D's Transparency Sort Axis does not
/// take effect on this renderer, so the order has to be written explicitly.
/// Runs in LateUpdate so it lands after movement has been applied this frame.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(100)]
public class YSort : MonoBehaviour
{
    [Tooltip("Order at world y = 0. Must stay above the tilemaps (max tilemap order is 2).")]
    public int baseOrder = 1000;

    [Tooltip("Sorting orders per world unit of height. 10 gives sub-tile precision.")]
    public float ordersPerUnit = 10f;

    [Tooltip("Nudge this renderer relative to its Y order (e.g. -1 to sit behind a unit on the same tile).")]
    public int offset = 0;

    SpriteRenderer sr;

    void Awake() { sr = GetComponent<SpriteRenderer>(); Apply(); }

    void LateUpdate() { Apply(); }

    void Apply()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;
        sr.sortingOrder = OrderFor(transform.position.y, baseOrder, ordersPerUnit) + offset;
    }

    public static int OrderFor(float worldY, int baseOrder = 1000, float ordersPerUnit = 10f)
    {
        return baseOrder - Mathf.RoundToInt(worldY * ordersPerUnit);
    }

    public static int OrderFor(Vector3 worldPos, int baseOrder = 1000, float ordersPerUnit = 10f)
    {
        return OrderFor(worldPos.y, baseOrder, ordersPerUnit);
    }
}
