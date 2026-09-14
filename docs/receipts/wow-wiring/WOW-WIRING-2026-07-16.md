# WOW-WIRING — 2026-07-16

Two player-visible "wow" features built on verified unused Providence VO (`Resources/Audio/_Solreign/Providence/`), each its own commit, in worktree `~/AI/solreign-trees/wow-wiring` (branch `feat/wow-wiring`, based on `master` @ `055b89858a`).

Design + review for both features were done with Grok (`~/AI/Tools/grk`) — full prompts/responses are in this directory (`grk-f1-*`, `grk-f2-*`).

## Feature 1 — "Providence welcomes you personally"

Commit: `aa61b6095e` — `feat(providence): personal first-shift welcome beat (wow-wiring F1)`

### What was found before building

The mission's grep hints (`FirstShift`, `first_shift_assignments_enabled`) point at `Content.Server/_Solreign/PlayerDelight/FirstShift/FirstShiftSystem.cs` — but that system is an unrelated UI/tutorial "assignment card" minigame (department orientation beacon), gated by `CCVars.SolreignFirstShiftAssignmentsEnabled`, with no spawn hook and no audio at all.

The system that actually matches the mission's INTENT ("PROVIDENCE welcomes a new player personally on their first shift") already existed and was already built: `Content.Server/_Solreign/Providence/ProvidenceWelcomeSystem.cs`. It hooks `PlayerSpawnCompleteEvent`, detects a brand-new account via `SeasonLedgerSystem.GetCareerStatsAsync(guid).Tours == 0`, and already fires an immediate private popup + a station-wide `PlayLine(NewPlayerWelcome)` VO line, once per player per round (`ProvidenceWelcomeGate`).

Two real gaps versus the mission spec:
1. The existing VO was **station-wide** (`PlayGlobal` + `Filter.Broadcast()`), not targeted to the one new player — mission explicitly wants targeted/PVS-scoped audio.
2. No character-name address ("Employee [Name]. Your onboarding is logged.") and no screen-fx pulse.

Rather than duplicate the well-tested "new player" detection/gate machinery, this wave adds an ADDITIVE, DELAYED (8s) personal layer on top of the existing system, gated by its own new CVar.

### What was built

- `Content.Shared/CCVar/CCVars.SolreignProvidenceFirstShiftWelcome.cs` — new CVar `solreign.providence.first_shift_welcome`, `CVar.REPLICATED | CVar.SERVER`, default **true**. Independent kill switch: if off, only the new delayed personal layer is suppressed — the existing immediate welcome is untouched.
- `Content.Server/_Solreign/Providence/ProvidenceFirstShiftPersonalQueue.cs` — pure (engine-free), unit-tested scheduling queue. Never stores an `ICommonSession` (sessions die on disconnect); schedules by account `Guid` + a fire time, `DrainDue(now)` removes-then-returns due entries (never double-fires the same entry).
- `Content.Server/_Solreign/Providence/ProvidenceVoiceSystem.cs` — new `PlayLineTo(category, ICommonSession)` method using the session-targeted `PlayGlobal` overload (confirmed by Grok to be `Filter.SinglePlayer` + `recordReplay: false` under the hood — never broadcasts, never leaks into replays).
- `Content.Server/_Solreign/Providence/ProvidenceWelcomeSystem.cs` — on the existing `career.Tours == 0` branch, schedules the delayed personal beat (if the new CVar is on). `Update()` drains due entries and fires them via `FireFirstShiftPersonal`, which:
  1. Re-checks the CVar live (mid-flight kill).
  2. Re-resolves the session by account Guid (never trusts a session/mob captured 8s earlier).
  3. Requires the session's **currently attached entity** (drops entirely if none — no stale-mob fallback, per Grok's review finding).
  4. Drops silently on `GhostComponent` or `MobState.Dead`.
  5. Addresses the player by `MetaData(target).EntityName`, plays the SAME `NewPlayerWelcome` VO collection but targeted, and raises `SolreignScreenFxEvent` via the session-targeted `RaiseNetworkEvent` overload (existing acid-green screen-border pulse, reused verbatim — no client code touched).
- `Resources/Locale/en-US/_solreign/providence-welcome.ftl` — 4 new personal-address lines + 1 nameless fallback, PG-13 corporate-surveillance tone ("Employee { $name }. Your onboarding is logged.").

### Real bug caught by testing (not by Grok's review)

