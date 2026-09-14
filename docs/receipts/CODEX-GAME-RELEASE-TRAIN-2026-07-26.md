# Codex GAME release-train composition receipt — 2026-07-26

## Verdict

`PASS-AS-LOCAL-RELEASE-EVIDENCE / CLAUDE INTEGRATION HOLD`

The smallest reviewed GAME train that closes the current FX warning-fatal,
Station Audit round-end markup, and Contracts explainer-parity gaps now passes
the exact combined SOLREIGN battery. This receipt is local integration evidence;
it does not authorize a canonical merge, package, push, deploy, restart, CVar
activation, launcher/hub change, or production mutation. Claude remains the
integrator.

## Identity and order

- Canonical base:
  `6368757ee6b980db829ad06c7c21b08a9f61c6f1`.
- Exact tested candidate:
  `aa5a51107bb312d2e7232ec70ca7327e0fe927b2`.
- Exact tested tree:
  `1a7c03f73be16baba2b5b62d168e54849ce6aa08`.
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`.

Cherry-pick/source order and local identities:

| Order | Reviewed source | Local commit | Purpose |
| ---: | --- | --- | --- |
| 1 | `bba52f6898cb7fc4430a2a12e83dfbec2f7242e7` | `a1e51b7c8b` | reject stale-anchor FX while classifying the one bounded expected-loss diagnostic below warning severity |
| 2 | `9b9bd6e31d11047c31f7bba96c2be9cd2de51a9b` | `bb6ddbd69c` | escape Station Audit inspection markup |
| 3 | `7669ba2f618663d17d292d92f6792d794bc4a5c1` | `aa5b4019de` | preserve the forced-inspection parser assertion and reviewed receipt |
| 4 | `e205e7454a089c5588806cf68bceafe8c072b5fd` | `f0b3cddc5b` | place Contracts explainers on Nocturne and Verdant and enforce seven-map parity |
| 5 | new evidence repair | `aa5a51107b` | make the disabled Station Audit assertion current-round scoped on a persistent pooled Ledger |

The three epics have no cross-epic path collisions. The two sequential Station
Audit commits intentionally overlap, and all five commits cherry-picked without
conflict. `git diff --check` is clean.

## Verification

| Gate | Result |
| --- | --- |
| Integration-project rebuild after test repair | 0 errors; 159 existing/offline warnings |
| YAML/prototype linter | `No errors found in 27979 ms` |
| FX unit/policy namespace | 322 passed / 0 failed |
| Focused affected sequence after repair | 46 passed / 0 failed / 1 expected skip |
| Full `_Solreign` unit/prototype band | 2,467 passed / 0 failed / 2 Windows-only skips |
| Authoritative `FullyQualifiedName~Solreign` integration band | **473 passed / 0 failed / 4 expected skips / 477 total** in 6m44s |

All .NET build/test commands were serialized with `-m:1`,
`-nodeReuse:false`, and `-p:UseSharedCompilation=false`. Local VSTest and the
SS14 integration harness required loopback socket permission; no live server,
panel, player data, or credentials were used.

## Test-hermeticity correction

The first focused multi-class sequence produced 46 passed / 1 failed / 1 skip.
`Disabled_ProducesZeroBehavior` saw one valid historical Station Audit row in
the pooled server's persistent test Ledger. The same test passed 1/1 in a fresh
process.

Read-only source archaeology confirmed:

- `Dirty = true` recycles a pooled server; it does not allocate a fresh one.
- the Season Ledger path is unique per pooled server instance and deliberately
  persists across recycled rounds;
- the pre-existing master assertion incorrectly required all audit history to
  be empty; and
- none of the candidate production commits caused that history.

The test now captures the current `GameTicker.RoundId` and asserts that no
audit row exists for that round. It still detects any disabled-system write
without rejecting valid history from earlier rounds or racing late prior-round
persistence. The exact reproducer then passed 46/0/1, followed by the green
authoritative band above.

## Deliberate exclusions and residual gates

- The separate sprite-prefix gate `7be45a81e3` remains a follow-up test package;
  it is not mixed into this runtime landing candidate.
- The HTN private-reflection diagnostic remains separate because it proves a
  historical non-reproduction and adds no runtime fix.
- No full upstream-wide unit/integration suite, Release package build,
  Windows CI, launcher join, hub listing, live round, or deploy rehearsal was
  run here.
- Existing offline `NU1900` vulnerability-feed warnings and upstream analyzer
  warnings remain visible; no warning suppression was added.

Claude should review and land the minimal train in the exact order above,
rebuild on the then-current canonical head, run Windows CI for the two skipped
ACL tests, and keep release/deploy authority separate.
