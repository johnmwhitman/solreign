namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Pure decision logic for the changeling ROUND-INTEGRATION layer (spec §6 follow-up:
///     docs/specs/2026-07-11-changeling-spec.md) — the "collect N identities" objective's progress
///     math and target safety clamp. No <see cref="Robust.Shared.GameObjects.EntityUid"/>, no IoC, no
///     component access — plain values, directly NUnit-testable (house pattern:
///     <c>ChangelingIdentityRules</c>, <c>VampireThirstMath</c>, <c>WerewolfMaulRules</c>).
///
///     The actual WHO-gets-picked-as-a-changeling selection logic is upstream's own
///     <c>AntagSelectionSystem</c>/<c>LinearAntagCount</c> machinery, entirely data-driven via
///     <c>Resources/Prototypes/_Solreign/GameRules/changeling.yml</c> — that machinery already has
///     its own upstream test coverage and isn't re-implemented or re-tested here (same "ECS wiring
///     isn't unit-testable without a full game harness" call the ability-skeleton spec made for
///     verbs/do-afters, extended to the engine's own antag-picking code). What genuinely IS new,
///     Solreign-authored gating logic — and so belongs here — is whether a rolled objective target is
///     even completable given the antag's own hard cap.
/// </summary>
public static class SolreignChangelingRoundRules
{
    /// <summary>
    ///     Clamps an objective's rolled target so it can never exceed the changeling's own
    ///     <see cref="SolreignChangelingComponent.MaxKnownAliases"/> hard cap (spec §3 rule 4) — an
    ///     un-clamped target above the cap would be a permanently uncompletable objective. Also
    ///     floors at 1 so a target of 0 (or a misconfigured negative) never counts as "trivially
    ///     already met" before a single identity has been absorbed.
    /// </summary>
    public static int ClampObjectiveTarget(int target, int maxKnownAliases)
    {
        if (maxKnownAliases < 1)
            maxKnownAliases = 1;

        if (target < 1)
            target = 1;

        return target > maxKnownAliases ? maxKnownAliases : target;
    }

    /// <summary>
    ///     Fractional progress toward the "collect N identities" objective, clamped to [0, 1]. A
    ///     target of zero-or-less is treated as already complete (mirrors upstream's own
    ///     <c>ChangelingObjectiveSystem.GetProgress</c> divide-by-zero guard).
    /// </summary>
    public static float GetObjectiveProgress(int knownAliasCount, int target)
    {
        if (target <= 0)
            return 1f;

        if (knownAliasCount <= 0)
            return 0f;

        if (knownAliasCount >= target)
            return 1f;

        return (float)knownAliasCount / target;
    }
}
