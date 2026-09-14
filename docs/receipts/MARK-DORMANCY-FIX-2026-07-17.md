# MARK-DORMANCY-FIX — 2026-07-17

Branch `fix/mark-dormancy`, cut from `master` @ `48e2d70a09b92ea8b0c21f98b3e5ab5fbb05019b`.

## Scope

Three failures on clean master in
`Content.IntegrationTests/Tests/_Solreign/MarkBeatsIntegrationTest.cs`:

1. `Dormant_MasterCVarOff_IsZeroBehaviorRegardlessOfSubGate` — expected 0 pending beats, got 1.
2. `Dormant_ReturnBeatSubGateOff_IsZeroBehaviorEvenWhileMarkIsLive` — expected 0, got 1.
3. `Overflow_SubstitutesC6ForBothBeats_ButStillConsumesTheSameStamps` — expected slot 1, got slot 4
   (passed in isolation, failed in-class).

## Bug A — the dormancy leak (tests 1 + 2)

**Verdict: TEST-SETUP DEFECT, not a production defect.** Production dormancy already held.

### Root cause

`MarkBeatsSystem.OnPlayerSpawnComplete` (the real, only production entry point into
`LoadMarkBeats`) correctly gates on `_markEnabled`/`_returnBeatEnabled` before doing anything else:

```csharp
private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
{
    if (!_markEnabled || !_returnBeatEnabled)
        return;
    ...
}
```

`_queue.Schedule` (the only way a beat enters `MarkBeatQueue`) is called from exactly two places,
both inside `LoadMarkBeats` — the nudge (C7) and growth (C2) schedule sites. No other system
(`SolreignMarkGardenSystem` included) ever calls `_queue.Schedule`. So in production, the CVar gate
is a complete kill switch: `LoadMarkBeats` — and therefore `Schedule` — can never run while the
master CVar is off, because `OnPlayerSpawnComplete` is its only caller.

The failing tests, however, did not go through `OnPlayerSpawnComplete` at all. They called the
test seam `MarkBeatsSystem.LoadMarkBeatsForTests(mob, guid, roundId)`, whose old implementation was:

```csharp
/// <summary>Runs the full spawn-complete path (CVar + Silent gates included) for one mob/account.</summary>
internal void LoadMarkBeatsForTests(EntityUid mob, Guid guid, int currentRoundId) =>
    LoadMarkBeats(mob, guid, currentRoundId);
```

