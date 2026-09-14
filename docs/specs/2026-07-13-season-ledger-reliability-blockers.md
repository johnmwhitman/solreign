# Season Ledger Reliability Blockers

## H0: Atomic round-end envelope and bounded retry

**Status:** Branch-implemented and independently reviewed on the Codex game reliability lane; still gated on
deployment, restore rehearsal, recovery metrics, and a live canary. It remains a merge blocker before the ledger
is treated as durable operational evidence. No public Ledger export is enabled or authorized by this work.

Before this branch, `SeasonLedgerSystem.OnRoundEnd` persisted each player's contribution in a separate
transaction and then persisted the contract audit batch. A failure after any successful player write could
leave a partial round: early players committed, later players absent, and the contract audit batch absent.
Exact replay protection made a manual replay safe, but the SS14 event lifecycle did not automatically replay a
failed `RoundEndMessageEvent`.

The branch implementation introduces one `PersistRoundEndAsync` envelope containing the complete player roster
and contract multiset, committed with its marker and outbox row in one SQLite transaction. `OnRoundEnd` now
freezes the complete immutable envelope before its first await and has no legacy per-player or separate contract
write path. Only transient `SQLITE_BUSY` and `SQLITE_LOCKED` failures receive the short capped retry. Exhausted
retries remain in the durable private spool for bounded automatic recovery and host-only, sanitized operator
status/retry/archive commands. Archive retains the evidence and an audit receipt.

The automatic recovery sweep is single-flight, begins on the first main-thread update after initialization,
runs no more frequently than every 30 seconds, and examines at most eight entries. Task completion is observed
from `EntitySystem.Update`; no ECS state is accessed from an async continuation. Operator output is bounded and
does not expose account identifiers, contract or gamemode content, exceptions, hashes, or filesystem paths.

The website and Discord surfaces must consume only a separately reviewed committed outbox projection, never the
live game database. This branch does not publish that projection and does not change any production CVar,
deployment, restart, or credential state.

Acceptance tests:

- Inject a failure after the first player write and prove that player stats, round logs, contract logs, and the
  outbox all remain unchanged.
- Replay an identical full envelope and prove exactly-once totals and audit rows.
- Replay the same round key with a different roster, contribution, or contract batch and require an explicit
  conflict with no mutation.
- Hold a competing SQLite writer, prove bounded transient retry, and prove exhausted retries remain visible and
  recoverable.
- Restart between failure and recovery and prove the pending envelope can still be committed exactly once.

Branch evidence covers the pure lifecycle capture, recovery cadence/single-flight behavior, opaque-token and
archive-reason policy, targeted retry, transaction rollback, replay identity, durable restart recovery, spool
durability, and bounded contention behavior. Operational closure still requires:

- deploy the independently reviewed commit through the normal release lane;
- expose and validate recovery/outbox metrics without publishing private payloads;
- complete an off-host backup and restore drill with no season progression loss;
- run a live canary and verify one committed marker/outbox record for a real round; and
- separately approve the game-to-web read model before any public export is activated.

## Integration-review addendum (2026-07-14, orchestrator)

**Archive/quarantine retention (P1-2):** the spool's `archive/` and `quarantine/` subdirectories are
append-only evidence with no automatic purge — deliberate for launch (evidence must outlive bugs), but
on a small host they are an unbounded-growth surface. Operational answer until a rotation tool exists:
entries are tiny (one JSON envelope + receipt per failed round; a pathological week is kilobytes, not
gigabytes); the 6-hourly Mac-side ledger backup captures them; and the operator runbook step is "if
`solreign_ledger_recovery_status` shows a growing archive, copy the directory off-host and delete
entries older than the last verified backup." A size-capped rotation belongs in a follow-up before H2
(unattended operation). NOTE: `HasUnresolved()` blocks season rollover while ANY unresolved entry
exists (including quarantined ones) — this is fail-closed by design; the operator archive command is
the release valve.

**P0 + P1-1 fixed in this integration pass:** the three recovery console commands now extend
`LocalizedEntityCommands` (direct `IConsoleCommand` + `[Dependency]`-on-EntitySystem kills server boot
— console commands are IoC-injected at boot against the global container, where entity systems don't
exist; the entity-command base resolves against the system collection instead. Full integration suite
is the only battery layer that catches this class — attribute-reflection unit tests cannot). The
recovery sweep now catches per-entry non-conflict failures (poison entry no longer starves the queue;
faulted count surfaced in the sanitized recovery log line).
