using System.Collections.Generic;

public enum RosterValidationReason : byte
{
    None,
    MissingRoster,
    IncorrectUnitCount,
    UnitCatalogUnavailable,
    InsufficientEligibleUnits,
    UnitIndexOutOfRange,
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
    /// <summary>
    /// Authoritative player roster size. Change this value to alter the number of units each
    /// player controls; roster validation, selection UI, and match spawning consume this value.
    /// </summary>
    public const int UnitsPerPlayer = 5;

    /// <summary>
    /// Builds a configured roster from a preference seed. Preferred indices are used first, then
    /// ascending indices fill any remaining slots. Catalog validation later reports if the project
    /// does not contain enough eligible units.
    /// </summary>
    public static int[] BuildPreferredRoster(params int[] preferredUnitIndices)
    {
        if (UnitsPerPlayer <= 0)
            throw new System.InvalidOperationException("UnitsPerPlayer must be greater than zero.");
        if (preferredUnitIndices == null)
            throw new System.ArgumentNullException(nameof(preferredUnitIndices));

        List<int> roster = new(UnitsPerPlayer);
        HashSet<int> selected = new();
        foreach (int unitIndex in preferredUnitIndices)
        {
            if (unitIndex >= 0 && selected.Add(unitIndex))
                roster.Add(unitIndex);
            if (roster.Count == UnitsPerPlayer)
                return roster.ToArray();
        }

        for (int unitIndex = 0; roster.Count < UnitsPerPlayer; unitIndex++)
        {
            if (selected.Add(unitIndex))
                roster.Add(unitIndex);
        }
        return roster.ToArray();
    }

    public static RosterValidationResult ValidateCatalog(IReadOnlyList<UnitData> unitCatalog)
    {
        if (unitCatalog == null)
            return RosterValidationResult.Invalid(RosterValidationReason.UnitCatalogUnavailable);

        int eligibleCount = 0;
        for (int unitIndex = 0; unitIndex < unitCatalog.Count; unitIndex++)
        {
            if (IsUnitEligible(unitCatalog, unitIndex))
                eligibleCount++;
        }
        return eligibleCount >= UnitsPerPlayer
            ? RosterValidationResult.Valid
            : RosterValidationResult.Invalid(RosterValidationReason.InsufficientEligibleUnits);
    }

    /// <summary>
    /// Validates an externally supplied fireteam. Failure priority is stable: shape, catalog,
    /// index range, then per-unit availability. Repeated picks of the same unit are allowed —
    /// a fireteam may field the same unit in more than one slot.
    /// </summary>
    public static RosterValidationResult Validate(
        IReadOnlyList<int> roster,
        IReadOnlyList<UnitData> unitCatalog
    )
    {
        if (roster == null)
            return RosterValidationResult.Invalid(RosterValidationReason.MissingRoster);
        if (roster.Count != UnitsPerPlayer)
            return RosterValidationResult.Invalid(RosterValidationReason.IncorrectUnitCount);
        RosterValidationResult catalogValidation = ValidateCatalog(unitCatalog);
        if (!catalogValidation.IsValid)
            return catalogValidation;

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
            or RosterValidationReason.IncorrectUnitCount =>
                $"Choose exactly {UnitsPerPlayer} units.",
            RosterValidationReason.UnitCatalogUnavailable =>
                "The unit roster is unavailable. Try again.",
            RosterValidationReason.InsufficientEligibleUnits =>
                $"At least {UnitsPerPlayer} eligible units are required to start a match.",
            RosterValidationReason.UnitIndexOutOfRange =>
                "One selected unit is no longer available. Choose another unit.",
            RosterValidationReason.UnitUnavailable =>
                "That unit is unavailable for deployment. Choose another unit.",
            _ => $"That fireteam is not valid. Choose {UnitsPerPlayer} units again.",
        };
    }
}
