using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Bounties;

/// <summary>
///     Marks an entity as a Liability Board — the Director daemon front door for browsing active
///     bounties and submitting a claim (<see cref="SolreignBountySystem"/>). Pure marker, same
///     idiom as <c>SolreignOracleComponent</c>: all state lives in the daemon and the BUI
///     snapshot, not on this component.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignBountyBoardComponent : Component
{
}
