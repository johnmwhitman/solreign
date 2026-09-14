# SOLREIGN Ledger Recovery Observability — Claude Integration Receipt

Date: 2026-07-15

Repository: GAME

Branch: `feat/codex-game-ledger-recovery-observability`

Authority: **review and integration input only; this receipt does not authorize merge or production action**

## Integration verdict

The feature tree passes its prescribed local tests, Release build, privacy scans, and a commit-tree
merge simulation against the current local `master`. Claude remains the only merge operator.

This branch adds bounded recovery telemetry and an assembly-internal offline restore verifier. It
does not schedule, create, transfer, retain, restore, or delete backups. It has no runtime kill
switch: the metrics are additive observations at existing completion points and the verifier is
internal, has no command/lifecycle entry point, and performs no live backup or restore operation.

## Commit chain and scope

- Base: `385de116a03db80d5d1b44404b54058f7f74b0c2`
- Task 1: `b725bbf8e88bbb25c30e0c1c8fb6c7ae1a665204` —
  `feat: add bounded ledger recovery metrics`
- Task 2: `7f8c9eff63e567ea1a91fffa67f384f30312c4d8` —
  `feat: verify ledger restore snapshots offline`
- Independent P1 repair: `bea46d5ea602229e5456a2dba87182d096ff29e5` —
  `fix: bind ledger restore proof to staged bytes`
- Reviewed feature head before this receipt: `bea46d5ea602229e5456a2dba87182d096ff29e5`
- Documentation commit: `093346283887928a2c5f4dd2a94e3505882d46c6` —
  `docs: hand off ledger recovery observability`
- Final-review repair: `3767b5c14aa71a16fd290dd531677f25557c9d0f` —
  `fix: harden ledger recovery and restore staging`
- Windows confidentiality repair: `bf6df44ad018e1147c79c77a414900ef0def6b31` —
  `fix: restrict Windows ledger restore staging`
- Windows residue-test cleanup: `b4c6e51ecc38b57ffc37ca911ec11cba64681700` —
  `test: clean restricted ledger restore residue`
- Exact independently reviewed executable/test head: `b4c6e51ecc38b57ffc37ca911ec11cba64681700`.

Exact executable/test paths changed by the feature and final-review commits:

- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerRecoveryMetrics.cs` (created)
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerRestoreVerifier.cs` (created)
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` (only the schema constant visibility)
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.RoundEnd.cs` (single leased sweep snapshot)
- `Content.Server/_Solreign/SeasonLedger/Commands/SeasonLedgerRecoveryCommands.cs` (scheduler offload)
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs` (metrics observation wiring)
- `Content.Tests/_Solreign/SeasonLedgerRecoveryMetricsTests.cs` (created)
- `Content.Tests/_Solreign/SeasonLedgerRestoreVerifierTests.cs` (created)
- `Content.Tests/_Solreign/SeasonLedgerRoundEndSystemTests.cs` (scheduler/snapshot regression coverage)

Documentation paths in the handoff commit:

- `docs/handoff/CODEX-LEDGER-RECOVERY-OBSERVABILITY-2026-07-14.md`
- `docs/superpowers/plans/2026-07-14-ledger-recovery-observability-restore-proof.md`

No schema SQL, migration behavior, CVar, admin command, public API, or backup scheduler was added.

## Capability receipt

### Eight bounded metric families

| Family | Type | Labels and allowed values |
| --- | --- | --- |
| `solreign_ledger_recovery_unresolved` | gauge | none |
| `solreign_ledger_recovery_state` | gauge | `state`: `pending`, `conflict`, `quarantined` |
| `solreign_ledger_recovery_oldest_age_days` | gauge | none |
| `solreign_ledger_recovery_max_attempts` | gauge | none |
| `solreign_ledger_recovery_sweep_examined` | gauge | none |
| `solreign_ledger_recovery_sweep_acknowledged` | gauge | none |
| `solreign_ledger_recovery_sweep_faulted` | gauge | none |
| `solreign_ledger_round_end_total` | counter | `status`: `committed`, `already_committed`, `pending`, `failed` |

The projection carries only aggregate counts, bounded state/status categories, age capped at 3,650
days, and attempts capped at 1,000,000. There is no GUID, token, path, season, hash, gamemode,
contract, player, or round label/dimension.

### Seven restore failure codes

- `InvalidManifest`
- `SnapshotUnavailable`
- `DigestMismatch`
- `IntegrityFailure`
- `UnsupportedSchema`
- `MissingStructure`
- `ProgressionMismatch`

Non-cancellation verifier errors expose only the bounded code as the exception message/property,
with no inner exception, path, hash, season, identifier, row content, or SQLite detail.

### Restore authenticity and privacy boundary

- Tests create synthetic local Ledgers and use `SqliteConnection.BackupDatabase`; no live database
  is opened.
- The supplied main database is opened once and copied from that handle into a fresh randomized
  private staging directory.
- The candidate and every parent path component must be alias-free. The opened handle must identify
  the same unique regular file as the current path and have exactly one hard link before sidecars
  are examined; unknown operating systems or unsupported Linux architectures fail closed.
- Adjacent `-wal`, `-shm`, and `-journal` state is rejected before and after capture.
- Unix staging modes remain directory `0700` and file `0400`. On Windows, the randomized staging
  directory receives a protected DACL with inheritance disabled and one explicit inheritable
  FullControl rule for the current service SID before the staged file is created. Directory owner,
  rule cardinality/identity/rights/inheritance, and the child's inherited owner/rule are read back;
  any missing identity, ACL write/read, descriptor mismatch, or unexpected rule fails closed with
  `SnapshotUnavailable`. The inherited file is additionally marked read-only.
- Atomic source pathname replacement after handle-open cannot change staged bytes.
- Hashing and SQLite inspection operate only on the staged artifact. SHA-256 is compared before
  inspection and rechecked after inspection with `CryptographicOperations.FixedTimeEquals`.
- Inspection uses `SqliteOpenMode.ReadOnly`, `SqliteCacheMode.Private`, `Pooling = false`, and
  `PRAGMA query_only=ON`; it never invokes store migrations or mutation APIs.
- Staging cleanup runs on success, bounded failure, and cancellation. Cleanup failures normally use
  fixed `SnapshotUnavailable`; if cancellation is already authoritative, cleanup is best-effort and
  cannot replace `OperationCanceledException`. A cleanup failure on that path may leave only the
  randomized owner-private staging directory/file for operating-system or operator cleanup.

### Recovery sweep scheduling boundary

- Each scheduled sweep acquires one recovery lease, performs one bounded spool snapshot, and derives
  both the sweep result and bounded health projection from that same snapshot.
- The system no longer performs a second unbounded snapshot after recovery.
- Scheduler delegate invocation is offloaded before it can execute synchronous filesystem work, and
  the single in-flight task remains the overlap guard observed by `EntitySystem.Update`.

## TDD and independent-review evidence

- Task 1 RED: `SeasonLedgerRecoveryMetrics` was absent (`CS0246`) before production implementation.
- Task 1 GREEN: 6/6 focused metric cases and 77/77 named Ledger baseline cases passed.
- Task 2 RED: restore contract types were absent (`CS0246`) before production implementation.
- Task 2 initial GREEN: 17/17 focused restore cases passed; combined Task 1/2/baseline was 100/100.
- Independent review found the original two-open hash/inspect pathname race and WAL authenticity
  gap. The old verifier demonstrably accepted a same-main-digest private-row mutation resident only
  in WAL.
- P1 RED also covered the missing deterministic staging seam and initially accepted adjacent
  rollback-journal state.
- P1 GREEN: atomic path replacement during manifest and verification, staged digest/proof identity,
  owner-private permissions, WAL/SHM/journal rejection, and cleanup/cancellation are covered.
- Final-review RED reproduced synchronous scheduler blocking and the duplicate spool snapshot, then
  alias/path-replacement acceptance and cleanup masking cancellation.
- Final-review GREEN adds one-snapshot/non-overlap/nonblocking scheduler tests; real WAL mutation
  tests through file and parent-directory symlinks and a hard link; native path/handle identity;
  and deterministic cleanup-failure tests for manifest and verification cancellation.
- Windows confidentiality RED reproduced acceptance of a sidecar directory and absence of a proven
  identity-bound staging ACL contract. GREEN adds fail-closed directory/dangling-link sidecar tests,
  a locally compiled source contract, and Windows-only runtime ACL checks for normal staging and a
  cleanup-failure residue. Those two runtime cases are intentionally skipped on non-Windows hosts.

## Final local verification

### Focused Task 1/2 and Ledger baseline, compile enabled

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~SeasonLedgerRestoreVerifierTests|FullyQualifiedName~SeasonLedgerRecoveryMetricsTests|FullyQualifiedName~RoundEndEnvelopeStoreTests|FullyQualifiedName~SeasonLedgerRoundEndSystemTests|FullyQualifiedName~RoundEndRecoveryTests" \
  --logger "console;verbosity=minimal" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `Passed 116, Failed 0, Skipped 2, Total 118` in 3 seconds. The two skips are the explicitly
Windows-only normal-stage and cleanup-residue DACL runtime proofs.

### All SOLREIGN Content.Tests, compile enabled

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore \
  --filter "FullyQualifiedName~Content.Tests._Solreign" \
  --logger "console;verbosity=minimal" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `Passed 1100, Failed 0, Skipped 2, Total 1102` in 3 seconds. The two skips are the same
Windows-only DACL runtime proofs; all compile/source and Unix/macOS behavior ran locally.

The test builds emitted existing `NU1510` package-pruning warnings in RobustToolbox projects. No
warning referenced a changed feature file.

### Non-incremental Release server build

```sh
dotnet build Content.Server/Content.Server.csproj --no-restore --configuration Release \
  --no-incremental --verbosity minimal -m:1 -nodeReuse:false \
  -p:UseSharedCompilation=false -clp:ErrorsOnly,Summary