The doc comment claimed the gate was included; the implementation called `LoadMarkBeats` directly,
skipping `OnPlayerSpawnComplete` entirely — so the CVar checks never ran, regardless of CVar state.
With a fresh planted mark row present, `LoadMarkBeats` always evaluated the nudge/growth conditions
and always scheduled the nudge beat (`record.PlantedRoundId != currentRoundId` was satisfied by the
test's own round-id argument), independent of whether `_markEnabled`/`_returnBeatEnabled` were true
or false. Hence the leaked "1 pending beat" in both dormancy tests — a defect in the test's calling
convention for that seam, not in the gate itself.

Evidence this is a test defect and not production behavior: grepping every caller of
`MarkBeatsSystem.LoadMarkBeats` and `MarkBeatQueue.Schedule` across the repo shows the ONLY
production caller is `OnPlayerSpawnComplete`, and the ONLY other caller is the mis-described test
seam. There is no second path into the queue.

### Fix

- `Dormant_MasterCVarOff_IsZeroBehaviorRegardlessOfSubGate`,
  `Dormant_ReturnBeatSubGateOff_IsZeroBehaviorEvenWhileMarkIsLive`, and a new adversarial test
  `Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence` now raise a REAL
  `PlayerSpawnCompleteEvent` through `entMan.EventBus.RaiseLocalEvent(mob, ev, broadcast: true)` —
  the same pattern `FirstDeathSceneIntegrationTest.MakeSpawnEvent` already establishes for the
  sibling first-death system — so they exercise the actual production gate
  (`OnPlayerSpawnComplete`) instead of a seam that bypasses it.
- Because the real event resolves the account from `ev.Player.UserId.UserId` (the connected
  session's identity), these three tests now claim the mark row under `session.UserId.UserId`
  instead of an unrelated random `Guid.NewGuid()` — otherwise the real gate would look up a row
  that doesn't exist for that identity and the test would pass for the wrong reason.
- `LoadMarkBeatsForTests`'s doc comment was corrected to say what it actually does (bypasses the
  CVar/Silent gate; callers must set CVars themselves; genuine dormancy proof requires the real
  event). The mechanic tests (`FirstReturnNudge_*`, `GrowthBeat_*`, `AntiFatigueCap_*`, `Overflow_*`)
  keep using the seam unchanged — they already set both CVars to their production-live values
  before calling it, so their behavior is identical either way; they exist to pin round-id-precise
  mechanics, not gating.

### Adversarial dormancy proof

`Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence` (new) proves, with
`solreign.mark.enabled=false`:

- A fresh-plant spawn, a later aged (stage-1) spawn, and a spawn with the sub-gate ALSO off all
  queue zero beats — three different real `PlayerSpawnCompleteEvent` raises, all through the real
  gate.
- Force-draining the queue (`FireDueBeatsForTests`) at any time, including far in the future, is a
  genuine no-op — the queue is provably empty, not merely unfired.
- The ledger write-first stamps (`nudge_shown`, `last_visit_stage`) were never touched — dormancy
  means the write path itself never ran, not just that a scheduled beat was swallowed at delivery.
- Flipping the master CVar back on and re-spawning the SAME account then fires exactly the expected
  2 beats (nudge + growth) and lands both stamps — proving the fix does not achieve dormancy by
  quietly breaking the feature once re-enabled.

## Bug B — the test-isolation leak (test 3, Overflow slot 4)

**Verdict: TEST-ISOLATION DEFECT** in the `mark` ledger table's interaction with pooled servers,
exactly as flagged.

### Root cause

`SeasonLedgerStore.TryClaimMarkAsync` assigns `slot_index` via `(SELECT COUNT(*) FROM mark)` inside
the same INSERT transaction — a GLOBAL dense count over the WHOLE table, not scoped to any one
account. This is structurally different from `first_death` (one row per account, looked up by that
account's own fresh GUID) and `social_firsts` (one row per `(account, flag)`) — those tables are
naturally isolated per test because every test draws a brand-new random account GUID, so stale rows
left by earlier tests are simply invisible to a later test's lookups. `mark`'s slot assignment has
no such natural isolation: it depends on every row ever inserted into the table, from any account.

`MarkBeatsIntegrationTest`'s `PoolSettings` (`Connected = true, Dirty = true, DummyTicker = false`)
does not set `Fresh`/`Destructive`/`NoLoadTestPrototypes`, so `PairSettings.CanFastRecycle` allows
the underlying pooled server (and its `SolreignSeasonLedgerDbPath`-resolved SQLite file — one per
pooled *server instance*, not per test *method*, per `TestPair.ServerOptions()`'s own comment) to be
reused across multiple test methods in the class. `Dirty = true` only forces a non-fast cleanup on
return; it does not force a brand-new server/DB per method. `ResetRoundStateForTests()` only clears
the in-memory `MarkBeatQueue`/`_inFlight` — it never touched the durable `mark` table. So earlier
tests in the class (`FirstReturnNudge_*`, `GrowthBeat_*`, `AntiFatigueCap_*`) planted rows into the
same shared `mark` table before `Overflow_*` ran, and its "first claim in this table = slot 0"
precondition silently inherited that count — landing on slot 4 instead of slot 1 in the reported
run (the exact count depends on execution order/prior test count, which is why it's non-deterministic
across relative to a stated "slot 1" expectation).

### Fix

- Added `SeasonLedgerStore.ClearAllMarksForTests()` (`DELETE FROM mark;`, under the store's lock,
  test-seam-only, no production caller) and a matching internal forwarder
  `SeasonLedgerSystem.ClearAllMarksForTests()`, mirroring the existing `SetMarkPlantedUtcForTests`
  seam idiom.
- Every test in `MarkBeatsIntegrationTest` now calls `await ledger.ClearAllMarksForTests();`
  immediately after setting its CVars and before any claim — so slot assignment always starts from
  a genuinely clean garden, independent of pool reuse or execution order. This was applied to ALL
  seven (now eight) tests, not just `Overflow_*`, since any test that plants a row is a potential
  future source of the same class of leak for a sibling test.

## Verify

All commands run from the worktree, Release configuration, stale `.trx` cleared first.

- `MarkBeats` filter, in-class: **8/8 passed** (7 original + 1 new adversarial dormancy test).
- Each of the four order-sensitive tests re-run alone (`--filter
  "FullyQualifiedName~MarkBeatsIntegrationTest.<Name>"`): `Dormant_MasterCVarOff_*`,
  `Dormant_ReturnBeatSubGateOff_*`, the new
  `Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence`, and
  `Overflow_SubstitutesC6ForBothBeats_ButStillConsumesTheSameStamps` — **all pass individually**.
- `Content.Tests` (full): **2555 passed, 3 skipped (unrelated Windows-ACL tests), 0 failed.**
- `Content.IntegrationTests` filtered on `~Solreign` (full Solreign integration surface):
  **358 passed, 1 skipped (unrelated, pre-existing), 0 failed.**
- `Content.YAMLLinter`: **no errors.**

## Diff scope

- `Content.Server/_Solreign/PlayerDelight/Mark/MarkBeatsSystem.cs` — corrected the
  `LoadMarkBeatsForTests` doc comment to match its actual (gate-free) behavior; no functional
  production change.
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.Mark.cs` — added
  `ClearAllMarksForTests()` test seam.
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.Mark.cs` — added the matching internal
  forwarder.
- `Content.IntegrationTests/Tests/_Solreign/MarkBeatsIntegrationTest.cs` — dormancy tests now drive
  the real `PlayerSpawnCompleteEvent`; every test clears the mark table before claiming; added one
  new adversarial dormancy regression test.

No production `Content.Server` behavior changed. Both bugs were entirely in test infrastructure:
the gate itself was already correct on master.
