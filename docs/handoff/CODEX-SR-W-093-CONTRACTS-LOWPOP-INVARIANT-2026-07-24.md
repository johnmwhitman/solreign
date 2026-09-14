# CODEX handoff — SR-W-093 bounded low-pop Contracts

**Status:** HANDOFF-READY; independently reviewed; not merged, pushed, deployed, or activated
**GAME branch:** `codex/game-contracts-lowpop-invariant`
**Implementation commit:** `8f0c6c843d`
**Fork base:** `4e4324f7d2aa52e5f85958b84e9f2ae3e7c53749`
**Validated target:** `origin/master` `78344ca922f705f7d66189d1b8c24d6fe5ba20c6`
**Implementation-commit synthetic merge tree:** `f988414c5bb01213984ea55a0958da2d56e22fa4`
**Paired OPS branch:** `codex/docs-contracts-lowpop-spec-amendment`
**Integrator:** Claude

## Outcome

The dormant low-pop Contracts extension now re-arms a claimable EasyTier contract through two
bounded unresolved claims without permitting unbounded pool growth or repeated normal-contract
drain.

- The pure gate allows an issue only through `MaxPersonal + 1`; issue reaches the unconditional
  `MaxPersonal + 2` hard cap.
- The first unresolved EasyTier claim may retire exactly one oldest OPEN non-easy row.
- Replacement is atomic from the board's perspective: issue first, remove only after success.
- A second re-arm uses the hard-cap slot. A third unresolved claim cannot append or retire another
  normal row.
- Claimed rows are never removed.
- Maintenance retirement does not award Standing, write the Season Ledger, alter streaks, or
  consume skip cooldown.
- Enable and threshold CVar callbacks use the same station reconciliation path; feature remains
  default OFF.
- Empty EasyTier content is fail-safe and warns once per station component state.
- Board UIs update only after an actual reconciliation mutation.

Narrow test-only read accessors expose current Standing and in-memory Ledger contract totals/log
count so exact-once economics are asserted without reflection or persistence changes.

## Verification receipts

Fresh terminal results on the branch:

- `ContractsLowPopIntegrationTest`: **9 passed / 0 failed / 0 skipped**.
- `Content.Tests`: **2,770 passed / 0 failed / 3 platform skips**.
- `Content.IntegrationTests --filter FullyQualifiedName~_Solreign`: first run produced
  **355 passed / 1 failed / 1 skipped** because the known unrelated HTN teardown flake fired in
  `TrashVariationScatterIntegrationTest.BasicTrashVariationPass_ScattersTrashAcrossStation_NotSinglePile`
  (`UtilityOperator.Plan` null reference).
- The failing test then passed **1/1** in isolation.
- A fresh complete `_Solreign` rerun passed **356 / 0 failed / 1 existing skip**.
- `git diff --check`: clean before commit.
- Implementation commit synthetic merge against current fetched `origin/master`: clean, tree
  `f988414c5bb01213984ea55a0958da2d56e22fa4`.

TDD receipts retained from implementation:

- Prior expectations failed 3/42 pure-rule cases before inversion, then passed.
- Old integration behavior failed 1/4 after the corrected invariant, then passed after test
  inversion.
- Atomic oldest-row replacement, live-enable reconciliation, repeated-claim one-retirement,
  once-only missing-content state, exact completion economics, and cross-claimant BUI behavior each
  had a focused failing test or compile failure before the implementation surface was added.

## Independent review

Two read-only reviewers examined the final GAME diff and paired OPS amendment:

- specification review: **CLEAN**, after narrowing the evidence to synthetic claimant A plus real
  connected BUI claimant B and aligning the hard-cap exception;
- correctness/quality review: **CLEAN**, after closing a repeated-claim normal-pool drain and
  correcting stale/absolute documentation.

Direct MiniMax dispatch was unavailable (`404` model unavailable); the sanctioned OpenCode
MiniMax reroute produced no result and was terminated after the doctrine timeout. Grok supplied a
sanitized adversarial analysis; local reviewers independently verified the final tree.

## Integration order and gates

1. Integrate the paired OPS documentation branch first so the governing spec no longer demands an
   impossible absolute guarantee.
2. Integrate this entire GAME branch. `8f0c6c843d` is the behavior commit; later branch commits carry
   the handoff and reviewed comment qualifications and must not be omitted.
3. Re-run the GAME battery on the actual merge result.
4. Keep `solreign.contracts_lowpop.enabled=false`. This handoff is not activation authority.

No push, merge, deployment, CVar flip, live player test, or production mutation was performed.

## Explicit residuals

- The cross-claimant test uses a synthetic unresolved claimant A and one real connected BUI actor
  B. It proves identity separation and the real claim hook; it is not a two-network-client wire
  test.
- The no-content behavior has pure once-only state coverage plus source review of the runtime
  branch. The real prototype registry intentionally contains EasyTier content, so no destructive
  registry mutation was introduced solely to manufacture an empty-pool integration fixture.
- Live enable is integration-tested. The threshold callback is source-reviewed as invoking the
  same reconciliation function; no redundant runtime test was added.
- Activation still requires an authorized canary and live-player evidence.
