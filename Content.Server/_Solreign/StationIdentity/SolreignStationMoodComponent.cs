using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Solreign's per-station "ambient identity" declaration — YAML-attached to a station entity via
///     its gameMap prototype's <c>stations: ... components:</c> override list (same slot as upstream's
///     <c>StationNameSetup</c>/<c>StationJobs</c>; see <c>Resources/Prototypes/Maps/_Solreign/solreign_oasis.yml</c>).
///     Read and driven entirely by <see cref="SolreignStationMoodSystem"/>. See that system's doc
///     comment for the full mechanic breakdown (day/night ambient lerp, weather-event preference,
///     ambient mood popups).
/// </summary>
[RegisterComponent, Access(typeof(SolreignStationMoodSystem))]
public sealed partial class SolreignStationMoodComponent : Component
{
    /// <summary>Whether this station's ambient light should slowly lerp between <see cref="ColorA"/>
    /// and <see cref="ColorB"/> over <see cref="CyclePeriodSeconds"/>.</summary>
    [DataField]
    public bool DayNightCycle;

    /// <summary>Full back-and-forth cycle length, in seconds (A -> B -> A).</summary>
    [DataField]
    public float CyclePeriodSeconds = 1800f;

    /// <summary>
    ///     First ambient-color endpoint (sRGB — the natural space for a YAML hex color). Converted
    ///     through <see cref="Color.FromSrgb"/> before being written to
    ///     <c>MapLightComponent.AmbientLightColor</c>, which expects linear-light values.
    /// </summary>
    [DataField]
    public Color ColorA = Color.FromHex("#2B2B44");

    /// <summary>Second ambient-color endpoint (sRGB) — see <see cref="ColorA"/>.</summary>
    [DataField]
    public Color ColorB = Color.FromHex("#D8D8F2");

    /// <summary>
    ///     Map-scoped weather-style station events this station prefers — read by
    ///     <see cref="SolreignSolarFlareRule"/>/<see cref="SolreignSporeDriftRule"/> (via
    ///     <see cref="SolreignStationMoodSystem.IsWeatherEventPreferred"/>) to lean harder into their
    ///     effect when this station specifically calls them out, and staged for a future
    ///     event-director/scheduler wave to read wholesale (see <see cref="StationMoodMath.PickWeatherEvent"/>'s
    ///     doc comment). Typed <see cref="EntProtoId"/> so the engine's own YAML-time validation catches
    ///     a typo'd/never-built event id here (same "werewolf lesson" guard
    ///     <c>SolreignPrototypeIdIntegrityTest</c> exists for) rather than silently no-opping.
    /// </summary>
    [DataField]
    public List<EntProtoId> WeatherEventPrototypes = new();

    /// <summary>Rare, localized ambient flavor lines — a random one is popped up at a random point on
    /// the station every <see cref="MoodPopupMinIntervalSeconds"/>-<see cref="MoodPopupMaxIntervalSeconds"/>.
    /// Empty (default) means no ambient popups fire at all.</summary>
    [DataField]
    public List<LocId> MoodPopupLines = new();

    /// <summary>Lower edge of the mood-popup random interval window, in seconds.</summary>
    [DataField]
    public float MoodPopupMinIntervalSeconds = 900f;

    /// <summary>Upper edge of the mood-popup random interval window, in seconds.</summary>
    [DataField]
    public float MoodPopupMaxIntervalSeconds = 2400f;

    /// <summary>Game time this station's mood tracking began (captured at <c>StationPostInitEvent</c>,
    /// i.e. effectively round/station start) — <see cref="DayNightCycle"/>'s elapsed-time reference.
    /// Server-side scheduling state only; not saved, not networked.</summary>
    [ViewVariables]
    public TimeSpan CycleStartTime;

    /// <summary>Game time the ambient color is next allowed to update (throttled well below per-tick
    /// to avoid spamming <c>MapLightComponent</c> network state deltas for an inherently slow lerp).</summary>
    [ViewVariables]
    public TimeSpan NextColorUpdateTime;

    /// <summary>Game time the next mood popup fires. Zero until the first schedule, same "unscheduled
    /// sentinel" idiom as <c>SolreignPeriodicEffectComponent.NextFireTime</c>.</summary>
    [ViewVariables]
    public TimeSpan NextMoodPopupTime;

    /// <summary>The map entity backing this station's first grid, resolved once at
    /// <c>StationPostInitEvent</c> — see <see cref="SolreignStationMoodSystem"/>'s doc comment for why
    /// "first grid's map" is the deliberate scope limit here.</summary>
    [ViewVariables]
    public EntityUid? MapUid;

    /// <summary>The map's ambient light color as found before this system started overriding it,
    /// restored on <c>ComponentShutdown</c> — same restore-on-shutdown idiom as upstream's
    /// <c>SharedLightCycleSystem</c>.</summary>
    [ViewVariables]
    public Color OriginalAmbientColor;
}
