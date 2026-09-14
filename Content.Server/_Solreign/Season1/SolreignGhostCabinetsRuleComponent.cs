using Content.Shared.Dataset;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Bounded handled outcome of Beat 3's latest synchronous clue-delivery attempt.
///     This is runtime observation only; it does not declare Showrunner eligibility
///     or authorize execution.
/// </summary>
public enum SolreignGhostCabinetsDeliveryOutcome : byte
{
    NotAttempted,
    NoStation,
    TranscriptDatasetUnavailable,
    NoInsertableStorage,
    InsertFailed,
    DroppedAdjacent,
    Inserted,
}

/// <summary>
///     "Filing Cabinets from the Void" — Solreign Season 1, "The Ledger Wakes", Beat 3 (see
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1). Admin-triggered station event: spectral
///     filing cabinets protrude from the bulkheads, A.U.D.I.T. cycles three PA lines over the event's
///     lifetime, and a dusty manila folder (one of several randomized "archived transcript" flavors) is
///     stashed in a random station locker. See <see cref="SolreignGhostCabinetsRule"/> for the behavior and
///     Resources/Prototypes/_Solreign/GameRules/season1.yml for the prototype.
/// </summary>
[RegisterComponent, Access(typeof(SolreignGhostCabinetsRule))]
public sealed partial class SolreignGhostCabinetsRuleComponent : Component
{
    /// <summary>The clue prop spawned and stashed at event start. See Entities/lore_papers.yml.</summary>
    [DataField]
    public EntProtoId CluePrototype = "SolreignPaperGhostCabinetFolder";

    /// <summary>The pool of pre-written "archived transcript" flavor texts one is picked from at spawn time.
    /// See Resources/Prototypes/_Solreign/GameRules/season1.yml.</summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype> TranscriptDataset = "Season1GhostCabinetTranscripts";

    /// <summary>Seconds between each A.U.D.I.T. PA line — see <see cref="SolreignSeason1BeatSequencer"/>.</summary>
    [DataField]
    public float LineCadenceSeconds = 20f;

    /// <summary>Seconds accumulated since the event started. Server-side throttle state only, not networked.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>Index (0-based) of the last PA line announced so far. -1 before the first line fires.</summary>
    [ViewVariables]
    public int LastLineIndex = -1;

    /// <summary>
    ///     The latest bounded delivery observation. It carries no entity, location,
    ///     player, account, or arbitrary error detail.
    /// </summary>
    [ViewVariables]
    public SolreignGhostCabinetsDeliveryOutcome LastObservation;
}
