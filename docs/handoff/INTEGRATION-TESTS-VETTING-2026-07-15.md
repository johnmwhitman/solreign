# Integration Tests Vetting — 2026-07-15

Branch: `feat/solreign-integration-tests`
Scope: the 3 unvetted test files added in `8e3c0e6eff` under
`Content.IntegrationTests/Tests/_Solreign/`:

- `SolreignCorporateRuleSystemIntegrationTest.cs` (3 tests)
- `SolreignGhostActivitySystemIntegrationTest.cs` (2 tests)
- `SolreignTameableSystemIntegrationTest.cs` (4 tests)

Command used throughout:

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build \
  --filter "FullyQualifiedName~SolreignCorporateRuleSystemIntegrationTest|FullyQualifiedName~SolreignGhostActivitySystemIntegrationTest|FullyQualifiedName~SolreignTameableSystemIntegrationTest" \
  --logger "trx" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

The console output is polluted by unrelated migration warnings; all verdicts below
were read from the clean `.trx` in `Content.IntegrationTests/TestResults/`
(`<UnitTestResult>` outcome/Message/StackTrace elements), not the console tail.

## Verdict table (9 tests)

| # | Test | Fresh-run verdict (pre-fix) | Final verdict | Classification |
|---|------|------------------------------|----------------|-----------------|
| 1 | Corporate: `AwardStanding_PositiveDelta_FoldsIntoSeasonLedgerStandingTotal_AfterRoundEnd` | Passed | Passed | n/a |
| 2 | Corporate: `AwardStanding_NonPositiveDelta_IsIgnored_LeavesStandingTotalAtZero` | Passed | Passed | n/a |
| 3 | Corporate: `KillAttribution_ThroughRealMobStateChangedEvent_CreditsOriginAccount_WhenRuleIsActive` | Passed | Passed | n/a |
| 4 | Ghost: `RequestActivities_AsGhost_ReturnsSeededEnabledActivities_ExcludesDisabled` | Passed | Passed | n/a |
| 5 | Ghost: `RequestActivities_AsNonGhost_ReceivesNoResponse` | Passed | Passed | n/a |
| 6 | Tameable: `Feed_FirstValidFood_NewlyTames_AndConsumesTheItem` | **FAILED** (or "NotExecuted / dirty-disposed" depending on batch composition — see note) | Passed | **TEST BUG** — fixed |
| 7 | Tameable: `Feed_NonFoodItem_IsRejected_CritterStaysUntamed_ItemSurvives` | Passed | Passed | n/a |
| 8 | Tameable: `Feed_AlreadyTamed_BySameOwner_StaysAlreadyBonded_NoOwnerChange` | **FAILED** (or "NotExecuted / dirty-disposed" — see note) | Passed | **TEST BUG** — fixed |
| 9 | Tameable: `Feed_AlreadyTamed_ByDifferentFeeder_Retames_TransfersOwnership` | Passed | Passed | n/a |

No product code was touched. No `[Ignore("SOLREIGN-DEFECT: ...")]` markers were needed —
both breakages were test bugs, not product defects.

## Root cause

Both failing tests are in `SolreignTameableSystemIntegrationTest.cs`, and both fail the
same way: an `Assert.That(entMan.Deleted(<food entity>), Is.True, ...)` reads `False`.

`SolreignTameableSystem.OnInteractUsing` (`Content.Server/_Solreign/Pets/SolreignTameableSystem.cs:92`)
eats the treat via `QueueDel(args.Used)`, **not** an immediate delete:

```csharp
if (TamingRules.ShouldConsumeFood(outcome) && component.ConsumeFood)
    QueueDel(args.Used);
```

`QueueDel` only enqueues the entity into `EntityManager.QueuedDeletions`. That queue is
drained by `EntityManager.ProcessQueueudDeletions()`
(`RobustToolbox/Robust.Shared/GameObjects/EntityManager.cs:298`), which is only called as
part of the per-tick update pass — never synchronously inside the call that queued it.

