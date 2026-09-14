using System;
using System.Collections.Generic;
using System.IO;
using Content.Shared.CCVar;
using Content.Shared._Solreign.WorldState;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.WorldState;

/// <summary>
///     Server system for Cross-season world-state consequences (SR-W-020).
///     Manages reading, writing, version validation, prototype/policy alteration, and offline rollback
///     for season finale decisions without arbitrary code execution.
/// </summary>
public sealed partial class SolreignWorldStateSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IResourceManager _resManager = default!;

    private readonly List<SolreignWorldStateDecision> _decisions = new();
    private Dictionary<string, string> _activeStationPolicies = new();
    private List<SolreignWorldStatePrototypeModifier> _activePrototypeModifiers = new();
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignWorldStateEnabled, OnEnabledChanged, true);
        ReloadLedger();
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        RebuildActiveState();
    }

    /// <summary>
    ///     Reloads decisions from the persistent world-state ledger file.
    /// </summary>
    public void ReloadLedger()
    {
        _decisions.Clear();
        var relPath = _cfg.GetCVar(CCVars.SolreignWorldStateFilePath);
        var resPath = new ResPath("/" + relPath.TrimStart('/'));

        if (_resManager.UserData.Exists(resPath))
        {
            try
            {
                using var stream = _resManager.UserData.OpenRead(resPath);
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var loaded = SolreignWorldStateStore.DeserializeEnvelope(content);
                _decisions.AddRange(loaded);
            }
            catch (Exception ex)
            {
                Logger.ErrorS("solreign.world_state", $"Failed to load world-state ledger file '{relPath}': {ex.Message}");
            }
        }

        RebuildActiveState();
    }

    /// <summary>
    ///     Saves the current decisions ledger to the persistent ledger file.
    /// </summary>
    public void SaveLedger()
    {
        var relPath = _cfg.GetCVar(CCVars.SolreignWorldStateFilePath);
        var resPath = new ResPath("/" + relPath.TrimStart('/'));

        try
        {
            var content = SolreignWorldStateStore.SerializeEnvelope(_decisions);
            using var stream = _resManager.UserData.OpenWrite(resPath);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
        catch (Exception ex)
        {
            Logger.ErrorS("solreign.world_state", $"Failed to save world-state ledger file '{relPath}': {ex.Message}");
        }
    }

    private void RebuildActiveState()
    {
        if (!_enabled)
        {
            _activeStationPolicies = new Dictionary<string, string>();
            _activePrototypeModifiers = new List<SolreignWorldStatePrototypeModifier>();
            return;
        }

        _activeStationPolicies = SolreignWorldStateStore.AggregateActivePolicies(_decisions);
        _activePrototypeModifiers = SolreignWorldStateStore.AggregateActiveModifiers(_decisions);
    }

    /// <summary>
    ///     Writes a new versioned world-state decision (e.g. from a season finale event).
    /// </summary>
    public bool RecordDecision(SolreignWorldStateDecision decision)
    {
        if (!SolreignWorldStateStore.IsSchemaSupported(decision.SchemaVersion))
        {
            Logger.WarningS("solreign.world_state", $"Rejected decision '{decision.DecisionId}' with unsupported schema version {decision.SchemaVersion}.");
            return false;
        }

        _decisions.Add(decision);
        SaveLedger();
        RebuildActiveState();
        Logger.InfoS("solreign.world_state", $"Recorded cross-season decision '{decision.DecisionId}' for {decision.SeasonId}.");
        return true;
    }

    /// <summary>
    ///     Records a world-state decision using a vetted consequence prototype.
    /// </summary>
    public bool RecordConsequenceFromPrototype(string prototypeId, string? customDecisionId = null)
    {
        if (!_prototypeManager.TryIndex<SolreignWorldStateConsequencePrototype>(prototypeId, out var proto))
        {
            Logger.WarningS("solreign.world_state", $"Consequence prototype '{prototypeId}' not found.");
            return false;
        }

        var decId = customDecisionId ?? $"decision_{proto.ID}_{DateTime.UtcNow.Ticks}";
        var decision = SolreignWorldStateStore.CreateDecision(
            decId,
            proto.SeasonId,
            proto.Title,
            proto.Description,
            proto.StationPolicyModifiers,
            proto.PrototypeModifiers);

        return RecordDecision(decision);
    }

    /// <summary>
    ///     Rolls back a specific decision by deactivating it. Supports offline rollback.
    /// </summary>
    public bool RollbackDecision(string decisionId, string reason = "Offline administrative rollback")
    {
        var result = SolreignWorldStateStore.RollbackDecision(_decisions, decisionId, reason);
        if (result)
        {
            SaveLedger();
            RebuildActiveState();
            Logger.InfoS("solreign.world_state", $"Rolled back decision '{decisionId}': {reason}");
        }

        return result;
    }

    /// <summary>
    ///     Deactivates all world-state decisions, resetting station policies to base defaults.
    /// </summary>
    public void RollbackAll(string reason = "Global administrative reset")
    {
        SolreignWorldStateStore.RollbackAll(_decisions, reason);
        SaveLedger();
        RebuildActiveState();
        Logger.InfoS("solreign.world_state", $"Rolled back all world-state decisions: {reason}");
    }

    /// <summary>
    ///     Gets an active station policy value by key.
    /// </summary>
    public string GetActivePolicy(string policyKey, string defaultValue = "")
    {
        if (!_enabled)
            return defaultValue;

        return _activeStationPolicies.TryGetValue(policyKey, out var val) ? val : defaultValue;
    }

    /// <summary>
    ///     Gets all active station policy overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetActiveStationPolicies() => _activeStationPolicies;

    /// <summary>
    ///     Gets all active prototype modifiers.
    /// </summary>
    public IReadOnlyList<SolreignWorldStatePrototypeModifier> GetActivePrototypeModifiers() => _activePrototypeModifiers;

    /// <summary>
    ///     Gets all recorded world-state decisions.
    /// </summary>
    public IReadOnlyList<SolreignWorldStateDecision> GetDecisions() => _decisions;
}
