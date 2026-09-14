using System;
using Content.Server.Chat.Systems;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Diagnostics;

/// <summary>
///     Gives the power and atmospherics diagnostics a voice.
///
///     Both diagnostic systems compute real conditions and raise real events — and nothing
///     subscribed to them, so ~1000 lines of analysis reached no player. Enabling the CVars made
///     them run; it did not make them observable. This is the consumer: PROVIDENCE reports the
///     station's condition, which is also the most on-theme place for it, since the fiction is a
///     corporate AI that notices things about the station before the crew does.
///
///     ANTI-SPAM IS LOAD-BEARING, not polish. These fire on condition TRANSITIONS, and an idle or
///     lightly-crewed station can flap across a threshold repeatedly. An AI that announces the
///     same brownout six times in a minute reads as broken, and at 0-5 players there is nobody to
///     act on it anyway. So: only degradations and full recoveries speak, never intermediate
///     churn, and a per-channel cooldown suppresses repeats.
/// </summary>
public sealed partial class SolreignDiagnosticVoiceSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color ProvidenceColor = Color.FromHex("#c0a062");
    private const string Sender = "PROVIDENCE";

    private TimeSpan _nextPower = TimeSpan.Zero;

    private SolreignPowerCondition _lastPower = SolreignPowerCondition.Normal;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SolreignPowerDisruptionEvent>(OnPowerDisruption);
    }

    private TimeSpan Cooldown => TimeSpan.FromSeconds(
        Math.Max(30, _cfg.GetCVar(CCVars.SolreignDiagnosticVoiceCooldownSeconds)));

    private void OnPowerDisruption(SolreignPowerDisruptionEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.SolreignDiagnosticVoiceEnabled))
            return;

        // Only speak when the condition actually got worse, or when it returns all the way to
        // nominal. Everything else is churn nobody can act on.
        var worsened = ev.NewCondition > ev.PreviousCondition;
        var recovered = ev.NewCondition == SolreignPowerCondition.Normal
                        && _lastPower != SolreignPowerCondition.Normal;
        _lastPower = ev.NewCondition;

        if (!worsened && !recovered)
            return;

        var now = _timing.CurTime;
        if (now < _nextPower)
            return;
        _nextPower = now + Cooldown;

        var key = recovered
            ? "solreign-diagnostic-power-restored"
            : ev.NewCondition switch
            {
                SolreignPowerCondition.Blackout => "solreign-diagnostic-power-blackout",
                SolreignPowerCondition.Brownout => "solreign-diagnostic-power-brownout",
                _ => "solreign-diagnostic-power-strained",
            };

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(key, ("headroom", (int) Math.Round(ev.BatteryHeadroomPercent))),
            Sender,
            playSound: true,
            colorOverride: ProvidenceColor);
    }
}
