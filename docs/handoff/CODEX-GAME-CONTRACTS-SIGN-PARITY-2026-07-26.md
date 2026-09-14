# Codex GAME Contracts explainer parity handoff

## State

- Branch: `codex/game-contracts-sign-parity-20260726`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-contracts-sign-parity-20260726`
- Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- Implementation: `e205e7454a089c5588806cf68bceafe8c072b5fd`
- Push/merge/deploy/activation: none
- Integrator: Claude
- Verdict: **HANDOFF-READY, subject to post-FX exact-head broad qualification**

## Candidate

The candidate closes Contracts explainer coverage from five of seven rotation maps to seven of
seven. Nocturne and Verdant each receive one existing how-to sign on a reviewed nearby wall tile.
A live-loaded integration test makes map parity, station ownership, proximity, and new-placement
non-overlap durable.

Durable evidence:

- `docs/plans/2026-07-26-codex-contracts-sign-parity.md`
- `docs/receipts/GAME-CONTRACTS-SIGN-PARITY-2026-07-26.md`

## Verification

- Test-first red: 5 passed / 2 failed, only Nocturne and Verdant missing.
- Final focused placement: 7/7 passed.
- Map content + health: 18/18 passed.
- `_Solreign` unit/prototype band: 2,461 passed / 0 failed / 2 Windows-only skips.
- YAML/prototype linter: no errors.
- Independent review: PASS after P2 overlap and P3 regression-test findings were repaired.

The current GAME base still contaminates unrelated integration tests with the known
`SolreignFx Validate:AnchorEntityUnresolvable` warning. The separate parked FX fail-soft candidate
must precede exact-head broad qualification; this branch does not duplicate or modify it.

## Claude integration guidance

1. Preserve the GAME landing order already established for the current head: FX fail-soft
   `a83bcd2`, then Station Audit markup `7669ba2`.
2. Cherry-pick `e205e7454a089c5588806cf68bceafe8c072b5fd`.
3. Re-run the focused 7-case placement test and 18-case map content/health band.
4. Run the full `_Solreign` integration band on that exact composed head.
5. Treat all results as review evidence only; release/deploy remains separately gated.

No generated `bin/` or `obj/` artifacts are tracked. No production action is authorized.
