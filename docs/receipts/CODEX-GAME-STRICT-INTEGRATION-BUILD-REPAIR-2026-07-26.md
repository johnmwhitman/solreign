# Codex GAME strict integration-build repair receipt — 2026-07-26

## Verdict

`PASS-AS-LOCAL-TEST-INFRASTRUCTURE-EVIDENCE / CLAUDE INTEGRATION HOLD`

The SOLREIGN integration-test project builds with its normal analyzer and
warning policy enabled, the full unit band is green, and the full
`FullyQualifiedName~Solreign` integration band is green on the exact candidate
identified below. This is test-infrastructure-only evidence. It does not
authorize a canonical merge, push, package, deploy, restart, activation,
launcher/hub change, credential use, or production mutation. Claude remains
the sole integrator.

## Exact identity

- Branch: `codex/game-strict-integration-build-repair-20260726`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-strict-integration-build-repair-20260726`
- Canonical/tested base:
  `70eb235834d1e43e2e7500e9b8dbbbeb64273df0`
- Implementation commit:
  `79fb230c10e695f97daea7055fdc9b34703135df`
- Implementation tree:
  `538fd4fd6b444d651a5394a64146db7c0327f625`
- Conflict-free `git merge-tree --write-tree origin/master
  79fb230c10e695f97daea7055fdc9b34703135df` result:
  `538fd4fd6b444d651a5394a64146db7c0327f625`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Binary diff SHA-256:
  `6782c1cad020990ad262758638f42826aece41c7ce0bd06cfa5d9611f7cd124d`
- Post-test `NetworkedComponentParityTest.cs` SHA-256:
  `c2ba67539f51b93fd304654dce8aacca742515bf930f6c1bbdc5f81a60eda239`
- Post-test `SolreignCorporateProjectIntegrationTest.cs` SHA-256:
  `fae09da9df77e0d2cff604e3fa4806ad0983f1b8ca68c0ae361bd26bfd2bd339`

At the post-test identity check, `origin/master`, `HEAD^`, and the merge base
were all the exact canonical base above. If canonical master advances before
landing, the preview-tree statement no longer applies to that new head; Claude
must recompose and rerun the strict build plus affected test bands.

## Scope and semantics

Only two integration-test source files changed:

1. `NetworkedComponentParityTest.cs` now fails explicitly when generated
   networked-component registrations are absent, instead of passing a nullable
   value into LINQ and failing later. This removes the real `CS8604` while
   preserving fail-closed behavior.
2. `SolreignCorporateProjectIntegrationTest.cs` uses a typed
   `ProtoId<SolreignCorporateProjectPrototype>` for the Nanite Refinery lookup.
   This removes four real `RA0033` diagnostics while preserving the same
   prototype identity and string-key assertions.

No production/runtime C#, prototype YAML, content, CVar, analyzer configuration,
warning policy, `NoWarn`, pragma, or suppression changed. The null guard does
intentionally improve test failure semantics from an eventual null failure to
an immediate `InvalidOperationException`.

## Compiler-driven failure-to-green ladder

All .NET commands were serialized with `-m:1`, `-nodeReuse:false`, and
`-p:UseSharedCompilation=false`. Analyzers and the repository warning policy
remained enabled.

| State | Result | Diagnostic delta |
| --- | --- | --- |
| Clean canonical base | **1,277 warnings / 5 errors** in 1m12.96s | one `CS8604` in network parity; four `RA0033` in Corporate Project |
| Null guard only | **149 warnings / 4 errors** in 4.51s | `CS8604` closed; the four `RA0033` remained |
| Typed `ProtoId` added | **149 warnings / 0 errors** in 4.51s | all five target diagnostics closed |

The reduced warning count after the first build reflects normal incremental
compiler output, not warning suppression. “Green build” here means zero errors;
it is not a claim that the wider project is warning-free. Existing offline
`NU1900` vulnerability-feed warnings remained visible.

## Verification

| Gate | Result |
| --- | --- |
| `git diff --check` | clean |
| Focused network-parity and Corporate Project tests | **5 passed / 0 failed / 0 skipped** in 15s |
| Current Shadow viability unit test | **1 passed / 0 failed / 0 skipped** in 1s |
| Full `Content.Tests` | **2,935 passed / 0 failed / 3 skipped / 2,938 total** in 5s |
| Full `FullyQualifiedName~Solreign` integration band | **487 passed / 0 failed / 4 skipped / 491 total** in 9m25s |

The three unit skips were the existing `TestAlertManager` skip and two
Windows-only ACL skips. The four integration skips were expected gated
scenarios: low-pop disabled, inspection disabled, authenticated lifecycle, and
persistent block.

## Deliberate exclusions and residual gates

- The integration result is the complete SOLREIGN-filtered band, not the full
  upstream integration suite.
- No YAML/prototype linter, full upstream-wide integration suite, Release
  package, Windows CI, launcher join, hub listing, live round, or deploy
  rehearsal was run because the owned diff is test-infrastructure-only.
- No claim is made about production gameplay behavior, player-visible behavior,
  packaging, deployment, or live compatibility.
- Claude should cherry-pick the implementation commit, inspect the two-file
  diff, rebuild on then-current canonical master with analyzers enabled, and
  rerun the focused and full SOLREIGN bands before landing.