Both failing tests called `Feed(...)` and then immediately checked
`entMan.Deleted(food)` **inside the same `server.WaitAssertion(...)` delegate**.
`WaitAssertion` posts an `AssertMessage` that the server's `IntegrationGameLoop` runs
as a bare delegate invocation (`RobustToolbox/Robust.UnitTesting/RobustIntegrationTest.cs:1255-1266`)
— it does **not** run a `RunTicksMessage` (`Update`/tick pass) before or after, so
`ProcessQueueudDeletions` never got a chance to run between the `Feed()` call and the
assertion. The tests were asserting a queued effect before the queue that produces it
had been drained — a bad assumption that `QueueDel` deletes synchronously.

This is a real, deterministic bug in the test code, confirmed by:

- Solo, single-test reruns (`--filter` narrowed to exactly one of the two tests, no
  other Solreign fixtures in the run, so zero `LevelOfParallelism(2)` contention)
  failed identically and deterministically 2/2 times each, with the real assertion
  message (`Expected: True / But was: False`) present both times — this rules out
  the project's known HTN/pool-race flake class (see Note below), which would not
  reproduce deterministically solo.
- The two tests that pass in every run never assert entity deletion in the same tick
  they cause it (`Feed_NonFoodItem_IsRejected...` asserts non-deletion, which needs no
  tick since nothing was queued; `Feed_AlreadyTamed_ByDifferentFeeder_Retames...`
  never checks deletion at all).

### Note on the "NotExecuted / dirty-disposed" noise seen in full-batch runs

Before the fix, running the full 9-test filter (all 3 files together, which triggers
`[assembly: LevelOfParallelism(2)]` contention across fixtures) sometimes recorded
these same two tests as `NotExecuted` with only a bare `Assert.Warn("Test was
dirty-disposed.")` message and no visible test-body logs, rather than a clean `Failed`
with the real assertion message. This is `Content.IntegrationTests/Fixtures/GameTest.cs`'s
`DoTeardown()`: when a test's outcome is `TestStatus.Failed`, teardown short-circuits
and dirty-disposes the pool pair (`Pair.DisposeAsync()` →
`TestPair.OnDirtyDispose()` → `Assert.Warn("Test was dirty-disposed.")`,
`RobustToolbox/Robust.UnitTesting/Pool/TestPair.Recycle.cs:44`). Under
`LevelOfParallelism(2)`, the TRX writer appears to occasionally record that teardown
warning without the originating assertion failure's own message/stack surviving
alongside it, making the same underlying failure look like a bare "skip" instead of a
"fail" depending on scheduling. This is downstream noise from the one real bug above,
not a second, independent defect — it did not reproduce even once across 3 consecutive
full-batch runs after the `QueueDel`/tick-timing fix went in (see Verification below).

## Fix

Kept both tests' intent (assert the treat is actually consumed) and split each into
two steps: the interaction + non-deletion assertions inside the original
`WaitAssertion`, then `await server.WaitRunTicks(1);` to let
`ProcessQueueudDeletions` run, then a second `WaitAssertion` to check
`entMan.Deleted(...)`. Entity handles that need to cross the two `WaitAssertion`
blocks were hoisted to method-scope locals.

Changed file: `Content.IntegrationTests/Tests/_Solreign/SolreignTameableSystemIntegrationTest.cs`
- `Feed_FirstValidFood_NewlyTames_AndConsumesTheItem`
- `Feed_AlreadyTamed_BySameOwner_StaysAlreadyBonded_NoOwnerChange`

No other test files were modified. No product code was modified.

## Verification

Full 9-test filtered run, `--no-build`, after the fix — 3 consecutive runs, all green:

```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 20 s
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 19 s
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 16 s
```

Pre-fix, isolated reruns used to pin down root cause (for reference):

- Tameable-file-only (4 tests), 2 runs: `Failed: 2, Passed: 2` both times, same two
  tests, same assertion message both times.
- Each failing test filtered down to a single test (no other Solreign fixture in the
  run at all): failed deterministically both times, with the real assertion message
  (not a bare pool-dispose warning) — ruling out the HTN/pool-race flake class per the
  task's discipline requirement.

---

## Post-merge addendum — 2026-07-15 (branch `fix/corporate-tests-perdb`, base master @ `dea9b8718a`)

