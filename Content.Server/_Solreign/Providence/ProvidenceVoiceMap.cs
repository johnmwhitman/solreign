using System;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure category-&gt;collection-id lookup for the Providence voice pack. Kept free of IoC/engine
///     types so it is directly unit-testable (Content.Tests/_Solreign/ProvidenceVoiceMapTests.cs),
///     same reasoning as <c>PeriodicEffectTiming</c> and <c>CorporateScoring</c>.
///
///     Every id returned here must have a matching <c>type: soundCollection</c> entry in
///     Resources/Prototypes/_Solreign/providence_sounds.yml — that file is the other half of this
///     contract and is not engine-checkable from a plain NUnit test, so keep the two in lockstep by
///     hand when adding a category.
/// </summary>
public static class ProvidenceVoiceMap
{
    /// <summary>Resolves a line category to its SoundCollectionPrototype id.</summary>
    public static string CollectionFor(ProvidenceLineCategory category) => category switch
    {
        ProvidenceLineCategory.ShiftStart => "SolreignProvidenceShiftStart",
        ProvidenceLineCategory.ShiftEnd => "SolreignProvidenceShiftEnd",
        ProvidenceLineCategory.TitleCeremony => "SolreignProvidenceTitleCeremony",
        ProvidenceLineCategory.EventAudit => "SolreignProvidenceEventAudit",
        ProvidenceLineCategory.EventRaid => "SolreignProvidenceEventRaid",
        ProvidenceLineCategory.EventAcidStorm => "SolreignProvidenceEventAcidStorm",
        ProvidenceLineCategory.ZooBreach => "SolreignProvidenceZooBreach",
        ProvidenceLineCategory.HotPotato => "SolreignProvidenceHotPotato",
        ProvidenceLineCategory.CakeDenial => "SolreignProvidenceCakeDenial",
        ProvidenceLineCategory.IdleMusings => "SolreignProvidenceIdleMusings",
        ProvidenceLineCategory.DeathCommiseration => "SolreignProvidenceDeathCommiseration",
        ProvidenceLineCategory.NewPlayerWelcome => "SolreignProvidenceNewPlayerWelcome",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unmapped Providence line category — add it here and to providence_sounds.yml."),
    };
}
