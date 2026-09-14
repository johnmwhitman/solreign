using Robust.Shared.Audio;

namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     BETA FEEDBACK item 4 (docs/BETA-FEEDBACK-01.md Lane A, "wrist-mounted post-apoc organizer —
///     real functionality"): marks the SolreignEgg15 wrist organizer as giving a
///     health-analyzer-style self readout on use, reusing the same data sources
///     <c>Content.Server.Medical.HealthAnalyzerSystem</c> reads (DamageableComponent,
///     MobThresholdSystem) rather than reimplementing its DoAfter/BoundUserInterface machinery —
///     this is a one-shot self-scan popup, not a scanning tool aimed at other people.
///
///     Server-only on purpose, same reasoning as <c>SolreignMartialArtistComponent</c>: the whole
///     effect is a popup + sound through existing shared systems, so nothing here needs its own
///     networking.
/// </summary>
[RegisterComponent, Access(typeof(SolreignWristOrganizerSystem))]
public sealed partial class SolreignWristOrganizerComponent : Component
{
    /// <summary>Seconds between readouts, so mashing use-in-hand can't spam the popup.</summary>
    [DataField]
    public TimeSpan ReadoutCooldown = TimeSpan.FromSeconds(3);

    /// <summary>Distinct sound for the readout — reuses the same scan cue HealthAnalyzerComponent's ScanningEndSound plays.</summary>
    [DataField]
    public SoundSpecifier ReadoutSound = new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");

    /// <summary>Game time at which the next readout may fire. Server-side scheduling state only — not saved, not networked.</summary>
    [ViewVariables]
    public TimeSpan NextReadoutTime;

    /// <summary>
    ///     Delight-eggs batch (feat/delight-eggs): how many successful readouts this specific unit has
    ///     given out so far. Server-side scheduling state only — not saved, not networked, same as
    ///     <see cref="NextReadoutTime"/>; naturally resets across rounds because these are per-round
    ///     map/spawn entities, so no explicit round-reset hook is needed.
    /// </summary>
    [ViewVariables]
    public int TimesUsedThisRound;
}
