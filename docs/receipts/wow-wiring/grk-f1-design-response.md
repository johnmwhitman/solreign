I'll ground the spec in the actual Providence/welcome/screen-fx code on disk, then write a precise feature contract and failure modes.# Spec: Providence first-shift personal address (delayed layer)

Additive only. Existing immediate popup + station-wide `NewPlayerWelcome` VO stays untouched.

---

## 1. Intent

After the existing first-shift beat (`career.Tours == 0`), wait a short delay, then deliver **one private** beat to that account:

| Channel | Target | Payload |
|---|---|---|
| Popup/chat line | that session only | Loc with character name |
| VO | that session only | same `SolreignProvidenceNewPlayerWelcome` collection |
| Screen FX | that session only | `SolreignScreenFxEvent` via session-targeted `RaiseNetworkEvent` |

Never station-wide. Cosmetic only.

---

## 2. CVars

| CVar | Default | Scope | Role |
|---|---|---|---|
| `solreign.providence.welcome_enabled` | `true` | existing | Immediate welcome (popup + broadcast VO). Unchanged. |
| `solreign.providence_enabled` | `true` | existing | Master Providence audio. `PlayLine*` no-ops when false. |
| **`solreign.providence.first_shift_welcome`** | **`true`** | **new, SERVERONLY** | Kill switch for **this delayed personal layer only**. |

### Contract (precise)

1. **Default ON** — feature ships live; CVar is emergency off, not a feature flag for staged rollout.
2. **Independent of `welcome_enabled`**: if welcome is off, `LoadWelcome` never runs → personal layer never schedules. If welcome is on and first-shift CVar is off → immediate beat still fires; delayed layer does not.
3. **Independent of `providence_enabled` for non-audio**: when master voice is off, delayed **popup + screen FX still fire**; delayed **VO no-ops** (same split as immediate welcome docs: text unconditional, voice gated).
4. **Check at fire time** (required). Schedule-time check optional but recommended (avoid useless pending work).
5. **Mid-delay flip OFF** → pending entry drops at fire, no effects.
6. **Mid-delay flip ON** → does not retro-schedule; only schedules from a future first-shift path (already gate-consumed this round → no fire until next round).
7. Put CVar in its own partial file (match `CCVars.SolreignProvidenceWelcome.cs` collision-control pattern), e.g. `CCVars.SolreignProvidenceFirstShiftWelcome.cs`.

---

## 3. Constants

```text
DelaySeconds          = 8f          // "few seconds after spawn rush"; not random
ScreenFxDuration      = 1.5f        // DefaultDuration; clamp still applies
```

Pin `8f` as a named const on `ProvidenceWelcomeSystem` (or pure timing helper if you want unit tests). Do **not** reuse idle-musings 20–40 min window.

---

## 4. Data structures

### Pending entry (private, system-owned)

```csharp
// Conceptual — keep pure fields, no ICommonSession stored
private readonly record struct PendingFirstShiftPersonal(
    Guid AccountId,           // NetUserId.UserId — reconnect-stable
    EntityUid SpawnMob,       // mob at schedule time (may die/delete)
    TimeSpan FireAt           // IGameTiming.CurTime + Delay
);
```

### System state

```text
List<PendingFirstShiftPersonal> _pendingPersonal   // polled in Update
bool _firstShiftPersonalEnabled                    // CVar cache
// Existing: ProvidenceWelcomeGate _gate  — unchanged
```

**Do not store `ICommonSession`.** Sessions die on disconnect; Guid does not.

**No second HashSet required for anti-spam** if scheduling is only from the already-gated first-shift branch (see §6). Optional hardening: `HashSet<Guid> _personalScheduledThisRound` if you want belt-and-suspenders against future double-call sites.

---

## 5. Hook points

### Schedule (only place)

`ProvidenceWelcomeSystem.LoadWelcome`, inside existing:

