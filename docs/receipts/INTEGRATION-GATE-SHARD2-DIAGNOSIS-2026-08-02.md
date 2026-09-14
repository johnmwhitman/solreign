# Integration gate — shard 2 refuses because a test genuinely fails

> 🔴 **READ THE CORRECTION AT THE BOTTOM FIRST.** The original headline of this document —
> "the cause is the gate's own warning policy" — is **WRONG**, and it is kept only so the reasoning
> that produced it stays auditable. The gate is right: `NUnit.MapWarningTo=Failed` surfaces a real
> failing assertion that the default warning mapping was hiding as "Skipped". The timing findings
> in §1–§2 stand; the causal conclusion in the original §"Headline" and §"What must NOT be done"
> does not.

**Date:** 2026-08-02 · **Tree:** `~/AI/solreign-trees/rel-intgate` @ `3f2fdbb436d`
(the gate work from `codex/restore-solreign-integration-gate-20260802`, deliberately WITHOUT its
trailing content commit `f6a301a84c6`). Machine quiet, no fleet dispatch during any run below.

## Headline

The six-shard runner is **sound and fast** — but it **refuses on shard 2**, and the refusal is
produced by `NUnit.MapWarningTo=Failed`, a setting the gate itself passes. Two tests emit an NUnit
**Warning**; the gate scores warnings as failures. This is a **pre-existing gate-policy issue, not
a regression from any content on master.**

## Timing — the split works

| Shard | Result | Duration |
|---|---|---|
| 1 (13 fixtures) | 69 passed, 0 skipped | **82.083 s** |
| 2 (16 fixtures) | 86 passed, 2 "failed" | ~4 m 10–41 s |

The plan's hosted run measured shard 1 at **745.999 s**. On operator hardware it is **82 s** — ~9×
faster. The 20-minute watchdog is nowhere near being reached per shard. **Sharding solves the
watchdog problem.** That part of the repair is confirmed.

## The controlled experiment (four runs, one variable at a time)

Identical tree, identical build, identical 16-fixture filter, identical 88 tests, same machine.

| Run | Extra NUnit settings | Result |
|---|---|---|
| gate run | `ConsoleOut=0`, `MapWarningTo=Failed`, `TestOutputXml`, `WorkDirectory` | **2 failed**, 86 passed, 0 skipped |
| gate re-run | same | **2 failed**, 86 passed, 0 skipped (identical — deterministic) |
| manual | *(none)* | **0 failed**, 86 passed, **2 skipped** |
| control | `ConsoleOut=0` **only** | **0 failed**, 86 passed, **2 skipped** |

`ConsoleOut=0` is **not** causal — the control isolates it and the run still passes.
`MapWarningTo=Failed` is the deciding setting.

⚠️ **I initially blamed `ConsoleOut=0`** on the theory that it suppressed the diagnostic text. That
was wrong twice over: the flag is not causal, and the large `shard-2.log` was *large because the
tests failed* (VSTest attaches captured stdout to failures), not because of any console setting.
The control is what caught it.

## The two tests

* `_Solreign.StationAuditSystemIntegrationTest.InspectionDisabled_RoundStart_SchedulesNoCheckpoint`
* `_Solreign.WingmateSystemIntegrationTest.PersistentBlockSurvivesRoundRestartAndPreventsOffer`

Both surface at `TestPair.Recycle.cs:44`, which is exactly
`Assert.Warn("Test was dirty-disposed.")` — an NUnit **warning**, not an assertion failure. No
`[ERRO]`/`[FATA]` line appears anywhere in either run's output, and no `Expected:`/`But was:`.

**A hypothesis I raised and then refuted with my own data:** that `PoolSettings.Dirty = true`
causes the warning. **72 fixtures** under `Tests/_Solreign` declare `Dirty = true`, and shard 1 is
full of them (ContractClaimFlow, EchoGarden, Feedback, HotPotatoLifecycle, MovementBob,
SolreignCorporateRule, SolreignMapContentSeed, SprintDrain) and passed 69/69. `Dirty = true` alone
does not warn.

**Unresolved and worth the next look:** both failing fixtures are **the last two entries in shard
2's list** (positions 15 and 16 of 16). That is a position/ordering signal, not a content signal.
The decisive next experiment is to run *only those two fixtures* under the gate's full settings —
pass alone ⇒ order-dependence; fail alone ⇒ inherent to the fixtures.

## What must NOT be done about it

* ⛔ **Do not drop `MapWarningTo=Failed` to make the gate green.** Scoring warnings as failures is a
  *tightening*, and tightening is in-authority while loosening never is. Removing it would silence
  every future dirty-dispose warning across all 72 dirty fixtures.
* ⛔ Do not touch `PoolManagerWatchdogConfiguration`, `AssemblyInfo.cs`, or the 20-minute default.
  Nothing here needs them; per-shard runtime is far inside the limit.

The right fix is at the root: make those two tests clean-return their pair so no warning is
emitted, and the gate then passes **under its own strict policy**. That is a tightening-neutral
repair, not a relaxation.

---

# CORRECTION (same day, later) — the gate is RIGHT, and this document's headline was wrong

