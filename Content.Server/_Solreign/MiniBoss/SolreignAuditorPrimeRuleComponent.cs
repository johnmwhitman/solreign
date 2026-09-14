using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     "The Auditor Prime" mini-boss event: an admin/Director-triggerable rule (`addgamerule
///     SolreignAuditorPrime`) that population-gates itself against a live alive-crew headcount (see
///     <see cref="MiniBossPopulationGate"/> — upstream <c>GameRuleComponent.MinPlayers</c> only checks at
///     round start, not mid-round admin triggers), then spawns the reskinned elite mob (parent
///     <c>BaseMobBehonker</c> — Resources/Prototypes/_Solreign/GameRules/minibosses.yml) with an
///     acid-green arrival announcement. See <see cref="SolreignAuditorPrimeRule"/> for behavior.
/// </summary>
[RegisterComponent, Access(typeof(SolreignAuditorPrimeRule))]
public sealed partial class SolreignAuditorPrimeRuleComponent : Component
{
    /// <summary>Mini-boss mob prototype spawned on a successful population-gate pass.</summary>
    [DataField]
    public EntProtoId BossPrototype = "SolreignMobAuditorPrime";

    /// <summary>Minimum alive crew required for the event to fire (see <see cref="MiniBossPopulationGate"/>).</summary>
    [DataField]
    public int MinPopulation = 12;

    /// <summary>Maximum alive crew the event will fire at. 0 (default) means uncapped.</summary>
    [DataField]
    public int MaxPopulation;

    /// <summary>True once the boss has been spawned this rule instance — guards a double-spawn.</summary>
    [ViewVariables]
    public bool Spawned;
}
