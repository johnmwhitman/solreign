using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Terminator;

/// <summary>
///     "Compliance Hunt" hunter ghost-role event: admin-startable mid-round rule
///     (<c>addgamerule SolreignComplianceHunterRule</c> / <c>startgamerule</c>) that spawns one
///     <c>SolreignMobComplianceRetrievalUnit</c> on a random station tile as a claimable ghost role.
///     Pattern mirrors upstream NinjaSpawn/LoneOpsSpawn (StationEvent fire-and-forget + ghost-role
///     mob) and <c>SolreignAuditorPrimeRule</c> (StationEventSystem spawn-on-tile + ForceEndSelf).
///     See Resources/Prototypes/_Solreign/GameRules/compliance_hunt.yml for the prototype and
///     <see cref="SolreignComplianceHunterRule"/> for behavior.
/// </summary>
[RegisterComponent, Access(typeof(SolreignComplianceHunterRule))]
public sealed partial class SolreignComplianceHunterRuleComponent : Component
{
    /// <summary>
    ///     Ghost-role hunter mob prototype. Defaults to the Compliance Retrieval Unit (Terminator
    ///     spike) which already carries <c>GhostRole</c> + <c>GhostTakeoverAvailable</c>.
    /// </summary>
    [DataField]
    public EntProtoId HunterPrototype = "SolreignMobComplianceRetrievalUnit";

    /// <summary>
    ///     Maximum live Compliance Retrieval Units allowed on the map before this rule no-ops.
    ///     Guards event-table re-fires stacking hunters on a low-pop Compliance Hunt round.
    /// </summary>
    [DataField]
    public int MaxConcurrent = ComplianceHunterSpawnRules.DefaultMaxConcurrent;

    /// <summary>
    ///     How many additional alive players are needed to allow one more concurrent hunter.
    /// </summary>
    [DataField]
    public int PlayersPerHunter = ComplianceHunterSpawnRules.DefaultPlayersPerHunter;

    /// <summary>True once a hunter has been spawned this rule instance — guards a double-spawn.</summary>
    [ViewVariables]
    public bool Spawned;
}
