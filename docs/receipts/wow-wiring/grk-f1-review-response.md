I'll review the full diff against the failure-mode matrix—first pulling the complete patch and implementation files.Reading the core implementation files for the guard chain, queue drain, CVar, and voice targeting.Checking audio API overloads, popup targeting, and a few edge cases around session/mob resolution.Checking one race path and how similar systems handle post-await round validity:## Verdict

Implementation matches the failure-mode matrix. **No ship-blockers.** A few real nits + one test comment lie; `ResetRoundStateForTests` is correct and does **not** mask a production hole.

---

### 1. Guard chain — **matches, right order**

`FireFirstShiftPersonal` (`ProvidenceWelcomeSystem.cs` ~255–271):

| Step | Spec | Code |
|------|------|------|
| CVar kill | mid-flight drop | `_personalEnabled` first |
| Session by Guid | never trust scheduled session | `TryGetSessionById(new NetUserId(pending.AccountId))` |
| Prefer live attach | over spawn snapshot | `session.AttachedEntity ?? SpawnMob` |
| Deleted | | `Deleted(target)` |
| Ghost | | `HasComp<GhostComponent>` |
| Dead body | | `MobState.Dead` |

No gap for the cases you listed. Disconnect → session miss → silent drain. Ghost attach → no-op. Dead-still-attached → no-op.

**Nits (not blockers):**
- Fallback to `SpawnMob` when `AttachedEntity is null` can still deliver VO/FX to the session (`PlayLineTo` / `RaiseNetworkEvent`) while popup targets a detached body. Rare (SSD/lobby). Prefer: if `AttachedEntity is null`, return — don’t fall back.
- Doc above the method says CVar re-check is “unnecessary,” then immediately re-checks. Comment is wrong; code is right.

---

### 2. `DrainDue` / double-fire — **your claim is correct**

Removes from `_pending` before returning (`ProvidenceFirstShiftPersonalQueue.cs` ~48–55). `Update` only iterates the returned list.

Re-entrancy: `FireFirstShiftPersonal` only does popup / audio / net event — none of those re-enter this system’s `Update` or `Schedule`. Even if they did, the entry is already gone. Unit test `DrainDue_CalledTwice_NeverReturnsTheSameEntryTwice` is the right proof.

No subtler same-tick double-fire here.

---

### 3. `REPLICATED | SERVER` — **no real correctness/security issue**

All three effects are **server-pushed** (popup, `PlayLineTo`, `RaiseNetworkEvent`). Client spoofing a local CVar cannot skip server scheduling/firing; there is no client gate on this CVar.

Cost: tiny bandwidth of a bool to clients. Future client UI can read it. Fine given mission text. Not a cheat surface.

---

### 4. `PlayLineTo` — **correct, equivalent, no replay leak**

Server impl (`Robust.Server/Audio/AudioSystem.cs` ~218–220):

```csharp
return PlayGlobal(filename, Filter.SinglePlayer(recipient), false, audioParams);
```

Session overload **is** `Filter.SinglePlayer` + `recordReplay: false`. Single recipient, not in replays. Doc comment is accurate.

---

### 5. Tone (ftl) — **on-spec**

Lines are short corporate acknowledgments, not monologue/threat. “Permanent record” / “file opened” sit with existing first-shift copy. Nameless fallback is safe. PG-13 OK.

---

### 6. Spec contract / CVar independence — **OK**

| CVar state | Immediate | Personal |
|------------|-----------|----------|
| welcome on, personal on | yes | schedules on `Tours==0` |
| welcome on, personal off | yes | never schedules; fire also no-ops |
| welcome off | neither (no hook) | neither |

Personal is nested inside `LoadWelcome`’s first-shift branch after base `_enabled` — same as documented. Not accidentally hard-coupled beyond that.

---

### 7. Other findings

**`ResetRoundStateForTests` — good, does not mask production**

Your diagnosis is right: pool `Connected + DummyTicker=false` real-spawns before the test body; default-on CVar schedules residue; gate blocks the synthetic re-spawn. Resetting gate + queue is the same as round start/restart.

Production is protected by `if (Deleted(mob)) return` after the ledger await — round restart deletes the mob, so a late `LoadWelcome` won’t schedule into the next round. The test helper doesn’t delete entities, but pool setup’s async is almost always finished before the body (residue already *in* the queue is the actual failure mode you hit). Not a silent production bug.

**Optional harden (only if you want belt-and-suspenders):** generation/epoch bumped on reset + re-check before `ScheduleFirstShiftPersonal`. Not required for ship.

**Test comment is false** (`FreshAccount_…` ~112–113):

```csharp
// Fresh guid every run -> guaranteed Tours == 0
// ...uses playerMan.Sessions[0] real UserId
```

No fresh guid. Works only because test ledger is empty for that account (`SUM → 0`). Fix the comment (or force a known empty guid if you ever share a dirty DB).

**Ghost integration test** is synthetic (`EnsureComponent<GhostComponent>` on the live body, no mind transfer). Still valid for the guard it claims to test; just don’t over-read it as “full death/ghost pipeline.”

**Order vs your writeup:** tests set CVars *then* reset. That’s fine (better than reset-then-CVar for the off case). Doc says “reset first, before touching CVars” — mismatch is docs only.

---

### Must-fix before commit?

| Priority | Item |
|----------|------|
| None | No correctness bug that violates the matrix |
| Nice | Fix “fresh guid” comment; fix contradictory CVar doc on `FireFirstShiftPersonal` |
| Optional | Drop `SpawnMob` fallback when `AttachedEntity is null` |
| Optional | Schedule epoch after `await` if you want zero async residue risk in tests |

**Ship it** after comment cleanup if you want a clean review trail; no functional rework required for the seven questions you listed.
