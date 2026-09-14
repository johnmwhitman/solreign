# Season Ledger Atomic Round Envelope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make one SS14 round-end an atomic, replay-safe, restart-recoverable Ledger operation with a privacy-safe committed outbox record.

**Architecture:** Capture and canonicalize the complete round before the first await, durably stage it in a private adjacent filesystem spool, and apply it under one `BEGIN IMMEDIATE` SQLite transaction. A global round marker makes identical replay a no-op and conflicting replay fail closed; a bounded retry policy handles only SQLite BUSY/LOCKED, while a restart sweep acknowledges committed spool entries and recovers pending ones.

**Tech Stack:** C# 13/.NET 10, Microsoft.Data.Sqlite, System.Text.Json, NUnit, SS14 EntitySystem lifecycle.

## Global Constraints

- Stack on GAME commit `cec4c50cab1e1426410800a1fe23bfee0de053ee`; do not merge, push, deploy, restart, or activate production state.
- Preserve existing `AddRoundRecordAsync` and `AddContractLogAsync` compatibility, but reject legacy writes to a round already owned by a committed envelope.
- One positive core SS14 `round_id` is globally unique and owns at most one canonical envelope across all seasons.
- Canonical identity includes schema version, bound season, round ID, normalized gamemode, sorted unique roster with every contribution field, and a sorted contract multiset with duplicates retained; timestamps and caller ordering are excluded.
- Durable pending state lives in a private adjacent filesystem spool because the SQLite database may be unavailable; no account identifier, contract identity, gamemode, or exception text reaches operator status, metrics, logs, or outbox.
- Retry only SQLite base error codes 5 (`BUSY`) and 6 (`LOCKED`), at most four attempts with 25/50/100 ms delays and no jitter.
- The committed outbox is `private-internal` and non-exportable: exact round/count/totals may be retained for a later privacy projector, but they are not an approved public aggregate and are not yet a signed `RoundChronicleEventV1`.
- A season bump is refused while pending, conflicted, or quarantined spool work exists.
- A shared cross-instance filesystem lease serializes `stage -> bind/commit-or-Pending` against `bump scan -> bump commit`; per-store semaphores alone are insufficient.
- Every behavior change follows RED → observed failure → GREEN → refactor; all existing `_Solreign` tests remain green.

---

### Task 1: Canonical envelope and one-transaction commit

**Files:**
- Create: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerRoundEnvelope.cs`
- Create: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.RoundEnd.cs`
- Create: `Content.Tests/_Solreign/RoundEndEnvelopeStoreTests.cs`
- Modify: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs`
- Modify: `Content.Server/_Solreign/SeasonLedger/TitleRules.cs`
- Modify: `Content.Tests/_Solreign/SeasonLedgerStoreTests.cs`

**Interfaces:**
- Produces: `RoundEndPlayerRecord(Guid User, RoundContribution Contribution)`.
- Produces: `RoundEndEnvelope(int RoundId, string Gamemode, IReadOnlyList<RoundEndPlayerRecord> Players, IReadOnlyList<ContractLogRecord> Contracts)`.
- Produces: `RoundEnvelopePersistResult.Committed|AlreadyCommitted` and typed `RoundEndReplayConflictException`.
- Produces: immutable unbound `CapturedRoundEndEnvelope` with a spool hash, and season-bound `BoundCanonicalRoundEndEnvelope` with a separate DB identity hash. Conversion occurs only inside `BEGIN IMMEDIATE`.
- Produces: internal `PersistRoundEndOnceAsync(CapturedRoundEndEnvelope, CancellationToken)` used by Task 2.
- Produces: friend-only `IRoundEnvelopeFaultInjector.Hit(RoundEnvelopeStage stage, int index)` for deterministic transaction rollback tests.

- [ ] **Step 1: Write failing canonicalization and validation tests**

  Cover positive round IDs, nested round/gamemode agreement, unique roster GUIDs, blank contract fields, immutable caller snapshots, player/contract order independence, duplicate-contract preservation, and hash changes for roster/contribution/gamemode/contract changes.

- [ ] **Step 2: Run the focused tests and observe RED**

  Run:
  `dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoundEndEnvelopeStoreTests' --logger 'console;verbosity=minimal'`

  Expected: compile/test failure because the envelope API does not exist.

- [ ] **Step 3: Implement the canonical domain model**

  Use copied arrays sorted with ordinal comparisons. Serialize a fixed unbound capture DTO with `schema_version = 1` for the spool hash. Inside the immediate transaction, bind exactly one season and serialize a second fixed DTO for the DB identity hash. Never serialize runtime type metadata or timestamps into either identity.

