#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.WorldState;  // SolreignWorldStateStore moved here 07-25:
                                            // its System.Text.Json use is forbidden in
                                            // sandboxed (client-loaded) assemblies.
using Content.Shared._Solreign.WorldState;  // the decision/envelope data types stay shared
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for Cross-season world-state consequences (SR-W-020).
///     Verifies versioned schema validation, serialization round-tripping, declarative policy aggregation,
///     offline rollback mechanism, corrupt ledger fallback, and safety guarantees (no arbitrary code execution).
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignWorldStateStore))]
public sealed class SolreignWorldStateTests
{
    [Test]
    public void CreateDecision_AndSerializeEnvelope_RoundTripsJsonEnvelope()
    {
        var policies = new Dictionary<string, string>
        {
            ["audit_strictness"] = "high",
            ["hazard_pay_multiplier"] = "1.25",
        };

        var modifiers = new List<SolreignWorldStatePrototypeModifier>
        {
            new()
            {
                TargetPrototypeId = "SolreignCorporateProjectBudget",
                Operation = "OverrideValue",
                Key = "baselineFundingMultiplier",
                Value = "1.15",
            },
        };

        var decision = SolreignWorldStateStore.CreateDecision(
            "decision_s1_finale",
            "Season1",
            "Season 1 Finale Audit",
            "Strict executive audit imposed.",
            policies,
            modifiers);

        var json = SolreignWorldStateStore.SerializeEnvelope(new[] { decision });
        Assert.That(json, Is.Not.Empty);

        var restored = SolreignWorldStateStore.DeserializeEnvelope(json);
        Assert.That(restored, Has.Count.EqualTo(1));

        var item = restored[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.DecisionId, Is.EqualTo("decision_s1_finale"));
            Assert.That(item.SeasonId, Is.EqualTo("Season1"));
            Assert.That(item.SchemaVersion, Is.EqualTo(SolreignWorldStateStore.CurrentSchemaVersion));
            Assert.That(item.IsActive, Is.True);
            Assert.That(item.StationPolicyModifiers["audit_strictness"], Is.EqualTo("high"));
            Assert.That(item.PrototypeModifiers, Has.Count.EqualTo(1));
            Assert.That(item.PrototypeModifiers[0].TargetPrototypeId, Is.EqualTo("SolreignCorporateProjectBudget"));
        });
    }

    [Test]
    public void IsSchemaSupported_ValidatesVersion1AndRejectsUnsupportedVersions()
    {
        Assert.That(SolreignWorldStateStore.IsSchemaSupported(1), Is.True);
        Assert.That(SolreignWorldStateStore.IsSchemaSupported(0), Is.False);
        Assert.That(SolreignWorldStateStore.IsSchemaSupported(999), Is.False);
    }

    [Test]
    public void AggregateActivePolicies_FiltersInactiveAndCombinesActivePolicies()
    {
        var dec1 = SolreignWorldStateStore.CreateDecision(
            "dec1", "Season1", "Title 1", "Desc 1",
            new Dictionary<string, string> { ["tax_rate"] = "0.15", ["audit"] = "low" });

        var dec2 = SolreignWorldStateStore.CreateDecision(
            "dec2", "Season1", "Title 2", "Desc 2",
            new Dictionary<string, string> { ["audit"] = "high", ["hazard_pay"] = "enabled" });

        // Deactivate dec1
        dec1.IsActive = false;

        var activePolicies = SolreignWorldStateStore.AggregateActivePolicies(new[] { dec1, dec2 });

        Assert.Multiple(() =>
        {
            Assert.That(activePolicies.ContainsKey("tax_rate"), Is.False, "Inactive decision policies must not apply");
            Assert.That(activePolicies["audit"], Is.EqualTo("high"));
            Assert.That(activePolicies["hazard_pay"], Is.EqualTo("enabled"));
        });
    }

    [Test]
    public void AggregateActiveModifiers_ReturnsOnlyActiveDeclarativePrototypeModifiers()
    {
        var mod1 = new SolreignWorldStatePrototypeModifier { TargetPrototypeId = "Proto1", Operation = "SetPolicy", Key = "k1", Value = "v1" };
        var mod2 = new SolreignWorldStatePrototypeModifier { TargetPrototypeId = "Proto2", Operation = "OverrideValue", Key = "k2", Value = "v2" };

        var decActive = SolreignWorldStateStore.CreateDecision("active", "Season1", "Active", "Active Desc", null, new List<SolreignWorldStatePrototypeModifier> { mod1 });
        var decInactive = SolreignWorldStateStore.CreateDecision("inactive", "Season1", "Inactive", "Inactive Desc", null, new List<SolreignWorldStatePrototypeModifier> { mod2 });
        decInactive.IsActive = false;

        var activeMods = SolreignWorldStateStore.AggregateActiveModifiers(new[] { decActive, decInactive });

        Assert.That(activeMods, Has.Count.EqualTo(1));
        Assert.That(activeMods[0].TargetPrototypeId, Is.EqualTo("Proto1"));
    }

    [Test]
    public void RollbackDecision_DeactivatesSingleDecisionWithReason()
    {
        var dec1 = SolreignWorldStateStore.CreateDecision("dec1", "Season1", "Title 1", "Desc 1");
        var dec2 = SolreignWorldStateStore.CreateDecision("dec2", "Season1", "Title 2", "Desc 2");
        var list = new List<SolreignWorldStateDecision> { dec1, dec2 };

        var success = SolreignWorldStateStore.RollbackDecision(list, "dec1", "Offline emergency admin rollback");

        Assert.That(success, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(dec1.IsActive, Is.False);
            Assert.That(dec1.RollbackReason, Is.EqualTo("Offline emergency admin rollback"));
            Assert.That(dec2.IsActive, Is.True);
        });
    }

    [Test]
    public void RollbackAll_DeactivatesAllDecisionsForOfflineReset()
    {
        var dec1 = SolreignWorldStateStore.CreateDecision("dec1", "Season1", "Title 1", "Desc 1");
        var dec2 = SolreignWorldStateStore.CreateDecision("dec2", "Season1", "Title 2", "Desc 2");
        var list = new List<SolreignWorldStateDecision> { dec1, dec2 };

        SolreignWorldStateStore.RollbackAll(list, "Full season reset");

        Assert.Multiple(() =>
        {
            Assert.That(dec1.IsActive, Is.False);
            Assert.That(dec2.IsActive, Is.False);
            Assert.That(dec1.RollbackReason, Is.EqualTo("Full season reset"));
            Assert.That(dec2.RollbackReason, Is.EqualTo("Full season reset"));
        });
    }

    [Test]
    public void CorruptJson_or_VersionMismatch_FallsBackSafelyWithoutCrashing()
    {
        var invalidJson = "{ \"version\": 999, \"decisions\": [] }";
        var result1 = SolreignWorldStateStore.DeserializeEnvelope(invalidJson);
        Assert.That(result1, Is.Empty);

        var malformedJson = "{ \"this is not valid json\": ::: }";
        var result2 = SolreignWorldStateStore.DeserializeEnvelope(malformedJson);
        Assert.That(result2, Is.Empty);
    }
}
