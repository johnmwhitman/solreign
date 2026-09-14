using System;
using System.Collections.Generic;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Solreign.WorldState;

/// <summary>
///     Declarative, safe prototype modifier instruction for cross-season consequences (SR-W-020).
///     Strictly bounded to predefined key-value operations — NO arbitrary code execution or scripting.
/// </summary>
[DataDefinition]
public sealed partial class SolreignWorldStatePrototypeModifier
{
    /// <summary>
    ///     Target prototype ID to alter for the upcoming season (e.g. station policy, entity prototype, directive).
    /// </summary>
    [DataField("targetPrototypeId")]
    public string TargetPrototypeId { get; set; } = string.Empty;

    /// <summary>
    ///     Operation type: e.g. "SetPolicy", "OverrideValue", "MultiplyNumeric", "SetFlag".
    /// </summary>
    [DataField("operation")]
    public string Operation { get; set; } = "SetPolicy";

    /// <summary>
    ///     Key or property name to modify.
    /// </summary>
    [DataField("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    ///     Value to apply.
    /// </summary>
    [DataField("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
///     Versioned record of a season finale world-state decision (SR-W-020).
///     Stored in a versioned ledger to guarantee safe offline rollback and prevent arbitrary code execution.
/// </summary>
[DataDefinition]
public sealed partial class SolreignWorldStateDecision
{
    /// <summary>
    ///     Version of the world-state decision schema. Used for forward/backward compatibility.
    /// </summary>
    [DataField("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    ///     Unique identifier for this world-state decision (e.g., "decision_season1_finale_audit").
    /// </summary>
    [DataField("decisionId")]
    public string DecisionId { get; set; } = string.Empty;

    /// <summary>
    ///     Associated season identifier (e.g., "Season1").
    /// </summary>
    [DataField("seasonId")]
    public string SeasonId { get; set; } = string.Empty;

    /// <summary>
    ///     Human-readable title of the decision.
    /// </summary>
    [DataField("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    ///     Narrative description of the world-state consequence.
    /// </summary>
    [DataField("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     ISO-8601 timestamp string when the season finale wrote this decision.
    /// </summary>
    [DataField("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    ///     Whether this decision is active. If false, it has been rolled back or deactivated.
    /// </summary>
    [DataField("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    ///     Station policy key-value overrides (e.g., "hazard_pay_multiplier" -> "1.5", "audit_strictness" -> "high").
    /// </summary>
    [DataField("stationPolicyModifiers")]
    public Dictionary<string, string> StationPolicyModifiers { get; set; } = new();

    /// <summary>
    ///     Declarative next-season prototype modifiers.
    /// </summary>
    [DataField("prototypeModifiers")]
    public List<SolreignWorldStatePrototypeModifier> PrototypeModifiers { get; set; } = new();

    /// <summary>
    ///     Reason string recorded if this decision was rolled back offline.
    /// </summary>
    [DataField("rollbackReason")]
    public string? RollbackReason { get; set; }
}

/// <summary>
///     Container payload for serializing/deserializing the complete versioned world-state decisions file.
/// </summary>
[DataDefinition]
public sealed partial class SolreignWorldStateLedgerEnvelope
{
    [DataField("version")]
    public int Version { get; set; } = 1;

    [DataField("decisions")]
    public List<SolreignWorldStateDecision> Decisions { get; set; } = new();
}
