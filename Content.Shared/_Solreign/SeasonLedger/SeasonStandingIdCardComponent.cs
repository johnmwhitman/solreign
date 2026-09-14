namespace Content.Shared._Solreign.SeasonLedger;

/// <summary>
///     Stamped onto a crewmember's ID card (server-side, in lockstep with <see cref="SeasonTitleComponent"/>
///     being stamped on their mob — see <c>SeasonLedgerSystem.StampIdCardStanding</c>) so that OTHER players
///     who examine the card, or the PDA holding it, see a compact standing line — not just the card's owner
///     on examining their own body. Purely a snapshot copy of fields <see cref="SeasonTitleComponent"/>
///     already carries: no new tracked stat, no DB schema change, and examine text is built server-side so
///     this does not need to be networked.
/// </summary>
[RegisterComponent]
public sealed partial class SeasonStandingIdCardComponent : Component
{
    /// <summary>Snapshot of the owner's earned title at last stamp (e.g. "Brand Ambassador").</summary>
    [DataField]
    public string Title = string.Empty;

    /// <summary>Snapshot flavor word for career standing (see <c>PersonnelFileRules.DescribeCareerStanding</c>).</summary>
    [DataField]
    public string Standing = string.Empty;
}
