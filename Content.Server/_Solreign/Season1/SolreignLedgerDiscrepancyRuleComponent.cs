using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     "Preemptive Bookkeeping" — Solreign Season 1, "The Ledger Wakes", Beat 1 (see
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1). Admin-triggered station event: the
///     obituary boards start printing early, A.U.D.I.T. cycles three PA lines over the event's lifetime, and
///     a carbon-copied ledger slip naming a currently-alive crew member is stashed in a random station
///     locker. See <see cref="SolreignLedgerDiscrepancyRule"/> for the behavior and
///     Resources/Prototypes/_Solreign/GameRules/season1.yml for the prototype.
/// </summary>
[RegisterComponent, Access(typeof(SolreignLedgerDiscrepancyRule))]
public sealed partial class SolreignLedgerDiscrepancyRuleComponent : Component
{
    /// <summary>The clue prop spawned and stashed at event start. See Entities/lore_papers.yml.</summary>
    [DataField]
    public EntProtoId CluePrototype = "SolreignPaperLedgerSlip";

    /// <summary>Seconds between each A.U.D.I.T. PA line. Three narrative lines total; the first fires the
    /// instant the event starts, the remaining two cycle in on this cadence (see
    /// <see cref="SolreignSeason1BeatSequencer"/>).</summary>
    [DataField]
    public float LineCadenceSeconds = 20f;

    /// <summary>Seconds accumulated since the event started. Server-side throttle state only, not networked.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>Index (0-based) of the last PA line announced so far. -1 before the first line fires.</summary>
    [ViewVariables]
    public int LastLineIndex = -1;
}
