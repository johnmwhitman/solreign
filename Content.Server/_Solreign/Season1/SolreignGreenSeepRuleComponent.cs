using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     "The Green Seep" — Solreign Season 1, "The Ledger Wakes", Beat 2 (see
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1). Admin-triggered station event: acid-green
///     "Ink" seeps into maintenance, A.U.D.I.T. cycles three PA lines over the event's lifetime, and a melted
///     clipboard stamped with the round's live player count is stashed in a random station locker. See
///     <see cref="SolreignGreenSeepRule"/> for the behavior and
///     Resources/Prototypes/_Solreign/GameRules/season1.yml for the prototype.
/// </summary>
[RegisterComponent, Access(typeof(SolreignGreenSeepRule))]
public sealed partial class SolreignGreenSeepRuleComponent : Component
{
    /// <summary>The clue prop spawned and stashed at event start. See Entities/lore_papers.yml.</summary>
    [DataField]
    public EntProtoId CluePrototype = "SolreignPaperInkClipboard";

    /// <summary>Seconds between each A.U.D.I.T. PA line — see <see cref="SolreignSeason1BeatSequencer"/>.</summary>
    [DataField]
    public float LineCadenceSeconds = 20f;

    /// <summary>Seconds accumulated since the event started. Server-side throttle state only, not networked.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>Index (0-based) of the last PA line announced so far. -1 before the first line fires.</summary>
    [ViewVariables]
    public int LastLineIndex = -1;
}
