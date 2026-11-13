using UnityEngine;

/// <summary>
/// Phase A stockpile caps. Both banks (the player's <see cref="Economy"/> and each
/// AI's private ledger in <see cref="AIController"/>) route every addition through
/// these helpers, so overflow is discarded per resource kind instead of spilling
/// into another kind or past the cap.
/// </summary>
public static class ResourceLimits
{
    public const int GoldCap = 2000;
    public const int WoodCap = 1200;
    public const int MeatCap = 1000;

    public static int Cap(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Gold: return GoldCap;
            case ResourceKind.Wood: return WoodCap;
            default:                return MeatCap;
        }
    }

    /// <summary>Bank <paramref name="amount"/> on top of <paramref name="current"/>,
    /// clamped to [0, cap]. Overflow above the cap is discarded.</summary>
    public static int ClampAdd(int current, int amount, ResourceKind kind)
        => Mathf.Min(Cap(kind), Mathf.Max(0, current + amount));

    public static bool IsFull(int current, ResourceKind kind) => current >= Cap(kind);

    public static int Clamp(int current, ResourceKind kind)
        => Mathf.Clamp(current, 0, Cap(kind));
}
