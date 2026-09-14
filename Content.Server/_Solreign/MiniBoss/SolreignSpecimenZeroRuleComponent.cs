using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     "Specimen Zero" mini-boss event: an admin/Director-triggerable rule (`addgamerule
///     SolreignSpecimenZero`) that population-gates itself (see <see cref="MiniBossPopulationGate"/>),
///     then plays a telegraphed <see cref="WarningSeconds"/>-long containment-breach warning before
///     spawning the reskinned nonlethal-leaning escapee (parent <c>MobKangaroo</c> — Resources/
///     Prototypes/_Solreign/GameRules/minibosses.yml). See <see cref="SolreignSpecimenZeroRule"/> for
///     behavior and <see cref="MiniBossTelegraph"/> for the pure warning-elapsed math.
/// </summary>
[RegisterComponent, Access(typeof(SolreignSpecimenZeroRule))]
public sealed partial class SolreignSpecimenZeroRuleComponent : Component
{
    /// <summary>Mini-boss mob prototype spawned once the telegraph warning elapses.</summary>
    [DataField]
    public EntProtoId BossPrototype = "SolreignMobSpecimenZero";

    /// <summary>Seconds between the arrival warning and the actual spawn (the "telegraph").</summary>
    [DataField]
    public float WarningSeconds = 60f;

    /// <summary>Minimum alive crew required for the event to fire (see <see cref="MiniBossPopulationGate"/>).</summary>
    [DataField]
    public int MinPopulation = 10;

    /// <summary>Maximum alive crew the event will fire at. 0 (default) means uncapped.</summary>
    [DataField]
    public int MaxPopulation;

    /// <summary>Seconds accumulated since the warning was announced. Server-side timer state only, not networked.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>True once the population gate passed and the telegraph warning has been announced.</summary>
    [ViewVariables]
    public bool WarningAnnounced;

    /// <summary>True once the boss has been spawned this rule instance — guards a double-spawn.</summary>
    [ViewVariables]
    public bool Spawned;

    /// <summary>The station the warning fired on, so the eventual spawn lands in the same place. Null before the warning fires.</summary>
    [ViewVariables]
    public EntityUid? TargetStation;
}
