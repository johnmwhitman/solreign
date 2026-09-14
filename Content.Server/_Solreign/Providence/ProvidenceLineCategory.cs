namespace Content.Server._Solreign.Providence;

/// <summary>
///     The 12 voice-pack categories in the 60-file Providence inventory, each backed
///     by one <c>soundCollection</c> in <c>Resources/Prototypes/_Solreign/providence_sounds.yml</c> —
///     see Resources/Audio/_Solreign/Providence/ATTRIBUTION.txt for provenance and
///     <see cref="ProvidenceVoiceMap"/> for the category-&gt;collection lookup.
///
///     Not every category has a live call site yet (see <see cref="ProvidenceVoiceSystem"/>'s doc
///     comment for the wired/unwired breakdown) — the full enum exists so a future wave can call
///     <see cref="ProvidenceVoiceSystem.PlayLine"/> for the rest without touching the audio plumbing.
/// </summary>
public enum ProvidenceLineCategory
{
    ShiftStart,
    ShiftEnd,
    TitleCeremony,
    EventAudit,
    EventRaid,
    EventAcidStorm,
    ZooBreach,
    HotPotato,
    CakeDenial,
    IdleMusings,
    DeathCommiseration,
    NewPlayerWelcome,
}