```csharp
if (career.Tours == 0)
{
    // EXISTING: immediate popup + PlayLine(NewPlayerWelcome)  — do not touch
    ...
    // NEW: if first_shift_welcome CVar && !Deleted(mob)
    //   enqueue PendingFirstShiftPersonal(guid, mob, CurTime + Delay)
}
```

Still after `Deleted(mob)` check. Still behind gate already marked at spawn.

### Fire

`ProvidenceWelcomeSystem.Update(float frameTime)`:

```text
foreach pending where CurTime >= FireAt:
  remove from list first (consume before side effects — anti-double-fire if Update re-enters)
  TryFirePersonal(entry)
```

### Clear

On `RoundStartingEvent` **and** `RoundRestartCleanupEvent` (same places `_gate.Reset()` runs):

```text
_pendingPersonal.Clear()
```

Round boundary must never leak a delayed fire into lobby/next round.

### Voice API (additive)

On `ProvidenceVoiceSystem`, add:

```csharp
public void PlayLineTo(ProvidenceLineCategory category, ICommonSession recipient)
{
    if (!_enabled) return;
    var collection = ProvidenceVoiceMap.CollectionFor(category);
    // Prefer session overload (already on SharedAudioSystem):
    _audio.PlayGlobal(new SoundCollectionSpecifier(collection), recipient);
    // Equiv: PlayGlobal(..., Filter.SinglePlayer(recipient), recordReplay: false)
}
```

**Do not** change `PlayLine` broadcast semantics.  
**Replay:** targeted personal beat should use `recordReplay: false` (or session overload). Broadcasting personal VO into replays is wrong; matching existing broadcast `recordReplay: true` is wrong for this path.

---

## 6. Fire-time resolution algorithm

```text
TryFirePersonal(entry):
  1. if !_firstShiftPersonalEnabled → return
  2. if !_players.TryGetSessionById(new NetUserId(entry.AccountId), out session) → return
     // disconnected / never reconnected: silent drop
  3. Resolve target entity for liveness + name:
       preferred: session.AttachedEntity
       fallback:  entry.SpawnMob if still exists and still controlled by this account
       if neither usable → return
  4. if Deleted(uid) → return
  5. if HasComp<GhostComponent>(uid) → return   // silent no-op (see §8)
  6. if dead (optional but recommended: MobState.Dead / Critical) → return
     // "onboarding logged" on a corpse is wrong tone; also avoids death-commiseration clash
  7. name = MetaData(uid).EntityName
     if string.IsNullOrWhiteSpace(name) → Loc line without name, or fallback "Employee"
  8. PopupEntity(Loc.GetString(key, ("name", name)), uid, uid, PopupType.Medium)
     // same private pattern as existing welcome (recipient = source mob)
     // OR PopupEntity(..., session) if you want session-hard privacy even if entity filter is weird
  9. _providence.PlayLineTo(NewPlayerWelcome, session)
 10. RaiseNetworkEvent(new SolreignScreenFxEvent(ScreenFxDuration), session)
```

**Session re-resolve is mandatory.** Never use a session captured at schedule time.

**Reconnect mid-delay:**
- Account Guid same; new session object → step 2 succeeds if reconnected and in-game.
- New body: use `session.AttachedEntity`, not stale `SpawnMob`.
- If reconnected only to lobby / no attached entity → silent drop.
- If reconnected and **respawned**, gate already marked → **no second schedule**. Pending from first spawn may still fire once against the **new** attached body if session resolves — **that is OK and intended** (one delayed personal, not one-per-body). Consume pending on fire so it cannot fire again.
- If disconnect forever → drop at fire; **no reschedule on reconnect**. Lost beat is correct (anti-spam > guaranteed delivery).

---

## 7. Anti-spam / fatigue matrix