- [ ] **Step 4: Add schema v4 under the existing migration transaction**

  Add:

  ```sql
  CREATE TABLE IF NOT EXISTS round_envelopes (
      round_id INTEGER PRIMARY KEY CHECK(round_id > 0),
      season_id TEXT NOT NULL,
      envelope_hash TEXT NOT NULL CHECK(length(envelope_hash) = 64),
      envelope_schema INTEGER NOT NULL,
      committed_utc TEXT NOT NULL,
      gamemode TEXT NOT NULL,
      player_count INTEGER NOT NULL,
      contract_count INTEGER NOT NULL
  );
  CREATE TABLE IF NOT EXISTS ledger_outbox (
      sequence INTEGER PRIMARY KEY AUTOINCREMENT,
      event_id TEXT NOT NULL UNIQUE,
      event_type TEXT NOT NULL,
      schema_version INTEGER NOT NULL,
      privacy_class TEXT NOT NULL,
      payload_json TEXT NOT NULL,
      created_utc TEXT NOT NULL,
      exported_utc TEXT NULL
  );
  ```

  Advance `PRAGMA user_version` to 4 and extend migration/fail-closed tests.

- [ ] **Step 5: Write failing atomicity, replay, compatibility, and privacy tests**

  Inject a throw after the first player mutation and assert zero changes in `player_stats`, `round_log`, `contract_log`, `round_envelopes`, and `ledger_outbox`. Cover exact reordered replay, each conflict dimension, duplicate contract occurrences, original-season replay after a bump, two-store serialization, and privacy assertions that outbox JSON contains no GUIDs, contract IDs/scopes, gamemode, antag/captain facts, hashes, or fine timestamp.

- [ ] **Step 6: Observe the new tests fail for the intended missing behavior**

  Use the Task 1 focused command and retain the RED receipt in the task report.

- [ ] **Step 7: Implement the one-transaction apply path**

  Under the store semaphore and an immediate SQLite transaction: find the global round marker; exact season-bound hash returns `AlreadyCommitted`; mismatch throws. If schema-v3 rows already exist, require every round/contract row for this round to agree on exactly one season and use that as the candidate binding; otherwise use the current season. Reconcile only provably matching player evidence, relying on the legacy invariant that a matching `round_log` row proves its `player_stats` mutation committed in the same transaction. Legacy contract rows must be either absent or an exact full canonical multiset match: `AddContractLogAsync` committed its full batch atomically and reset occurrence ordinals per call, so a partial subset is ambiguous and fails closed rather than inserting “missing” contracts. Strict-insert only missing player rows, upsert stats only for those missing players, insert a `private-internal` non-exportable outbox row and marker, then commit. Any ambiguous, extraneous, or mismatched legacy evidence fails closed without mutation. Legacy writers must reject a round after its envelope marker exists.

- [ ] **Step 8: Run focused and existing Ledger tests GREEN**

  Run the Task 1 filter plus the existing 50-test Ledger filter. Commit only after both pass and `git diff --check` is clean.

### Task 2: Durable spool, bounded retry, and restart recovery

**Files:**
- Create: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSpool.cs`
- Create: `Content.Tests/_Solreign/RoundEndRecoveryTests.cs`
- Modify: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.RoundEnd.cs`
- Modify: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs`

**Interfaces:**
- Consumes: canonical envelope and `PersistRoundEndOnceAsync` from Task 1.
- Produces: `PersistRoundEndAsync(..., CancellationToken)` returning `Committed|AlreadyCommitted|Pending` with a sanitized token/attempt count.
- Produces: `RecoverPendingRoundEndsAsync(int maxEntries = 8, CancellationToken)` and `GetRecoverySnapshot()` with sanitized metadata only.
- Produces: internal `ILedgerRetryRuntime` and `LedgerRetryPolicy` seams; Task 1 already owns the
  transaction fault injector used to prove rollback.

- [ ] **Step 1: Write failing spool safety tests**

  Require cryptographically random opaque-token filenames independent of both content hashes,
  `CreateNew` temp files, flush-to-disk then same-volume atomic rename, bounded file size,
  schema/hash verification, private permissions where supported, quarantine of malformed/tampered/oversize
  input, and no path traversal. Operator snapshots expose only token, round, coarse capture time,
  attempts, state, and bounded error category.

- [ ] **Step 2: Observe spool tests RED**

  Run:
  `dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoundEndRecoveryTests' --logger 'console;verbosity=minimal'`

