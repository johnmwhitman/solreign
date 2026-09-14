# FD-W3.5 — plaque backfill build receipt (game side)

**Date:** 2026-07-17 · **Lane:** FD-W3.5 (plaque backfill) · **Branch:** `feat/plaque-backfill` off `origin/master` @ `2064500f83` (FD-W1..W3 merged)
**Problem:** the authored first death shipped LIVE in v13.3 with W1/W2 only — every first death in
the wire gap (v13.3 live → next release) banks a once-per-account-EVER `first_death` claim row but
NO website plaque, because the FD-W3 crypt wire (`ReportFirstDeath` → daemon `handle_first_death`)
only fires at claim time, which for those players has already passed. Their memorial must not be
silently lost.
**Companion receipts:** `docs/receipts/FIRST-DEATH-W3-2026-07-17.md` (the wire this lane re-drives);
daemon contract verified against `~/AI/solreign-director` `main` @ `f2fbf95` (`orchestrator/crypt.py`).

## What this lane adds

An idempotent round-start backfill keyed on one additive column:

| File | Change |
|---|---|
| `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` | `crypt_reported INTEGER NOT NULL DEFAULT 0` appended to the `first_death` CREATE TABLE **and** to the `EnsureColumnAsync` additive-migration battery (the standing_total idiom) — a live v13.3 DB comes forward with every banked row defaulting to "memorial not yet minted" |
| `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.FirstDeath.cs` | `CryptReported` on `FirstDeathRecord`; `TryMarkCryptReportedAsync` (conditional UPDATE, rowcount guard — the `TryMarkRehireShownAsync` idiom); `GetCryptUnreportedFirstDeathsAsync` (unstamped rows, oldest death first) |
| `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.FirstDeath.cs` | thin delegations for both new store methods |
| `Content.Server/Administration/Systems/SolreignCryptSystem.cs` | `ReportFirstDeath` now returns `bool` = "passed every gate and was HANDED to the fire-and-forget sender" (sole caller updated; per-death `ReportDeath` untouched); `FirstDeathChannelReady` (the backfill scan's gate); paired test seams `FirstDeathGateOverrideForTests`/`FirstDeathPostOverrideForTests` (see Testing law below) |
| `Content.Server/_Solreign/Providence/FirstDeathCryptBackfillQueue.cs` | NEW — pure paced FIFO queue (the `FirstDeathBeatQueue` idiom + a one-item-per-spacing-window release) |
| `Content.Server/_Solreign/Providence/ProvidenceFirstDeathSystem.CryptBackfill.cs` | NEW partial — round-start scan (`StartCryptBackfill`, fail-closed on the kill switch AND channel readiness), report re-composition from the claim row (`BuildBackfillReport`), per-tick paced pump + stamp-on-hand-off dispatch |
| `Content.Server/_Solreign/Providence/ProvidenceFirstDeathSystem.cs` | claim-time path now stamps on a successful hand-off; round hooks reset/start the backfill; `Update` pumps it |
| `Content.Tests/_Solreign/FirstDeathCryptBackfillTests.cs` | NEW — 16 unit tests (stamp semantics, unreported query, **legacy v13.3-schema migration**, report re-composition incl. byte-identical-to-claim-time, queue pacing) |
| `Content.IntegrationTests/Tests/_Solreign/FirstDeathCryptBackfillIntegrationTest.cs` | NEW — 4 end-to-end tests (banked row backfills exactly once + stamps + double-round idempotence; closed channel banks-not-burns; claim-time stamps and the backfill finds nothing; claim-time with closed channel leaves the row recoverable) |

Round-start was chosen as the scan hook (the `RoundStartingEvent`/`RoundRestartCleanupEvent`
family `ProvidenceFirstDeathSystem` already resets on) — the backfill is server-wide, not
per-player, so the spawn-complete family the rehire beat uses does not fit.

## Design choice: WHEN to stamp (the load-bearing decision)

**Chosen: stamp immediately after — and only after — the report is successfully HANDED to the
crypt sender** (gates + rate limit passed, JSON handed to the fire-and-forget POST task). Applied
identically to the claim-time path and the backfill, so the two paths ride one law.

The house idiom (write-before-dispatch, `TryMarkRehireShownAsync`) was **deliberately rejected**
here, for two facts this lane verified rather than assumed:

1. **The daemon is NOT idempotent per victim.** `handle_first_death`
   (`orchestrator/crypt.py`, daemon `main` @ `f2fbf95`) is a plain
   `INSERT INTO crypt_obituaries` with **no unique constraint on `victim_id`** (the table
   legitimately holds multiple rows per victim — the legacy legendary path inserts there too).
   Every POST mints a plaque. **The game-side stamp is therefore the ONLY dedupe in the whole
   system** — this is why requirement (2)'s "check the daemon contract" mattered: there is no
   daemon-side safety net to lean on.
2. **The crypt gates default OFF** and can stay off for whole rounds (Q7 is still a live-box
   decision). Pure stamp-before-dispatch means one shot ever: the first round the backfill ran
   with the daemon off, every banked memorial (the owner's son's included) would be stamped and
   burned with zero plaques — the exact silent-loss failure this lane exists to fix. Fail-closed
   gating and stamp-before-dispatch are incompatible for a RETRYING path.

So the alternative the lane brief anticipated is the one built: the wire gives a usable
just-before-send signal ("composed + gates passed + handed"), `ReportFirstDeath` now returns it,
and the stamp is written on `true` only. This also **matches the live claim-time path's own
semantics** — FD-W3 is fire-and-forget and never confirms delivery, so "handed = done" is already
the strongest meaning "reported" has anywhere in this feature.

**Accepted residuals (all documented in code):**
- *Duplicate-on-crash window:* a crash/db-failure in the milliseconds between hand-off and the
  stamp committing re-reports one row next round → one duplicate plaque on the website feed.
  Cosmetic, admin-prunable, and a cousin of the FD-W3 receipt's already-accepted
  legendary+first-death double-mint. Preferred over the inverse failure (silent permanent loss).
- *At-most-once delivery is unchanged:* a handed POST can still fail in flight; no path retries a
  stamped row. This is exactly the live claim-time guarantee — the backfill makes the system
  strictly better (gate-refused hand-offs now retry next round) without pretending the wire
  confirms delivery.
- *Same-round overlap is structurally impossible:* the scan runs at round start, claims happen
  only InRound, and a claimed row exists before any later scan — claim-time and backfill can
  never race on one row (and the conditional UPDATE would still hand one winner if they somehow did).

**Claim-time behavior change (intentional, an improvement):** a claim whose hand-off is refused
(gates closed — the live default!) now leaves `crypt_reported = 0` and is recovered by the
backfill in a later round. Pre-W3.5, that plaque was lost forever.

**Prod-timeline safety:** v13.3 live = W1/W2 only (no wire), so no production row was ever plaqued
at claim time → every `crypt_reported = 0` row is genuinely unplaqued and safe to re-report. This
lane must ship in the same release as W3 (it does — both on master). A hypothetical DB that ran
W3-with-gates-ON before this lane could hold plaqued-but-unstamped rows; re-reporting those would
duplicate (accepted: gates have never been ON in prod).

## Rate-limit / pacing law

The backfill re-reports through the SAME `ReportFirstDeath` wire, on its dedicated
`crypt_first_death` channel (1 per 750ms, `DirectorChannel.MinRequestInterval`). The pump releases
**at most one item per 1.5s** (2× the interval — headroom for a real first death landing
mid-backfill), across ticks, never bursting. A rate-limit refusal (or mid-round gate flip) drops
the item back to "banked" — unstamped, re-scanned next round — never burned.

## Testing law (gate directions)

The pool's offline law forbids flipping the real Director master CVar in integration (it would arm
the Oracle poll loop's genuine outbound HTTP → `Log.Error` → flaky suite kills). So:
- the **closed-gate** direction (banks-not-burns, claim-time-unstamped) rides the REAL fail-closed
  `DirectorChannel` chain at production-default CVars — zero seams, zero HTTP;
- the **open-gate** direction rides the paired `FirstDeathGateOverrideForTests` +
  `FirstDeathPostOverrideForTests` seams (gate override without a post override fails closed — an
  empty token can never sign or send). The rate limiter is always real (static, wall-clock);
  dispatch-expecting tests first wait out the window.

The new integration fixture is `Fresh + Destructive` (stronger than the FirstDeathScene fixture's
`Dirty`): the backfill scans the pair's whole ledger DB, and the per-pair SQLite file survives pool
recycling — recycled pairs carrying other tests' unstamped rows made scan-count assertions flaky
(observed, not theorized: the first fixture run failed exactly this way).

## Verification (this branch, all foreground, Release)

| Command | Result |
|---|---|
| `dotnet build -c Release` | 50 projects, **0 errors** (warnings pre-existing; none in touched files) |
| `dotnet test Content.Tests -c Release --no-build` | **2417 passed / 3 skipped (known)** — baseline 2401/3 + the 16 new unit tests |
| `dotnet test Content.IntegrationTests -c Release --no-build --filter FullyQualifiedName~Solreign` | **317 passed / 1 skipped (known)** — baseline 313/1 + the 4 new integration tests |
| `dotnet run --project Content.YAMLLinter -c Release` | **"No errors found"** |

## Notes for the orchestrator

- **Recovery note (2026-07-17):** the build session died at its cap mid-verification; the recovery
  lane re-ran the FULL battery above from scratch on the committed tree (all four rows are its
  results) and independently re-verified the daemon non-idempotence finding against
  `orchestrator/crypt.py` @ `f2fbf95` (plain INSERT into `crypt_obituaries`, no unique
  constraint on `victim_id` — confirmed, the game-side stamp is the only dedupe).
- **Rebase-check vs master (2026-07-17):** `origin/master` moved 6 commits ahead of the branch
  point (FD-W4 Discord obituary leg — touches `ProvidenceFirstDeathSystem.cs` — plus font/HUD-theme
  chores). `git merge-tree --write-tree HEAD origin/master` is CLEAN (exit 0, zero conflicts); the
  anticipated `SeasonLedgerStore.cs` schema-battery collision did not materialize (those commits
  don't touch the store). No keep-both resolution needed; merge normally.

- **WORKTREE-MAP registration pending:** `~/AI/solreign-trees/plaque-backfill` (GAME,
  `feat/plaque-backfill`, this lane) needs its row in the OPS-lane `docs/WORKTREE-MAP.md`.
- **Zero daemon changes; zero website changes.** The backfill POST is byte-identical in shape to a
  claim-time FD-W3 POST (unit-pinned), so the daemon cannot tell a backfilled memorial from a live
  one — plate text, §8D cause label, pseudonym law all inherited.
- **Go-live note:** the moment the crypt gates are first turned ON in prod, the next round start
  will trickle out every banked wire-gap memorial (oldest death first, 1 per 1.5s). That is the
  intended behavior — the owner's son's plaque mints on that round start.
- An unknown persisted plate id (a plate retired from the copy pack after a claim) degrades to the
  deterministic re-pick over the recorded (tours, cause, title) triple; an unparseable cause
  string degrades to Unknown — both the LoadRehire idiom, both unit-pinned.
