using Content.Server._Solreign.Providence;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared._Solreign.FX;
using Robust.Shared.Audio;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Title ceremonies: when an account EARNS a new Season Ledger title (a genuinely new grant — see
///     <see cref="TitleRules.IsNewGrant"/> — never a respawn/reconnect reload of an already-held title),
///     Solreign HR announces it station-wide in acid green. Compliance is its own reward.
///
///     Guards:
///       * NEW grants only — the grant is recorded in the store's <c>title_grants</c> table (season-scoped)
///         before dispatch, so reloads and racing double-spawns can never re-fire the bulletin.
///       * Active rounds only — <see cref="AnnounceTitleCeremony"/> checks <c>GameTicker.RunLevel</c> so a
///         late title load never blares over the lobby or the post-round summary.
///
///     Threading: called exclusively from <see cref="SeasonLedgerSystem.Update"/> while draining the pending
///     queue, i.e. always on the main thread — same discipline as the rest of the system.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;

    /// <summary>
    ///     Sender label stamped on every ceremony bulletin — matches the Corporate Ladder's PA voice,
    ///     so it shares that voice's loc key (loc-string sweep, Phase2 A4: was a bare hardcoded literal).
    /// </summary>
    private string CeremonySender => Loc.GetString("solreign-corporate-hr-sender");

    /// <summary>Solreign acid green — the brand color for HR proclamations.</summary>
    private static readonly Color CeremonyColor = Color.FromHex("#39FF14");

    /// <summary>
    ///     Solreign's in-house triumphant-fanfare stinger for HR title ceremonies (AI-generated via
    ///     Google Lyria 2, see Resources/Audio/_Solreign/attributions.yml). Was the default announcement
    ///     sound before Providence; now kept as the fallback for whenever Providence is disabled (see
    ///     <see cref="ProvidenceVoiceSystem.Enabled"/>) — never removed, per the wiring brief.
    /// </summary>
    private static readonly SoundPathSpecifier CeremonySound = new("/Audio/_Solreign/ceremony_stinger.ogg");

    /// <summary>
    ///     Fires the station-wide HR bulletin for a freshly granted title. Main-thread only (called from
    ///     <c>Update</c>); the caller has already verified the mob is alive and the grant is new.
    /// </summary>
    private void AnnounceTitleCeremony(EntityUid mob, string title)
    {
        // Active rounds only: no ceremonies over the lobby, during setup, or after round end.
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        // Brand sting, additive: reuse the general-purpose acid-green screen-FX hook (the same one
        // the Corporate rule's quarterly audit and the Solar Flare event use - see
        // Content.Shared._Solreign.FX.SolreignScreenFxEvent) so an HR title ceremony gets the same
        // signature flash as every other Solreign "mark this moment" broadcast. Station-wide, same
        // as the bulletin itself, not directed at just the honoree.
        RaiseNetworkEvent(new SolreignScreenFxEvent());

        var name = MetaData(mob).EntityName;

        // Providence voice replaces the stinger when enabled; the stinger is the fallback sound
        // when Providence is turned off (solreign.providence_enabled), so ceremonies never go silent.
        var useVoice = _providence.Enabled;

        _chat.DispatchGlobalAnnouncement(
            TitleRules.FormatCeremonyBulletin(name, title),
            CeremonySender,
            playSound: !useVoice,
            announcementSound: useVoice ? null : CeremonySound,
            colorOverride: CeremonyColor);

        if (useVoice)
            _providence.PlayLine(ProvidenceLineCategory.TitleCeremony);
    }
}
