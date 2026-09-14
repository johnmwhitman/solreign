You are reviewing a design for a new feature in a Space Station 14 fork called SOLREIGN (C#, Robust Toolbox engine, server-authoritative ECS, YAML entity prototypes). You have no other context than what's pasted below — treat this as a complete spec.

BACKGROUND — PROVIDENCE is the station's sinister-corporate AI announcer (think a dystopian HR/onboarding voice, PG-13, unsettling-corporate NOT horror). There is unused recorded VO: `new_player_welcome_01.ogg` / `_02.ogg`, registered as a soundCollection prototype `SolreignProvidenceNewPlayerWelcome` in `providence_sounds.yml`.

EXISTING CODE (already on disk, verified, not hypothetical):

1. `Content.Server/_Solreign/Providence/ProvidenceWelcomeSystem.cs` — already implements "Providence welcomes a brand-new player" as a private, once-per-round beat:

```csharp
private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
{
    if (!_enabled) return;
    if (ev.Silent) return; // mirrors vanilla job-greet-station-name Silent check
    var guid = ev.Player.UserId.UserId;
    if (!_gate.CanFire(guid)) return; // hard cap: one welcome per player per round
    _gate.MarkFired(guid); // marked BEFORE the async read resolves, anti-race
    LoadWelcome(ev.Mob, guid);
}

private async void LoadWelcome(EntityUid mob, Guid guid)
{
    try
    {
        var career = await _ledger.GetCareerStatsAsync(guid); // SeasonLedgerSystem, SQLite-backed
        if (Deleted(mob)) return;
        if (career.Tours == 0) // "Tours" increments once per player at every round-end this account finished; ==0 means brand-new account, this is their first-ever shift
        {
            var line = FirstShiftKeys[_random.Next(FirstShiftKeys.Length)];
            _popup.PopupEntity(Loc.GetString(line), mob, mob, PopupType.Medium); // private popup, only visible to that player
            _providence.PlayLine(ProvidenceLineCategory.NewPlayerWelcome);
        }
        else { /* welcome-back branch, unrelated to this feature */ }
    }
    catch (Exception e) { Log.Error(...); }
}
```

`ProvidenceWelcomeGate` is a pure `HashSet<Guid>` with `CanFire`/`MarkFired`/`Reset()`, reset on `RoundStartingEvent`/`RoundRestartCleanupEvent`. This is the idiom this codebase always uses for "fire once per player per round, survives reconnect since it's keyed by account Guid not session/entity."

2. `Content.Server/_Solreign/Providence/ProvidenceVoiceSystem.cs`'s `PlayLine`:

```csharp
public void PlayLine(ProvidenceLineCategory category)
{
    if (!_enabled) return;
    var collection = ProvidenceVoiceMap.CollectionFor(category);
    _audio.PlayGlobal(new SoundCollectionSpecifier(collection), Filter.Broadcast(), recordReplay: true);
}
```
This is STATION-WIDE (every connected client hears it) — deliberate, documented design for Providence's other categories (shift start/end, event announcements). But for the "personal welcome" beat, station-wide is wrong: every other player on the station would hear "you're new here" VO meant for one specific person.

3. Screen-FX "brand sting" mechanism (already built, reusable, capped short):
```csharp
// Content.Shared/_Solreign/FX/SolreignScreenFxEvent.cs
[Serializable, NetSerializable]
public sealed class SolreignScreenFxEvent : EntityEventArgs {
    public readonly float Duration; // clamped to [0.1, 5] seconds by SolreignScreenFxTiming
    public SolreignScreenFxEvent(float duration = 1.5f) { Duration = SolreignScreenFxTiming.ClampDuration(duration); }
}
// Content.Client/_Solreign/FX/SolreignScreenFxSystem.cs (client-side)
SubscribeAllEvent<SolreignScreenFxEvent>(OnScreenFx); // holds a pulsing acid-green screen-border overlay for Duration seconds, extending on overlap
```
Today this is always raised via `RaiseNetworkEvent(new SolreignScreenFxEvent(duration))` with NO session argument — meaning it broadcasts to literally every client (e.g. `SolreignSolarFlareRule.Started` uses it this way for a station-wide event). The engine DOES support a targeted single-recipient overload, `RaiseNetworkEvent(EntityEventArgs ev, ICommonSession recipient)`, and the client's `SubscribeAllEvent<T>` receives whatever the server sends it regardless of whether the server broadcasted or targeted it — so calling the targeted overload with just this one player's session, with ZERO client-code changes, would make the pulse visible only to them.

THE NEW FEATURE I'm about to build on top of the above (as an ADDITIVE layer, not modifying the existing tested behavior above):

When a brand-new player (`career.Tours == 0`) spawns for their first-ever shift, a few seconds after the existing immediate popup+station-wide-VO already fires (so it lands after the spawn rush, not stacked on it), Providence delivers ONE more, distinctly personal beat:
- A private chat/popup line that addresses the player BY THEIR CHARACTER NAME, e.g. "Employee { $name }. Your onboarding is logged." (PG-13, unsettling-corporate, not horror — corporate surveillance vibe, not threatening)
- The SAME `new_player_welcome` VO collection, but played TARGETED to just that player's session (not station-wide this time — reusing `PlayLine`'s collection-resolution logic but with `Filter.SinglePlayer(session)` instead of `Filter.Broadcast()`)
- One subtle screen-fx pulse (reusing `SolreignScreenFxEvent`/`SolreignAcidBorderOverlay` exactly as-is, just raised via the targeted session overload instead of broadcast)
- All three gated behind a NEW CVar `solreign.providence.first_shift_welcome` (default TRUE, acting purely as a kill switch for this delayed personal-address layer — separate from the existing `solreign.providence.welcome_enabled` CVar which already gates the immediate generic popup+VO and will keep working unchanged)

