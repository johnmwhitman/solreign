using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Effects;

/// <summary>
///     Solreign's reusable ambient-FX primitive: at a random interval inside a configured window this entity
///     spawns a cosmetic effect entity at its own position, plays a sound, and/or shows a popup. Fully
///     YAML-declarable — see <c>Resources/Prototypes/_Solreign/Entities/delighters/unicorn.yml</c> for the
///     proof-of-concept ("corporate unicorn": rainbow burst + parp + compliance popup every 2-10 minutes).
///
///     Modeled on upstream <c>SpamEmitSoundComponent</c> (Content.Shared/Sound/Components), generalized to
///     also spawn an effect prototype. Server-only and purely cosmetic: no gameplay state is altered, so
///     nothing here needs networking. Perf: a precomputed <see cref="NextFireTime"/> is compared against
///     <c>IGameTiming.CurTime</c> each tick — no per-tick randomness, no per-tick allocation.
/// </summary>
[RegisterComponent, Access(typeof(SolreignPeriodicEffectSystem))]
public sealed partial class SolreignPeriodicEffectComponent : Component
{
    /// <summary>Minimum seconds between firings (lower edge of the random window).</summary>
    [DataField]
    public float MinIntervalSeconds = 120f;

    /// <summary>Maximum seconds between firings (upper edge of the random window).</summary>
    [DataField]
    public float MaxIntervalSeconds = 600f;

    /// <summary>
    ///     Optional entity to spawn at the owner's coordinates when the effect fires. Use a short-lived
    ///     cosmetic entity (see upstream <c>EffectHearts</c>/<c>EffectSparks</c> in
    ///     Resources/Prototypes/Entities/Effects: TimedDespawn + Sprite + EffectVisuals).
    /// </summary>
    [DataField]
    public EntProtoId? EffectPrototype;

    /// <summary>
    ///     Optional sound played (PVS) at the owner when the effect fires. YAML accepts either a
    ///     <c>collection:</c> or a <c>path:</c> form, same as any upstream SoundSpecifier field.
    /// </summary>
    [DataField]
    public SoundSpecifier? SoundCollection;

    /// <summary>Optional locale id for a popup shown above the owner when the effect fires.</summary>
    [DataField]
    public LocId? PopupText;

    /// <summary>
    ///     Game time at which the effect next fires. Set on map-init and after every firing; never rolled
    ///     per-tick. Server-side scheduling state only — not saved, not networked.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextFireTime;
}
