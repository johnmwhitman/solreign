namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Shared PA identity for both mini-boss events (The Auditor Prime, Specimen Zero). Kept as one
///     small shared constant pair — same idiom as <c>Season1.SolreignSeason1Audit</c> — since arrival
///     warnings, defeat announcements, and Ledger-trace logging all read as a single "Solreign Ops"
///     voice regardless of which mini-boss fired.
/// </summary>
public static class SolreignMiniBossAudit
{
    /// <summary>
    ///     Sender label stamped on every mini-boss PA line (loc-string sweep, Phase2 A4: was a bare
    ///     hardcoded literal).
    /// </summary>
    public static string Sender => Loc.GetString("solreign-miniboss-sender");

    /// <summary>Solreign acid green — matches the Season Ledger's own ceremony color (#39FF14).</summary>
    public static readonly Color Color = Color.FromHex("#39FF14");
}
