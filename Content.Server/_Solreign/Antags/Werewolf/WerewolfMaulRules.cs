namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Pure, unit-testable gating logic for the werewolf maul (spec
///     docs/specs/2026-07-11-werewolf-vampire-spec.md §3.3-3.5). No ECS, no I/O — <see cref="SolreignWerewolfSystem"/>
///     feeds it the clock and target state and applies whatever comes back (same pure-logic split as
///     <see cref="WerewolfStateMachine"/>; tested in Content.Tests/_Solreign/WerewolfStateMachineTests.cs).
///
///     Encodes two of the anti-grief invariants (spec §3.5) as one gate so the ECS handler never has to
///     get the ordering right itself:
///     - Rule 2: a target already Moon-Touched is immune to a re-maul (no knockdown-chaining a victim).
///     - Rule 3: a maul cooldown prevents corridor bowling of different victims back-to-back.
/// </summary>
public static class WerewolfMaulRules
{
    /// <summary>
    ///     Whether a maul may land right now. False if the wolf's cooldown hasn't elapsed, or if the
    ///     target is already Moon-Touched (immunity token, upstream <c>NonSpreaderZombie</c> idiom).
    /// </summary>
    public static bool CanMaul(TimeSpan now, TimeSpan nextMaulAllowed, bool targetIsMoonTouched)
    {
        if (targetIsMoonTouched)
            return false;

        return now >= nextMaulAllowed;
    }

    /// <summary>
    ///     Computes the game time before which the next maul is refused. Negative/zero cooldowns
    ///     (hostile YAML) clamp to "no cooldown" rather than throwing (house doctrine: sanitize, don't
    ///     throw) — the next maul is allowed immediately.
    /// </summary>
    public static TimeSpan NextMaulAllowedAt(TimeSpan now, float cooldownSeconds)
    {
        var cooldown = cooldownSeconds < 0f ? 0f : cooldownSeconds;
        return now + TimeSpan.FromSeconds(cooldown);
    }
}