| Scenario | Immediate beat | Delayed personal |
|---|---|---|
| First eligible spawn, tours==0 | once | once (scheduled once) |
| Respawn same round | blocked by `_gate` | no new schedule |
| Reconnect same round, re-spawn | blocked by `_gate` | no new schedule; at most one pending already |
| Mid-delay disconnect, never return | already fired | pending drops at fire |
| Mid-delay disconnect + reconnect (no new spawn) | already fired | fire once if session+live body resolve |
| Mid-delay disconnect + reconnect + new spawn | gate blocks re-welcome | same single pending; fire once if still pending |
| Next round | gate reset | new schedule possible if still tours==0 **until first round-end record** |
| Latejoin mid-round, tours==0 | works (no round-start assumption) | works (delay from their spawn time) |
| Silent spawn | neither | neither |
| CVar off | N/A / existing path | not scheduled / no-op at fire |

**Per-session fire count:** not a key. **Per account Guid per round:** at most one schedule + one fire.

**Tours edge:** `Tours == 0` until first finished round is recorded at round-end. Same account can still be first-shift across **multiple rounds** until they complete a round. That matches existing immediate welcome semantics — do not invent a permanent career gate for this layer.

---

## 8. Ghost / observer / corpse

| State at fire | Behavior |
|---|---|
| Live crew body | fire all three |
| Ghost / observer (`GhostComponent`) | **silent no-op** all three |
| Dead body, player still attached | **silent no-op** (recommended) |
| Dead body, mind already on ghost | AttachedEntity is ghost → no-op via ghost check |
| Entity deleted | no-op |
| Session gone | no-op |

**Do not** popup on corpse while player is ghost (wrong entity; message may miss client).  
**Do not** deliver “onboarding logged” VO into ghost ear — competes with death-commiseration station-wide VO and feels like surveillance after death, not onboarding.

Immediate beat already only runs on real spawn, so no change there.

---

## 9. Mid-round join

No round-start-only assumptions. Path is `PlayerSpawnCompleteEvent` → async ledger → schedule. Latejoin first-timers get delay from **their** spawn clock. Fine.

Do **not** key delay off round elapsed time or shift-start VO.

---

## 10. Screen FX targeting — is it safe?

**Yes**, as described:

- Server: `RaiseNetworkEvent(new SolreignScreenFxEvent(d), session)` is the established targeted path (FirstShift / Wingmate already do session-targeted net events).
- Client: `SubscribeAllEvent<SolreignScreenFxEvent>` runs for any event that client receives; broadcast vs targeted is a **server delivery** concern only.
- Overlay is client-local (`IOverlayManager`); no shared state with other clients.
- Duration still clamped; overlap with a real station-wide sting uses `ExtendRemaining` on **that** client only — acceptable (personal sting may slightly lengthen an audit flash for them only).

**Doc nit:** `SolreignScreenFxEvent` XML says “broadcast to all clients” — wording is historical, not a delivery constraint. Optional comment update only; no schema change.

**Not a problem:** no client filter, no PVS, no entity attachment required for the overlay.

---

## 11. Known double-VO (accept or note)

Unchanged immediate path still does:

```text
PlayLine(NewPlayerWelcome) → Filter.Broadcast()
```

So for a new hire:

1. **Everyone** hears welcome VO once (existing).
2. **They** hear welcome VO again ~8s later (new, targeted), possibly different random file from the 2-line collection.

That is loud/fatiguing for the new player. Out of scope if you refuse to touch tested immediate behavior — but **name it in the PR**. Future fix: immediate first-shift uses `PlayLineTo` only; delayed personal is text+FX only (or vice versa). Not required for this additive layer.

---

## 12. Tone / loc

Your sample is on-brand with existing first-shift lines (file, onboarding, permanent record). PG-13 corporate surveillance, not threat.

### Recommended keys (3–4 variants, pick random like existing)

```ftl
solreign-providence-first-personal-1 = Employee { $name }. Your onboarding is logged.
solreign-providence-first-personal-2 = { $name }. File opened. Attendance is noted.
solreign-providence-first-personal-3 = Welcome, { $name }. Solreign has recorded your arrival.
solreign-providence-first-personal-4 = Employee { $name }. Your permanent record begins with this shift.
```

### Avoid

- “We are watching you / always watching”
- Death, injury, punishment, “don’t fail”, “or else”
- Horror / body / void / whispers
- Over-familiar (“buddy”, “friend”) or parody HR slang that breaks dread

