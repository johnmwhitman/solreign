using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Ents;

/// <summary>
///     Marks a normal-looking, static decorative tree (see the <c>SolreignDormantEnt</c> entity
///     prototype, parented off upstream <c>BaseTree</c> — Resources/Prototypes/Entities/Objects/
///     Decoration/flora.yml) as a rare candidate for waking up into a fully playable Ent ghost role.
///     <see cref="SolreignDormantEntSystem"/> periodically rolls a low-probability check and, on a
///     hit, deletes this entity and spawns <see cref="AwakenedPrototype"/> in its place.
///
///     Scheduling shape is modeled on <see cref="Content.Server._Solreign.Effects.SolreignPeriodicEffectComponent"/>
///     (a precomputed <see cref="NextCheckTime"/> compared against <c>IGameTiming.CurTime</c> each
///     tick, randomness only rolled when (re)scheduling — see
///     <see cref="Content.Server._Solreign.Effects.PeriodicEffectTiming"/>, reused directly here).
///     This is deliberately a separate component rather than a repurposed SolreignPeriodicEffect:
///     that primitive's doc comment promises it is "purely cosmetic: no gameplay state is altered",
///     and this one very much does alter gameplay state (deletes an entity, spawns a takeover-able
///     mob). Server-only and unsaved: no gameplay-relevant client behavior depends on this timer.
/// </summary>
[RegisterComponent, Access(typeof(SolreignDormantEntSystem))]
public sealed partial class SolreignDormantEntComponent : Component
{
    /// <summary>Minimum seconds between awaken-chance rolls (lower edge of the random window).</summary>
    [DataField]
    public float MinCheckIntervalSeconds = 300f;

    /// <summary>Maximum seconds between awaken-chance rolls (upper edge of the random window).</summary>
    [DataField]
    public float MaxCheckIntervalSeconds = 900f;

    /// <summary>
    ///     Probability in [0, 1] that any given roll wakes the tree. Kept low by default —
    ///     this is meant to stay rare across a whole round, not a countdown to a guaranteed event.
    /// </summary>
    [DataField]
    public float AwakenChance = 0.05f;

    /// <summary>The Ent prototype spawned at this tree's coordinates when it wakes.</summary>
    [DataField]
    public EntProtoId AwakenedPrototype = "SolreignMobAwakenedTree";

    /// <summary>Creak sound played (PVS) at the tree's position at the moment it wakes.</summary>
    [DataField]
    public SoundSpecifier? CreakSound;

    /// <summary>Locale id for the popup shown above the tree when it wakes (e.g. "The tree stirs.").</summary>
    [DataField]
    public LocId? AwakenPopup;

    /// <summary>
    ///     Game time of the next awaken-chance roll. Set on map-init and after every roll; never
    ///     rolled per-tick. Server-side scheduling state only — not saved, not networked.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextCheckTime;
}
