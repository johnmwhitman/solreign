# SOLREIGN Autonomous Lane Takeover Implementation Plan

> Execute with subagent-driven development. The controller owns scope, verification, merge, release, and production decisions; delegated reports are drafts until checked against the worktree.

## Objective

Restore a truthful, repeatable SOLREIGN Game integration gate without relaxing the existing watchdog; land that repair first; then rebase and reconsider the held arrivals antag-roll fix. Production work remains ineligible until fresh runtime-log creation is proved.

## Global Constraints

- Work only in the isolated Game worktree and branch named by the live operator lock. Do not edit the dirty Game primary checkout or the held arrivals worktree.
- Preserve `solreign.contracts_lowpop.enabled` with default `true`. Do not disable the feature to make the stale test pass.
- Do not change `PoolManagerWatchdogConfiguration`, `AssemblyInfo.cs`, parallelism, or the 20-minute default. The new runner must not set `SOLREIGN_INTEGRATION_WATCHDOG_MINUTES`.
- Discover exactly 92 fixture classes under `Content.IntegrationTests/Tests/_Solreign`, assign each exactly once to six deterministic sequential shards, and fail closed on inventory drift or ambiguous parsing. Start from sorted round-robin assignment, then apply only documented evidence-bound fixture moves needed to keep every shard inside the unchanged 12-minute operating target. Hosted run `30734065890` established the first such move: shard 1 passed all 99 tests but took 745.999 seconds, so its 21-test `ProvidencePowerContractorSystemIntegrationTest` fixture moves to the underloaded shard 4 without changing inventory or aggregate acceptance.
- The full-suite inventory is exactly 473 tests. The pre-repair baseline was 469 passed and 4 skipped; Task 1 intentionally converts the stale low-pop disabled-default skip into a passing explicit-false test, so the repaired acceptance split is 470 passed, 3 skipped, 0 failed. Any further count change requires explicit review.
- No fleet dispatch or other deliberate compute load may overlap a real integration run.
- No player-facing API, protocol, or CVar name change. The only new interface is an operator CLI.
- No force-push, safety-gate loosening, box/config mutation, credential action, spend, John-identity output, destructive live operation, or external-lane overlap.

## Task 1: Correct low-pop activation truth test-first

Repair the stale disabled-default story while preserving the intentionally activated default.

- In `ContractsLowPopIntegrationTest`, rename the disabled case to `LowPopExtension_ExplicitlyDisabled_DoesNotForceAnyContract` and explicitly set `CCVars.SolreignContractsQuestBoardEnabled` to `false` before exercising the CVar-off path.
- Update its class-level documentation and the CVar source documentation to state that the extension is activated by default and retains a runtime kill switch.
- Add a behavioral regression assertion that protects default-enabled operation. Prefer exercising the existing forced-easy behavior without explicitly enabling the CVar, rather than a source-text or bare-constant assertion.
- Watch the new/default behavior test fail against a deliberate temporary false-default mutation, restore the true default, and prove the focused fixture green.
- Commit only the Task 1 files with a focused test receipt and self-review.

## Task 2: Add the fail-closed six-shard operator CLI test-first

Add `Tools/_Solreign/IntegrationGate/solreign_integration_gate.py` and Python unit tests in the same directory.

- Default invocation: `python3 -B Tools/_Solreign/IntegrationGate/solreign_integration_gate.py`.
- Optional diagnostics: `--list` prints the deterministic shard inventory without invoking dotnet; `--shard N` runs one 1-based shard while retaining all inventory checks.
- Discover C# files recursively. Every file containing `[Test` must yield at least one non-abstract public/internal fixture class whose name ends in `Test`; reject duplicate classes and parsing ambiguity.
- Require exactly 92 discovered fixture classes. Sort class names and assign by `index % 6`, then apply the documented contractor-fixture move from shard 1 to shard 4; require every fixture exactly once and no empty shard.
- Build `Content.IntegrationTests/Content.IntegrationTests.csproj` once with DebugOpt, serial MSBuild settings, and no watchdog override. Then run shards sequentially using `--no-build --no-restore -c DebugOpt -m:1 -nodeReuse:false -p:UseSharedCompilation=false`, `NUnit.ConsoleOut=0`, and class-boundary `FullyQualifiedName` filters scoped to `_Solreign`.
- Write per-shard logs under `.gstack/integration-gate/`. Require subprocess exit 0, a parseable final VSTest summary, zero failures, nonzero execution, and no watchdog/hard-stop/pool-poison evidence.
- The default all-shard run must require the repaired aggregate 470 passed + 3 skipped = 473. A single-shard diagnostic validates that shard but does not claim full-suite acceptance.
- Never mutate the inherited environment. Refuse to run if `SOLREIGN_INTEGRATION_WATCHDOG_MINUTES` is already present, so an operator cannot accidentally loosen the gate.
- Unit tests must watch RED before implementation and cover: deterministic six-way coverage; duplicates; `[Test]` file with no fixture; zero-match/malformed summary; nonzero child exit; watchdog text despite a green-looking summary; incorrect aggregate; sequential subprocess order; and no watchdog override.
- Commit only Task 2 after unit tests and `--list` are green, with self-review.

## Task 3: Wire the standing gate documentation and prove the repair

- Update `Tools/solreign_gate.sh` comments so content changes point to the new integration CLI while the fast gate remains unchanged.
- Run the runner unit tests, Task 1 focused fixture, `./Tools/solreign_gate.sh`, and a Release `Content.Server` build.
- On a quiet machine with no fleet jobs, run all six integration shards sequentially. Require aggregate 470 passed, 3 skipped, 0 failed; no watchdog/pool-poison evidence; and a per-shard operating target of at most 12 minutes. If a shard exceeds the target, stop and rebalance before merge.
- Run `git diff --check`, staged gitleaks, a task review, and a final whole-branch review. Resolve all load-bearing findings before landing.
- Push a feature branch and create/merge a PR explicitly against `johnmwhitman/kolton-ss14`. Verify the merged master commit with the fast gate and all six shards again.

## Task 4: Rebase and reconsider the held arrivals fix

- Rebase or replay commit `8500070bd0f` onto the verified merged Game master without force-pushing the existing held branch.
- Repeat its focused four-scenario integration test, Release server build, fast gate, all six integration shards, diff/secret checks, and independent review.
- Merge only if every required gate is green, then repeat merged-master proof.

## Task 5: Release readiness and durable close

- Update OPS `HANDOFF.md` and `docs/ops/JOURNAL.md` with exact Game hashes, per-shard counts and times, review outcomes, and the production blocker state.
- Recheck the live runtime-log condition read-only. If no fresh nonempty `log_*` exists, stop release work after documenting process environment/launch-line evidence available without box mutation.
- If fresh logging is proved, preserve the live rollback artifact before building; cut an artifact from verified master; require build verification 7/7, sandbox scan, identity stamp, digest, and rollback receipt.
- Deploy only through the documented full autodeploy command and only when every conditional rail is satisfied. Production acceptance is `/status`, a newly created runtime log, matching server/client identity, no skew, and rollback readiness.
- Append a release receipt to the operator lock and release only this session's row. Do not clear another owner's lock or delete unrelated worktrees.