Existing immediate lines already carry “permanent file / never quite ends” — personal layer should feel like a **follow-up acknowledgment**, not a second induction monologue.

**Name source:** `MetaData(uid).EntityName` — matches title ceremony. Do **not** use `Identity.Name` (can be masked/disguised; wrong for HR address).

---

## 13. Dependencies to inject

On `ProvidenceWelcomeSystem` (new):

- `IGameTiming _timing`
- `IPlayerManager _players`
- existing `_cfg`, `_popup`, `_providence`, `_ledger`, `_random`

---

## 14. Failure modes checklist (implement against these)

1. **Double schedule from double spawn** — prevented by `_gate.MarkFired` before async; still schedule only once inside tours==0.
2. **Pending survives round restart** — clear on both round events.
3. **Stale session pointer** — never store session; re-resolve by Guid.
4. **Stale mob after body change** — prefer `session.AttachedEntity`.
5. **Disconnect mid-delay** — silent drop; no queue persistence across sessions.
6. **Reconnect spam** — gate + single pending consume; no re-fire.
7. **Ghost leak** — explicit `GhostComponent` (and preferably dead) no-op.
8. **Popup to deleted entity** — `Deleted` check before popup.
9. **CVar kill mid-flight** — fire-time check.
10. **Master voice off** — audio no-op; popup+FX still if personal CVar on.
11. **Welcome CVar off** — no schedule path.
12. **Silent spawn** — never enters path.
13. **Ledger throw** — existing catch; no schedule if exception before tours branch.
14. **Empty name** — fallback string; never throw in Loc.
15. **Update list mutation** — remove-then-fire or iterate copy; no collection-modified-during-enumerate.
16. **async void after schedule** — schedule only after tours confirmed and mob alive; mark-first gate already prevents parallel welcomes.
17. **Latejoin** — OK; no PreRound/InRound special case beyond existing spawn event.
18. **Tours still 0 next round** — personal can fire again next round (same as immediate); intentional career semantics.
19. **Station-wide FX doc vs targeted use** — safe; no client change.
20. **Replay pollution** — do not `recordReplay: true` on personal VO.
21. **Admin / observer join without crew spawn** — no `PlayerSpawnComplete` for live job → no schedule.
22. **Cryo silent return** — `Silent` blocks; correct.
23. **Same collection twice (broadcast + personal)** — accepted additive wart; document.
24. **Screen FX during existing station-wide sting** — local extend only; fine.
25. **Filter.SinglePlayer vs session PlayGlobal** — either OK; session overload is cleaner and already used for private briefings.

---

## 15. Tests (minimum)

**Pure / unit**

- Pending timing helper if extracted: fire when `CurTime >= FireAt`, not before.
- CVar independence documented in system doc comment (mirror welcome CVar docs).

**Gate behavior (existing tests unchanged)**

- Still one immediate welcome per Guid per round.

**Integration / system-level (if you already have Providence welcome integration)**

- tours==0 schedules pending; after delay + session present → targeted path invoked once.
- disconnect before fire → no throw, no second schedule on reconnect spawn.
- ghost at fire → no popup/audio/FX.
- round restart clears pending.

---

## 16. Implementation order

1. CVar + Subs.CVar cache  
2. `PlayLineTo` on voice system  
3. Pending list + Update + round clears  
4. Schedule in `Tours == 0` branch  
5. Loc strings  
6. Fire resolver with session/mob/ghost guards  
7. Targeted screen FX  
8. Tests / manual: solo first-shift, second client must not hear personal VO or see personal FX  

---

## 17. Non-goals

- Fixing immediate first-shift station-wide VO leakage  
- Syncing specific ogg file to specific loc line  
- Career-permanent “only ever once in account lifetime” (tours already approximates until first completed round)  
- Chat channel announcement / radio / station AI dialogue  
- Client-side changes to screen FX  

This is the contract to implement against; send the diff when ready and I’ll review against this matrix.
