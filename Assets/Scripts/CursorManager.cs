using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Swaps the hardware cursor to match what a click would do right now — the single
/// cheapest way to make an RTS feel like an RTS.
/// </summary>
public class CursorManager : MonoBehaviour
{
    [Header("光标图（64x64，需开 Read/Write）")]
    public Texture2D normal;
    public Texture2D move;
    public Texture2D attack;
    public Texture2D blocked;

    [Tooltip("热点像素，从左上角算。")]
    public Vector2 hotspot = new Vector2(6f, 6f);

    public Camera cam;
    public SelectionManager selection;
    public BuildingPlacer placer;

    enum Kind { Normal, Move, Attack, Blocked }
    Kind current = Kind.Normal;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (selection == null) selection = FindFirstObjectByType<SelectionManager>();
        if (placer == null) placer = FindFirstObjectByType<BuildingPlacer>();
    }

    void Update()
    {
        if (cam == null) return;
        Apply(Decide());
    }

    Kind Decide()
    {
        // while a build ghost is up the ghost itself already shows valid/invalid
        if (placer != null && placer.IsPlacing)
            return string.IsNullOrEmpty(placer.LastRefusalReason) ? Kind.Move : Kind.Blocked;

        // an armed attack order shows the swords wherever the cursor points
        if (selection != null && selection.AttackArmed) return Kind.Attack;

        if (selection == null || selection.selected.Count == 0) return Kind.Normal;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return Kind.Normal;

        Vector3 mw = cam.ScreenToWorldPoint(Input.mousePosition);
        mw.z = 0f;
        var enemy = Combatants.FindAt(mw, SelectionTeam());
        return enemy != null ? Kind.Attack : Kind.Move;
    }

    Team SelectionTeam()
    {
        if (selection == null) return Team.Neutral;
        for (int i = 0; i < selection.selected.Count; i++)
        {
            var u = selection.selected[i];
            if (u == null) continue;
            var a = u.GetComponent<UnitActor>();
            if (a != null) return a.team;
        }
        return Team.Neutral;
    }

    void Apply(Kind k)
    {
        if (k == current && Cursor.visible) return;
        current = k;
        Texture2D tex = k == Kind.Move ? move
                      : k == Kind.Attack ? attack
                      : k == Kind.Blocked ? blocked
                      : normal;
        if (tex == null) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
        Cursor.SetCursor(tex, hotspot, CursorMode.Auto);
    }

    void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
