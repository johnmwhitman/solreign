#nullable enable
using System.Collections.Generic;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.Configuration;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class ActivationCVarContractTests
{
    private static IEnumerable<TestCaseData> ActivatedFeatures()
    {
        yield return Activated(
            CCVars.SolreignShiftArchiveEnabled,
            "solreign.shift_archive.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignStationAuditEnabled,
            "solreign.station_audit.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignStationAuditInspectionEnabled,
            "solreign.station_audit.inspection_enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignEchoEnabled,
            "solreign.echo.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignMarkEnabled,
            "solreign.mark.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignFleetOperationsEnabled,
            "solreign.fleet_operations_enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignRecordsTerminalEnabled,
            "solreign.records_terminal.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignDirectivesFaxEnabled,
            "solreign.directives_fax.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignProvidenceReactiveEnabled,
            "solreign.providence.reactive_enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignFxWorldFeedbackV1Enabled,
            "solreign.fx.world_feedback_v1",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignFxCueV1Enabled,
            "solreign.fx.cue_v1",
            CVar.REPLICATED | CVar.SERVER);
        yield return Activated(
            CCVars.SolreignMovementBobEnabled,
            "solreign.movement_bob.enabled",
            CVar.REPLICATED | CVar.SERVER);
        yield return Activated(
            CCVars.SolreignCryptEnabled,
            "solreign.crypt.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignLivemapEnabled,
            "solreign.livemap.enabled",
            CVar.SERVERONLY);
        yield return Activated(
            CCVars.SolreignFxWorldFeedbackObserveEnabled,
            "solreign.fx.world_feedback_observe",
            CVar.SERVERONLY);
    }

    [TestCaseSource(nameof(ActivatedFeatures))]
    public void ActivatedFeature_HasPinnedDefaultNameAndFlags(
        CVarDef<bool> cvar,
        string expectedName,
        CVar expectedFlags)
    {
        Assert.Multiple(() =>
        {
            Assert.That(cvar.DefaultValue, Is.True);
            Assert.That(cvar.Name, Is.EqualTo(expectedName));
            Assert.That(cvar.Flags, Is.EqualTo(expectedFlags));
        });
    }

    private static TestCaseData Activated(CVarDef<bool> cvar, string name, CVar flags)
    {
        return new TestCaseData(cvar, name, flags)
            .SetName($"ActivatedFeature_{name}_HasPinnedContract");
    }
}
