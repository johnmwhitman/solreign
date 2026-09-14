You are reviewing an actual code diff for a Space Station 14 fork (SOLREIGN, C#/Robust Toolbox engine). You previously specced this exact feature for me (Providence's delayed, personally-addressed first-shift welcome beat) — I implemented against your spec. Now review the real diff below for correctness bugs and whether I actually followed your own failure-mode matrix. You have no other context than this diff — it's complete.

CONTEXT RECAP (in case this is a fresh context for you): PROVIDENCE is a sinister-corporate AI announcer in this game, PG-13, unsettling-corporate not horror. This feature adds a DELAYED (8s), TARGETED (single-player, not station-wide) follow-up to an EXISTING immediate "new player welcome" beat: a chat line addressing the player by character name, a targeted VO line, and a single-player screen-fx pulse. Gated by a new CVar `solreign.providence.first_shift_welcome` (default true).

IMPORTANT WRINKLE I DISCOVERED DURING IMPLEMENTATION, not in your original spec: integration tests using a pool-connected session (DummyTicker=false) go through a REAL spawn during test-pool setup, before the test body runs. With the new CVar defaulting to true, that real pre-test spawn already schedules a pending personal beat the test never intended — and the pre-existing per-round anti-fatigue gate then silently blocks the test's OWN synthetic spawn from scheduling a second one. This caused a real test failure ("expected queue count 0, got 1") that had NOTHING to do with the CVar-gating logic actually being broken — it was test-harness residue. Fixed by adding `ProvidenceWelcomeSystem.ResetRoundStateForTests()` (calls the same two Reset()/Clear() the system already calls on round start/restart) and invoking it at the top of every integration test, before touching CVars or firing a synthetic spawn. Flagging this because it's exactly the kind of "obvious residue bug that looks like a real bug" you'd want a second pair of eyes on — tell me if you think this masks something else.

Full diff (9 files, ~645 insertions):

```diff
PASTE_DIFF_HERE
```

YOUR TASK: Review this diff against your own spec + failure-mode matrix from before. Specifically:
1. Does `FireFirstShiftPersonal`'s guard chain (session re-resolve by Guid, prefer session.AttachedEntity over the stale spawn-mob snapshot, ghost check, dead-body check, deleted check) actually match what you speced, in the right order, with no gap?
2. `ProvidenceFirstShiftPersonalQueue.DrainDue` removes an entry from the internal list BEFORE handing it to the caller for firing — does that actually prevent double-fire the way I claim, or is there a subtler re-entrancy issue (e.g. if `Update` iterates `DrainDue`'s returned list and firing one entry somehow re-triggers `Update` on the same tick)?
3. The new CVar is `CVar.REPLICATED | CVar.SERVER` (mirroring `SolreignMovementBob`) rather than this codebase's usual `SERVERONLY` for Solreign CVars, per the mission's explicit spec (not my own suggestion, not yours — I deviated from your SERVERONLY suggestion because the mission's requester explicitly wrote "REPLICATED|SERVER" in the ask). Any actual correctness/security implication of a replicated CVar here I should double check (e.g., can a malicious client fake it being off to skip some client-side check that shouldn't exist anyway)?
4. `ProvidenceVoiceSystem.PlayLineTo` uses the session-targeted `PlayGlobal(sound, ICommonSession)` overload instead of `Filter.SinglePlayer` + `recordReplay: false` — confirm this is equivalent/correct and doesn't accidentally still broadcast or leak into replays.
5. Tone check the actual final loc lines in the diff (providence-welcome.ftl) once more against your own PG-13 guidance.
6. Anything in the diff that contradicts your original spec's contract (e.g. did I accidentally make immediate + personal beat coupled in a way that breaks independence of the two CVars)?
7. Any other correctness bug, reuse/simplification opportunity, or missed edge case you see fresh in the actual code (not just the spec).

Be concrete and terse — cite exact line/hunk if you flag something. I will fix real findings before committing.
