namespace Content.Shared._Solreign.SeasonLedger;

/// <summary>
///     Stamped onto a player's mob at spawn, populated from the Season Ledger by their NetUserId.
///     Holds the earned title and accumulated tour count, surfaced on examine. Server-authoritative —
///     examine text is built server-side, so this does not need to be networked.
/// </summary>
[RegisterComponent]
public sealed partial class SeasonTitleComponent : Component
{
    /// <summary>The earned title (e.g. "Brand Ambassador"). Empty until computed from ledger stats.</summary>
    [DataField]
    public string Title = string.Empty;

    /// <summary>Number of completed rounds ("tours") this account has on record for the season.</summary>
    [DataField]
    public int Tours;

    /// <summary>The earned career rank (e.g. "Director"). Empty until computed from career ledger stats.</summary>
    [DataField]
    public string Rank = string.Empty;

    /// <summary>Ordinal index of the earned rank (0 = Probationary Asset … 10 = Founder Emeritus).</summary>
    [DataField]
    public int RankIndex;

    /// <summary>
    ///     Lifetime HR Points (Beta Feedback 01, Lane B — the ALWAYS-CUMULATIVE model): career total across
    ///     every season, never reduced by a season bump. Surfaced on examine alongside Title/Rank.
    /// </summary>
    [DataField]
    public int HrPoints;

    /// <summary>
    ///     True when <see cref="Title"/> is an admin-granted title (community rewards program) masking the
    ///     earned one. Lets the examine/ID-card "has this account done anything" gates surface a granted
    ///     title even on a zero-history account.
    /// </summary>
    [DataField]
    public bool HasAdminGrant;

    /// <summary>
    ///     True after a verified Director response grants the cosmetic GoldenName perk. Kept
    ///     separately so a later asynchronous title load can reapply the prefix idempotently.
    /// </summary>
    [DataField]
    public bool HasGoldenNamePerk;
}
