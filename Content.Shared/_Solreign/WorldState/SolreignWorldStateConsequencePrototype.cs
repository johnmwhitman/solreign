using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._Solreign.WorldState;

/// <summary>
///     Prototype definition for a vetted cross-season world-state consequence (SR-W-020).
///     Defines vetted prototype structures and station policies that season finale events can write into decisions.
/// </summary>
[Prototype]
public sealed partial class SolreignWorldStateConsequencePrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Human-readable title of this consequence option.
    /// </summary>
    [DataField("title")]
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    ///     Narrative description of the world-state change.
    /// </summary>
    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    ///     Season identifier (e.g. "Season1").
    /// </summary>
    [DataField("seasonId")]
    public string SeasonId { get; private set; } = "Season1";

    /// <summary>
    ///     Station policy overrides associated with this consequence.
    /// </summary>
    [DataField("stationPolicyModifiers")]
    public Dictionary<string, string> StationPolicyModifiers { get; private set; } = new();

    /// <summary>
    ///     Vetted prototype modifiers applied when this consequence is written.
    /// </summary>
    [DataField("prototypeModifiers")]
    public List<SolreignWorldStatePrototypeModifier> PrototypeModifiers { get; private set; } = new();
}
