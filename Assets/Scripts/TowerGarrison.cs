using UnityEngine;

/// <summary>One-archer garrison for a tower. The original archer is not cloned: it
/// rides visibly on top of the tower while only its logic, collision and unit
/// registration are disabled. Exiting or losing the tower re-enables that same
/// object nearby, with its original transform, sorting and health bar restored.
/// The tower keeps its own health and remains the damageable target.</summary>
[RequireComponent(typeof(Building))]
public class TowerGarrison : MonoBehaviour
{
    public bool HasArcher { get; private set; }
    public UnitActor Archer => archer;

    /// <summary>Range the tower actually covers right now: the garrisoned archer's
    /// range when manned, otherwise the tower's own defensive minimum.</summary>
    public float ActiveRange => HasArcher ? range : defenseRange;

    [Header("防御")]
    [Tooltip("完成建造的塔自身最低防御射程；驻塔弓手更高时取弓手射程。")]
    public float defenseRange = 5.5f;

    [Header("驻塔表现")]
    [Tooltip("Archer position relative to the tower root while garrisoned.")]
    public Vector2 garrisonLocalOffset = new Vector2(0f, 2.0f);

    [Tooltip("How far above the tower sprite the garrisoned archer is sorted.")]
    public int garrisonSortingBonus = 20;

    Building building;
    UnitActor archer;
    float range;
    float cooldown;
    float projectileSpeed;
    GameObject projectile;
    float timer;

    // Original archer state, captured when it enters the tower.
    Transform originalParent;
    Vector3 originalLocalPosition;
    Quaternion originalLocalRotation;
    Vector3 originalLocalScale;
    SpriteRenderer archerSprite;
    Animator archerAnimator;
    YSort archerYSort;
    HealthBar archerBar;
    UnitMover archerMover;
    CombatAI archerCombat;
    Gatherer archerGatherer;
    Healer archerHealer;
    Collider2D[] archerColliders;
    bool[] archerColliderStates;

    bool actorWasEnabled;
    bool moverWasEnabled;
    bool combatWasEnabled;
    bool gathererWasEnabled;
    bool healerWasEnabled;
    bool ysortWasEnabled;
    int spriteSorting;
    bool barWasActive;
    bool hasAttackTrigger;

    static readonly int AttackHash = Animator.StringToHash("Attack");

    void Awake()
    {
        building = GetComponent<Building>();
    }

    /// <summary>Normal player/AI order. A half-built tower cannot be manned.</summary>
    public bool Garrison(UnitActor candidate) => TryGarrison(candidate, false);

    /// <summary>Save restoration only. Old saves may contain a tower that was
    /// garrisoned before its construction finished, so this path keeps the relation
    /// while Update keeps the tower silent until construction completes.</summary>
    public bool RestoreGarrison(UnitActor candidate) => TryGarrison(candidate, true);

    bool TryGarrison(UnitActor candidate, bool allowUnderConstruction)
    {
        // A disabled UnitActor is already garrisoned (or otherwise out of play) and
        // must not be claimed by a second tower.
        if (HasArcher || candidate == null || !candidate.enabled || !candidate.IsAlive ||
            !candidate.gameObject.activeInHierarchy) return false;
        if (building == null || !building.IsAlive || building.isGhost) return false;
        if (!allowUnderConstruction && building.IsUnderConstruction) return false;
        if (candidate.projectile == null || candidate.team != building.team) return false;

        archer = candidate;
        range = Mathf.Max(defenseRange, candidate.attackRange);
        cooldown = Mathf.Max(0.05f, candidate.attackCooldown);
        projectile = candidate.projectile;
        projectileSpeed = candidate.projectileSpeed;
        HasArcher = true;
        timer = 0f;

        CaptureAndApply(candidate);
        return true;
    }

