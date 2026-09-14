namespace Content.Server._Solreign.Pets;

/// <summary>
///     What happens when a critter with <see cref="SolreignTameableComponent"/> is fed a valid food
///     item. Pure, unit-testable state-transition logic — no ECS, no I/O. The ECS layer
///     (<see cref="SolreignTameableSystem"/>) resolves whether the fed item counts as food and whether
///     the feeder already owns the critter, then hands both booleans to <see cref="TamingRules.Evaluate"/>.
/// </summary>
public enum TamingOutcome
{
    /// <summary>The fed item isn't valid food for this critter — nothing happens, food is not consumed.</summary>
    Rejected,

    /// <summary>Critter had no owner yet; the feeder becomes the new owner.</summary>
    NewlyTamed,

    /// <summary>Critter already has an owner and the feeder is a different Solreign asset; ownership transfers.</summary>
    Retamed,

    /// <summary>Critter is already owned by the feeder — a happy top-up, not a state change.</summary>
    AlreadyBonded,

    /// <summary>Outcome when releasing a pet bond.</summary>
    Released,

    /// <summary>No bond existed to release.</summary>
    NoOwnerToRelease,
}

/// <summary>
///     Outcome when toggling pet follow vs stay command.
/// </summary>
public enum FollowToggleOutcome
{
    Untamed,
    NowFollowing,
    NowStaying,
}

/// <summary>
///     Outcome when grooming/cleaning a pet.
/// </summary>
public enum CleanupOutcome
{
    RejectedItem,
    AlreadyClean,
    Cleaned,
}

/// <summary>
///     Outcome when recording a cosmetic ledger stamp.
/// </summary>
public enum CosmeticStampOutcome
{
    NoBond,
    AlreadyStamped,
    Stamped,
}

/// <summary>
///     Pure taming state-transition rules (Solreign Pets v1). Given whether the fed item is valid food,
///     whether the critter already has an owner, and whether that owner is the current feeder, computes
///     the resulting <see cref="TamingOutcome"/>. No ECS, no I/O — safe for NUnit.
/// </summary>
public static class TamingRules
{
    /// <summary>
    ///     Computes the taming outcome for a single feed attempt.
    /// </summary>
    /// <param name="isValidFood">Whether the item used on the critter passes its food whitelist.</param>
    /// <param name="wasTamed">Whether the critter already had a registered owner before this feed.</param>
    /// <param name="sameOwnerAsFeeder">
    ///     Whether the critter's existing owner is the same entity performing this feed. Ignored (may be
    ///     any value) when <paramref name="wasTamed"/> is false.
    /// </param>
    public static TamingOutcome Evaluate(bool isValidFood, bool wasTamed, bool sameOwnerAsFeeder)
    {
        if (!isValidFood)
            return TamingOutcome.Rejected;

        if (!wasTamed)
            return TamingOutcome.NewlyTamed;

        return sameOwnerAsFeeder ? TamingOutcome.AlreadyBonded : TamingOutcome.Retamed;
    }

    /// <summary>
    ///     Whether <paramref name="outcome"/> should register/update ownership and (re)point the critter's
    ///     follow-AI at the feeder. True for <see cref="TamingOutcome.NewlyTamed"/> and
    ///     <see cref="TamingOutcome.Retamed"/>; false otherwise.
    /// </summary>
    public static bool ShouldAssignOwner(TamingOutcome outcome)
    {
        return outcome is TamingOutcome.NewlyTamed or TamingOutcome.Retamed;
    }

    /// <summary>
    ///     Whether the fed item should be consumed (deleted) for <paramref name="outcome"/>. Rejected feeds
    ///     never consume the item; every other outcome does (taming or a happy top-up both "spend" the
    ///     treat).
    /// </summary>
    public static bool ShouldConsumeFood(TamingOutcome outcome)
    {
        return outcome != TamingOutcome.Rejected;
    }

    /// <summary>
    ///     Evaluates releasing a pet.
    /// </summary>
    public static bool EvaluateRelease(bool hasOwner)
    {
        return hasOwner;
    }

    /// <summary>
    ///     Evaluates toggling follow mode. Returns new IsFollowing state if tamed, or false if untamed.
    /// </summary>
    public static FollowToggleOutcome EvaluateFollowToggle(bool isTamed, bool currentlyFollowing)
    {
        if (!isTamed)
            return FollowToggleOutcome.Untamed;

        return currentlyFollowing ? FollowToggleOutcome.NowStaying : FollowToggleOutcome.NowFollowing;
    }

    /// <summary>
    ///     Evaluates grooming/cleaning a pet.
    /// </summary>
    public static CleanupOutcome EvaluateCleanup(bool isValidCleaner, bool isDirty)
    {
        if (!isValidCleaner)
            return CleanupOutcome.RejectedItem;

        return isDirty ? CleanupOutcome.Cleaned : CleanupOutcome.AlreadyClean;
    }

    /// <summary>
    ///     Evaluates granting a cosmetic ledger stamp.
    /// </summary>
    public static CosmeticStampOutcome EvaluateCosmeticStamp(bool isTamed, bool hasStamp)
    {
        if (!isTamed)
            return CosmeticStampOutcome.NoBond;

        return hasStamp ? CosmeticStampOutcome.AlreadyStamped : CosmeticStampOutcome.Stamped;
    }
}