First integration test run failed: `FirstShiftWelcomeCvar_Off_NeverSchedulesAnything` expected queue count 0, got 1 — even though the CVar was verified `false` at spawn time via a diagnostic assertion. Root cause: a pool-connected test session (`DummyTicker=false`) goes through a REAL spawn during test-pool setup, *before* the test body runs — with the new CVar defaulting to true, that real pre-test spawn already scheduled a pending personal beat, and the pre-existing per-round anti-fatigue gate then silently blocked the test's own synthetic spawn from scheduling a second one. Fixed with `ProvidenceWelcomeSystem.ResetRoundStateForTests()` (same two `Reset()`/`Clear()` calls the system already makes on round start/restart), called at the top of every test before touching CVars or firing a synthetic spawn.

### Grok's most important catch

On review of the diff, Grok flagged that `FireFirstShiftPersonal`'s fallback (`session.AttachedEntity ?? SpawnMob`) could still deliver VO/screen-fx to a session with **no live attached entity** (lobby, SSD) while the popup silently failed on a detached body — a real, if rare, correctness gap. Fixed by requiring `session.AttachedEntity` outright and dropping (no fallback) if null.

### Pre-existing repo drift fixed to obtain a working test baseline (unrelated to this feature)

`Content.IntegrationTests` did not compile at all on `master` before this wave, due to two unrelated pre-existing breaks (verified via `git stash -u` + rebuild on clean master):
- `StationDirectiveIntegrationTest.cs:92` — RobustToolbox recently added `[ForbidLiteral]` to `GameTicker.StartGameRule(string)` (analyzer rule RA0033), and this pre-existing test passed a raw string literal. Fixed with a named `const string` field (analyzer-verified to be allowed: a const/readonly-static reference is not "a literal" per `ForbidLiteralAnalyzerTest`).
- `SolreignMapHealthTypesTest.cs:18` — missing `#nullable enable`, using a `?` nullable annotation outside a nullable context (`CS8632`). Added the directive.

