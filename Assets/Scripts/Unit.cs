using System.Collections.Generic;
using UnityEngine;

public enum Team { Neutral = 0, Blue = 1, Red = 2 }

/// <summary>
/// A unit's identity: which side it is on, how much health it has, and how it fights.
///
/// Named UnitActor, not Unit, because the Visual Scripting package also defines a
/// type called Unit and the two collide in any file that has its namespace in scope.
/// </summary>
[RequireComponent(typeof(UnitMover))]
public class UnitActor : MonoBehaviour, IDamageable
{
    public static readonly List<UnitActor> All = new List<UnitActor>();

    [Header("阵营")]
    public Team team = Team.Blue;

    [Header("生命")]
    public int maxHealth = 60;
    public int health = 60;
    public bool showHealthBar = true;
    [Tooltip("血条挂多高（世界单位）。")]
    public float barHeight = 1.5f;

    [Header("战斗")]
    public bool isCombatant = true;
    public int attackDamage = 8;
    public float attackRange = 1.1f;
    public float attackCooldown = 1.4f;
    [Tooltip("近战从挥击开始到命中结算的秒数；0 = 当帧。远程忽略。")]
    public float attackHitDelay = 0f;
    [Tooltip("多远之内主动去找敌人打。")]
    public float aggroRange = 7f;
    [Tooltip("留空 = 近战；填了就是远程，会发射这个预制体。")]
    public GameObject projectile;
    public float projectileSpeed = 9f;

    [Header("死亡")]
    [Tooltip("尸体停留多久后消失，0 = 立刻消失。")]
    public float corpseSeconds = 0.8f;

    public bool IsAlive => health > 0;
    public string DisplayName { get; set; }

    /// <summary>Damage this unit actually deals, including the researched Forge bonus.
    /// The authored <see cref="attackDamage"/> field stays the source of truth and is
    /// never rewritten; workers are noncombatants and never receive the bonus.</summary>
    public int EffectiveAttackDamage =>
        isCombatant ? attackDamage + Technology.DamageBonus(team) : attackDamage;

    /// <summary>Maximum health including the researched Armour bonus. Only combatants
    /// receive it, mirroring <see cref="EffectiveAttackDamage"/>: workers and monks are
    /// attackable but are not combat upgrades, and <see cref="Healer"/> treats
    /// health &gt;= this value as "topped up", so buffing a non-combatant would turn
    /// every worker into a permanent patient at 70/81. The multiplier is computed from
    /// the per-team mask (never written into <see cref="maxHealth"/>), so a unit can only
    /// ever be at base x 1.15, never compounded. Integer arithmetic keeps the rounding
    /// stable: 110 -> 127, 60 -> 69.</summary>
    public int EffectiveMaxHealth
    {
        get
        {
            if (!isCombatant) return maxHealth;
            int percent = Technology.MaxHealthBonusPercent(team);
            return percent <= 0 ? maxHealth : maxHealth + (maxHealth * percent + 50) / 100;
        }
    }

    /// <summary>Long-lived state captured by the save system. Transient paths and
    /// attack targets are intentionally omitted; the AI reacquires them after loading.</summary>
    [System.Serializable]
    public class SaveState
    {
        public Team team;
        public int health;
        public int maxHealth;
        public string displayName;
        public bool flipX;
    }

    public SaveState CaptureSaveState()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        return new SaveState
        {
            team = team,
            health = health,
            maxHealth = maxHealth,
            displayName = DisplayName,
            flipX = sr != null && sr.flipX,
        };
    }

    /// <summary>Called after the instance's first frame so its health bar exists.</summary>
    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        team = state.team;
        maxHealth = Mathf.Max(1, state.maxHealth);
        // Saved health may be above the authored maximum when Armour is researched; the
        // mask is restored before any unit, so the effective maximum is already right.
        health = Mathf.Clamp(state.health, 0, EffectiveMaxHealth);
        DisplayName = state.displayName;
        UnitLimits.Invalidate();       // the restored team may differ from the prefab
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.flipX = state.flipX;
        RefreshBar();
    }

    /// <summary>Raised after a nonlethal hit. Workers listen for this to run to a
    /// base instead of fighting back; a lethal hit goes through <see cref="Die"/>
    /// and does not fire it.</summary>
    public event System.Action Damaged;

    // IDamageable — explicit so the property names cannot clash with the fields above
    Team IDamageable.Team => team;
    Transform IDamageable.Transform => transform;

    HealthBar bar;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); UnitLimits.Invalidate(); }
    void OnDisable() { All.Remove(this); UnitLimits.Invalidate(); }

    void Start()
    {
        // A fresh prefab is authored at full health; with Armour researched "full" is the
        // effective maximum, so a newly produced unit is born at base x 1.15.
        int cap = EffectiveMaxHealth;
        health = health <= 0 || health >= maxHealth ? cap : Mathf.Clamp(health, 1, cap);
        if (string.IsNullOrEmpty(DisplayName)) DisplayName = name;
        if (showHealthBar)
        {
            bar = HealthBar.Create(transform, barHeight, 0.9f);
            RefreshBar();
        }
    }

    // ------------------------------------------------------------------ damage

    public void TakeDamage(int amount)
    {
        // A disabled UnitActor is out of play: a garrisoned archer stays visible but
        // must not be hurt, even by damage that bypasses targeting.
        if (!enabled || !IsAlive || amount <= 0) return;
        health -= amount;
        RefreshBar();
        if (health <= 0) Die();
        else Damaged?.Invoke();
    }

    public void Heal(int amount)
    {
        if (!IsAlive) return;
        health = Mathf.Min(EffectiveMaxHealth, health + amount);
        RefreshBar();
    }

    /// <summary>Adds head-room when a flat bonus raises the maximum (Armour completing for
    /// a unit that already exists). It is not a heal: the current damage is preserved.</summary>
    public void GrantMaxHealthIncrease(int amount)
    {
        if (!IsAlive || amount <= 0) return;
        health = Mathf.Min(EffectiveMaxHealth, health + amount);
        RefreshBar();
    }

    void Die()
    {
        health = 0;
        if (bar != null) bar.gameObject.SetActive(false);

        var mover = GetComponent<UnitMover>();
        if (mover != null) mover.Stop();
        var g = GetComponent<Gatherer>();
        if (g != null) g.enabled = false;
        var ai = GetComponent<CombatAI>();
        if (ai != null) ai.enabled = false;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        All.Remove(this);
        UnitLimits.Invalidate();       // a corpse leaves the roster immediately

        // leave a body behind — the skull animation settles into a small pile
        var bank = FxBank.Instance;
        if (bank != null) bank.Corpse(transform.position, team);

        if (corpseSeconds > 0f) Destroy(gameObject, corpseSeconds);
        else Destroy(gameObject);
    }

    void RefreshBar()
    {
        if (bar == null) return;
        int m = Mathf.Max(1, EffectiveMaxHealth);
        bar.SetRatio((float)health / m);
    }
}