Everything above about timing stands. **The conclusion about the cause does not.**

## What the earlier passes missed

Running the two fixtures *together* returns `0 failed, 86 passed, 2 skipped`, which reads like
health. Running the offending test **truly alone, with no gate settings at all**, does not:

```
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
  Error Message:
     Assert.That(system.CheckpointScheduledForTests, Is.False)
```

That is a **real assertion failure in the test body**. The dirty-dispose warning at
`TestPair.Recycle.cs:44` is a *consequence*, not a cause: `GameTest.DoTeardown` sets
`_pairDestroyed` when `TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed`, and
only then does `DisposeAsync()` route to `OnDirtyDispose`.

So `NUnit.MapWarningTo=Failed` does **not** manufacture a failure. It is the only reason the
failure is visible at all — **the default mapping (warning → Skipped) was hiding a genuinely
failing test as a skip.** Dropping that setting would not fix the gate; it would restore the
blindfold.

Also refuted, by my own follow-up: the "both failures are shard 2's last two fixtures of 16"
ordering lead. They fail alone too (2 failed / 20 passed in 26s over just those two fixtures).

## Root cause of failure #1

`InspectionDisabled_RoundStart_SchedulesNoCheckpoint`:

* `solreign.station_audit.inspection_enabled` **defaults to `true`**
  (`Content.Shared/CCVar/CCVars.SolreignStationAuditInspection.cs:29`).
* The pooled server has therefore already run a round start with the layer ON, setting
  `_checkpointScheduled = true`.
* The test flips the CVar off and calls `SimulateRoundStartForTests()`, but
  `OnRoundStartingInspection()` (`StationAuditSystem.Inspection.cs:120`) returns early when the
  CVar is off **without resetting `_checkpointScheduled`**. The stale `true` fails the assertion.

**The kill switch itself is not broken.** The fire-time re-check at `:111` still suppresses the
fire and sets `_checkpointFired`, so nothing player-visible leaks. What is wrong is per-round state
hygiene plus a test that assumes a fresh system.

Two doc comments still claim this CVar is off-by-default and are now false:
`StationAuditSystem.cs:103` and `StationAuditSystem.Inspection.cs:37`.

## The decision the next iteration must make (not mine to guess)

* **Option A — product fix (recommended).** Reset per-round inspection state at the top of
  `OnRoundStartingInspection()` *before* the CVar gate, so a round that starts with the layer
  disabled does not inherit the previous round's scheduling. Makes the state honest and the test's
  assertion true. Tightening, so in-authority — but it changes documented system semantics, and
  the existing doc comments are already stale/contradictory about the intended contract, which is
  why I did not guess at it.
* **Option B — test fix.** Assert against a freshly constructed system, or explicitly clear the
  flag first. Smaller, but leaves the sticky state in the product.

**Failure #2** (`WingmateSystemIntegrationTest.PersistentBlockSurvivesRoundRestartAndPreventsOffer`)
is NOT yet root-caused. Same dirty-dispose signature; it exercises a round restart, so the sticky
per-round-state family is the first place to look.

## Method note

`grep -rn "CheckpointScheduledForTests" --include "*.cs" .` returned **nothing** for a symbol that
is defined in this repo. `find … -not -path "*/obj/*" -print0 | xargs -0 grep -l` found it
immediately in a partial class (`StationAuditSystem.Inspection.cs`). A silent-empty grep looked
exactly like "the symbol does not exist" and would have sent the next reader down a false trail.

---

# RESOLUTION (2026-08-02, /goal iteration) — two failures, two DIFFERENT causes

The two shard-2 failures are unrelated. Treating them as one family is what made this take three
passes.

## Failure #1 — FIXED

`StationAuditSystemIntegrationTest.InspectionDisabled_RoundStart_SchedulesNoCheckpoint`.

**Root cause: an unfaithful test seam, not a product bug.**
`SimulateRoundStartForTests()` was `=> OnRoundStartingInspection();`. The real
`RoundStartingEvent` handler (`StationAuditSystem.OnRoundStarting`) calls `ClearRoundState()` —
which includes `ClearInspectionRoundState()` — **before** it calls `OnRoundStartingInspection()`.
Since `OnRoundStartingInspection` returns early when the layer is disabled, the seam left the
previous round's `_checkpointScheduled` intact. Against a pooled server that has already had a real
round start under the CVar's `true` default, the "disabled round start" therefore still reported a
scheduled checkpoint.

The product was always right: the real handler clears first, and the fire-time re-check at
`Update()` still enforces the documented mid-flight-off guarantee. Fixed by making the seam mirror
the real handler. The seam is `internal` and used only by this fixture (7 call sites).

* RED: fails alone, fresh process, no gate settings — `Assert.That(system.CheckpointScheduledForTests, Is.False)`.
* GREEN: `StationAuditSystemIntegrationTest` **13/13** under `ConsoleOut=0 MapWarningTo=Failed`.
* `Tools/solreign_gate.sh` GREEN (2716/0, parity 1/0, YAML ok).