Without these two fixes, **zero** integration tests (not just this wave's) could run on this worktree. Both fixes are one-line, mechanical, and don't change either test's behavior — verified by running both files' full test suites green after the fix.

### CVars

| CVar | Default | Flags | Role |
|---|---|---|---|
| `solreign.providence.welcome_enabled` | true | SERVERONLY | Pre-existing — gates the immediate popup + station-wide VO. Unchanged. |
| `solreign.providence.first_shift_welcome` | **true** | REPLICATED \| SERVER | New — kill switch for the delayed, targeted, name-addressed personal layer only. |

### Tests

- `Content.Tests/_Solreign/ProvidenceFirstShiftPersonalQueueTests.cs` — 6 pure unit tests (schedule/drain timing, never-double-fire, per-entity independence, Clear, no auto-dedup).
- `Content.IntegrationTests/Tests/_Solreign/ProvidenceFirstShiftWelcomeIntegrationTest.cs` — 4 integration tests: fresh account schedules exactly one beat; CVar-off is a full kill switch; firing against a ghost is a silent no-op that still drains the entry (never retries/double-fires); silent spawn (cryo return) never schedules.

### First live shift observation list (things to eyeball on a real playtest)

- A genuinely new account's first spawn: immediate popup fires right away, then ~8s later a second, distinctly personal popup addressing the character by name, a quiet VO line only that player hears, and a brief acid-green screen-border pulse only on their screen.
- A SECOND player on the same station should hear/see NONE of the personal beat's targeted VO/screen-fx (only the FIRST, existing station-wide VO, which everyone still hears — a known, documented, accepted double-VO wart from the pre-existing immediate beat, not introduced by this wave).
- If the new player dies within 8 seconds of spawning (unlikely but possible), the personal beat should silently not fire — no popup on their ghost view.
- Toggling `solreign.providence.first_shift_welcome` to false mid-round should stop new personal beats from firing without touching the existing immediate welcome.

---

## Feature 2 — "Acid Storm" station event

Commit: the commit carrying this receipt — `feat(events): Acid Storm atmospheric station event (wow-wiring F2)` (child of F1's `aa61b6095e`).

### What was found before building

Two existing Solreign "weather-style" station events (`SolreignSolarFlareRule`, `SolreignSporeDriftRule`, `Content.Server/_Solreign/StationIdentity/`) are the direct precedent — same `StationEventSystem<T>` base class, same registration shape (own YAML file, `SolreignWeatherEventsTable` entityTable, admin-startable only via `event start <Id>`, not wired into upstream `Resources/Prototypes/GameRules/events.yml`'s random tables because no per-station scheduler exists anywhere in this codebase yet). Providence's `EventAcidStorm` voice category and its 3-file `SolreignProvidenceEventAcidStorm` sound collection were already fully staged in `providence_sounds.yml`/`ProvidenceVoiceMap` with **zero call sites** — confirmed via `grep -rn "AcidStorm"` returning only the plumbing files.

The acid-green screen-border shader the mission asked me to check for **does exist** (`Resources/Textures/Shaders/_Solreign/acid_screen_border.swsl`, `SolreignScreenFxEvent`/`SolreignAcidBorderOverlay`, already wired client-side) — but it's hard-capped at 5 seconds per trigger (`SolreignScreenFxTiming.MaxDuration`), architected as a momentary "sting," not a sustained ambient effect. Per Grok's design review, the right call for a 60-120s event is ONE opening sting, not a repeated pulse (which would read as a UI glitch, not weather) — the sustained atmosphere comes from the ambient particle scatter and the announcement text instead.

### What was built

- `Content.Shared/CCVar/CCVars.SolreignEventsAcidStorm.cs` — new CVar `solreign.events.acid_storm`, `CVar.SERVERONLY`, default **true**.
- `Content.Server/_Solreign/StationIdentity/SolreignAcidStormRuleComponent.cs` / `SolreignAcidStormRule.cs` — `StationEventSystem<SolreignAcidStormRuleComponent>`, mirroring `SolreignSolarFlareRule`'s shape exactly. `Started()`:
  1. If the CVar is off, `ForceEndSelf` immediately (no admin-startable-only event has an earlier gate point to check at) and returns before any other side effect runs.
  2. Raises one `SolreignScreenFxEvent` (station-wide broadcast, ~4s sting).
  3. Plays Providence's `EventAcidStorm` VO line (`_providence.PlayLine`) — the first-ever call site for this staged category.
  4. Picks a random station, scatters 10 (or 20 if the station prefers this event via `SolreignStationMoodComponent`) acid-green ambient motes across random tiles.
  5. No `Ended()` override — the base `StationEventSystem<T>.Ended` already dispatches the text-only `endAnnouncement` automatically, which is the ENTIRE all-clear (no VO replay — there is no dedicated "all clear" audio recorded, only 3 storm-ARRIVAL lines, and replaying one of those at the end would be tonally backwards).
- `Resources/Prototypes/_Solreign/Entities/StationIdentity/spores.yml` — new `SolreignAcidMote` entity prototype. **Reuses `SolreignSporeParticleComponent`/its system verbatim** (same generic drift primitive `SolreignAmbientSpore` uses — no physics, no collision, a self-rolled random drift velocity, `TimedDespawn`) — just a differently-tinted (acid-green `#39FF14` vs bioluminescent-teal `#7CFFB2`), longer-lived (30s vs 8s, so motes stay visible longer into the storm's much longer window) entity. Zero new C# for the particle mechanic itself.
- `Resources/Prototypes/_Solreign/StationIdentity/game_rules_weather.yml` — new `SolreignAcidStorm` entity prototype (`weight: 3` — deliberately below both siblings, per the mission's explicit "weight it rare" instruction; `duration: 60`/`maxDuration: 120`, matching the ~60-120s mission ask via the base class's own random-duration roll, no hand-rolled timer), added to the existing `SolreignWeatherEventsTable`.
- `Resources/Locale/en-US/_solreign/station_identity.ftl` — start/end announcements + a glow-popup line, corporate-euphemism framing throughout ("external contaminant plume", "neutralizing agents deployed", "contaminant levels have returned to standard parameters") — never melting/burning/body-horror imagery, confirmed on Grok's tone review.

### Gameplay / hazard

**Zero mechanical damage this wave** — matches both sibling events (SolarFlare only flickers APCs, SporeDrift is purely cosmetic). There is no "breach"/exterior-hazard detection concept anywhere in this codebase to hook a "mild damage near breaches" behavior into without inventing new mechanics out of scope for this wave — confirmed as the right call (not a cop-out) by Grok's design review, given the mission text explicitly permits "or none at all this wave." Deferred: a future wave could add a small hazard scoped specifically to station-exterior/space-adjacent tiles.

### Grok's most important catch (this feature)

On review of the diff, Grok flagged that gating only inside `Started()` is NOT a full kill switch for a `StationEventSystem<T>` rule: the base class's `Added` (fires BEFORE `Started`, unconditionally) already dispatches the `StartAnnouncement`/`StartAudio`, and its `Ended` (fired when `ForceEndSelf` ends the rule) dispatches the `EndAnnouncement` — both entirely outside `Started`'s control. Without a fix, a CVar-off admin-start would still announce "an external contaminant plume is moving across the hull," immediately followed by "conditions normalizing," with zero real effects — not silent. Fixed by also overriding `Added`/`Ended` in `SolreignAcidStormRule` to skip `base` entirely while the CVar is off, so a disabled event produces no announcement text of any kind, not just no FX/VO/motes.

Grok also caught a real must-fix unrelated to gating: `SolreignAcidMote` (the new ambient-particle entity, spawned only from C#) needed an entry in `Resources/_Solreign/orphan_allowlist.yml` — this codebase's orphan-reachability gate (`SolreignOrphanReachabilityTest`) does not parse C#-only spawn paths, and would have failed CI without it (mirrors the existing `SolreignAmbientSpore` entry for the exact same reason). Added and verified green.

Two soft nits from the same review were also applied: the `spores.yml` comment claiming motes "stay visible across the storm's much longer 60-120s window" was corrected (they deliberately do NOT — 30s lifetime, one scatter at start, no continuous respawn, matching the design verdict to keep `ActiveTick` empty), and the glow-popup line's "hissing faintly against the deck plating" was softened to match the more restrained corporate-euphemism tone of the announcement text.

### CVar

| CVar | Default | Flags | Role |
|---|---|---|---|
| `solreign.events.acid_storm` | **true** | SERVERONLY | Kill switch — checked in `Added`, `Started`, AND `Ended` (see above) so a disabled event produces zero announcement text and zero effects, not just a rule that stops running. |

### Tests

- `Content.IntegrationTests/Tests/_Solreign/SolreignAcidStormIntegrationTest.cs` — 2 integration tests: the rule starts live without throwing and is actually active (`IsGameRuleActive<SolreignAcidStormRuleComponent>`) with the CVar on; with the CVar off, starting the rule never throws and the rule is immediately NOT active (`ForceEndSelf` took effect, not just scheduled for later). The announcement-suppression side of the CVar fix is verified by code review of the paired `Added`/`Started`/`Ended` checks rather than a chat-capture test, which this codebase has no existing harness for.
- Prototype-id typo safety for `SolreignAcidMote` (referenced from `SolreignAcidStormRuleComponent.MoteEffectPrototype`) is already covered by the pre-existing `SolreignPrototypeIdIntegrityTest` — no new pure test needed.
- Orphan reachability (`SolreignOrphanReachabilityTest`) — verified green after adding `SolreignAcidMote` to the allowlist.
- Every `ProvidenceLineCategory` (including `EventAcidStorm`) already has regression coverage via the pre-existing `ProvidenceVoiceSystemIntegrationTest.PlayLine_ForEveryLiveCategory_DoesNotThrow` test-case-source — confirmed still green.

### First live shift observation list

- `event start SolreignAcidStorm` (admin command) should announce, play a green-flavored screen pulse once, scatter visible drifting acid-green motes across a random station, run for roughly 1-2 minutes, then announce an all-clear with no further VO.
- Toggling `solreign.events.acid_storm` to false and then admin-starting the event should produce NO visible effects and the event should not show as active/running.
- Weight 3 means this should feel meaningfully rarer than the Solar Flare/Spore Drift siblings if/when a future scheduler wave wires all three into automatic rotation.

---

## Test/linter numbers (full battery, run from the worktree root)

```
dotnet test Content.Tests --filter FullyQualifiedName~_Solreign --no-build
  -> Passed: 1393, Failed: 0, Skipped: 2 (pre-existing Windows-only ACL tests)

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build \
  --filter "FullyQualifiedName~Content.IntegrationTests.Tests._Solreign" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
  -> Passed: 175, Failed: 0, Skipped: 1 (pre-existing PersistentBlockSurvivesRoundRestartAndPreventsOffer
     skip), Total: 176 -- identical shape to the pre-wave baseline, now including this wave's 6 new
     integration tests (4 first-shift-welcome + 2 acid-storm).

dotnet run --project Content.YAMLLinter -c Release
  -> No errors found.
```

Process note, recorded honestly: two intermediate full-suite runs produced garbage numbers (one "Aborted,
115 passed / 1 failed", one "102 failed" with PoolManager.GetPair SetUp cascades) — both were
self-inflicted invalid runs, caused by rebuilding the test DLLs underneath an in-flight vstest and by
accidentally running two copies of the 13-minute suite concurrently against the same bin directory. The
number above is from a single clean, uncontended re-run against the final binaries.

## Deferred / out of scope

- Feature 1's immediate beat still plays its VO station-wide (pre-existing behavior, not introduced by this wave) — a genuinely new player hears the collection twice (once for everyone, once targeted). Documented as a known wart in `ProvidenceWelcomeSystem`'s doc comment; not fixed here to avoid touching already-tested existing behavior.
- Feature 2 has zero mechanical hazard this wave (see above) — future wave territory.
- Neither event is wired into upstream `events.yml`'s automatic random-selection tables (matches this codebase's own existing precedent for its two sibling weather events — no per-station scheduler exists anywhere yet to wire into). Both remain admin-startable only (`event start SolreignAcidStorm`), same as `SolreignSolarFlare`/`SolreignSporeDrift`.
- Two pre-existing, unrelated `Content.IntegrationTests` compile breaks were fixed as a minimal, mechanical prep step (see Feature 1 section) to obtain a working test baseline for this wave's own battery — flagged clearly as out-of-scope repo drift, not part of either feature's design.
