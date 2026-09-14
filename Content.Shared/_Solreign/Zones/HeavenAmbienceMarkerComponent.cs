using Robust.Shared.GameStates;

namespace Content.Shared._Solreign.Zones;

/// <summary>
///     Marks an entity as contributing to the Heaven Zone ("Serenity &amp; Compliance Atrium")
///     ambient music, following the exact upstream idiom used by
///     <c>AmbientMusicMarkerMedicalComponent</c> / <c>AmbientMusicMarkerEngineeringComponent</c>
///     (see Content.Shared/Audio/AmbientMusic/) and the <c>AmbientMusicMarkerMedical</c> /
///     <c>AmbientMusicMarkerEngineering</c> entries in Resources/Prototypes/AmbientMusic/rules.yml.
///
///     ASSET GAP (do not wire the rule until this is closed): there is no dedicated calm/serene
///     Heaven Zone ambient track yet. The only Solreign track that currently namechecks this zone
///     is <c>SolreignHellZone</c> (Resources/Prototypes/AmbientMusic/_Solreign/solreign_ambient.yml,
///     sound path hell_zone.ogg — "Doom-inspired dark energy loop for the Heaven &amp; Hell zone"),
///     which is tonally wrong for a serene white-marble atrium. This marker exists so the wiring
///     is a one-line follow-up (add a <c>rules</c> entry with a <c>NearbyComponentsRule</c> against
///     this component, e.g. count: 3 / range: 5 to mirror NearMedical) once a calm-loop ogg is
///     generated for this zone (audio pipeline lane, task #25) — do NOT point this component at
///     hell_zone.ogg in the meantime, leave the rule unwired.
///
///     Placed directly on Heaven Zone fixtures in Resources/Prototypes/_Solreign/Entities/
///     heavenzone_props.yml (the healing fountain + the 3 resonance chimes), matching the
///     upstream idiom of attaching the marker component onto real furniture rather than a
///     standalone invisible marker entity (see e.g. AmbientMusicMarkerMedical on OperatingTable,
///     ChemDispenser, beds).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HeavenAmbienceMarkerComponent : Component
{
}
