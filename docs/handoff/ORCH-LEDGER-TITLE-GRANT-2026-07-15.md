# ORCH ledger title grant — admin-grantable Season Ledger titles (2026-07-15)

**Branch:** `feat/ledger-title-grant` (worktree `~/AI/solreign-trees/ledger-title-grant`, off GAME master `b74fe79d3f`)
**Code commit:** `167458539b`
**Purpose:** the community rewards program (website /rewards page) wants real in-game titles as grantable rewards.
**Status:** HELD FOR CLAUDE INTEGRATION — do NOT merge to master, do NOT deploy (post-Grand-Opening merge; opening Sat 2026-07-18). Game-tree waves serialize; `feat/humanoid-move-bob` and `feat/sprite-idle-anims` were in flight when this lane opened, so this branch was built in its own worktree and never touched the master tree.

---

## What shipped

Two Host-gated console commands on the Season Ledger:

| Command | Form | Effect |
|---|---|---|
| `solreign_grant_title` | `<player> <title text>` | Grants a custom display title (season-scoped; replaces any prior grant this season). Shows on examine, persists across rounds, applies immediately if the target is online. |
| `solreign_revoke_title` | `<player>` | Removes the current season's grant; the earned title shows again. Reports when there was nothing to revoke. |

Targets resolve via `IPlayerLocator` (name **or** GUID), so rewards can be granted to **offline** players — the expected rewards-program flow.

### Files

| File | What |
|---|---|
| `TitleGrantRules.cs` (new) | Pure policy: input validation/normalization, display fold, paper-trail copy. No ECS, no I/O. |
| `SeasonLedgerStore.AdminTitles.cs` (new) | Store partial: `SetAdminTitleAsync` / `GetAdminTitleAsync` / `RevokeAdminTitleAsync` over a new `admin_title_grants` table. |
| `SeasonLedgerStore.cs` (edited) | `admin_title_grants` added to the additive `CREATE TABLE IF NOT EXISTS` batch + schema doc header. |
| `SeasonLedgerSystem.TitleGrants.cs` (new) | System partial: grant/revoke orchestration — persist, paper trail, live refresh for online targets. |
| `SeasonLedgerSystem.cs` (edited) | `LoadTitle` reads the grant and folds it into the display title; `PendingTitle` gained `CeremonyTitle`. |
| `Commands/SeasonLedgerTitleGrantCommands.cs` (new) | The two console commands, recovery-command pattern. |
| `Content.Tests/_Solreign/TitleGrantRulesTests.cs` (new) | 16 pure-policy tests. |
| `Content.Tests/_Solreign/AdminTitleGrantStoreTests.cs` (new) | 13 persistence-contract tests (temp-DB pattern). |

## Requirement → implementation map

