using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     CLEAN SEAM for a future directive-outcome/fax feature (council C1 deltav-008 lineage, v14
///     wave-1 #3: "integrates with feat/directives-fax IF merged by then — design the seam loosely,
///     degrade gracefully if the fax feature is absent/dormant"). Station Audits raises this once at
///     shift end and reads back whatever a subscriber reported; if nothing answers (the feature
///     hasn't landed yet, or is dormant behind its own kill switch) every field stays at its default
///     and the audit renders an honest "outcome not on record" line rather than fabricating a verdict.
///
///     Zero compile-time coupling by construction: this event is defined and owned by Station Audits,
///     not by the directive system, so Station Audits never references a directives-fax type that may
///     not exist yet. A future directive-outcome system participates by subscribing
///     <c>SubscribeLocalEvent&lt;SolreignDirectiveOutcomeQueryEvent&gt;</c> and calling
///     <see cref="Report"/> exactly once, from wherever it already knows this shift's directive
///     resolved (win/loss/quota-met/etc).
///
///     Deliberately carries no directive identity of its own — Station Audits already resolves
///     *which* directive was on file this shift by reading <c>StationDirectiveRuleComponent</c>
///     directly (a real, already-shipped, purely-cosmetic system); this event exists solely for the
///     one piece that system does NOT compute today: whether the shift's directive was fulfilled.
/// </summary>
public sealed class SolreignDirectiveOutcomeQueryEvent : EntityEventArgs
{
    /// <summary>True once some subscriber has called <see cref="Report"/>.</summary>
    public bool Reported { get; private set; }

    /// <summary>Only meaningful when <see cref="Reported"/> is true.</summary>
    public bool Fulfilled { get; private set; }

    /// <summary>
    ///     Called by a directive-outcome subscriber to answer the query. Idempotent-last-writer: if
    ///     more than one subscriber ever reports (should not happen with a single directives feature
    ///     active), the last call wins rather than throwing — the audit is flavor, not a ledger of
    ///     record, so failing soft here is the right default.
    /// </summary>
    public void Report(bool fulfilled)
    {
        Reported = true;
        Fulfilled = fulfilled;
    }
}
