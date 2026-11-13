using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves a unit along an A* cell path. Drives the Animator Speed parameter and
/// flips the sprite horizontally.
/// Sorting is handled by <see cref="YSort"/>, not here.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class UnitMover : MonoBehaviour
{
    public float speed = 3f;
    public SpriteRenderer sr;
    public Animator anim;

    List<Vector3> pts;
    List<Vector3Int> pathCells;
    int i;
    System.Action onArrived;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    int hasSpeedParam = -1;          // -1 unchecked, 0 absent, 1 present

    /// <summary>Sets Speed only if the controller actually declares it. The tool-aware
    /// Pawn controller is driven by its own Look/Moving/Act parameters and has no Speed,
    /// and writing to a parameter that does not exist logs a warning every frame.</summary>
    void SetSpeed(float value)
    {
        if (anim == null) return;
        if (hasSpeedParam < 0)
        {
            hasSpeedParam = 0;
            if (anim.runtimeAnimatorController != null)
                foreach (var p in anim.parameters)
                    if (p.nameHash == SpeedHash) { hasSpeedParam = 1; break; }
        }
        if (hasSpeedParam == 1) anim.SetFloat(SpeedHash, value);
    }

    public bool Moving => pts != null && i < pts.Count;

    public void SetPath(List<Vector3Int> cells) { SetPath(cells, null); }

    /// <summary>Walk the path and invoke <paramref name="onArrive"/> once the unit
    /// reaches the last cell. The callback is dropped if a new path replaces it.</summary>
    public void SetPath(List<Vector3Int> cells, System.Action onArrive)
    {
        onArrived = onArrive;
        if (cells == null || cells.Count == 0 || MapGrid.Instance == null)
        {
            // No path: the unit never set out, so do NOT report arrival — the caller
            // is responsible for checking FindPath's result first.
            pts = null;
            pathCells = null;
            onArrived = null;
            return;
        }
        pts = new List<Vector3>(cells.Count);
        pathCells = new List<Vector3Int>(cells);
        foreach (var c in cells) pts.Add(MapGrid.Instance.CellCenter(c));
        i = 0;
        if (pts.Count > 1 && Vector2.Distance(transform.position, pts[0]) < 0.2f) i = 1;
    }

    public void Stop()
    {
        pts = null;
        pathCells = null;
        onArrived = null;
        SetSpeed(0f);
    }

    void FireArrived()
    {
        var cb = onArrived;
        onArrived = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (!Moving)
        {
            SetSpeed(0f);
            if (pts != null)
            {
                pts = null;
                pathCells = null;
                FireArrived();   // finish of the previous path
            }
            return;
        }

        // A building may have been placed after this path was calculated. Never
        // step into the now-blocked waypoint, and never turn that stop into arrival.
        var grid = MapGrid.Instance;
        if (grid == null || pathCells == null || i >= pathCells.Count ||
            !grid.Walkable(pathCells[i]))
        {
            Stop();
            return;
        }

        Vector3 target = pts[i];
        Vector3 pos = transform.position;
        Vector3 next = Vector3.MoveTowards(pos, target, speed * Time.deltaTime);
        float dx = target.x - pos.x;
        if (sr != null && Mathf.Abs(dx) > 0.01f) sr.flipX = dx < 0f;

        if ((next - target).sqrMagnitude < 1e-6f)
        {
            // Landed exactly on the waypoint: snap so drift does not accumulate
            // across a long path (and so the unit does not stop 2cm short).
            next = target;
            i++;
            if (i > pts.Count) i = pts.Count;
        }

        transform.position = next;
        SetSpeed(Moving ? 1f : 0f);
        if (!Moving)
        {
            pts = null;
            pathCells = null;
            FireArrived();
        }
    }
}