1. **Admin-permission-gated, matching existing solreign commands** — `[AdminCommand(AdminFlags.Host)]`, same flag as `solreign_season_reset` and all three `solreign_ledger_recovery_*` commands. (Wingmate moderation commands use `Moderator`, but every SeasonLedger command is Host — matched the ledger family.)
2. **Length-capped ~48 and sanitized** — `TitleGrantRules.TryNormalize`: whitespace collapse (trim + runs→single space), then **fail-closed rejection** of anything non-printable-ASCII and of the RobustToolbox rich-text metacharacters `[` `]` `\`, then a hard 48-char cap measured post-collapse. Rejection (with a console-safe reason) was chosen over silent rewriting so the granting admin sees exactly what will be stored. The title renders through `args.PushMarkup` in `OnExamined`, so markup metacharacters are the real attack surface.
3. **Persists across rounds like earned titles** — new `admin_title_grants(user_id, season_id, title, granted_by, granted_utc, PK(user_id, season_id))` SQLite table in the ledger's own DB. Season-scoped exactly like earned titles: a season bump retires the grant (row stays archived under the old season id), mirroring how earned titles reset on bump. Deliberately **separate** from `title_grants`, which is the earned-title *ceremony-announcement dedup* record, not a display authority.
4. **Matching revoke** — `solreign_revoke_title`; store `DELETE` scoped to current season, returns whether a grant existed; archived prior-season rows never touched.
5. **Paper trail** — `IAdminLogManager.Add(LogType.Action, LogImpact.Medium, …)` + a sawmill `Log.Info` line, both via pure `TitleGrantRules.FormatGrantAudit/FormatRevokeAudit` ("who granted what to whom", with target name AND GUID). `granted_by`/`granted_utc` also persist on the row itself. No trail is written for a no-op revoke.
6. **Unit tests per _Solreign conventions** — pure-logic + store-against-temp-DB only (no ECS harness), same shape as `TitleRulesTests` / `SeasonLedgerStoreTests`.

## Design decisions worth knowing at review

- **A grant MASKS the earned title; earned progression keeps running underneath.** `LoadTitle` reads the grant AFTER the ceremony gate, so bulletins, the announced-title record, and the title-earned HR bonus are all computed from the earned title as before. `PendingTitle.CeremonyTitle` (new field) carries the earned title so an HR ceremony can never announce an admin mask as though it were earned. Revoking simply un-masks.
- **No schema-version bump.** `user_version` stays 4: the table is purely additive (`CREATE TABLE IF NOT EXISTS` batch), an older binary opening the DB ignores it, and `SeasonLedgerRestoreVerifier.RequiredTables` was checked — it is a *minimum* set (`title_grants` isn't in it either), so backup/restore manifests are unaffected.
- **Store guard is defense in depth**: `SetAdminTitleAsync` throws on empty title/operator even though the command layer already validates — no unchecked path can persist an empty grant.
- **Fail-closed console**: both commands catch-all and never echo exception details (paths/SQL), matching `SeasonLedgerRecoveryCommands`.
- **Live refresh is main-thread-safe**: grant/revoke on an online player re-runs the existing `LoadTitle` → `_pending` queue → `Update` path; no entity is mutated off-thread.

## Proofs

- Build: `dotnet build Content.Server` — 0 errors (pre-existing warnings only).
- `dotnet test Content.Tests --filter FullyQualifiedName~_Solreign` — **1176 passed / 0 failed** (2 Windows-only skips).
- Full suite `dotnet test Content.Tests` — **1595 passed / 0 failed / 3 skipped**.
- New tests in isolation (`~TitleGrant|~AdminTitleGrant`) — **29 passed**.
- Coverage highlights: persistence across store **reopen** (the "across rounds/restarts" claim), season-bump retirement **plus** archived-row survival, regrant-replaces, revoke true/false semantics, `granted_by`/`granted_utc` audit columns read back via raw SQL, and isolation from the earned `title_grants` record (grant+revoke leave `GetAnnouncedTitleAsync` untouched).

## cdx adversarial review r1 (2026-07-15) — verdict was NOT-MERGE-SAFE; all findings dispositioned

r1 found 1 HIGH / 6 MED / 1 LOW. Orchestrator verified each against the code; fixes landed in the
second code commit on this branch. Dispositions:

| # | Sev | Finding | Disposition |
|---|---|---|---|
| 1 | HIGH | `RefreshTitleIfOnline` re-entered the full `LoadTitle` path; racing a spawn load could double-pay the earned-title HR bonus and double-announce | **FIXED** — refresh is now display-only (`LoadTitle(…, allowCeremony: false)`): never reads/writes the announced-title record, never pays the bonus. The narrower pre-existing double-SPAWN race is unchanged and out of this lane's scope. |
| 2 | MED | Grant to a zero-history account was invisible (personnel-file "unfiled" gate + ID-card `ShouldStamp` gate hide it) | **FIXED** — `SeasonTitleComponent.HasAdminGrant` (new field) carried through `PendingTitle`; both gates now surface an active grant; `StampIdCardStanding` also un-stamps the carried card when the gate drops (fresh-account revoke). |
| 3 | MED | `TryNormalize` enforced only in the console command; a future Director/web caller could persist markup via the system/store APIs | **FIXED** — full policy now enforced in `SetAdminTitleAsync` at the persistence boundary; the normalized form is what's stored. 3 new store tests. |
| 4 | MED | Post-commit failures (admin log, live refresh) bubbled into the command's catch → console claims "nothing was recorded" about a durable grant | **FIXED** — post-commit steps are best-effort with error logging; the row's own `granted_by`/`granted_utc` audit survives log-sink failure. |
| 5 | MED | Masked title flows onto ID cards; a card dropped before revoke keeps showing it; receipt had claimed cards were untouched | **PARTIAL FIX + CORRECTION** — carried-card revoke now un-stamps (see #2). A card handed off before revoke stays stale until round end only (cards are per-round entities — bounded, same class as earned-title mid-round staleness). The earlier "ID-card stamp untouched" claim in this receipt was WRONG and is retracted: grants do surface on cards by design of the shared `comp.Title` path. |
| 6 | MED | Season bump doesn't live-refresh online players' titles | **ACCEPTED AS CONSISTENT** — identical to pre-existing earned-title semantics (`solreign_season_reset` never refreshed anyone mid-round); grants follow earned titles' lifecycle by design. |
| 7 | MED | `RestoreVerifier` doesn't require/count `admin_title_grants` | **REJECTED FIX** — requiring the table would break restore of every legitimate pre-feature v4 snapshot; precedent: `title_grants` (earned announcements) is likewise not required/counted. Grants are cosmetic entitlements re-derivable from the rewards program's own records. |
| 8 | LOW | Cross-store-instance race between season `SELECT` and mutation | **DEFERRED** — identical shape to pre-existing `SetAnnouncedTitleAsync`/`AwardHrPointsAsync`; a fix belongs in a store-wide `BEGIN IMMEDIATE` pass, not this lane. |

r1 verified non-findings worth keeping: no markup sequence passing `TryNormalize` can open Robust
markup; admin masks never reach `FormatCeremonyBulletin`; console continuations resume on the
main-thread sync context; the additive table migrates cleanly onto pre-existing v4 DBs; no path
overlap with the movement-bob/sprite-anims waves.

Post-fix proof: full `Content.Tests` suite **1598 passed / 0 failed / 3 skipped** (3 new store tests).

## Adversarial review r2 (Grok, 2026-07-15) — verdict **MERGE-SAFE**; residuals fixed

cdx hit its usage quota (returns Jul 21, post-opening), so r2 ran on Grok with the r1 findings, fix
diff, and all current sources inlined — a different model verifying the fixes, not the r1 reviewer
re-grading its own work. r2 confirmed all four r1 fixes (HIGH ceremony race CLOSED), accepted all
four dispositions, and found 3 MED / 2 LOW residuals. Orchestrator verified each (confirmed
`TryFindIdCard`'s active-held-item priority in `SharedIdCardSystem.cs` — R2-1 was real) and fixed:

| # | Sev | Finding | Disposition |
|---|---|---|---|
| R2-1 | MED | Un-stamp branch could clear a FOREIGN held card (TryFindIdCard prefers the active held item) and miss the mob's own PDA card | **FIXED** — un-stamp now clears only a stamp matching the exact title this mob just lost (`previousTitle` captured in Update before overwrite); a foreign card's own standing can no longer be clobbered. Own-card-unreachable staleness stays bounded to round end (documented). |
| R2-2 | MED | Concurrent LoadTitle results applied last-writer-wins; a stale spawn load could overwrite a fresher grant refresh until next spawn | **FIXED** — monotonic per-account load generation (`_titleLoadGeneration`, claimed pre-await on the main thread); Update drops any result superseded while in flight. |
| R2-3 | LOW | `Log.Info` sat outside the post-commit try; a throw could still misreport a durable write | **FIXED** — whole paper trail is one best-effort block. |
| R2-4 | LOW | System-layer audit logged the caller's raw title while the store persisted the normalized form (divergence for future non-console callers) | **FIXED** — `GrantAdminTitleAsync` normalizes first; audit and store both use the normalized form (store re-validates as defense in depth). |
| R2-5 | MED | No behavioral tests for the ECS-side fix surfaces (gates, un-stamp, ceremony gating, enqueue ordering) | **ACCEPTED GAP** — _Solreign test convention is pure-logic + store-vs-temp-DB; these surfaces need the integration harness. Flagged for the merge review: if the integrator wants them, they belong in `Content.IntegrationTests` as a follow-up, not a blocker for a Host-only cosmetic lever. |

r2 also endorsed the r1#7 rejection with a caveat now recorded: a post-feature restore from a
snapshot lacking `admin_title_grants` silently loses grants — intentional tradeoff (grants are
re-derivable from the rewards program's records); note it in ops restore runbooks.

Post-r2-fix proof: build 0 errors; full `Content.Tests` suite **1598 passed / 0 failed / 3 skipped**.

## Not done / follow-ups

- No website→server wiring: this lane only delivers the in-game lever. Design options memo (fleet-drafted, orchestrator-verified) at OPS `docs/architecture/DESIGN-OPTIONS-WEB-TITLE-GRANT-PIPE-2026-07-15.md` — recommends phased Manual-console (C) → Director-mediated typed capabilities (A2); 14 John gates enumerated.
- Granted titles surface on examine AND the ID-card/PDA standing line (shared `comp.Title` path); rank line and HR-points detail remain earned-only.
- `GetCompletion` offers online session names only (locator accepts offline names/GUIDs fine; completion just can't suggest them).
