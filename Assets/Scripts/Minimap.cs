using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Live top-down map rendered by a second camera. The component lives on the
/// RawImage so it can own both the render target and click-to-pan input.
/// No fog of war is implied; the viewport is just the main camera's current view.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class Minimap : MonoBehaviour, IPointerClickHandler
{
    public Camera mainCamera;
    public RTSCamera rtsCamera;
    [Tooltip("UI child containing the four viewport border strips.")]
    public RectTransform viewport;
    public int textureWidth = 900;
    public int textureHeight = 500;
    [Tooltip("刷新小地图渲染的帧间隔。2 = 约每两帧一次；边框仍每帧更新。")]
    public int renderEveryNthFrame = 2;
    public Color background = new Color(0.035f, 0.055f, 0.06f, 1f);

    RawImage image;
    RectTransform imageRect;
    Camera mapCamera;
    RenderTexture target;

    void Awake()
    {
        image = GetComponent<RawImage>();
        imageRect = image.rectTransform;
        if (mainCamera == null) mainCamera = Camera.main;
        if (rtsCamera == null && mainCamera != null) rtsCamera = mainCamera.GetComponent<RTSCamera>();
        if (rtsCamera == null) rtsCamera = FindFirstObjectByType<RTSCamera>();
        if (mainCamera == null && rtsCamera != null) mainCamera = rtsCamera.GetComponent<Camera>();
        CreateCamera();
    }

    void OnEnable()
    {
        if (mapCamera != null) mapCamera.enabled = true;
        if (image != null && target != null) image.texture = target;
    }

    void OnDisable()
    {
        if (mapCamera != null) mapCamera.enabled = false;
    }

    void CreateCamera()
    {
        if (mapCamera != null) return;
        int w = Mathf.Max(64, textureWidth);
        int h = Mathf.Max(64, textureHeight);
        target = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
        {
            name = "Minimap RT (runtime)",
            filterMode = FilterMode.Point,
            antiAliasing = 1,
        };
        target.Create();

        var go = new GameObject("Minimap Camera");
        mapCamera = go.AddComponent<Camera>();
        mapCamera.orthographic = true;
        mapCamera.depth = -10f;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = background;
        mapCamera.cullingMask = -1;
        mapCamera.allowHDR = false;
        mapCamera.allowMSAA = false;
        mapCamera.useOcclusionCulling = false;
        mapCamera.targetTexture = target;
        image.texture = target;
    }

    void LateUpdate()
    {
        if (mapCamera == null || rtsCamera == null) return;
        Rect map = rtsCamera.mapBounds;
        if (map.width <= 0f || map.height <= 0f) return;

        // The viewport border is cheap and must track the main camera every frame.
        UpdateViewport(map);

        // A second full-map camera is one of the larger per-frame costs (measured in the
        // 2026-11-08 audit). Throttle only the render; the RenderTexture keeps the last
        // frame between updates, so the minimap stays live at half rate.
        int stride = Mathf.Max(1, renderEveryNthFrame);
        bool renderThisFrame = (Time.frameCount % stride) == 0;
        mapCamera.enabled = renderThisFrame;
        if (!renderThisFrame) return;

        float aspect = (float)target.width / target.height;
        mapCamera.orthographicSize = Mathf.Max(map.height * 0.5f, map.width * 0.5f / aspect);
        mapCamera.transform.position = new Vector3(map.center.x, map.center.y, -100f);
        mapCamera.transform.rotation = Quaternion.identity;
    }

    void UpdateViewport(Rect map)
    {
        if (viewport == null || mainCamera == null) return;
        float halfH = mainCamera.orthographicSize;
        float halfW = halfH * mainCamera.aspect;
        Vector3 p = mainCamera.transform.position;

        float left = Mathf.Clamp01((p.x - halfW - map.xMin) / map.width);
        float right = Mathf.Clamp01((p.x + halfW - map.xMin) / map.width);
        float bottom = Mathf.Clamp01((p.y - halfH - map.yMin) / map.height);
        float top = Mathf.Clamp01((p.y + halfH - map.yMin) / map.height);
        viewport.anchorMin = new Vector2(left, bottom);
        viewport.anchorMax = new Vector2(right, top);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (PauseMenu.IsAnyOpen) return;
        if (GameState.Instance != null && GameState.Instance.IsOver) return;
        if (mainCamera == null || rtsCamera == null || imageRect == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                imageRect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;

        Rect r = imageRect.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        float u = Mathf.Clamp01((local.x - r.xMin) / r.width);
        float v = Mathf.Clamp01((local.y - r.yMin) / r.height);
        Rect map = rtsCamera.mapBounds;
        Vector3 p = mainCamera.transform.position;
        p.x = Mathf.Lerp(map.xMin, map.xMax, u);
        p.y = Mathf.Lerp(map.yMin, map.yMax, v);
        p.z = -10f;
        mainCamera.transform.position = p;
    }

    void OnDestroy()
    {
        if (image != null && image.texture == target) image.texture = null;
        if (mapCamera != null) Destroy(mapCamera.gameObject);
        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
    }
}
