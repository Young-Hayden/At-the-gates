using UnityEngine;

/// <summary>
/// The Monk: walks to the most badly hurt friendly unit in range and patches it up,
/// playing the heal animation and dropping the heal effect on the patient.
/// It never fights — <see cref="UnitActor.isCombatant"/> is off for monks.
/// </summary>
[RequireComponent(typeof(UnitActor), typeof(UnitMover))]
public class Healer : MonoBehaviour
{
    [Header("治疗")]
    [Tooltip("够得着才治的距离。")]
    public float healRange = 4.5f;
    [Tooltip("在这以内找伤兵，找到就走过去。必须明显大于 healRange，否则它永远只在\n" +
             "原地找病人，隔着一段距离的伤兵一概发现不了。")]
    public float searchRange = 16f;
    public int healAmount = 15;
    public float healCooldown = 2.5f;

    [Header("节奏")]
    public float retargetInterval = 0.6f;
    public float repathInterval = 0.5f;

    UnitActor unit;
    UnitMover mover;
    Animator anim;
    SpriteRenderer sr;
    bool hasHealTrigger;
    bool manualPosition;

    UnitActor patient;
    float cooldown;
    float retargetTimer;
    float repathTimer;
    Vector3Int lastGoal = new Vector3Int(int.MinValue, int.MinValue, 0);

    public UnitActor Patient => patient;
    public string Status => patient != null ? "治疗中" : "待命";

    /// <summary>A normal move order makes the monk hold its ordered position. It may
    /// still heal, but only patients already inside healRange; it must not chase.</summary>
    public void OrderMove()
    {
        patient = null;
        manualPosition = true;
        retargetTimer = 0f;
        mover.Stop();
    }

    void Awake()
    {
        unit = GetComponent<UnitActor>();
        mover = GetComponent<UnitMover>();
        anim = GetComponentInChildren<Animator>();
        sr = GetComponentInChildren<SpriteRenderer>();
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var p in anim.parameters)
                if (p.name == "Heal") { hasHealTrigger = true; break; }
    }

    void Update()
    {
        if (!unit.IsAlive) return;
        // A starving side's monks do not heal. The famine is soft: nothing is lost,
        // and healing resumes the frame the side is fed again.
        if (Upkeep.IsStarving(unit.team)) return;
        float dt = Time.deltaTime;
        if (cooldown > 0f) cooldown -= dt;

        if (Combatants.IsGone(patient)) patient = null;

        // re-pick a patient now and then, or as soon as the current one is topped up.
        // "Topped up" means the researched Armour maximum, not the authored one.
        retargetTimer -= dt;
        if (patient == null || patient.health >= patient.EffectiveMaxHealth || retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            patient = FindWoundedAlly();
        }
        if (patient == null) return;

        float dist = Vector2.Distance(transform.position, patient.transform.position);
        if (dist > healRange)
        {
            if (manualPosition) return;
            repathTimer -= dt;
            if (repathTimer <= 0f)
            {
                repathTimer = repathInterval;
                Chase();
            }
            return;
        }

        // An ordered move owns the mover: do not stop mid-route just because a
        // patient happens to be in reach on the way past. Heal once the move ends;
        // the destination is never silently abandoned.
        if (manualPosition && mover.Moving) return;
        if (mover.Moving) mover.Stop();
        if (sr != null) sr.flipX = patient.transform.position.x < transform.position.x;
        if (cooldown > 0f) return;
        cooldown = healCooldown;
        DoHeal();
    }

    /// <summary>Friendly unit in search range with the worst health ratio. This looks
    /// well beyond heal range on purpose — the monk walks to the patient, it does not
    /// wait for one to wander into reach.</summary>
    UnitActor FindWoundedAlly()
    {
        UnitActor best = null;
        float worst = 1f;
        float range = manualPosition ? healRange : Mathf.Max(searchRange, healRange);
        for (int i = 0; i < UnitActor.All.Count; i++)
        {
            var u = UnitActor.All[i];
            if (u == null || u == unit || !u.IsAlive) continue;
            if (u.team != unit.team) continue;
            if (u.health >= u.EffectiveMaxHealth) continue;
            float d = Vector2.Distance(transform.position, u.transform.position);
            if (d > range) continue;
            float ratio = (float)u.health / Mathf.Max(1, u.EffectiveMaxHealth);
            if (ratio < worst) { worst = ratio; best = u; }
        }
        return best;
    }

    void Chase()
    {
        var grid = MapGrid.Instance;
        if (grid == null || patient == null) return;
        var goal = grid.WorldToCell(patient.transform.position);
        if (goal == lastGoal && mover.Moving) return;
        lastGoal = goal;

        var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
        if (!start.HasValue) return;
        int region = grid.Region(start.Value);
        var dest = grid.NearestWalkableTo(start.Value, goal, 3, region);
        if (!dest.HasValue) return;
        var path = grid.FindPath(start.Value, dest.Value);
        if (path != null) mover.SetPath(path);
    }

    void DoHeal()
    {
        if (hasHealTrigger) anim.SetTrigger("Heal");
        patient.Heal(healAmount);
        var bank = FxBank.Instance;
        if (bank != null) bank.Heal(patient.transform.position);
    }
}