Implementation sketch: hook the SAME `career.Tours == 0` branch inside `LoadWelcome` (I already have `mob`, `guid` in scope there) to schedule a delayed follow-up (checked via a scheduled-time list polled in `Update(float frameTime)` — same idiom `ProvidenceVoiceSystem` already uses for its "idle musings" 20-40 minute random timer via `IGameTiming`/`PeriodicEffectTiming`). At fire time: re-resolve the player's session and mob (they may have disconnected, died, or the entity may be deleted in the intervening few seconds — must handle gracefully, no throw, no popup-to-nowhere), fetch the character's display name (likely `MetaData(mob).EntityName`), and fire the three effects above.

Player data / session tracking in this codebase is via `Robust.Server.Player.IPlayerManager` (`TryGetSessionById(NetUserId, out ICommonSession)`), and mob liveness via `EntityManager.Deleted(uid)` / `TryComp<T>`.

Ghost/ObserverExclusion: this codebase's convention is `HasComp<GhostComponent>(uid)` to check for ghost/observer status; there's no existing explicit ghost-check inside ANY Providence system today because `PlayerSpawnCompleteEvent` only fires for a genuine live mob spawn, never a ghost — but my delayed second beat fires several seconds LATER, so the player could plausibly have died and become a ghost in that window before my delayed callback runs.

YOUR TASK: Spec this feature precisely (data structures, exact hook points, exact CVar semantics) AND aggressively name every failure mode you can find, specifically covering:
- Spam / fatigue (can this fire more than once per player per round? per session? what if they reconnect?)
- Repeat-fire on reconnect specifically (mid-delay window: player disconnects after the immediate beat but before the delayed personal beat fires — then reconnects with a NEW session object but the SAME account Guid — does the delayed callback need to re-resolve the session, and what happens if it can't?)
- Mid-round-join edge cases (a new player who joins mid-round rather than at round start — does anything above assume round-start timing that would break?)
- Ghost/observer leakage (player dies or ghosts before the delayed beat fires — should the popup/audio/screen-fx still land on their now-ghost view? on their corpse? should it just silently no-op?)
- PG-13/tone check on "Employee { $name }. Your onboarding is logged." and any other phrasing you'd suggest — must read as sinister-corporate, never actually threatening or horror
- Anything about the "CVar default TRUE, kill switch" contract that could be more precisely specified
- Whether reusing the broadcast-oriented `SolreignScreenFxEvent`/overlay for a single targeted player via the targeted `RaiseNetworkEvent` overload is actually safe/correct, or if you see a reason it wouldn't work as I've described

Be concrete and terse. I will implement based on your answer, then send you a follow-up with the actual diff for review.
