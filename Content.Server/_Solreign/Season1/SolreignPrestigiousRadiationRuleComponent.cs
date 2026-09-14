using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     "Prestigious Radiation" — Solreign Season 1, "The Ledger Wakes", Beat 4 (see
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1). Admin-triggered station event:
///     crew who have earned a high enough Season Ledger rank begin emitting a faint acid-green
///     bioluminescence, A.U.D.I.T. cycles three PA lines over the event's lifetime, and a hand-engraved
///     employee-of-the-month plaque is stashed in a random station locker. See
///     <see cref="SolreignPrestigiousRadiationRule"/> for the behavior and
///     Resources/Prototypes/_Solreign/GameRules/season1.yml for the prototype.
/// </summary>
[RegisterComponent, Access(typeof(SolreignPrestigiousRadiationRule))]
public sealed partial class SolreignPrestigiousRadiationRuleComponent : Component
{
    /// <summary>The clue prop spawned and stashed at event start. Static engraved text — no per-round
    /// stamping — so it's baked straight into the prototype's Paper component, the same idiom as the
    /// Auditor's Memos in Entities/lore_papers.yml. See that file.</summary>
    [DataField]
    public EntProtoId CluePrototype = "SolreignPaperEmployeePlaque";

    /// <summary>Minimum Season Ledger <c>RankIndex</c> a crew member needs to start glowing — 4 is
    /// "Director" and up (see <c>Content.Server._Solreign.SeasonLedger.RankRules.CorporateRank</c>).
    /// Exposed as a DataField so an admin can retune "how high is 'high-ranked'" per-event without a
    /// code change. See <see cref="SolreignSeason1PrestigeRules"/> for the (unit-tested) threshold check.</summary>
    [DataField]
    public int HighRankThreshold = 4;

    /// <summary>Seconds between each A.U.D.I.T. PA line — see <see cref="SolreignSeason1BeatSequencer"/>.</summary>
    [DataField]
    public float LineCadenceSeconds = 20f;

    /// <summary>Seconds accumulated since the event started. Server-side throttle state only, not networked.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>Index (0-based) of the last PA line announced so far. -1 before the first line fires.</summary>
    [ViewVariables]
    public int LastLineIndex = -1;

    /// <summary>Crew mobs this event lit up at <c>Started</c> — tracked so <c>Ended</c> strips the
    /// bioluminescence back off only from entities this event itself lit, never touching a light source
    /// that already existed on a mob (a held flashlight, night-vision goggles, etc.).</summary>
    [ViewVariables]
    public List<EntityUid> Glowing = new();
}
