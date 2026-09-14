# Season Ledger Recovery Observability and Restore Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add privacy-bounded recovery metrics and a deterministic offline verifier proving that a consistent Season Ledger snapshot can be restored without losing schema, season, round, player, or outbox progression.

**Architecture:** Keep the existing v4 SQLite store and durable spool authoritative. A pure telemetry projector summarizes only bounded counts, state/category bands, coarse age, and sweep outcomes; `SeasonLedgerSystem` publishes those values to fixed Prometheus metrics. A separate no-command offline restore verifier opens a supplied snapshot read-only, validates a manifest digest, SQLite integrity, schema/tables, and aggregate progression, and emits only fixed reason codes and aggregate proof.

**Tech Stack:** C# 13/.NET 10, Microsoft.Data.Sqlite, prometheus-net, NUnit.

## Global Constraints

- Work only in `feat/codex-game-ledger-recovery-observability` from GAME `385de116a03db80d5d1b44404b54058f7f74b0c2`.
- No live Ledger, player database, hosted backup, network, deploy, restart, CVar, push, or merge operation.
- No GUID, token, path, gamemode, contract, exception text, season value, hash, or row payload in metrics, metric labels, logs, console text, or formatted proof.
- Metric labels are fixed enum values only; no unbounded labels.
- Restore verification is offline code only: no admin command accepts a path and no game tick invokes it.
- A raw live `.db`/WAL copy is not a supported backup method; tests create a consistent SQLite backup first.
- Use serial local builds/tests: `-m:1 -nodeReuse:false`; vstest requires the approved local loopback permission.
- Preserve the existing spool, retry, archive, season-bump, and round-envelope semantics.

---

### Task 1: Pure recovery health projection and Prometheus publication

**Files:**
- Create: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerRecoveryMetrics.cs`
- Modify: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs`
- Test: `Content.Tests/_Solreign/SeasonLedgerRecoveryMetricsTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<LedgerRecoverySnapshotItem>`, `LedgerRecoverySweepResult`, and a supplied `DateOnly today`.
- Produces: `LedgerRecoveryHealthSnapshot Summarize(...)`, `PublishSnapshot(...)`, `RecordRoundEnd(...)`, and fixed Prometheus series under `solreign_ledger_*`.

- [x] **Step 1: Write failing pure projection tests**

Create tests that require this exact immutable result:

```csharp
internal readonly record struct LedgerRecoveryHealthSnapshot(
    int Unresolved,
    int Pending,
    int Conflict,
    int Quarantined,
    int OldestAgeDays,
    int MaximumAttempts,
    int SweepExamined,
    int SweepAcknowledged,
    int SweepFaulted);
```

Cover empty input; mixed states; future dates clamped to zero; oldest age capped at 3650 days; attempts capped at 1,000,000; sweep values copied without identifiers; and reflection/source scans proving metric names and fixed labels contain no token, round, path, season, user, GUID, gamemode, or contract dimension.

- [x] **Step 2: Run the focused test and verify RED**

Run:

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~SeasonLedgerRecoveryMetricsTests' --logger 'console;verbosity=minimal' -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Expected: compile/test failure because `LedgerRecoveryHealthSnapshot` and `SeasonLedgerRecoveryMetrics` do not exist.

- [x] **Step 3: Implement the pure projector and fixed metrics**

Use `Prometheus.Metrics.CreateGauge/CreateCounter`. Required series:

```text
solreign_ledger_recovery_unresolved
solreign_ledger_recovery_state{state="pending|conflict|quarantined"}
solreign_ledger_recovery_oldest_age_days
solreign_ledger_recovery_max_attempts
solreign_ledger_recovery_sweep_examined
solreign_ledger_recovery_sweep_acknowledged
solreign_ledger_recovery_sweep_faulted
solreign_ledger_round_end_total{status="committed|already_committed|pending|failed"}
```

The only labels are the literal state/status values above. `Summarize` is deterministic and performs no I/O. `PublishSnapshot` sets gauges from a completed summary. `RecordRoundEnd` increments only the bounded status counter.

- [x] **Step 4: Wire publication at existing observation points**

In `SeasonLedgerSystem.Update`, when a sweep completes, obtain the already-sanitized recovery snapshot asynchronously through the existing scheduler/store boundary and publish on the main-thread observation pass. Do not start overlapping snapshot work or block. In `LogRoundEndResult`, record the bounded result status; in the round-end catch, record `failed`. Preserve existing sanitized logs.

