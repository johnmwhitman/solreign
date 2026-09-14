You are reviewing a design for a new mid-round station event in a Space Station 14 fork called SOLREIGN (C#, Robust Toolbox engine, server-authoritative ECS, YAML entity prototypes, "station events" = timed GameRule entities). You have no other context than what's pasted below — treat this as complete.

BACKGROUND — PROVIDENCE is the station's sinister-corporate AI announcer, PG-13, unsettling-corporate NOT horror. There is unused recorded VO: `event_acid_storm_01/02/03.ogg`, already registered as soundCollection `SolreignProvidenceEventAcidStorm` in `providence_sounds.yml`, and an enum value `ProvidenceLineCategory.EventAcidStorm` already exists with NO call site yet (fully staged, unused). There is no separate "all clear" audio recorded for this category — only the 3 storm-start lines.

THE FEATURE: a new "Acid Storm" station event. Primarily ATMOSPHERIC — mild hazard at most, duration ~60-120 seconds, then an all-clear moment. Must feel like a real weather-style event, not a combat encounter.

EXISTING CODE PRECEDENT (already on disk, verified — this codebase already ships two near-identical "weather-style" Solreign station events I'm meant to mirror, not reinvent):

```csharp
// Content.Server/_Solreign/StationIdentity/SolreignSolarFlareRule.cs — a few-seconds "weather" event
public sealed partial class SolreignSolarFlareRule : StationEventSystem<SolreignSolarFlareRuleComponent>
{
    public const string EventPrototypeId = "SolreignSolarFlare";
    [Dependency] private ApcSystem _apcSystem = default!;
    [Dependency] private SolreignStationMoodSystem _mood = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;

    protected override void Started(EntityUid uid, SolreignSolarFlareRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        RaiseNetworkEvent(new SolreignScreenFxEvent(component.ScreenFxDurationSeconds)); // acid-green screen-border pulse, BROADCAST to all clients
        _providence.PlayLine(ProvidenceLineCategory.EventAudit);
        if (!TryGetRandomStation(out var chosenStation)) return;
        var preferred = _mood.IsWeatherEventPreferred(chosenStation.Value, EventPrototypeId);
        var toFlicker = preferred ? component.PreferredApcsToFlicker : component.ApcsToFlicker;
        // ... flickers a few APCs off, restores them in Ended() ...
    }
    protected override void Ended(...) { /* restores flickered APCs */ }
}
```

```csharp
// Content.Server/_Solreign/StationIdentity/SolreignSporeDriftRule.cs — scatters harmless drifting particles
public sealed partial class SolreignSporeDriftRule : StationEventSystem<SolreignSporeDriftRuleComponent>
{
    public const string EventPrototypeId = "SolreignSporeDrift";
    [Dependency] private SolreignStationMoodSystem _mood = default!;
    [Dependency] private PopupSystem _popup = default!;

    protected override void Started(EntityUid uid, SolreignSporeDriftRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        if (!TryGetRandomStation(out var chosenStation)) return;
        if (!TryComp<StationDataComponent>(chosenStation.Value, out var data)) return;
        var preferred = _mood.IsWeatherEventPreferred(chosenStation.Value, EventPrototypeId);
        var count = preferred ? component.PreferredSporeCount : component.SporeCount;
        for (var i = 0; i < count; i++)
        {
            if (!TryFindRandomTileOnStation((chosenStation.Value, data), out _, out _, out var coords)) continue;
            var spore = Spawn(component.SporeEffectPrototype, coords); // spawns "SolreignAmbientSpore" entity
            if (component.GlowPopup is { } popup) _popup.PopupEntity(Loc.GetString(popup), spore, PopupType.Small);
        }
    }
}
```
The spawned particle entity (`SolreignAmbientSpore`, in `spores.yml`) is a self-contained cosmetic mote: `SolreignSporeParticleComponent` (rolls its own random drift velocity on MapInit, a simple position nudge each tick, NO physics/collision, so it can never push/block/collide with anything) + `TimedDespawn lifetime: 8` + a small `Sprite`/`PointLight` tinted green. The component/system is generic — it doesn't know or care what color the sprite is; a NEW entity prototype that reuses the SAME `SolreignSporeParticleComponent`/system but with a different (acid-green/toxic) sprite tint would be "reuse," not "reinvent."

Both events are registered the SAME way, side by side, in `Resources/Prototypes/_Solreign/StationIdentity/game_rules_weather.yml`:
```yaml
- type: entityTable
  id: SolreignWeatherEventsTable
  table: !type:AllSelector
    children:
    - id: SolreignSolarFlare
    - id: SolreignSporeDrift

- type: entity
  id: SolreignSolarFlare
  parent: BaseGameRule
  components:
  - type: StationEvent
    weight: 8
    startAnnouncement: station-event-solreign-solar-flare-start-announcement
    endAnnouncement: station-event-solreign-solar-flare-end-announcement
    startAudio: { path: /Audio/Announcements/attention.ogg }
    duration: 3
    maxDuration: 5
  - type: SolreignSolarFlareRule
```
Neither event is wired into upstream `Resources/Prototypes/GameRules/events.yml`'s random-selection tables — no per-station event-scheduling concept exists anywhere in this engine/codebase for a per-map preference to plug into yet (confirmed by reading the actual scheduler). Both are admin-startable only (`event start SolreignSolarFlare`). This is Solreign's own established precedent, not a gap I need to fix — my new event should follow the exact same "own YAML file, own weight, added to the SAME `SolreignWeatherEventsTable`, admin-startable, not touching upstream `events.yml`" shape.

`StationEventComponent`'s real fields include `Weight` (float; named constants `WeightVeryLow=0, WeightLow=5, WeightNormal=10, WeightHigh=15, WeightVeryHigh=20`; observed range in the real random-table events.yml is roughly 1–15, with "1" explicitly commented `# rare`), `StartAnnouncement`/`EndAnnouncement` (Loc keys, handled automatically by the `StationEventSystem<T>` base class's `Added`/`Ended` — I don't write that plumbing myself), `StartAudio`/`EndAudio` (a generic non-Providence chime, e.g. `/Audio/Announcements/attention.ogg` — separate from the Providence VO line, which I call myself via `_providence.PlayLine`), `Duration`/`MaxDuration` (`TimeSpan?`, base class rolls a random value in that range and ends the rule automatically when it elapses — I don't have to hand-roll a timer for a fixed-length event... except THIS event needs to be MUCH longer, ~60-120 SECONDS, unlike the existing two which are only 1-5 seconds).

Screen-FX brand sting (already exists, reusable): `Content.Shared._Solreign.FX.SolreignScreenFxEvent` — raised via `RaiseNetworkEvent(new SolreignScreenFxEvent(duration))`, station-wide broadcast, holds a pulsing acid-green screen-BORDER overlay (not a full-screen tint, just the edges) for `duration` seconds, HARD-CAPPED at 5 seconds max per single trigger (`SolreignScreenFxTiming.MaxDuration = 5f`) — explicitly designed as a momentary "sting," not a sustained ambient effect. Overlapping triggers EXTEND the hold rather than stacking (so re-triggering it while already active just refreshes the countdown, doesn't double up).

`ForceEndSelf(EntityUid uid, GameRuleComponent? component = null)` exists on the base `GameRuleSystem` class — an existing event (`GasLeakRule`) calls this from inside `ActiveTick` to abort itself early under a bad-state condition, gracefully ending the rule (not throwing). This is the idiom for "this event should not actually run" (e.g. a disabled-by-CVar check) — call it from `Started()` to make an admin-started-but-disabled event immediately end itself rather than run for its full duration doing nothing (or doing something anyway).

Sibling GameRule `StationDirectiveLayerSystem` shows the CVar kill-switch convention for a Solreign game rule (not this exact StationEventSystem<T> shape, but same codebase convention): check the CVar BEFORE the rule is allowed to actually start, own partial CCVars file, `CVar.SERVERONLY`, default true.

YOUR TASK — spec this feature precisely and name failure modes. Specific open questions I need your judgment on:

1. **Sustained visual over 60-120s given the screen-fx shader's hard 5-second cap per trigger**: should I (a) fire ONE opening screen-fx sting at Started() (a few seconds, like the existing two events) and rely on the ambient particle scatter + station announcement text for the rest of the duration, or (b) periodically re-trigger the screen-fx event every N seconds during `ActiveTick` to create a recurring "pulsing storm" rhythm across the full duration? If (b), what's a tasteful re-trigger interval and does re-triggering a broadcast screen event repeatedly for 60-120s risk reading as spammy/nauseating rather than atmospheric?
2. **Gameplay/damage**: the brief says "mild hazard at most (e.g. brief minor damage only to players outside/near breaches, or none at all this wave)" and explicitly permits doing NOTHING mechanical this wave, matching this codebase's own precedent (both existing weather events do zero damage — SolarFlare only flickers APCs, SporeDrift is 100% cosmetic). Given no "breach" detection concept exists anywhere in this codebase to hook into, and given the instruction to reuse/not invent new mechanics this wave, do you agree the right call is ZERO damage this wave (pure atmosphere: announcement + VO + screen-fx + ambient particles), with a documented note that hazard is a deferred future wave, exactly mirroring how the two existing events already document their own deferred scope? Or is skipping damage entirely a cop-out here?
3. **All-clear**: there is no dedicated "all clear" VO recorded for this category (only 3 storm-STARTING lines exist in the sound collection) — so the "all-clear line" can only be the text-only `endAnnouncement` Loc string (handled automatically by the `StationEventSystem<T>` base class), NOT a second Providence voice line. Confirm this is the honest, correct call given what's actually recorded, rather than something I should route around (e.g. reusing one of the 3 storm-start lines again at the end, which would be tonally wrong — "the storm has arrived" phrasing played again to mean "it's over").
4. **CVar gating for an ADMIN-STARTABLE-ONLY event (not in any random table)**: given there's no "should this even be allowed to start" decision point upstream of the rule itself (unlike `StationDirectiveLayerSystem`, which gates before `GameTicker.StartGameRule` is ever called), is `ForceEndSelf` inside `Started()` (immediately ending the rule right after it starts, if the CVar is off) the right idiom, or would you gate differently?
5. **Failure modes** — name and address: spam/repeat-fire (can an admin or a future scheduler start a second Acid Storm while one is already running, and if so what happens — do effects stack, does one event's `Ended()` clobber the other's restored state?), ghost/observer leakage (screen-fx is a station-wide broadcast to EVERY connected client including ghosts/observers/admins spectating — is that fine for an atmospheric weather event, unlike a personal address beat which explicitly should NOT hit ghosts?), mid-round-join (a player joining mid-event — do they immediately see the ongoing screen-fx/particles or do they miss it entirely, and does that matter for an atmospheric event?), and PG-13 tone check on treating literal "acid" imagery (green screen tint + "acid storm" framing) as unsettling-corporate rather than body-horror.
6. Anything else you'd flag before I implement.

Be concrete and terse. I will implement based on your answer, then send you a follow-up with the actual diff for review.
