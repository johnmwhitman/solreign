using System;
using Content.Server._Solreign.Providence;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     One projected Echo entity — the round-local physical representation of an already-claimed
///     <c>first_death</c> ledger row (Echoes of the Departed, v14, EOD-spec §8). Mirrors
///     <see cref="SolreignMarkComponent"/> almost exactly, with one difference: a first death is
///     already a station-wide public disclosure by the time it can ever become an Echo (the
///     eulogy, the crypt plaque, and the Discord obituary all name the player publicly at claim
///     time), so there is no owner/stranger split here — every field cached on this component
///     renders identically to every examiner.
///
///     Attached in CODE at projection time, never in YAML, and never networked. The entity is
///     disposable by design — deleting it costs one round; the <c>first_death</c> row is the
///     truth and the next <see cref="Content.Server.Station.Events.StationPostInitEvent"/>
///     re-projects it.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignEchoComponent : Component
{
    /// <summary>The character name as it was recorded at the moment of the first death.</summary>
    public string CharacterName = string.Empty;

    /// <summary>The classified cause of death, parsed from the ledger row at projection time.</summary>
    public FirstDeathCause Cause;

    /// <summary>Tours completed at the time of death — feeds <see cref="FirstDeathEpitaphPicker.Pick"/>.</summary>
    public int ToursAtDeath;

    /// <summary>Title held at the time of death — feeds <see cref="FirstDeathEpitaphPicker.Pick"/>.</summary>
    public string TitleAtDeath = string.Empty;

    /// <summary>The visual stage (0..<see cref="MarkAgeRules.MaxStage"/>) this entity was projected AT — frozen for the round.</summary>
    public int Stage;
}
