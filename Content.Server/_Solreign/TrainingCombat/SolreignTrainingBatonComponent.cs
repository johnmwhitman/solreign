using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.TrainingCombat;

/// <summary>
///     COMBAT SPIKE (roadmap Program 3): a training melee weapon whose hits on humanoids land in one
///     of three server-picked zones — head (brief stutter + jitter "stumble"), legs (short stackable
///     slowdown), hands (target drops their active held item). The pick is weighted random for the
///     spike; the full targeting program replaces the roll with player zone selection and keeps the
///     outcomes. Zone math lives in <see cref="ZoneRules"/>.
///
///     Server-only on purpose: every outcome is applied through existing predicted status-effect /
///     hands systems, so nothing here needs its own networking. See the spike report for the
///     prediction concerns this sidesteps and the ones the full doll cannot.
/// </summary>
[RegisterComponent, Access(typeof(SolreignTrainingBatonSystem))]
public sealed partial class SolreignTrainingBatonComponent : Component
{
    /// <summary>Relative weight of a head connection. Weights need not sum to 1.</summary>
    [DataField]
    public float HeadWeight = 1f;

    /// <summary>Relative weight of a legs connection.</summary>
    [DataField]
    public float LegsWeight = 1f;

    /// <summary>Relative weight of a hands connection.</summary>
    [DataField]
    public float HandsWeight = 1f;

    /// <summary>
    ///     Seconds between zone effects for THIS baton, so a swing flurry lands damage but not a
    ///     stack of status effects. Pure math in <see cref="ZoneRules.NextEffectTime"/>.
    /// </summary>
    [DataField]
    public TimeSpan EffectCooldown = TimeSpan.FromSeconds(1.5);

    /// <summary>How long the head outcome's stutter + jitter lasts. Deliberately brief and mild.</summary>
    [DataField]
    public TimeSpan HeadEffectDuration = TimeSpan.FromSeconds(4);

    /// <summary>Jitter amplitude for the head "stumble". Kept low — this is a training tap, not a stun.</summary>
    [DataField]
    public float HeadJitterAmplitude = 6f;

    /// <summary>Jitter frequency for the head "stumble".</summary>
    [DataField]
    public float HeadJitterFrequency = 3f;

    /// <summary>
    ///     Duration added per legs connection. Repeated hits ACCUMULATE duration on the same status
    ///     effect (the "stack" of this spike) rather than deepening the slow.
    /// </summary>
    [DataField]
    public TimeSpan LegsSlowdownDuration = TimeSpan.FromSeconds(3);

    /// <summary>Walk/sprint speed multiplier while the legs slowdown is active.</summary>
    [DataField]
    public float LegsSpeedModifier = 0.65f;

    /// <summary>Movement status effect applied by the legs outcome.</summary>
    [DataField]
    public EntProtoId LegsSlowdownEffect = "SolreignTrainingSlowdownStatusEffect";

    /// <summary>Distinct sound for a head connection.</summary>
    [DataField]
    public SoundSpecifier HeadSound = new SoundPathSpecifier("/Audio/Effects/hit_kick.ogg");

    /// <summary>Distinct sound for a legs connection.</summary>
    [DataField]
    public SoundSpecifier LegsSound = new SoundPathSpecifier("/Audio/Effects/slip.ogg");

    /// <summary>Distinct sound for a hands connection (the classic disarm swoosh).</summary>
    [DataField]
    public SoundSpecifier HandsSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    /// <summary>
    ///     Game time at which the next zone effect may fire. Server-side scheduling state only — not
    ///     saved, not networked (same pattern as <c>SolreignPeriodicEffectComponent.NextFireTime</c>).
    /// </summary>
    [ViewVariables]
    public TimeSpan NextEffectTime;
}