If adding a second asynchronous task would complicate the scheduler, extend `LedgerRecoverySweepResult` with the aggregate snapshot fields computed under the existing spool lease instead of launching another read. Do not carry tokens or items into the metrics class.

- [x] **Step 5: Run tests and verify GREEN**

Run the focused metric test, then the three baseline Ledger fixtures. Expected: all pass; no new warning/error in changed files.

- [x] **Step 6: Commit Task 1**

```sh
git add Content.Server/_Solreign/SeasonLedger/SeasonLedgerRecoveryMetrics.cs Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs Content.Tests/_Solreign/SeasonLedgerRecoveryMetricsTests.cs
git commit -m "feat: add bounded ledger recovery metrics"
```

### Task 2: Offline manifest and restore verifier

**Files:**
- Create: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerRestoreVerifier.cs`
- Modify: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs`
- Test: `Content.Tests/_Solreign/SeasonLedgerRestoreVerifierTests.cs`

**Interfaces:**
- Consumes: a consistent SQLite snapshot path and a `LedgerRestoreManifest` generated from that snapshot before transfer.
- Produces: `CreateManifestAsync(string snapshotPath, CancellationToken)`, `VerifyAsync(string restoredPath, LedgerRestoreManifest, CancellationToken)`, `LedgerRestoreProof`, and fixed `LedgerRestoreFailureCode` values.

- [x] **Step 1: Write failing manifest/restore tests**

Require these public-to-assembly contracts:

```csharp
internal sealed record LedgerRestoreManifest(
    int ManifestSchema,
    string DatabaseSha256,
    int LedgerSchemaVersion,
    string SeasonFingerprint,
    long RoundEnvelopes,
    long PlayerStats,
    long OutboxRows);

internal readonly record struct LedgerRestoreProof(
    int LedgerSchemaVersion,
    long RoundEnvelopes,
    long PlayerStats,
    long OutboxRows);

internal enum LedgerRestoreFailureCode
{
    InvalidManifest,
    SnapshotUnavailable,
    DigestMismatch,
    IntegrityFailure,
    UnsupportedSchema,
    MissingStructure,
    ProgressionMismatch,
}
```

The success test must:

1. Persist one round into a temp source Ledger.
2. Use `SqliteConnection.BackupDatabase` to create a consistent snapshot.
3. Generate a manifest from the snapshot.
4. Copy that closed snapshot to a separate restore path.
5. Verify exact schema and aggregate progression.
6. Open a new `SeasonLedgerStore` on the restored file and prove the round replay returns `AlreadyCommitted`, the current season is preserved, and outbox/player counts do not duplicate.

Failure tests cover malformed/uppercase/incorrect digest; tampered/truncated file; future `user_version`; missing required table; changed progression count; cancellation; and fixed exception messages that contain only the enum code.

- [x] **Step 2: Run the focused test and verify RED**

Run the new fixture with the serial test command. Expected: compile failure because restore types do not exist.

- [x] **Step 3: Expose the schema constant without widening mutation APIs**

Change `SeasonLedgerStore.CurrentSchemaVersion` from `private const` to `internal const`. Make no other schema change.

- [x] **Step 4: Implement strict manifest validation and read-only inspection**

Requirements:

- `ManifestSchema == 1`.
- Digest is exactly 64 lowercase hexadecimal characters and compared with `CryptographicOperations.FixedTimeEquals`.
- `SeasonFingerprint` is lowercase SHA-256 of the UTF-8 season ID; the raw season value never leaves the verifier.
- Open with `SqliteOpenMode.ReadOnly`, `SqliteCacheMode.Private`, and `PRAGMA query_only=ON`.
- Run `PRAGMA integrity_check` and require the single exact result `ok`.
- Require `meta`, `player_stats`, `round_envelopes`, and `ledger_outbox` from `sqlite_schema`.
- Require `user_version == SeasonLedgerStore.CurrentSchemaVersion`; older and newer versions fail rather than migrating the candidate.
- Read only aggregate counts and the current-season value needed for its fingerprint.
- Wrap all non-cancellation failures in `LedgerRestoreVerificationException` whose message is exactly the enum name and whose public properties contain no path/value/hash.
- Stream the file hash with SHA-256; do not load the whole database into memory.

- [x] **Step 5: Run restore tests and verify GREEN**

Expected: success proof passes, every corrupt/mismatch case fails with the exact fixed code, and restored replay remains idempotent.

- [x] **Step 6: Commit Task 2**

