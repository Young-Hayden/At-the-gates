using UnityEngine;

/// <summary>
/// Picks the worker's animation set from what it is actually doing: the axe while it
/// walks to a tree, the log-carrying pose while it hauls wood home, and so on.
///
/// Tiny Swords ships the Pawn with a full set of tool and carry variants per state
/// (Pawn_Idle Axe, Pawn_Run Pickaxe, Pawn_Interact Knife ...), so the tool only has to
/// be reported to the Animator — see BuildPawnAnimation for the controller it drives.
/// </summary>
[RequireComponent(typeof(Gatherer))]
public class WorkerLook : MonoBehaviour
{
    [Tooltip("一次挥动动作持续多久（秒）。")]
    public float swingSeconds = 0.55f;

    Gatherer gatherer;
    UnitMover mover;
    CombatAI combat;
    Animator anim;
    bool ready;

    float swing;
    bool wasHarvesting;

    static readonly int LookHash = Animator.StringToHash("Look");
    static readonly int MovingHash = Animator.StringToHash("Moving");
    static readonly int ActHash = Animator.StringToHash("Act");

    // Look indices, matching the controller's state names
    const int LookPlain = 0;
    const int LookCarryWood = 5, LookCarryGold = 6, LookCarryMeat = 7;

    void Awake()
    {
        gatherer = GetComponent<Gatherer>();
        mover = GetComponent<UnitMover>();
        combat = GetComponent<CombatAI>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var p in anim.parameters)
                if (p.name == "Look") { ready = true; break; }
    }

    /// <summary>True while the worker is mid-swing because it just hit something.</summary>
    bool Fighting => combat != null && Time.time - combat.LastAttackTime < swingSeconds;

    void Update()
    {
        if (!ready) return;

        bool harvesting = gatherer.IsHarvesting;
        if (harvesting && !wasHarvesting) swing = swingSeconds;   // started a new chop
        wasHarvesting = harvesting;

        // A worker defending itself swings too, on the enemy's cooldown rather than its own.
        if (Fighting) swing = Mathf.Max(swing, swingSeconds - (Time.time - combat.LastAttackTime));

        if (swing > 0f) swing -= Time.deltaTime;
    }

    void LateUpdate()
    {
        if (!ready) return;
        anim.SetInteger(LookHash, LookIndex());
        anim.SetBool(MovingHash, mover != null && mover.Moving);
        anim.SetInteger(ActHash, swing > 0f ? ToolIndex() : 0);
    }

    /// <summary>Which Idle/ Run variant to be in.</summary>
    int LookIndex()
    {
        if (gatherer.Carrying > 0)
            return CarryLook(gatherer.CarryingKind);

        var node = gatherer.TargetNode;
        if (node != null && gatherer.IsWorking) return ToolLook(node.kind);

        if (Fighting) return 1;      // squared up with something, tool out
        return LookPlain;
    }

    /// <summary>Which Interact variant to be in. 0 = not swinging.</summary>
    int ToolIndex()
    {
        var node = gatherer.TargetNode;
        if (gatherer.Carrying > 0) return CarryLook(gatherer.CarryingKind) switch
        {
            LookCarryWood => 1, LookCarryGold => 2, LookCarryMeat => 3, _ => 1
        };
        if (node == null || !gatherer.IsWorking) return 1;   // fighting: swing the axe
        return ToolLook(node.kind);
    }

    static int ToolLook(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Wood: return 1;    // axe     — Pawn_* Axe
            case ResourceKind.Gold: return 2;    // pickaxe — Pawn_* Pickaxe
            case ResourceKind.Meat: return 3;    // knife   — Pawn_* Knife
            default:                return 1;
        }
    }

    static int CarryLook(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Wood: return LookCarryWood;
            case ResourceKind.Gold: return LookCarryGold;
            case ResourceKind.Meat: return LookCarryMeat;
            default:                return LookCarryWood;
        }
    }
}