After the vetted branch merged to GAME master, all 3
`SolreignCorporateRuleSystemIntegrationTest` tests FAILED on master while the other 6
stayed green.

### Root cause: stale shared-DB assumption vs master's per-instance temp DB infra

The Corporate fixture carried its own private path resolver:

```csharp
private static string ResolveLedgerDbPath(IResourceManager res)
{
    var dir = res.UserData.RootDir ?? Directory.GetCurrentDirectory();
    return Path.Combine(dir, "solreign_season_ledger.db");
}
```

That hand-derives the PRODUCTION FALLBACK path (the shared file in
`bin/Content.IntegrationTests/`). The vetting branch's base predated master's
ledger-collision flake fix, under which:

- `Content.IntegrationTests/Pair/TestPair.cs` (`ServerOptions`) now injects a **unique
  per-server-instance temp file** via the `CCVars.SolreignSeasonLedgerDbPath` CVar
  (`Path.GetTempPath()/solreign-test-ledger-{Guid}.db`), and
- every live consumer (`SeasonLedgerSystem.Initialize`, `WingmateSystem`) resolves the
  file through the single canonical helper
  `Content.Server/_Solreign/SeasonLedger/SeasonLedgerDbPath.Resolve(IConfigurationManager, IResourceManager)`,
  which honors that CVar override before falling back to the bin path.

On master, therefore, the live `SeasonLedgerSystem` under test writes to the
per-instance temp file, while the Corporate tests polled the shared bin path — a file
nothing writes anymore. All 3 polls expired with "records never landed in
.../bin/Content.IntegrationTests/solreign_season_ledger.db". Classification: TEST BUG
(stale environment assumption), no product defect.

The tests also hardcoded round ids (9201/9202/9203) — prohibited, since the ledger's
round-envelope replay protection keys on (round id, account) and hardcoded ids collide
as soon as the same DB sees a second fold.

### The canonical DB-location pattern (what the port uses)

Exactly what `SeasonLedgerSystemIntegrationTest` on master does:

```csharp
private string ResolveLedgerDbPath()
{
    var server = Server;
    return SeasonLedgerDbPath.Resolve(
        server.ResolveDependency<IConfigurationManager>(),
        server.ResolveDependency<IResourceManager>());
}
```

i.e. resolve through the SAME production helper the live system uses, so the test
always opens whatever file the server under test is actually writing (in integration
runs: the pool-injected per-instance temp DB). Never re-derive the path by hand.

### Fix

`Content.IntegrationTests/Tests/_Solreign/SolreignCorporateRuleSystemIntegrationTest.cs`:

- Replaced the private hardcoded resolver with the canonical
  `SeasonLedgerDbPath.Resolve(...)` pattern above (added `Robust.Shared.Configuration`
  using).
- Replaced the 3 hardcoded round ids with a `UniqueRoundId()` helper
  (`Random.Shared.Next(100_000, int.MaxValue - 16)`, same discipline as
  `SeasonLedgerSystemIntegrationTest`).
- Assertion intent untouched: positive-delta round-end fold lands exactly 5;
  non-positive deltas ignored (tours >= 1 gate then standing == 0); kill attribution
  via real `MobStateChangedEvent` credits exactly +1.

No product code touched. No shared DB paths or hardcoded round ids reintroduced.

### Verification (this worktree, `--no-build` after a clean build)

Full 9-test _Solreign filter (Corporate + Ghost + Tameable), 3 consecutive runs:

```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 20 s
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 19 s
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 21 s
```

Interference gates:

- Pre-existing `SeasonLedgerSystemIntegrationTest` (integration, 2 tests):
  `Failed: 0, Passed: 2`.
- Pre-existing SeasonLedger-related unit filters in Content.Tests
  (`SeasonLedger* | CorporateScoring | TitleRules | RankProgression`):
  `Failed: 0, Passed: 136, Skipped: 2` — the 2 skips are pre-existing
  Windows-ACL platform-gated tests (`WindowsCleanupFailureResidueRetainsRestrictedAcl`,
  `WindowsStageAclAllowsOnlyCurrentServiceIdentity`) that never run on macOS,
  unrelated to this change.