    /// <summary>Cache everything Ungarrison has to put back, then pin the archer on
    /// top of the tower. Only logic, collision and unit registration are disabled —
    /// the sprite and animator stay live so the tower visibly mans an archer.</summary>
    void CaptureAndApply(UnitActor candidate)
    {
        var t = candidate.transform;
        originalParent = t.parent;
        originalLocalPosition = t.localPosition;
        originalLocalRotation = t.localRotation;
        originalLocalScale = t.localScale;

        archerSprite = candidate.GetComponentInChildren<SpriteRenderer>(true);
        archerAnimator = candidate.GetComponentInChildren<Animator>(true);
        archerYSort = candidate.GetComponentInChildren<YSort>(true);
        archerBar = candidate.GetComponentInChildren<HealthBar>(true);
        archerMover = candidate.GetComponent<UnitMover>();
        archerCombat = candidate.GetComponent<CombatAI>();
        archerGatherer = candidate.GetComponent<Gatherer>();
        archerHealer = candidate.GetComponent<Healer>();

        actorWasEnabled = candidate.enabled;
        moverWasEnabled = archerMover != null && archerMover.enabled;
        combatWasEnabled = archerCombat != null && archerCombat.enabled;
        gathererWasEnabled = archerGatherer != null && archerGatherer.enabled;
        healerWasEnabled = archerHealer != null && archerHealer.enabled;
        ysortWasEnabled = archerYSort != null && archerYSort.enabled;
        spriteSorting = archerSprite != null ? archerSprite.sortingOrder : 0;
        barWasActive = archerBar != null && archerBar.gameObject.activeSelf;

        hasAttackTrigger = false;
        if (archerAnimator != null && archerAnimator.runtimeAnimatorController != null)
            foreach (var p in archerAnimator.parameters)
                if (p.nameHash == AttackHash) { hasAttackTrigger = true; break; }

        archerColliders = candidate.GetComponentsInChildren<Collider2D>(true);
        archerColliderStates = new bool[archerColliders.Length];
        for (int i = 0; i < archerColliders.Length; i++)
            archerColliderStates[i] = archerColliders[i].enabled;

        // Stop the unit and drop its orders before it leaves the fight.
        if (archerMover != null) archerMover.Stop();
        if (archerCombat != null) archerCombat.ClearOrder();

        // Ride on top of the tower.
        t.SetParent(transform, false);
        t.localPosition = garrisonLocalOffset;
        t.localRotation = Quaternion.identity;
        t.localScale = originalLocalScale;

        // Draw above the tower instead of Y-sorting with the world.
        if (archerYSort != null) archerYSort.enabled = false;
        if (archerSprite != null)
            archerSprite.sortingOrder = TowerSortingOrder() + garrisonSortingBonus;

        if (archerBar != null) archerBar.gameObject.SetActive(false);
        if (archerMover != null) archerMover.enabled = false;
        if (archerCombat != null) archerCombat.enabled = false;
        if (archerGatherer != null) archerGatherer.enabled = false;
        if (archerHealer != null) archerHealer.enabled = false;
        candidate.enabled = false;                       // OnDisable leaves UnitActor.All
        for (int i = 0; i < archerColliders.Length; i++)
            archerColliders[i].enabled = false;
    }

    /// <summary>The tower's effective draw order, computed from its own YSort even
    /// if the archer is garrisoned before the tower's first LateUpdate.</summary>
    int TowerSortingOrder()
    {
        var ysort = GetComponent<YSort>();
        if (ysort != null)
            return YSort.OrderFor(transform.position.y, ysort.baseOrder, ysort.ordersPerUnit) + ysort.offset;
        var sr = GetComponent<SpriteRenderer>();
        return sr != null ? sr.sortingOrder : 0;
    }

