using UnityEngine;

public enum ResourceKind { Gold = 0, Wood = 1, Meat = 2 }

/// <summary>An amount of each resource. Serialized on buildings so costs are
/// editable per prefab in the Inspector.</summary>
[System.Serializable]
public struct ResourceCost
{
    public int gold, wood, meat;

    public ResourceCost(int gold, int wood = 0, int meat = 0)
    {
        this.gold = gold; this.wood = wood; this.meat = meat;
    }

    public bool IsZero => gold <= 0 && wood <= 0 && meat <= 0;

    public int Get(ResourceKind k)
    {
        switch (k)
        {
            case ResourceKind.Gold: return gold;
            case ResourceKind.Wood: return wood;
            default:                return meat;
        }
    }

    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (gold > 0) parts.Add(gold + "金");
        if (wood > 0) parts.Add(wood + "木");
        if (meat > 0) parts.Add(meat + "肉");
        return parts.Count == 0 ? "免费" : string.Join(" ", parts);
    }
}

/// <summary>
/// The player's stockpile. Everything that spends or earns resources goes through
/// here so the HUD only has one thing to listen to.
/// </summary>
public class Economy : MonoBehaviour
{
    public static Economy Instance { get; private set; }

    [Header("开局资源")]
    public int gold = 600;
    public int wood = 300;
    public int meat = 200;

    /// <summary>Raised whenever any resource changes. The HUD listens to this.</summary>
    public event System.Action Changed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public int Get(ResourceKind k)
    {
        switch (k)
        {
            case ResourceKind.Gold: return gold;
            case ResourceKind.Wood: return wood;
            default:                return meat;
        }
    }

    public void Add(ResourceKind k, int amount)
    {
        if (amount == 0) return;
        switch (k)
        {
            case ResourceKind.Gold: gold = ResourceLimits.ClampAdd(gold, amount, k); break;
            case ResourceKind.Wood: wood = ResourceLimits.ClampAdd(wood, amount, k); break;
            default:
                meat = ResourceLimits.ClampAdd(meat, amount, k);
                // A worker hauling meat home ends a famine at once, not at the next
                // ten-second upkeep tick.
                Upkeep.NoteMeatReceived(Upkeep.PlayerTeam);
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Meat-only upkeep payment. Spends what the bank can afford and returns
    /// the amount actually paid, so the caller can see the unpaid remainder and never
    /// book it as debt. Routed through <see cref="TrySpend"/>, the same literal spend
    /// path every other purchase uses, so the bank can never go negative.</summary>
    public int SpendMeat(int amount)
    {
        if (amount <= 0) return 0;
        int paid = Mathf.Min(meat, amount);
        if (paid <= 0) return 0;
        return TrySpend(new ResourceCost(0, 0, paid)) ? paid : 0;
    }

    public bool CanAfford(ResourceCost c)
    {
        return gold >= c.gold && wood >= c.wood && meat >= c.meat;
    }

    public bool TrySpend(ResourceCost c)
    {
        if (!CanAfford(c)) return false;
        gold -= c.gold; wood -= c.wood; meat -= c.meat;
        Changed?.Invoke();
        return true;
    }

    public void Refund(ResourceCost c)
    {
        gold = ResourceLimits.Clamp(gold + c.gold, ResourceKind.Gold);
        wood = ResourceLimits.Clamp(wood + c.wood, ResourceKind.Wood);
        meat = ResourceLimits.Clamp(meat + c.meat, ResourceKind.Meat);
        Changed?.Invoke();
    }

    /// <summary>Forces every stockpile back inside its Phase A cap. The save loader
    /// writes the fields directly behind <see cref="Add"/>'s back, so
    /// <see cref="RaiseChanged"/> calls this first: a file written before the caps
    /// existed (or edited by hand) cannot load an over-cap bank.</summary>
    public void ClampStockpiles()
    {
        gold = ResourceLimits.Clamp(gold, ResourceKind.Gold);
        wood = ResourceLimits.Clamp(wood, ResourceKind.Wood);
        meat = ResourceLimits.Clamp(meat, ResourceKind.Meat);
    }

    /// <summary>Re-raises Changed without moving any money. The save loader writes the
    /// fields directly, and listeners (the HUD resource bar) only repaint on Changed —
    /// without this the bar shows the scene defaults until the first delivery. Any
    /// direct over-cap write is clamped here so the load path cannot exceed the caps.</summary>
    public void RaiseChanged()
    {
        ClampStockpiles();
        Changed?.Invoke();
    }
}
