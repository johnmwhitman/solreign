# GAME watchdog + H3 merge-result receipt

Date: 2026-07-27

Branch: `codex/game-land-watchdog-h3-20260727`

Base: `origin/master` at
`fa12e6ee0295a42b8efb97ce2633f2816bbb92ef`

Qualified merge result:
`5b5779dce1d671225f74a1086363df76151cddde`

## Composition

This branch composes two independently reviewed tranches:

1. the test-only SOLREIGN integration watchdog from
   `codex/game-land-watchdog-20260727` at
   `6f906cd000138c79dba4b4c571d714be295e6c4a`; and
2. the H3 Tier-2 Cargo-role recovery from
   `codex/game-horizon3-recovery-ship-20260727` at
   `dcb0ffaf63a225919721a67faebfa80300cfe5fc`.

The merge was conflict-free. Repeatable kart heats, the autonomous H3.1-H3.3
drafts, engine work, website work, and deployment code are not included.

## Exact merge-result verification

All .NET work ran serially. No concurrent or retry-loop build was used.

| Gate | Result | Receipt SHA-256 |
| --- | --- | --- |
| Tier-2 source contracts | 5 passed, 0 failed | `3fed59d5e6be8a14f03cd590a91423993ef2f6a4cf71ac497f25dd1fc69ced68` |
| Tier-2 engine integrations | 11 passed, 0 failed | `d077e3e0c0456c32a7cb1d44a41f14124cb191220c1a1cc0313eac6b6f43ebf0` |
| Full `Content.Tests` | 2,997 passed, 0 failed, 3 platform skips | `1934d344f6f31df28e1dc315864180d32dce1549c645f872e3a740b3e13cc9e3` |
| SOLREIGN integration band | 534 passed, 0 failed, 4 intentional skips | `5182fb87ce48c280a117d565eedbb9077dea71fd9125b13d4048d32cf2db09c8` |
| YAML/prototype linter | `No errors found in 27905 ms` | command output |
| Whitespace/conflict checks | pass | `git diff --check` |

The SOLREIGN count is the prior 519-test H3 band plus the watchdog's 15
configuration tests. This confirms that both intended tranches are present in
the exact merge result.

The first YAML-linter command used `--no-restore` and stopped immediately with
`NETSDK1004` because this fresh worktree had no linter
`obj/project.assets.json`. The corrected serial command restored that missing
project state and completed successfully. This was a diagnosed prerequisite,
not a product-test retry.

Test receipts:

- `/private/tmp/solreign-watchdog-h3-results/tier2-source-merge.trx`
- `/private/tmp/solreign-watchdog-h3-results/tier2-integration-merge.trx`
- `/private/tmp/solreign-watchdog-h3-results/content-tests-merge.trx`
- `/private/tmp/solreign-watchdog-h3-results/solreign-integration-merge.trx`

## Reachability and deployment boundary

The H3 tests prove both new Cargo roles resolve, spawn, and equip correctly on
all seven maps when runtime configuration selects `SolreignMapPool`.
Checked-in defaults still select `DefaultMapPool`; production activation of
the SOLREIGN pool remains a separate deployment/configuration gate.

Production deployment remains **HOLD**. The installed deployment checkout is
not converged with reviewed release-control work, and no current composition
transactionally parks and restores the complete root runtime bundle with
Resources, client ZIP, and the Season Ledger. Remote artifact identity is also
not SHA-256 bound and a fail-closed typed boot proof is not yet composed.

This receipt qualifies the Game merge result. It does not authorize production
mutation.
