using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Configuration for <see cref="SolreignSporeDriftRule"/> — a harmless drift of glowing spore
///     particles across a station, Solreign's other "weather-style" event (see
///     <see cref="SolreignSolarFlareRule"/> for the first).
/// </summary>
[RegisterComponent, Access(typeof(SolreignSporeDriftRule))]
public sealed partial class SolreignSporeDriftRuleComponent : Component
{
    /// <summary>The cosmetic spore entity spawned per drifting mote — see
    /// Resources/Prototypes/_Solreign/Entities/StationIdentity/spores.yml. Typed <see cref="EntProtoId"/>
    /// with a real compile-time default so <c>SolreignPrototypeIdIntegrityTest</c> catches a typo'd id
    /// here at build time (the "werewolf lesson" this test exists for).</summary>
    [DataField]
    public EntProtoId SporeEffectPrototype = "SolreignAmbientSpore";

    /// <summary>How many spores spawn when the chosen station has NOT declared this event preferred.</summary>
    [DataField]
    public int SporeCount = 6;

    /// <summary>How many spores spawn when the chosen station HAS declared this event preferred (see
    /// <see cref="SolreignStationMoodComponent.WeatherEventPrototypes"/>) — a denser drift reads as
    /// "the director leans into this map".</summary>
    [DataField]
    public int PreferredSporeCount = 14;

    /// <summary>Locale id for the small glow popup shown above each spawned spore. Null disables the
    /// popup entirely.</summary>
    [DataField]
    public LocId? GlowPopup = "solreign-spore-drift-glow-popup";
}
