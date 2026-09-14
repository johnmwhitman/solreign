using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceVoiceMap))]
public sealed class ProvidenceVoiceMapTests
{
    // Mirrors Resources/Prototypes/_Solreign/providence_sounds.yml's collection ids one-for-one.
    // If this test breaks, either the map or the yml drifted — fix whichever one is wrong.
    private static readonly Dictionary<ProvidenceLineCategory, string> Expected = new()
    {
        [ProvidenceLineCategory.ShiftStart] = "SolreignProvidenceShiftStart",
        [ProvidenceLineCategory.ShiftEnd] = "SolreignProvidenceShiftEnd",
        [ProvidenceLineCategory.TitleCeremony] = "SolreignProvidenceTitleCeremony",
        [ProvidenceLineCategory.EventAudit] = "SolreignProvidenceEventAudit",
        [ProvidenceLineCategory.EventRaid] = "SolreignProvidenceEventRaid",
        [ProvidenceLineCategory.EventAcidStorm] = "SolreignProvidenceEventAcidStorm",
        [ProvidenceLineCategory.ZooBreach] = "SolreignProvidenceZooBreach",
        [ProvidenceLineCategory.HotPotato] = "SolreignProvidenceHotPotato",
        [ProvidenceLineCategory.CakeDenial] = "SolreignProvidenceCakeDenial",
        [ProvidenceLineCategory.IdleMusings] = "SolreignProvidenceIdleMusings",
        [ProvidenceLineCategory.DeathCommiseration] = "SolreignProvidenceDeathCommiseration",
        [ProvidenceLineCategory.NewPlayerWelcome] = "SolreignProvidenceNewPlayerWelcome",
    };

    [TestCaseSource(nameof(AllCategories))]
    public void CategoryResolvesToExpectedCollection(ProvidenceLineCategory category)
    {
        Assert.That(ProvidenceVoiceMap.CollectionFor(category), Is.EqualTo(Expected[category]));
    }

    [Test]
    public void EveryEnumValueIsCovered()
    {
        var enumValues = Enum.GetValues<ProvidenceLineCategory>();

        Assert.That(Expected.Keys, Is.EquivalentTo(enumValues),
            "Expected map in this test is missing/has extra categories vs the enum — keep both in sync.");
    }

    [Test]
    public void NoTwoCategoriesShareACollection()
    {
        var ids = Enum.GetValues<ProvidenceLineCategory>()
            .Select(ProvidenceVoiceMap.CollectionFor)
            .ToList();

        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), "Two categories resolved to the same collection id.");
    }

    private static IEnumerable<ProvidenceLineCategory> AllCategories() => Enum.GetValues<ProvidenceLineCategory>();
}
