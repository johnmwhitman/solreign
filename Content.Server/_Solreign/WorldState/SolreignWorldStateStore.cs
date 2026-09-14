using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

// MOVED from Content.Shared on 2026-07-25. This class serializes world state with
// System.Text.Json, and JsonSerializer/JsonSerializerOptions/JsonIgnoreCondition are all
// OUTSIDE the RobustToolbox client sandbox allowlist (only JsonIgnoreAttribute and
// JsonPropertyNameAttribute are permitted there). While it lived in Content.Shared the
// client type-checked it on load, failed, and aborted before the lobby - a total outage.
//
// It never belonged in Shared: nothing in Content.Shared or Content.Client referenced it.
// Its only consumers are SolreignWorldStateSystem (server) and its tests. Server-side JSON
// persistence is fine here because the sandbox does not apply to Content.Server.
//
// The DATA types it operates on (SolreignWorldStateDecision, ...LedgerEnvelope) stay in
// Content.Shared deliberately - they use no forbidden types and are networked.
using Content.Shared._Solreign.WorldState;

namespace Content.Server._Solreign.WorldState;

/// <summary>
///     Pure, unit-testable engine for managing, serializing, validating, and rolling back
///     versioned cross-season world-state decisions (SR-W-020).
///     Guarantees safe declarative policy alteration without arbitrary code execution.
/// </summary>
public static class SolreignWorldStateStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    ///     Creates a versioned decision record from a vetted consequence prototype.
    /// </summary>
    public static SolreignWorldStateDecision CreateDecision(
        string decisionId,
        string seasonId,
        string title,
        string description,
        Dictionary<string, string>? stationPolicyModifiers = null,
        List<SolreignWorldStatePrototypeModifier>? prototypeModifiers = null)
    {
        return new SolreignWorldStateDecision
        {
            SchemaVersion = CurrentSchemaVersion,
            DecisionId = string.IsNullOrWhiteSpace(decisionId) ? $"decision_{Guid.NewGuid():N}" : decisionId,
            SeasonId = seasonId,
            Title = title,
            Description = description,
            Timestamp = DateTime.UtcNow.ToString("o"),
            IsActive = true,
            StationPolicyModifiers = stationPolicyModifiers != null
                ? new Dictionary<string, string>(stationPolicyModifiers)
                : new Dictionary<string, string>(),
            PrototypeModifiers = prototypeModifiers != null
                ? new List<SolreignWorldStatePrototypeModifier>(prototypeModifiers)
                : new List<SolreignWorldStatePrototypeModifier>(),
        };
    }

    /// <summary>
    ///     Validates whether a decision's schema version is supported.
    /// </summary>
    public static bool IsSchemaSupported(int schemaVersion)
    {
        return schemaVersion >= 1 && schemaVersion <= CurrentSchemaVersion;
    }

    /// <summary>
    ///     Serializes a list of decisions to a versioned JSON ledger string.
    /// </summary>
    public static string SerializeEnvelope(IEnumerable<SolreignWorldStateDecision> decisions)
    {
        var envelope = new SolreignWorldStateLedgerEnvelope
        {
            Version = CurrentSchemaVersion,
            Decisions = decisions.ToList(),
        };

        return JsonSerializer.Serialize(envelope, JsonOpts);
    }

    /// <summary>
    ///     Deserializes a JSON ledger string into a validated list of world-state decisions.
    ///     Unsupported schema versions or corrupt elements are safely filtered out.
    /// </summary>
    public static List<SolreignWorldStateDecision> DeserializeEnvelope(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return new List<SolreignWorldStateDecision>();

        try
        {
            var envelope = JsonSerializer.Deserialize<SolreignWorldStateLedgerEnvelope>(jsonContent, JsonOpts);
            if (envelope == null || !IsSchemaSupported(envelope.Version))
            {
                return new List<SolreignWorldStateDecision>();
            }

            var validDecisions = new List<SolreignWorldStateDecision>();
            foreach (var d in envelope.Decisions)
            {
                if (IsSchemaSupported(d.SchemaVersion))
                {
                    validDecisions.Add(d);
                }
            }

            return validDecisions;
        }
        catch
        {
            return new List<SolreignWorldStateDecision>();
        }
    }

    /// <summary>
    ///     Aggregates active station policies across all valid decisions in priority order.
    /// </summary>
    public static Dictionary<string, string> AggregateActivePolicies(IEnumerable<SolreignWorldStateDecision> decisions)
    {
        var policies = new Dictionary<string, string>();
        foreach (var decision in decisions)
        {
            if (!decision.IsActive || !IsSchemaSupported(decision.SchemaVersion))
                continue;

            foreach (var (k, v) in decision.StationPolicyModifiers)
            {
                policies[k] = v;
            }
        }

        return policies;
    }

    /// <summary>
    ///     Aggregates active prototype modifiers across all valid decisions.
    /// </summary>
    public static List<SolreignWorldStatePrototypeModifier> AggregateActiveModifiers(IEnumerable<SolreignWorldStateDecision> decisions)
    {
        var result = new List<SolreignWorldStatePrototypeModifier>();
        foreach (var decision in decisions)
        {
            if (!decision.IsActive || !IsSchemaSupported(decision.SchemaVersion))
                continue;

            result.AddRange(decision.PrototypeModifiers);
        }

        return result;
    }

    /// <summary>
    ///     Marks a target decision as rolled back (inactive) offline.
    /// </summary>
    public static bool RollbackDecision(List<SolreignWorldStateDecision> decisions, string decisionId, string reason)
    {
        var target = decisions.FirstOrDefault(d => d.DecisionId.Equals(decisionId, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return false;

        target.IsActive = false;
        target.RollbackReason = reason;
        return true;
    }

    /// <summary>
    ///     Rolls back all active decisions to restore baseline next-season prototypes and policies.
    /// </summary>
    public static void RollbackAll(List<SolreignWorldStateDecision> decisions, string reason)
    {
        foreach (var d in decisions)
        {
            if (d.IsActive)
            {
                d.IsActive = false;
                d.RollbackReason = reason;
            }
        }
    }
}
