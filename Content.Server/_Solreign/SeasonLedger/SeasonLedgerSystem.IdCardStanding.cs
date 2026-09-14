using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Content.Shared.PDA;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Surfaces the Season Ledger's standing where OTHER crewmembers actually glance in-round: a
///     player's ID card (and the PDA holding it), not only the personnel-file flourish that fires on
///     deliberately examining their body (see <see cref="SeasonLedgerSystem.OnExamined"/>). Churn
///     insight: cross-round ledger persistence only changes how OTHER players treat someone THIS round
///     if it is visible somewhere people routinely look at each other anyway — door/Security ID checks,
///     handing over a card — not buried behind a deliberate "examine crewmate" action.
///
///     Additive only: reuses the exact snapshot <see cref="SeasonTitleComponent"/> already carries once
///     <see cref="SeasonLedgerSystem.Update"/> drains a pending title load, so this never touches the
///     ledger DB a second time, adds no schema, and tracks no new stat.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    [Dependency] private SharedIdCardSystem _idCard = default!;

    private void InitializeIdCardStanding()
    {
        SubscribeLocalEvent<SeasonStandingIdCardComponent, ExaminedEvent>(OnStandingCardExamined);

        // A crewmember's ID typically lives INSIDE their PDA (the PDA occupies the "id" equipment
        // slot), so most in-round "check someone's ID" interactions examine their PDA, not a bare
        // card. Relay straight to the contained card's standing line without touching PdaSystem.cs.
        SubscribeLocalEvent<PdaComponent, ExaminedEvent>(OnStandingPdaExamined);
    }

    /// <summary>
    ///     Mirrors the freshly-computed Title/RankIndex from a mob's <see cref="SeasonTitleComponent"/>
    ///     onto whatever ID card that mob is currently carrying — bare in the "id" slot, or nested
    ///     inside a PDA — so the card's own examine text can show them without a second DB round-trip.
    ///     Called from <see cref="SeasonLedgerSystem.Update"/> right after the mob's component fields
    ///     are set, i.e. on spawn and on any later refresh of the same mob.
    /// </summary>
    private void StampIdCardStanding(EntityUid mob, SeasonTitleComponent comp, string previousTitle)
    {
        // An admin-granted title (community rewards) stamps even a zero-history account's card — same
        // reasoning as the personnel-file gate in OnExamined.
        if (!IdCardStandingRules.ShouldStamp(
                comp.Tours,
                comp.RankIndex,
                comp.HrPoints,
                comp.HasAdminGrant,
                comp.HasGoldenNamePerk))
        {
            // Un-stamp on the way down: if a fresh account's grant was just revoked, their card must stop
            // displaying the revoked reward (PushStandingMarkup skips empty titles). TryFindIdCard prefers
            // the ACTIVE HELD item, which may be someone ELSE's card — so only clear a stamp that matches
            // the title THIS mob just lost, never a foreign card's own standing. A card that can't be
            // reached this way (e.g. own PDA card while holding another ID) stays stale until round end —
            // cards are per-round entities, so the staleness is bounded to the current round.
            if (previousTitle.Length > 0 &&
                _idCard.TryFindIdCard(mob, out var carried) &&
                TryComp<SeasonStandingIdCardComponent>(carried, out var stale) &&
                stale.Title == previousTitle)
            {
                stale.Title = string.Empty;
            }

            return;
        }

        if (!_idCard.TryFindIdCard(mob, out var idCard))
            return;

        var standing = EnsureComp<SeasonStandingIdCardComponent>(idCard);
        standing.Title = comp.Title;
        standing.Standing = PersonnelFileRules.DescribeCareerStanding(comp.RankIndex);
    }

    private void OnStandingCardExamined(EntityUid uid, SeasonStandingIdCardComponent comp, ExaminedEvent args)
    {
        PushStandingMarkup(comp, args);
    }

    private void OnStandingPdaExamined(EntityUid uid, PdaComponent pda, ExaminedEvent args)
    {
        if (pda.ContainedId is not { } containedId
            || !TryComp<SeasonStandingIdCardComponent>(containedId, out var comp))
            return;

        PushStandingMarkup(comp, args);
    }

    private void PushStandingMarkup(SeasonStandingIdCardComponent comp, ExaminedEvent args)
    {
        if (string.IsNullOrEmpty(comp.Title))
            return;

        args.PushMarkup(Loc.GetString("solreign-id-card-standing-examine",
            ("title", comp.Title),
            ("standing", comp.Standing)));
    }
}
