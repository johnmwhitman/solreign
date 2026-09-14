using Content.Server._Solreign.StationDirective.Components;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Configuration;

namespace Content.Server._Solreign.StationDirective;

/// <summary>
///     "Station Directive" a.k.a. random corporate round modifiers (Solreign, SR-W-081 follow-up to the
///     churn-insight v1: persistence should feel social + present, not just a private ledger number). A
///     layerable, cosmetic-first game rule that runs every round on top of whatever preset is playing,
///     same additive idiom as <c>SolreignCorporateRuleSystem</c>. On start it picks one Corporate
///     Directive from the rotating set (see <see cref="StationDirectiveCatalog"/>) and announces it over
///     the PA in the Solreign HR voice; periodically it tickers a flavor progress line for that directive.
///     A handful of directives additionally pair their announcement with Solreign's existing brand-sting
///     screen overlay (<see cref="StationDirectiveDefinition.ScreenFxDurationSeconds"/>) — the only
///     mechanical primitive in play, reused as-is from <c>SolreignSolarFlareRule</c> / the Corporate
///     Ladder's earnings call. No new metric tracking, no Standing/currency mutation, no targeting of
///     individual players — just a shared, present, station-wide "thing to care about" this shift.
///
///     Layering is handled without touching core presets by <see cref="StationDirectiveLayerSystem"/>,
///     which starts this rule on <c>RoundStartingEvent</c> (and honors
///     <see cref="CCVars.SolreignStationDirectiveEnabled"/> — the kill switch: flip that CVar off to
///     disable the whole layer for the round; there is no separate gamerule-removal mechanism needed
///     because <c>StationDirectiveLayerSystem.OnRoundStarting</c> simply never starts the rule when it
///     reads false).
/// </summary>
public sealed partial class StationDirectiveRuleSystem : GameRuleSystem<StationDirectiveRuleComponent>
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>Sender label the PA stamps on every Station Directive broadcast (same HR voice as the Corporate Ladder).</summary>
    private string HrSender => Loc.GetString("solreign-station-directive-hr-sender");

    /// <summary>Cached mirror of <see cref="CCVars.SolreignStationDirectiveTickerIntervalSeconds"/>, live-updated so an admin can retune cadence mid-round.</summary>
    private float _tickerIntervalSeconds;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignStationDirectiveTickerIntervalSeconds, v => _tickerIntervalSeconds = v, invokeImmediately: true);
    }

    protected override void Started(EntityUid uid, StationDirectiveRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.DirectiveIndex = StationDirectiveSelection.SelectDirectiveIndex(GameTicker.RoundId, StationDirectiveCatalog.Directives.Count);
        component.TickerLineIndex = 0;
        component.SinceLastTicker = 0f;

        var directive = StationDirectiveCatalog.Directives[component.DirectiveIndex];

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(directive.AnnouncementLocKey),
            HrSender,
            playSound: true,
            colorOverride: Color.FromHex("#c0a062"));

        // Optional mechanical beat for a handful of directives (e.g. Safety Inspection, Surveillance
        // Sweep): the same already-shipped, engine-clamped, self-clearing screen overlay SolarFlareRule
        // and the Corporate Ladder's earnings call already raise. Most directives leave this null and are
        // announcement/ticker text only.
        if (directive.ScreenFxDurationSeconds is { } fxDuration)
            RaiseNetworkEvent(new SolreignScreenFxEvent(fxDuration));
    }

    protected override void ActiveTick(EntityUid uid, StationDirectiveRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        component.SinceLastTicker += frameTime;
        if (!StationDirectiveSelection.ShouldFireTicker(component.SinceLastTicker, _tickerIntervalSeconds))
            return;

        component.SinceLastTicker = 0f;
        BroadcastTicker(component);
    }

    /// <summary>Reads out the next flavor progress line for the round's chosen directive, then advances to the next line so consecutive tickers don't repeat.</summary>
    private void BroadcastTicker(StationDirectiveRuleComponent component)
    {
        var directive = StationDirectiveCatalog.Directives[component.DirectiveIndex];
        if (directive.TickerLocKeys.Count == 0)
            return;

        var line = Loc.GetString(directive.TickerLocKeys[component.TickerLineIndex]);
        _chat.DispatchGlobalAnnouncement(line, HrSender, playSound: false, colorOverride: Color.FromHex("#c0a062"));

        component.TickerLineIndex = StationDirectiveSelection.NextTickerIndex(component.TickerLineIndex, directive.TickerLocKeys.Count);
    }
}