⚠️ The first gate run went RED with `Segmentation fault: 11` in the NetId parity leg. That is the
recorded `dotnet 10.0.301` arm64 SIGSEGV, not a real failure — re-run, green. A red suite is not
evidence until it survives a re-run.

## Failure #2 — ROOT-CAUSED, not yet fixed

`WingmateSystemIntegrationTest.PersistentBlockSurvivesRoundRestartAndPreventsOffer`.

**It is intra-fixture contamination, and the contaminating sibling is identified.**

| run | result |
|---|---|
| the test alone, fresh process | **passes** 1/1 |
| its whole fixture (9 tests) | **fails** 1/8 |
| just `FailedBlockWriteNoticeSurvivesBeingUnattachedAndDeliversOnNextBeaconUiOpen` + it | **fails** 1/1 — minimal repro |

Two tests reproduce it. NUnit runs a fixture alphabetically, so `FailedBlockWrite…` precedes
`PersistentBlock…`; that sibling calls `BlockCurrentPartnerForTests(requester)`
(`WingmateSystemIntegrationTest.cs:481`) against the same `(requester, Guide)` pair.

`WingmateSystem._persistentBlocks` (`:55`) is a process-lifetime `HashSet<(Blocker, Blocked)>` that
`ClearRound()` **deliberately does not clear** — persistent blocks are supposed to survive a round
restart, which is precisely the guarantee the failing test exists to prove. So the leak is correct
system behaviour and the isolation belongs on the test side.

Scope note: the contamination does **not** cross process boundaries — the test passes alone in a
fresh process — so the on-disk season ledger is not the carrier; the in-memory set is.

**Do NOT "fix" this by clearing `_persistentBlocks` in `ClearRound()`.** That would delete the very
behaviour under test. The fix is test-side isolation: give each test a distinct
`(requester, Guide)` identity pair, or add a test-only reset invoked from `[SetUp]`.

Unlike failure #1 this one produces **no assertion message and no `[ERRO]` line** — only the bare
dirty-dispose warning, with the test body executing ~17ms after a 256ms pair recycle. That is what
a precondition-violated-before-the-assertions failure looks like here.

---

# FAILURE #2 — third hypothesis REFUTED; stopping to instrument rather than guess again

**Shard-level effect of the failure-#1 fix, measured:** shard 2 went from
**2 failed / 86 passed** to **1 failed / 87 passed**. That half is closed and verified at the shard
level, not just at the fixture level.

**The remaining failure is still `PersistentBlockSurvivesRoundRestartAndPreventsOffer`.**

## What I tried, and why it was wrong

Hypothesis: the contamination is an identity collision — the sibling
`FailedBlockWriteNoticeSurvivesBeingUnattachedAndDeliversOnNextBeaconUiOpen` blocks the same
`(requester, Guide)` pair, and `WingmateSystem._persistentBlocks` is process-lifetime and
deliberately not cleared by `ClearRound()`, so give the failing test its own guide id.

Applied it — a dedicated `PersistentBlockGuide` (`5555…`) replacing all 10 `Guide` references
inside that test only, siblings untouched, compiles clean.

**Shard 2 still returned 1 failed / 87 passed. The hypothesis is refuted, and the change was
reverted** rather than left in the tree: its comment asserted an isolation rationale that the
evidence does not support, and a comment that explains a non-cause is worse than no comment.

## Why I stopped here instead of trying a fourth

This lane's own standard: **two failed blind fixes → instrument, never a third.** Counting the
identity-collision attempt, failure #2 has now consumed three refuted hypotheses:

1. `PoolSettings.Dirty = true` is the trigger — refuted (72 fixtures declare it; shard 1 passes).
2. It is positional (last two fixtures of 16) — refuted (fails in a 2-test run).
3. It is a `(requester, guide)` identity collision — refuted (dedicated guide id changes nothing).

## What IS established, and is worth trusting

* Passes alone in a fresh process (1/1). Fails inside its own fixture (1 failed / 8 passed).
* **Minimal repro is two tests**: `FailedBlockWriteNotice…` then `PersistentBlock…`, in NUnit's
  alphabetical in-fixture order. Either alone passes.
* The contamination does **not** cross process boundaries, so the on-disk season ledger is not the
  carrier.
* It produces **no assertion message and no `[ERRO]` line** — only the bare dirty-dispose warning,
  with the body running ~17ms after a ~256ms pair recycle. Something fails the test's preconditions
  before its assertions are reached.
* It is **not** the guide identity, so the carrier is some other piece of per-process
  `WingmateSystem` state that the sibling mutates — the pending-write queue, the failure-notice
  tally, `_approvedGuides`, or the detached-session path the sibling exercises
  (`WingmateSystemIntegrationTest.cs:486` calls `SetAttachedEntity(session, null)`).

## The instrumented next step (do this, do not guess)

Run the 2-test repro with `NUnit.ConsoleOut` left ON and a temporary probe that dumps
`WingmateSystem` state at the START of `PersistentBlockSurvives…` — status, partner, pending-write
queue depth, notice tallies, `_approvedGuides` membership — and diff it against the same dump from
a passing solo run. The delta names the carrier. The 2-test repro makes this a ~15s loop.