- [ ] **Step 3: Implement atomic private spool and sanitized inspection**

  Default beside the DB at `solreign_season_ledger.pending/`. Use a random opaque token independent of both deterministic hashes for the filename and operator handle; under the shared filesystem lease, reuse an existing token for the same validated round/capture hash. Stage the unbound capture before SQLite access. Flush file data, atomically rename on the same volume, and fsync the parent directory where supported before reporting durable staging. On restart, quarantine abandoned `.tmp` files rather than treating them as valid work. Delete only after `Committed` or `AlreadyCommitted` returns. A commit-before-delete crash leaves an entry that recovery safely acknowledges later.

- [ ] **Step 4: Write failing retry-classifier and real-contention tests**

  Pure-test that only base SQLite codes 5/6 retry. With a competing `BEGIN IMMEDIATE` writer and `DefaultTimeout = 0`, prove release after the first 25 ms delay commits on attempt two; a retained lock performs exactly four attempts with 25/50/100 ms delays, returns `Pending`, preserves the spool, and mutates no Ledger table. Nontransient exceptions and cancellation do not delay.

- [ ] **Step 5: Implement the bounded retry policy**

  Use four total attempts, deterministic capped backoff, no unbounded loop, and sanitized categories. Do not swallow replay conflicts, corrupt/schema errors, IO errors, or cancellation.

- [ ] **Step 6: Write failing restart/exactly-once-effects tests**

  A fresh store must recover a pending entry once; a second sweep is empty. Inject crash after DB commit but before spool acknowledgement and prove recovery produces no duplicate stats/logs/outbox. Conflicting and quarantined entries remain visible. `BumpSeasonAsync` must refuse while any unresolved entry exists. A cross-instance contention test must prove a season bump cannot pass between first-stage spool persistence and season binding.

- [ ] **Step 7: Implement recovery sweep and season gate**

  Process at most `maxEntries`, one at a time, using the same retry policy, store lock, and cross-instance filesystem lease. The lease covers both `stage -> bind/commit-or-Pending` and `bump scan -> bump commit`; a crashed process releases the OS file handle. Preserve unresolved files; acknowledge exact committed replay; never expose private envelope content in results.

- [ ] **Step 8: Run Task 2, Task 1, and existing Ledger suites GREEN**

  Commit after fresh passing evidence and `git diff --check`.

### Task 3: SS14 lifecycle and operator visibility

**Files:**
- Modify: `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs`
- Create: `Content.Server/_Solreign/SeasonLedger/Commands/SeasonLedgerRecoveryCommands.cs`
- Create: `Content.Tests/_Solreign/SeasonLedgerRoundEndSystemTests.cs`
- Modify: `docs/specs/2026-07-13-season-ledger-reliability-blockers.md`

**Interfaces:**
- Consumes: `PersistRoundEndAsync`, recovery sweep, and sanitized recovery snapshot.
- Produces: host-only `solreign_ledger_recovery_status`, `solreign_ledger_recovery_retry [token]`, and evidence-preserving `solreign_ledger_recovery_archive [token] <reason-code>` commands.

- [ ] **Step 1: Write failing lifecycle tests**

  Verify `OnRoundEnd` snapshots two players and the complete contract multiset before its first await, calls only the envelope API, and produces one marker/outbox with an empty spool. Verify no legacy per-player/separate-contract write path remains.

- [ ] **Step 2: Replace round-end persistence with the envelope API**

  Log only round ID, sanitized token, attempt count, and error category. `Pending` is an operator-visible warning, not a dropped exception. Start one single-flight recovery sweep at initialization and at most every 30 seconds, max eight entries per sweep.

- [ ] **Step 3: Write failing command privacy/authorization tests**

  Require Host permission, bounded output, no GUID/contract/gamemode/exception/hash leakage, status-only default, explicit token for targeted retry/archive, an allowlisted archive reason code, and no arbitrary path or payload input. Archive moves the original envelope plus an audit receipt into a private archive directory; it never silently deletes evidence.

- [ ] **Step 4: Implement commands and update the blocker specification**

  Mark atomic commit/retry/restart recovery implemented on this branch while leaving deployment, restore drill, metrics wiring, and live-player canary explicitly gated. A season bump is blocked by pending/conflicted/quarantined work until recovery or Host archival preserves and acknowledges the evidence. Do not claim public export exists.

- [ ] **Step 5: Run complete verification**

  Run focused Task 1–3 tests, all `Content.Tests._Solreign`, and `dotnet build Content.Server/Content.Server.csproj --no-restore`. Run keysweep and `git diff --check`, then request independent architecture/privacy and SQLite-concurrency reviews.
