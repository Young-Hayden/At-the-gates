using UnityEngine;

/// <summary>
/// A frame-by-frame effect that needs no Animator: it walks a Sprite[] on a timer and
/// destroys itself when it finishes. Everything visual that is "fire and forget" —
/// hits, explosions, dust, corpses — runs through this.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Fx : MonoBehaviour
{
    public Sprite[] frames;
    public float fps = 20f;
    public bool loop;
    [Tooltip("循环特效的存活秒数；0 = 无限。")]
    public float lifetime;
    [Tooltip("播完是否销毁自己。")]
    public bool destroyOnEnd = true;
    [Tooltip("每个特效自带的排序基准。")]
    public int sortingOrder = 2500;

    SpriteRenderer sr;
    float t;
    int i;
    float life;
    bool done;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = sortingOrder;
    }

    void OnEnable()
    {
        t = 0f; i = 0; done = false; life = lifetime;
        if (frames != null && frames.Length > 0 && sr != null) sr.sprite = frames[0];
    }

    void Update()
    {
        // The lifetime has to be checked BEFORE the "finished playing" early-out.
        // A one-shot with destroyOnEnd=false (the corpse) sets done on its last frame and
        // would then return here forever, so its lifetime never counted down and it was
        // never destroyed — one leaked GameObject per death, for the whole match.
        if (lifetime > 0f)
        {
            life -= Time.deltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }
        }

        if (done) return;

        if (frames == null || frames.Length == 0 || sr == null) return;

        t += Time.deltaTime * Mathf.Max(1f, fps);
        while (t >= 1f)
        {
            t -= 1f;
            i++;
            if (i >= frames.Length)
            {
                if (loop) i %= frames.Length;
                else
                {
                    done = true;
                    if (destroyOnEnd) Destroy(gameObject);
                    return;
                }
            }
        }
        sr.sprite = frames[i];
    }

    /// <summary>Drop a one-shot effect prefab at a world position.</summary>
    public static Fx Play(GameObject prefab, Vector3 pos, Color tint, float scale)
    {
        if (prefab == null) return null;
        pos.z = 0f;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
        var fx = go.GetComponent<Fx>();
        if (tint != Color.clear)
            foreach (var s in go.GetComponentsInChildren<SpriteRenderer>()) s.color = tint;
        return fx;
    }
}
