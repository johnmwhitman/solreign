using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Configuration for <see cref="SolreignAcidStormRule"/> — the third "weather-style" Solreign
///     station event (see <see cref="SolreignSolarFlareRule"/> and <see cref="SolreignSporeDriftRule"/>
///     for the first two). Primarily atmospheric: an opening screen-fx sting (reusing the same brand
///     hook the Solar Flare uses), Providence's staged <c>EventAcidStorm</c> voice line, and a single
///     scatter of acid-green ambient motes (reusing <see cref="SolreignSporeParticleComponent"/>
///     verbatim — same component/system as Spore Drift's spores, just a different, longer-lived,
///     differently-tinted entity prototype). Zero damage this wave — no "breach"/exterior-hazard
///     concept exists anywhere in this codebase to hook a mild hazard into yet; see this class's own
///     wow-wiring receipt for the deferred-hazard note, same documented-scope-limit idiom
///     <see cref="SolreignSolarFlareRuleComponent"/>/<see cref="SolreignSporeDriftRuleComponent"/>
///     already use for their own deferred pieces.
/// </summary>
[RegisterComponent, Access(typeof(SolreignAcidStormRule))]
public sealed partial class SolreignAcidStormRuleComponent : Component
{
    /// <summary>How long the opening acid-green screen-border sting
    /// (<c>Content.Shared._Solreign.FX.SolreignScreenFxEvent</c>) holds, in seconds. Clamped into
    /// [0.1, 5] by <c>SolreignScreenFxTiming.ClampDuration</c> regardless of what's configured here — a
    /// one-time "it begins" hit, not a sustained ambient effect (the shader is architected as a
    /// momentary sting; the 60-120s atmosphere comes from the particle scatter and the station
    /// announcement text instead, not from re-triggering this repeatedly).</summary>
    [DataField]
    public float ScreenFxDurationSeconds = 4f;

    /// <summary>The cosmetic acid-mote entity spawned per drift particle — see
    /// Resources/Prototypes/_Solreign/Entities/StationIdentity/spores.yml. Typed <see cref="EntProtoId"/>
    /// with a real compile-time default so <c>SolreignPrototypeIdIntegrityTest</c> catches a typo'd id
    /// here at build time (the "werewolf lesson" this test exists for) — same idiom as
    /// <see cref="SolreignSporeDriftRuleComponent.SporeEffectPrototype"/>.</summary>
    [DataField]
    public EntProtoId MoteEffectPrototype = "SolreignAcidMote";

    /// <summary>How many motes spawn when the chosen station has NOT declared this event preferred.</summary>
    [DataField]
    public int MoteCount = 10;

    /// <summary>How many motes spawn when the chosen station HAS declared this event preferred (see
    /// <see cref="SolreignStationMoodComponent.WeatherEventPrototypes"/>) — a denser drift reads as
    /// "the director leans into this map", same idiom as Spore Drift's own preferred count.</summary>
    [DataField]
    public int PreferredMoteCount = 20;

    /// <summary>Locale id for the small popup shown above each spawned mote. Null disables the popup
    /// entirely.</summary>
    [DataField]
    public LocId? GlowPopup = "solreign-acid-storm-glow-popup";
}
