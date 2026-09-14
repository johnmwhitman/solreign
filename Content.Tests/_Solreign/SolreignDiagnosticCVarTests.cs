using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.Configuration;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class SolreignDiagnosticCVarTests
{
    [Test]
    public void DiagnosticMasterSwitches_AreReplicatedAndDefaultOn()
    {
        // Default ON + replicated per the 2026-07-24 owner activation decision; the
        // original dark-by-default assertions predated that ruling.
        Assert.Multiple(() =>
        {
            Assert.That(CCVars.SolreignPowerDiagnosticsEnabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignPowerDiagnosticsEnabled.Flags, Is.EqualTo(CVar.SERVER));
            Assert.That(CCVars.SolreignAtmosDiagnosticsEnabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignAtmosDiagnosticsEnabled.Flags, Is.EqualTo(CVar.SERVER));
        });
    }
}
