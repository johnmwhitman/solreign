using Content.Shared.Mobs;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Why an absorb attempt was refused. Kept as a reason (not just a bool) so both the do-after
///     completion handler and the verb-offer gate can surface a specific popup (spec §3: "PG rule
///     ... never damages the victim" — refusal is always silent-to-the-victim, informative-to-the-
///     changeling).
/// </summary>
public enum AbsorbDenialReason
{
    /// <summary>Not denied — the absorb may proceed.</summary>
    None,

    /// <summary>Target is not <see cref="MobState.Critical"/> (spec §1: unconscious, not dead, not fully healthy).</summary>
    TargetNotCritical,

    /// <summary>This exact source entity has already been absorbed once (spec §3 anti-grief rule 2).</summary>
    TargetAlreadyAbsorbed,

    /// <summary>The changeling is still on cooldown from a previous successful absorb (spec §3 rule 3).</summary>
    OnCooldown,

    /// <summary><see cref="Content.Server._Solreign.Changeling.SolreignChangelingComponent.MaxKnownAliases"/> already reached (spec §3 rule 4).</summary>
    AliasLimitReached,
}

/// <summary>
///     Pure decision logic for the changeling identity mechanic (spec: docs/specs/2026-07-11-changeling-spec.md).
///     No <see cref="Robust.Shared.GameObjects.EntityUid"/>, no <see cref="Robust.Shared.IoC.IoCManager"/>, no
///     component/system access — everything here is plain values so it is directly NUnit-testable
///     (house pattern: <c>VampireThirstMath</c>, <c>WerewolfMaulRules</c>).
/// </summary>
public static class ChangelingIdentityRules
{
    /// <summary>
    ///     Evaluates every absorb precondition (spec §1 PG rule + §3 anti-grief rules 2-4). Callers must
    ///     re-run this at do-after COMPLETION time, not just when the verb is offered (spec §3 rule 6) —
    ///     same idiom as <c>VampireThirstMath.CanFeed</c> re-checked in <c>OnDonationDoAfter</c>.
    /// </summary>
    public static AbsorbDenialReason CanAbsorb(
        MobState targetState,
        bool alreadyAbsorbedThisSource,
        TimeSpan now,
        TimeSpan nextAbsorbAllowed,
        int knownAliasCount,
        int maxKnownAliases)
    {
        if (targetState != MobState.Critical)
            return AbsorbDenialReason.TargetNotCritical;

        if (alreadyAbsorbedThisSource)
            return AbsorbDenialReason.TargetAlreadyAbsorbed;

        if (now < nextAbsorbAllowed)
            return AbsorbDenialReason.OnCooldown;

        if (knownAliasCount >= maxKnownAliases)
            return AbsorbDenialReason.AliasLimitReached;

        return AbsorbDenialReason.None;
    }

    /// <summary>The next time an absorb may be attempted, starting from a successful one at <paramref name="now"/>.</summary>
    public static TimeSpan NextAbsorbAllowedAt(TimeSpan now, float cooldownSeconds)
    {
        return now + TimeSpan.FromSeconds(cooldownSeconds);
    }

    /// <summary>Whether Transform has anything to transform into.</summary>
    public static bool CanTransform(int knownAliasCount)
    {
        return knownAliasCount > 0;
    }

    /// <summary>
    ///     Which known alias Transform applies (spec §2.2: "the most recently absorbed" — MVP picks the
    ///     freshest identity; a multi-alias picker UI is backlog, spec §2.2). Returns -1 when there is
    ///     nothing to transform into; callers must check <see cref="CanTransform"/> first.
    /// </summary>
    public static int MostRecentAliasIndex(int knownAliasCount)
    {
        return knownAliasCount > 0 ? knownAliasCount - 1 : -1;
    }
}
