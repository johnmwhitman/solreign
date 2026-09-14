using Robust.Shared.GameStates;

namespace Content.Shared.Audio.AmbientMusic;

/// <summary>
/// Marks an entity as contributing to Sublevel H ("Doom Hell Zone") ambient music.
/// Stamped per-instance on hell-wing decor placed in Resources/Maps/_Solreign/zone_hell.yml —
/// see Resources/Prototypes/AmbientMusic/rules.yml (NearHellZone) and
/// Resources/Prototypes/AmbientMusic/_Solreign/solreign_ambient.yml (SolreignHellZone).
/// Mirrors the upstream AmbientMusicMarkerMedical/Engineering pattern exactly.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AmbientMusicMarkerHellZoneComponent : Component
{

}