```

Result: exit `0`; build succeeded with `837 Warning(s), 0 Error(s)` in `00:01:12.85`. The warnings
are existing upstream/RobustToolbox and unrelated server obsolescence/analyzer debt; none names a
changed feature file. Warnings were inspected in unsuppressed runs before the concise completion
receipt.

### Diff, source, and merge gates

- `git diff --check`: exit `0` before the final-review commit.
- Added-line scan: no new logger/console call, formatted sensitive value, whole-file read helper,
  write-capable/shared/pooled SQLite inspection, dynamic exception detail, TODO, TBD, or placeholder.
- Positive controls found all eight metric families and their exact bounded labels; read-only,
  private, no-pooling, query-only inspection; alias-free unique-file binding; single-handle staging;
  sidecar rejection; one leased recovery snapshot; scheduler offload; and both post-inspection
  fixed-time digest checks.
- Against clean GAME `master` `cdc054716196fb5e666b1a0e7879be39937a5899`, the exact reviewed
  executable/test head produced conflict-free synthetic tree
  `6e68350288468e72a717f86c9150993cb1d0ec21` and detached validation commit
  `129293db76054f7bc8c515ecd46e9efeaac48034`.
- That combined candidate passed the focused suite with `Passed 116, Failed 0, Skipped 2, Total 118`
  and all SOLREIGN tests with `Passed 1135, Failed 0, Skipped 2, Total 1137`. The two skips are the
  Windows-only ACL runtime proofs described above.

Commit-tree mergeability does **not** prove semantic compatibility, so the combined detached candidate was
built and tested rather than relying on the merge tree alone. At final-review time the canonical GAME
master worktree was clean at `cdc054716196fb5e666b1a0e7879be39937a5899`; this lane did not alter it.

## Rollback and residual gates

Before integration, rollback is to revert the feature commits in newest-first order:

1. `b4c6e51ecc38b57ffc37ca911ec11cba64681700`
2. `bf6df44ad018e1147c79c77a414900ef0def6b31`
3. `3767b5c14aa71a16fd290dd531677f25557c9d0f`
4. `093346283887928a2c5f4dd2a94e3505882d46c6`
5. `bea46d5ea602229e5456a2dba87182d096ff29e5`
6. `7f8c9eff63e567ea1a91fffa67f384f30312c4d8`
7. `b725bbf8e88bbb25c30e0c1c8fb6c7ae1a665204`

Claude should perform any revert/merge; none was executed here.

These gates remain unsatisfied and are not authorized by this receipt:

- no live or offsite restore proof;
- no encrypted offsite backup or retention/destruction receipt;
- no independent restore-host drill;
- no production scrape or alert rules;
- no signed release receipt or rollback artifact;
- no merge, push, deploy, restart, CVar activation, canary, or live-player evidence.
- no genuine Windows CI/runtime execution of the two ACL proofs; that receipt is a mandatory pre-landing
  and pre-activation gate and is not waived by the macOS build or source-contract tests.

Therefore `SR-W-002` remains `held`; `SR-W-003` and `SR-W-021` remain `active`.

## Provenance and authorization boundary

This is original SOLREIGN code. No external code or assets were copied. All verification used local
synthetic files and local build/test processes only. No network, secrets, hosted system, live Ledger,
offsite backup, merge, push, deploy, restart, CVar, or production mutation was used.
