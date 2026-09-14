namespace Content.Server._Solreign.Season1;

/// <summary>
///     Shared PA identity for Solreign Season 1, "The Ledger Wakes" (station-event beats 1-3, see
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1). Every beat's announcements are spoken
///     as the station AI, A.U.D.I.T. — not "Solreign HR", which is the Corporate Ladder's separate PA voice
///     (see <see cref="Corporate.SolreignCorporateRuleSystem"/>). Kept as one small shared constant pair,
///     rather than three independently-tuned colors (the convention used by the antag rules), because these
///     three beats are explicitly meant to read as a single escalating voice across the season arc.
/// </summary>
public static class SolreignSeason1Audit
{
    /// <summary>
    ///     Sender label stamped on every Season 1 A.U.D.I.T. PA line (loc-string sweep, Phase2 A4:
    ///     was a bare hardcoded literal).
    /// </summary>
    public static string Sender => Loc.GetString("solreign-season1-audit-sender");

    /// <summary>Solreign acid green — matches the Season Ledger's own ceremony color.</summary>
    public static readonly Color Color = Color.FromHex("#39FF14");
}
