using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     "The Final Balance Sheet" — Solreign Season 1, "The Ledger Wakes", Beat 6, THE SEASON FINALE HOOK
///     (see docs/research/2026-07-11-season1-narrative-bible.md Section 1). Admin-triggered station
///     event: gravity fluctuates like turning pages, A.U.D.I.T.'s voice glitches through three PA lines
///     ending on an unresolved cliffhanger, and a heavy leather-bound ledger book — its final page
///     listing every currently-attached crew member with a green "AUDITED & APPROVED FOR RECYCLING"
///     checkbox — is stashed in a random station locker. Deliberately does NOT resolve: Beat 6's payoff
///     is the live Grand Opening event, not anything in this station event. See
///     <see cref="SolreignFinalBalanceSheetRule"/> for the behavior and
///     Resources/Prototypes/_Solreign/GameRules/season1.yml for the prototype.
/// </summary>
[RegisterComponent, Access(typeof(SolreignFinalBalanceSheetRule))]
public sealed partial class SolreignFinalBalanceSheetRuleComponent : Component
{
    /// <summary>The clue prop spawned and stashed at event start. See Entities/lore_papers.yml.</summary>
    [DataField]
    public EntProtoId CluePrototype = "SolreignPaperLedgerVolumeOne";

    /// <summary>Hard cap on how many crew names get listed on the book's final page, so an unusually
    /// full round can't blow up the Paper component's text past a readable length.</summary>
    [DataField]
    public int MaxRosterEntries = 40;

    /// <summary>Seconds between each A.U.D.I.T. PA line — see <see cref="SolreignSeason1BeatSequencer"/>.
    /// Line 3 is the season's cliffhanger and, unlike Beats 1-3, is deliberately never followed by a
    /// resolution line — the event simply runs out its duration with line 3 still standing.</summary>
    [DataField]
    public float LineCadenceSeconds = 20f;

    /// <summary>Seconds accumulated since the event started. Server-side throttle state only, not networked.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>Index (0-based) of the last PA line announced so far. -1 before the first line fires.</summary>
    [ViewVariables]
    public int LastLineIndex = -1;
}
