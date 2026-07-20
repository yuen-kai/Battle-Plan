using System.Collections.Generic;

public enum RosterValidationReason : byte
{
    None,
    MissingRoster,
    IncorrectUnitCount,
    UnitCatalogUnavailable,
    UnitIndexOutOfRange,
    DuplicateUnit,
    UnitUnavailable,
}

public readonly struct RosterValidationResult : System.IEquatable<RosterValidationResult>
{
    public bool IsValid => Reason == RosterValidationReason.None;
    public RosterValidationReason Reason { get; }
    public int SlotIndex { get; }
    public int UnitIndex { get; }

    private RosterValidationResult(
        RosterValidationReason reason,
        int slotIndex,
        int unitIndex
    )
    {
        Reason = reason;
        SlotIndex = slotIndex;
        UnitIndex = unitIndex;
    }

    public static RosterValidationResult Valid =>
        new(RosterValidationReason.None, -1, -1);

    public static RosterValidationResult Invalid(
        RosterValidationReason reason,
        int slotIndex = -1,
        int unitIndex = -1
    )
    {
        return new RosterValidationResult(reason, slotIndex, unitIndex);
    }

    public bool Equals(RosterValidationResult other)
    {
        return Reason == other.Reason
            && SlotIndex == other.SlotIndex
            && UnitIndex == other.UnitIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is RosterValidationResult other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(Reason, SlotIndex, UnitIndex);
    }
}

public static class RosterRules
{
    public const int FireteamSize = 3;

    /// <summary>
    /// Validates an externally supplied fireteam. Failure priority is stable: shape, catalog,
    /// index range, duplicates, then per-unit availability.
    /// </summary>
    public static RosterValidationResult Validate(
        IReadOnlyList<int> roster,
        IReadOnlyList<UnitData> unitCatalog
    )
    {
        if (roster == null)
            return RosterValidationResult.Invalid(RosterValidationReason.MissingRoster);
        if (roster.Count != FireteamSize)
            return RosterValidationResult.Invalid(RosterValidationReason.IncorrectUnitCount);
        if (unitCatalog == null)
            return RosterValidationResult.Invalid(RosterValidationReason.UnitCatalogUnavailable);

        for (int slotIndex = 0; slotIndex < roster.Count; slotIndex++)
        {
            int unitIndex = roster[slotIndex];
            if (unitIndex < 0 || unitIndex >= unitCatalog.Count)
            {
                return RosterValidationResult.Invalid(
                    RosterValidationReason.UnitIndexOutOfRange,
                    slotIndex,
                    unitIndex
                );
            }
        }

        HashSet<int> selected = new();
        for (int slotIndex = 0; slotIndex < roster.Count; slotIndex++)
        {
            int unitIndex = roster[slotIndex];
            if (!selected.Add(unitIndex))
            {
                return RosterValidationResult.Invalid(
                    RosterValidationReason.DuplicateUnit,
                    slotIndex,
                    unitIndex
                );
            }
        }

        for (int slotIndex = 0; slotIndex < roster.Count; slotIndex++)
        {
            int unitIndex = roster[slotIndex];
            if (!IsUnitEligible(unitCatalog, unitIndex))
            {
                return RosterValidationResult.Invalid(
                    RosterValidationReason.UnitUnavailable,
                    slotIndex,
                    unitIndex
                );
            }
        }

        return RosterValidationResult.Valid;
    }

    public static bool IsUnitEligible(IReadOnlyList<UnitData> unitCatalog, int unitIndex)
    {
        return unitCatalog != null
            && unitIndex >= 0
            && unitIndex < unitCatalog.Count
            && unitCatalog[unitIndex] != null
            && unitCatalog[unitIndex].IsRosterEligible;
    }

    public static string GetUserMessage(RosterValidationResult result)
    {
        return result.Reason switch
        {
            RosterValidationReason.None => string.Empty,
            RosterValidationReason.MissingRoster
            or RosterValidationReason.IncorrectUnitCount => "Choose exactly three units.",
            RosterValidationReason.UnitCatalogUnavailable =>
                "The unit roster is unavailable. Try again.",
            RosterValidationReason.UnitIndexOutOfRange =>
                "One selected unit is no longer available. Choose another unit.",
            RosterValidationReason.DuplicateUnit =>
                "Choose three different units; duplicate picks are not allowed.",
            RosterValidationReason.UnitUnavailable =>
                "That unit is unavailable for deployment. Choose another unit.",
            _ => "That fireteam is not valid. Choose three units again.",
        };
    }
}
