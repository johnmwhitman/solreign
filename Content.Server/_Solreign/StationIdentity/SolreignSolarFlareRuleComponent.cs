namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Configuration for <see cref="SolreignSolarFlareRule"/> — Solreign's own brief "weather-style"
///     solar flare moment (screen-border shader flash + a brief APC power flicker + Providence's
///     event_audit line), distinct from upstream's <c>SolarFlareRule</c> (which jams radio channels for
///     minutes, not a few seconds of brand sting).
/// </summary>
[RegisterComponent, Access(typeof(SolreignSolarFlareRule))]
public sealed partial class SolreignSolarFlareRuleComponent : Component
{
    /// <summary>How long the acid-green screen-border overlay
    /// (<c>Content.Shared._Solreign.FX.SolreignScreenFxEvent</c>) holds, in seconds. Clamped into
    /// [0.1, 5] by <c>SolreignScreenFxTiming.ClampDuration</c> regardless of what's configured here.</summary>
    [DataField]
    public float ScreenFxDurationSeconds = 3f;

    /// <summary>How many of the chosen station's powered APCs briefly flicker off (see
    /// <see cref="SolreignSolarFlareRule.Ended"/> for the guaranteed restore) when this station has
    /// NOT declared the event in its <c>SolreignStationMoodComponent.WeatherEventPrototypes</c>.</summary>
    [DataField]
    public int ApcsToFlicker = 2;

    /// <summary>Same as <see cref="ApcsToFlicker"/>, but used when the station HAS declared this event
    /// preferred — "the director prefers it here" reads as a more pronounced flicker.</summary>
    [DataField]
    public int PreferredApcsToFlicker = 4;

    /// <summary>The APCs this run actually flickered off, so <see cref="SolreignSolarFlareRule.Ended"/>
    /// can guarantee-restore exactly them (mirrors upstream <c>PowerGridCheckRule</c>'s
    /// Unpowered-list-then-restore idiom). Server-side run state only; not saved, not networked.</summary>
    [ViewVariables]
    public List<EntityUid> FlickeredApcs = new();
}