```sh
git add Content.Server/_Solreign/SeasonLedger/SeasonLedgerRestoreVerifier.cs Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs Content.Tests/_Solreign/SeasonLedgerRestoreVerifierTests.cs
git commit -m "feat: verify ledger restore snapshots offline"
```

### Task 3: Recovery receipt, full verification, and handoff

**Files:**
- Create: `docs/handoff/CODEX-LEDGER-RECOVERY-OBSERVABILITY-2026-07-14.md`
- Modify only if evidence requires it: `docs/superpowers/plans/2026-07-14-ledger-recovery-observability-restore-proof.md`

**Interfaces:**
- Consumes: committed Task 1–2 behavior and test output.
- Produces: a non-authorizing Claude integration receipt with exact SHAs, commands, results, rollback, and residual live/offsite gates.

- [x] **Step 1: Run focused tests**

Run metric, restore, round-envelope store, recovery system, and store fixtures with `--no-restore` and serial MSBuild. Record exact passed/failed/skipped totals.

- [x] **Step 2: Run broader verification**

Run all `Content.Tests._Solreign` tests and a non-incremental `Content.Server` build using serial MSBuild. Run `git diff --check` and scan changed executable files for GUID/token/path/season/hash/contract/gamemode metric labels or formatted proof fields.

- [x] **Step 3: Run a synthetic merge check**

Fetch is not required. Compare against the then-current local GAME `master` with `git merge-tree --write-tree --messages master HEAD`. If `master` moved, report the exact target and conflicts; do not merge or rebase.

- [x] **Step 4: Write the handoff receipt**

Record:

- base/head SHAs and changed paths;
- RED and GREEN evidence;
- baseline and final test/build totals;
- metric names/labels and restore failure codes;
- confirmation that restore tests used synthetic local files only;
- no live/offsite restore, deploy, push, merge, restart, or CVar action;
- rollback as reverting the feature commits before any integration;
- residual gates: encrypted offsite backup, independent restore host, production metric scrape/alert rules, signed release receipt, and live-player round evidence.

- [x] **Step 5: Commit the receipt**

```sh
git add docs/handoff/CODEX-LEDGER-RECOVERY-OBSERVABILITY-2026-07-14.md docs/superpowers/plans/2026-07-14-ledger-recovery-observability-restore-proof.md
git commit -m "docs: hand off ledger recovery observability"
```

## Self-review

- Spec coverage: telemetry, privacy, restart/restore, progression preservation, corruption, rollback, and residual offsite/live gates each have a named task.
- Placeholder scan: no TODO/TBD or unspecified error handling remains.
- Type consistency: Task 1 uses existing Ledger recovery records; Task 2 names each new manifest/proof/failure type consistently; Task 3 consumes only committed outputs.
- Scope check: public projection, offsite storage, deployment, alerts, and production activation remain explicitly outside this branch.

### Final-review repair addendum — 2026-07-15

- [x] Derive sweep outcome and bounded recovery health from exactly one spool snapshot while holding
  the recovery lease; remove the system's second unbounded snapshot.
- [x] Offload recovery delegate invocation so synchronous pre-await filesystem work cannot block
  `EntitySystem.Update`; prove one in-flight task and no overlapping sweep.
- [x] Bind restore input to an alias-free path and native file identity, require a unique regular
  file before sidecar checks, and fail closed on unsupported platforms.
- [x] Exercise real WAL mutations through a file symlink, parent-directory symlink, and hard link,
  plus atomic path replacement, with zero skipped cases on the local macOS arm64 host.
- [x] Preserve `OperationCanceledException` when deterministic private cleanup fails. The accepted
  residual is a randomized owner-private staged artifact requiring OS/operator cleanup; no source
  or live Ledger content is logged or published.
- [x] Re-run focused, combined, all `_Solreign`, Release, privacy/source, diff, canonical-status, and
  merge-tree gates; append exact evidence to the receipt and SDD report.

### Windows staging confidentiality repair — 2026-07-15

- [x] Install and read back an inheritance-disabled Windows staging-directory DACL limited to the
  current service SID, with container/object inheritance, before creating the staged database.
- [x] Read back the staged file owner and inherited DACL; fail closed with `SnapshotUnavailable` for
  missing identity, ACL/descriptor errors, extra principals/rules, or inheritance/rights mismatch.
- [x] Keep Unix directory `0700` and file `0400` unchanged.
- [x] Reject sidecar directories and dangling sidecar links as unbound SQLite state.
- [x] Add Windows-only normal-stage and cleanup-residue ACL runtime tests plus locally executable
  source-contract and sidecar tests; disclose the two expected macOS skips and require Windows CI.
