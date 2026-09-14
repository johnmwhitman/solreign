using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Research;

/// <summary>
///     SR-W-063: Research Tech Web & Department Point Escrow Component.
///     Tracks research point escrow balances, passive generation rates, experimental node unlocks,
///     and department milestone reward tiers.
/// </summary>
// Server-only bookkeeping. The client has no counterpart for this type, so marking it networked
// breaks the shared component NetId table and corrupts client entity state during initial sync.
[RegisterComponent]
public sealed partial class SolreignTechWebEscrowComponent : Component
{
    /// <summary>Points held in science department escrow prior to distribution.</summary>
    [DataField("escrowPointBalance")]
    public int EscrowPointBalance = 0;

    /// <summary>Base research point generation rate per cycle/second.</summary>
    [DataField("basePointGenerationRate")]
    public int BasePointGenerationRate = 10;

    /// <summary>Efficiency multiplier applied to research point generation (e.g. 1.0 = 100%, 1.5 = +50% lab bonus).</summary>
    [DataField("generationEfficiencyMultiplier")]
    public float GenerationEfficiencyMultiplier = 1.0f;

    /// <summary>Total research points generated during the shift.</summary>
    [DataField("totalPointsGenerated")]
    public int TotalPointsGenerated = 0;

    /// <summary>Set of unlocked technology node IDs.</summary>
    [DataField("unlockedNodes")]
    public HashSet<string> UnlockedNodes = new();

    /// <summary>Set of unlocked experimental technology node IDs.</summary>
    [DataField("experimentalNodesUnlocked")]
    public HashSet<string> ExperimentalNodesUnlocked = new();

    /// <summary>Set of department milestone tiers claimed (e.g., tier 1, 2, 3).</summary>
    [DataField("claimedMilestoneTiers")]
    public HashSet<int> ClaimedMilestoneTiers = new();

    /// <summary>Bonus multiplier for department milestone point rewards (e.g., 1.0 = standard, 1.25 = 25% bonus).</summary>
    [DataField("departmentMilestoneMultiplier")]
    public float DepartmentMilestoneMultiplier = 1.0f;

    /// <summary>Total department milestone bonus points awarded.</summary>
    [DataField("totalMilestonePointsAwarded")]
    public int TotalMilestonePointsAwarded = 0;
}
