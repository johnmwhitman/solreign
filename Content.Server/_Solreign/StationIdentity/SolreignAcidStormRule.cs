using Content.Server._Solreign.Providence;
using Content.Server.Popups;
using Content.Server.StationEvents.Events;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     "Acid Storm" — the third Solreign "weather-style" station event (see
///     <see cref="SolreignSolarFlareRule"/> and <see cref="SolreignSporeDriftRule"/> for the first two,
///     whose <c>Started</c>/registration shape this class mirrors exactly). Wow-wiring wave feature 2
///     (docs/receipts/wow-wiring/WOW-WIRING-2026-07-16.md) — designed and reviewed with Grok (grk); see
///     that receipt for the design rationale behind every choice below.
///
///     Primarily ATMOSPHERIC, ~60-120 seconds (the <c>SolreignAcidStorm</c> entity prototype's
///     <c>StationEvent</c> component sets <c>duration: 60</c>/<c>maxDuration: 120</c> — the
///     <see cref="StationEventSystem{T}"/> base class rolls the actual length and ends the rule
///     automatically; this class never hand-rolls a timer):
///       * ONE opening screen-fx sting (<see cref="SolreignScreenFxEvent"/>, broadcast — same acid-green
///         screen-border shader <see cref="SolreignSolarFlareRule"/> already uses for its own brand
///         sting) — a single "it begins" hit, not a repeated pulse: the shader is architected as a
///         momentary sting (hard-capped at 5s per trigger), and re-triggering it every few seconds for
///         60-120s would read as a flickering UI glitch, not weather. The sustained atmosphere for the
///         rest of the duration comes from the particle scatter below and the station announcement
///         text, not from re-firing this.
///       * Providence's staged <see cref="ProvidenceLineCategory.EventAcidStorm"/> voice line — fully
///         plumbed already (<c>providence_sounds.yml</c>'s <c>event_acid_storm_*.ogg</c>,
///         <c>ProvidenceVoiceMap</c>), simply never had a call site before this wave.
///       * ONE scatter of acid-green ambient motes at <see cref="Started"/> (reusing
///         <see cref="SolreignSporeParticleComponent"/>/its system VERBATIM — same generic drift
///         component <see cref="SolreignSporeDriftRule"/> uses, just spawning a differently-tinted,
///         longer-lived entity prototype, <c>SolreignAcidMote</c>, instead of a new mechanic). No
///         continuous respawn loop — keeps <c>ActiveTick</c> empty, same "as dumb as Spore Drift" shape,
///         and avoids ever needing a spam-control budget beyond the one-time scatter count.
///
///     ZERO mechanical hazard this wave (matches both sibling events, which are cosmetic/light-infra
///     only) — there is no "breach"/exterior-hazard detection concept anywhere in this codebase to hook
///     a mild damage tick into without inventing new mechanics out of scope for this wave. Deferred: a
///     future wave could add a small hazard scoped to station-exterior/space-adjacent tiles specifically
///     (not a blanket station-wide damage tick), same "documented gap, not silently dropped" idiom this
///     StationIdentity wave already uses elsewhere.
///
///     All-clear is TEXT ONLY (<c>StationEventComponent.EndAnnouncement</c>, handled automatically by the
///     <see cref="StationEventSystem{T}"/> base class's own <c>Ended</c>) — there is no dedicated
///     "all clear" VO recorded for this category, only 3 storm-ARRIVAL lines
///     (<c>event_acid_storm_01/02/03.ogg</c>). Replaying one of those lines at the end would be tonally
///     wrong ("the storm has arrived" phrasing meaning "it's over"), so this class deliberately does
///     NOT override <c>Ended</c> at all — the base class's automatic text-only announcement is exactly
///     the right amount of all-clear, and there is no restored state (like the Solar Flare's flickered
///     APCs) that would require an override anyway.
///
///     Gated by <see cref="CCVars.SolreignAcidStormEnabled"/> (default on) — startable by admins AND,
///     since ALIVENESS P1 #8 (2026-07-16), organically via the random event path (wired into
///     <c>Resources/Prototypes/GameRules/events.yml</c>'s <c>BasicCalmEventsTable</c> at its own low
///     StationEvent weight; its two siblings remain admin-only — see
///     <c>Resources/Prototypes/_Solreign/StationIdentity/game_rules_weather.yml</c>'s header comment).
///     Either start path enters through the same <c>GameTicker</c> rule lifecycle, so there is no
///     earlier decision point upstream of the rule itself (unlike <c>StationDirectiveLayerSystem</c>,
///     which gates before <c>GameTicker.StartGameRule</c> is ever called) to check the CVar at
///     instead. A SINGLE check
///     inside <see cref="Started"/> is not enough on its own, caught during this wave's own Grok
///     review: <c>StationEventSystem{T}.Added</c> (fired BEFORE <c>Started</c>, unconditionally,
///     dispatching <c>StartAnnouncement</c>/<c>StartAudio</c>) and its <c>Ended</c> (fired when
///     <c>ForceEndSelf</c> ends the rule, dispatching <c>EndAnnouncement</c>) both run outside
///     <see cref="Started"/>'s control — a CVar-off admin-start would otherwise still announce
///     "an external contaminant plume is moving across the hull" and then immediately announce
///     "conditions normalizing" with zero actual effects, which is not a silent kill switch. This
///     class therefore ALSO overrides <see cref="Added"/> and <see cref="Ended"/>, each skipping
///     <c>base</c> entirely while the CVar is off, so a disabled Acid Storm produces no announcement
///     text of any kind, not just no FX/VO/motes.
///
///     Double-start (an admin starting a second Acid Storm while one is already running) is accepted as
///     harmless: unlike <see cref="SolreignSolarFlareRule"/> (which restores flickered APCs in
///     <see cref="Ended"/> and would need a mutex if two instances raced on the same APC set), this rule
///     holds NO shared mutable state to restore — a second instance just means a second sting, a second
///     VO line, and a second mote scatter layered on top of the first. Cosmetic overlap only.
///
///     Ghosts/observers/admins spectating DO see the screen-fx sting (it's a station-wide broadcast, same
///     as the Solar Flare's) — deliberately NOT filtered, unlike the wow-wiring wave's OTHER feature
///     (Providence's personal first-shift address, which explicitly must never hit a ghost's view). A
///     weather event is environmental, not a personal address; there is no reason to hide station
///     weather from someone spectating the station. Same reasoning for a player joining mid-event: they
///     may miss the opening sting/VO/scatter entirely and only see whatever motes are still drifting —
///     acceptable for an atmospheric event, no join-sync attempted.
/// </summary>
public sealed partial class SolreignAcidStormRule : StationEventSystem<SolreignAcidStormRuleComponent>
{
    /// <summary>Matches the <c>SolreignAcidStorm</c> entity prototype id — see
    /// <see cref="SolreignSolarFlareRule.EventPrototypeId"/> for why this constant exists.</summary>
    public const string EventPrototypeId = "SolreignAcidStorm";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SolreignStationMoodSystem _mood = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;
    [Dependency] private PopupSystem _popup = default!;

    /// <summary>
    ///     Skips the base class's automatic <c>StartAnnouncement</c>/<c>StartAudio</c> dispatch entirely
    ///     while <see cref="CCVars.SolreignAcidStormEnabled"/> is off — this fires BEFORE
    ///     <see cref="Started"/> (which is where the CVar was originally checked), so that check alone
    ///     could never suppress this announcement; it had already gone out by the time <see cref="Started"/>
    ///     runs. Silent-kill-switch fix from this wave's Grok review.
    /// </summary>
    protected override void Added(EntityUid uid, SolreignAcidStormRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        if (!_cfg.GetCVar(CCVars.SolreignAcidStormEnabled))
            return;

        base.Added(uid, component, gameRule, args);
    }

    /// <summary>
    ///     Mirror of <see cref="Added"/>'s skip, for the same reason on the other end: when
    ///     <see cref="Started"/>'s <c>ForceEndSelf</c> ends this rule while disabled, the base class's
    ///     automatic <c>EndAnnouncement</c> dispatch would otherwise still fire — an all-clear for a
    ///     storm that, from the CVar-off admin's perspective, never actually started.
    /// </summary>
    protected override void Ended(EntityUid uid, SolreignAcidStormRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        if (!_cfg.GetCVar(CCVars.SolreignAcidStormEnabled))
            return;

        base.Ended(uid, component, gameRule, args);
    }

    protected override void Started(EntityUid uid, SolreignAcidStormRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!_cfg.GetCVar(CCVars.SolreignAcidStormEnabled))
        {
            // Disabled by CVar: end immediately rather than silently running inert for the full
            // 60-120s duration in admin tooling. Added()/Ended() above already skip their own
            // announcement dispatch while this CVar is off -- this call's job is only to stop the
            // rule from actually running (FX/VO/motes below, and the 60-120s active window).
            ForceEndSelf(uid, gameRule);
            return;
        }

        RaiseNetworkEvent(new SolreignScreenFxEvent(component.ScreenFxDurationSeconds));
        _providence.PlayLine(ProvidenceLineCategory.EventAcidStorm);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        if (!TryComp<StationDataComponent>(chosenStation.Value, out var data))
            return;

        var preferred = _mood.IsWeatherEventPreferred(chosenStation.Value, EventPrototypeId);
        var count = preferred ? component.PreferredMoteCount : component.MoteCount;

        for (var i = 0; i < count; i++)
        {
            if (!TryFindRandomTileOnStation((chosenStation.Value, data), out _, out _, out var coords))
                continue;

            var mote = Spawn(component.MoteEffectPrototype, coords);

            if (component.GlowPopup is { } popup)
                _popup.PopupEntity(Loc.GetString(popup), mote, PopupType.Small);
        }
    }
}
