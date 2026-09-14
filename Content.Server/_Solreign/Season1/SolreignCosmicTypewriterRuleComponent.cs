using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     "The Cosmic Typewriter" — Solreign Season 1, "The Ledger Wakes", Beat 5 (see
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1). Admin-triggered station event: a
///     localized spatial rift opens in the plaza, A.U.D.I.T. cycles three PA lines over the event's
///     lifetime, and a massive detached typewriter key (the letter "S") — stamped with how far into the
///     round the rift tore open — is stashed in a random station locker. See
///     <see cref="SolreignCosmicTypewriterRule"/> for the behavior and
///     Resources/Prototypes/_Solreign/GameRules/season1.yml for the prototype.
/// </summary>
[RegisterComponent, Access(typeof(SolreignCosmicTypewriterRule))]
public sealed partial class SolreignCosmicTypewriterRuleComponent : Component
{
    /// <summary>The clue prop spawned and stashed at event start. See Entities/lore_papers.yml.</summary>
    [DataField]
    public EntProtoId CluePrototype = "SolreignPaperTypewriterKey";

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