    public void Ungarrison()
    {
        if (!HasArcher) return;
        HasArcher = false;
        var returning = archer;
        archer = null;

        // Scene teardown: never reparent or re-enable into a scene that is going away.
        if (returning == null || !returning.IsAlive ||
            !Application.isPlaying || !gameObject.scene.isLoaded ||
            !returning.gameObject.scene.isLoaded)
        {
            ClearCachedArcherState();
            return;
        }

        var t = returning.transform;

        // 1. Detach and restore the original transform before re-enabling logic.
        t.SetParent(originalParent != null ? originalParent : null, false);
        t.localPosition = originalLocalPosition;
        t.localRotation = originalLocalRotation;
        t.localScale = originalLocalScale;

        // 2. Restore presentation: world sorting, YSort and the health bar.
        if (archerYSort != null) archerYSort.enabled = ysortWasEnabled;
        if (archerSprite != null) archerSprite.sortingOrder = spriteSorting;
        if (archerBar != null) archerBar.gameObject.SetActive(barWasActive);

        // 3. Put the archer back on the field next to its tower.
        var grid = MapGrid.Instance;
        if (grid != null)
        {
            var cell = grid.NearestWalkable(grid.WorldToCell(transform.position), 4);
            if (cell.HasValue) t.position = grid.CellCenter(cell.Value);
        }

        // 4. Re-enable movement, combat, gathering and collision.
        for (int i = 0; i < archerColliders.Length; i++)
            if (archerColliders[i] != null) archerColliders[i].enabled = archerColliderStates[i];
        if (archerGatherer != null) archerGatherer.enabled = gathererWasEnabled;
        if (archerHealer != null) archerHealer.enabled = healerWasEnabled;
        if (archerCombat != null) archerCombat.enabled = combatWasEnabled;
        if (archerMover != null) archerMover.enabled = moverWasEnabled;
        returning.enabled = actorWasEnabled;              // OnEnable re-enters UnitActor.All

        ClearCachedArcherState();
    }

    void Update()
    {
        if (HasArcher && (archer == null || !archer.IsAlive))
        {
            HasArcher = false;
            archer = null;
            ClearCachedArcherState();
            return;
        }
        if (!HasArcher) return;

        // Defensive: damage is already rejected while garrisoned (see UnitActor and
        // Combatants.IsGone), but keep the pinned archer's bar hidden if anything
        // ever re-shows it.
        if (archerBar != null && archerBar.gameObject.activeSelf)
            archerBar.gameObject.SetActive(false);

        // A dead, preview or half-built tower must not acquire targets or advance the
        // firing timer. Old saves can restore a garrison mid-construction; that archer
        // stays put visually but only starts shooting once the tower is finished.
        if (building == null || !building.IsAlive || building.isGhost || building.IsUnderConstruction)
            return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        var target = Combatants.FindNearestEnemy(building.team, transform.position, range);
        if (target == null) return;
        timer = cooldown;

        // Fire from the archer on top of the tower, playing its own attack.
        if (archerAnimator != null && hasAttackTrigger)
            archerAnimator.SetTrigger(AttackHash);
        if (archerSprite != null)
            archerSprite.flipX = target.Transform.position.x < archer.transform.position.x;

        Vector3 from = archer.transform.position + Vector3.up * 0.8f;
        Color tint = building.team == Team.Red ? new Color(1f, 0.75f, 0.75f) : Color.white;
        // Read the archer's effective damage at fire time, not a snapshot taken when it
        // manned the tower: Forge can complete while the archer is garrisoned, and the
        // tower must then deal the buffed damage without being re-manned.
        Projectile.Spawn(projectile, from, target, archer.EffectiveAttackDamage, projectileSpeed, tint);
    }

    void ClearCachedArcherState()
    {
        originalParent = null;
        archerSprite = null;
        archerAnimator = null;
        archerYSort = null;
        archerBar = null;
        archerMover = null;
        archerCombat = null;
        archerGatherer = null;
        archerHealer = null;
        archerColliders = null;
        archerColliderStates = null;
        hasAttackTrigger = false;
        range = 0f;
        cooldown = 0f;
        projectile = null;
        projectileSpeed = 0f;
        timer = 0f;
    }

    void OnDisable()
    {
        Ungarrison();
    }
}
