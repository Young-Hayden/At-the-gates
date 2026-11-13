using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;

/// <summary>
/// LoL-style RTS camera: screen-edge pan, middle-mouse grab-drag,
/// and scroll-wheel zoom centred on the cursor.
///
/// The whole camera must stay over the map, so the thing that gets clamped is the
/// VIEW rectangle, not the camera's centre point. Clamping the centre to the map's
/// edges still lets half the screen hang off the side — which is exactly what it
/// looked like on the map's north coast. The map rectangle is read off the tilemaps at
/// start, and the maximum zoom is derived from it, so neither can drift apart from the
/// level again.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RTSCamera : MonoBehaviour
{
    [Header("平移")]
    public float panSpeed = 18f;
    public float edgeSize = 20f;
    public bool edgePan = true;
    public bool middleDragPan = true;

    [Header("缩放")]
    public float zoomSpeed = 3f;
    public float minZoom = 6f;
    [Tooltip("上限还会被地图尺寸进一步压低，保证视野不会超出地图。")]
    public float maxZoom = 40f;

    [Header("地图范围（留空 = 开机自动从瓦片地图量）")]
    public bool autoMapBounds = true;
    public Rect mapBounds = new Rect(-45f, -20f, 90f, 50f);

    [Tooltip("开始时把镜头放在这里（世界坐标）。留空则用场景里保存的位置。")]
    public bool useStartPosition = true;
    public Vector2 startPosition = new Vector2(0f, 5f);
    public float startZoom = 15f;

    Camera cam;
    Vector3 dragAnchorWorld;
    bool dragging;

    void Awake()
    {
        GameSettings.ApplyCamera(this);
        cam = GetComponent<Camera>();
        if (autoMapBounds) MeasureMap();

        float fit = FitZoomLimit();
        if (maxZoom > fit) maxZoom = fit;
        if (startZoom > maxZoom) startZoom = maxZoom;

        if (useStartPosition)
        {
            transform.position = new Vector3(startPosition.x, startPosition.y, -10f);
            cam.orthographicSize = Mathf.Clamp(startZoom, minZoom, maxZoom);
        }
    }

    /// <summary>Union of every tilemap's cell bounds — the whole drawn world, water
    /// included. That is what "the map" means to the player.</summary>
    void MeasureMap()
    {
        bool any = false;
        Bounds b = new Bounds();
        foreach (var t in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            var cb = t.cellBounds;
            if (cb.size.x <= 0 || cb.size.y <= 0) continue;
            Vector3 min = t.CellToWorld(new Vector3Int(cb.xMin, cb.yMin, 0));
            Vector3 max = t.CellToWorld(new Vector3Int(cb.xMax, cb.yMax, 0));
            var r = new Bounds((min + max) * 0.5f, new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), 0f));
            if (!any) { b = r; any = true; } else b.Encapsulate(r);
        }
        if (any) mapBounds = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
    }

    /// <summary>Largest orthographic size whose view still fits inside the map.</summary>
    float FitZoomLimit()
    {
        float byHeight = mapBounds.height * 0.5f;
        float byWidth = mapBounds.width * 0.5f / Mathf.Max(0.0001f, cam.aspect);
        return Mathf.Min(byHeight, byWidth);
    }

    public Vector3 HandlePointerInput(Vector3 p, Vector2 screen, Vector2 scroll,
        bool middleDown, bool middleUp, bool overUI, bool modalOpen)
    {
        if (modalOpen)
        {
            dragging = false;
            return p;
        }

        if (middleDragPan)
        {
            if (middleDown && !overUI)
            {
                dragAnchorWorld = cam.ScreenToWorldPoint(screen);
                dragging = true;
            }
            if (middleUp) dragging = false;
            if (dragging)
                p += dragAnchorWorld - cam.ScreenToWorldPoint(screen);
        }

        if (!overUI && Mathf.Abs(scroll.y) > 0.01f)
        {
            Vector3 before = cam.ScreenToWorldPoint(screen);
            float size = Mathf.Clamp(cam.orthographicSize - scroll.y * zoomSpeed, minZoom, maxZoom);
            if (!Mathf.Approximately(size, cam.orthographicSize))
            {
                cam.orthographicSize = size;
                p += before - cam.ScreenToWorldPoint(screen);
            }
        }
        return p;
    }

    void Update()
    {
        Vector3 p = transform.position;
        Vector2 screen = Input.mousePosition;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool modalOpen = PauseMenu.IsAnyOpen ||
            (GameState.Instance != null && GameState.Instance.IsOver);

        p = HandlePointerInput(p, screen, Input.mouseScrollDelta,
            Input.GetMouseButtonDown(2), Input.GetMouseButtonUp(2), overUI, modalOpen);

        // --- screen-edge pan (LoL-style mouse control) ---
        Vector2 dir = Vector2.zero;

        if (edgePan && Application.isFocused && !dragging && !modalOpen)
        {
            Vector3 m = Input.mousePosition;
            if (m.x >= 0f && m.x < edgeSize) dir.x -= 1f;
            if (m.x <= Screen.width && m.x > Screen.width - edgeSize) dir.x += 1f;
            if (m.y >= 0f && m.y < edgeSize) dir.y -= 1f;
            if (m.y <= Screen.height && m.y > Screen.height - edgeSize) dir.y += 1f;
        }

        if (dir.sqrMagnitude > 0f)
        {
            float spd = panSpeed * (cam.orthographicSize / 25f);
            p += (Vector3)(dir.normalized * spd * Time.deltaTime);
        }

        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);

        // --- keep the whole view over the map ---
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        p.x = ClampAxis(p.x, mapBounds.xMin, mapBounds.xMax, halfW);
        p.y = ClampAxis(p.y, mapBounds.yMin, mapBounds.yMax, halfH);
        p.z = -10f;
        transform.position = p;
    }

    /// <summary>Keeps [centre - half, centre + half] inside [min, max]. If the view is
    /// wider than the map there is nothing to choose between, so centre it.</summary>
    static float ClampAxis(float centre, float min, float max, float half)
    {
        if (max - min <= half * 2f) return (min + max) * 0.5f;
        return Mathf.Clamp(centre, min + half, max - half);
    }
}
