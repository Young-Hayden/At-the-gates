using UnityEngine;

/// <summary>
/// One place that owns every effect prefab, so combat code can just say "hit here"
/// without holding references of its own. Built by Assets/Editor/BuildFx.cs.
/// </summary>
public class FxBank : MonoBehaviour
{
    public static FxBank Instance { get; private set; }

    [Header("战斗")]
    public GameObject hit;          // 命中火花
    public GameObject explosion;    // 大爆炸（建筑被毁）
    public GameObject waterSplash;  // 水花

    [Header("单位")]
    public GameObject dust;         // 尘土（阵亡 / 移动）
    public GameObject corpse;       // 尸体

    [Header("其他")]
    public GameObject heal;         // 治疗特效
    public GameObject goldSparkle;  // 资源点高亮

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    static Vector3 Lift(Vector3 p, float h) { p.z = 0f; return p + Vector3.up * h; }

    public void Hit(Vector3 p)      { if (Instance != null) Fx.Play(hit, Lift(p, 0.6f), Color.clear, 0.9f); }
    public void Boom(Vector3 p, float scale = 1f) { Fx.Play(explosion, Lift(p, 0.8f), Color.clear, scale); }
    public void Splash(Vector3 p)   { Fx.Play(waterSplash, Lift(p, 0.4f), Color.clear, 1f); }
    public void Dust(Vector3 p)     { Fx.Play(dust, Lift(p, 0.3f), Color.clear, 1f); }
    public void Heal(Vector3 p)     { Fx.Play(heal, Lift(p, 0.6f), Color.clear, 1f); }
    public void Build(Vector3 p)    { Fx.Play(dust, Lift(p, 0.3f), Color.clear, 1.6f); }

    /// <summary>A little shine when gold is banked at a drop-off.</summary>
    public void GoldSparkle(Vector3 p) { Fx.Play(goldSparkle, Lift(p, 1.8f), Color.clear, 1.2f); }

    /// <summary>Leaves a body behind. Tinted per faction so you can tell whose it is.</summary>
    public void Corpse(Vector3 p, Team team)
    {
        Color tint = team == Team.Red ? new Color(1f, 0.72f, 0.72f) : Color.white;
        Fx.Play(corpse, Lift(p, 0.2f), tint, 1f);
    }

    /// <summary>Convenience that tolerates the bank not existing yet.</summary>
    public static FxBank Get => Instance;
}
