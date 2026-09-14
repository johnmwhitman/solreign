# Directives Fax Consequence Contract Plan — 2026-07-26

## Objective

Close one bounded SR-W-094 evidence gap: prove that the already-integrated Directives Fax
systems compose into exactly one player-visible and durable consequence under a synthetic
duplicate round-end signal.

## Pins

- GAME base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- Base tree: `e0a2482db0bdfc0dbf0830ac56e21d6d0b08c818`
- RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Branch: `codex/game-directives-consequence-v1-20260726`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-directives-consequence-v1-20260726`

## Contract

One controlled rule must produce:

1. exactly one directive start fax;
2. exactly one Directives Fax round-end summary block;
3. exactly one stamped compliance report;
4. exactly one logical Season Ledger streak fold for an account satisfying the real five-minute
   eligibility threshold.

Repeating `RoundEndTextAppendEvent` must not produce a second Directives block, report, or
Ledger fold. Other active game rules may still contribute their own blocks to the shared event.

## Method

- Use a fresh, connected, dirty integration pair with the real ticker and Ledger system.
- Remove only the automatically started Directives Fax rule, snapshot pre-existing paper UIDs
  without deleting them, then start one controlled production rule.
- Protect the connected synthetic player while advancing through the real five-minute presence
  threshold.
- Attach a controlled station bank and cargo-order database, then satisfy every currently
  defined directive clause with zero casualties, four orders, and a 4,000-credit Cargo delta.
- Raise the production round-end event twice.
- Poll the real per-pair Ledger path by its public system API until the async write lands.
- Identify only post-snapshot artifacts: the start fax by selected directive text and lack of
  report stamp, and the end report by its dedicated prototype plus stamp. Do not count or delete
  unrelated papers.

## Verification ladder

1. Focused new integration fixture.
2. New fixture plus the existing Directives Fax live-system fixture.
3. All Directives Fax unit/store tests.
4. `git diff --check`.
5. Independent code review and test-evidence audit.

## Hard boundaries

- Test-only change; no production system, schema, CVar, prototype, map, or asset edits.
- No changes to default-on/default-off, activation, rule-start, or mid-round semantics.
- No claim of live canary, player comprehension, production state, or `proven` backlog status.
- No push, merge, package, deploy, restart, publication, or activation. Claude remains the
  integrator.

## Deferred design issue

`directives_fax_streak.last_round_id` lives in the separate Season Ledger while the integer
round ID comes from the main game database. Rejecting every numerically older round would prevent
stale replays, but could also reject legitimate rounds after a main-database reset. This lane
therefore does not make that unsafe shortcut. A future change needs an explicit durable round
identity contract or a per-outcome history design.
