# GAME FX stale-anchor fail-soft receipt

Date: 2026-07-25

Branch: `codex/solreign-fx-stale-anchor-failsoft-20260725`

Base: `db7c1321fbd0532f0010342a4b69678863b37702`

Verdict: **PARKED / REVIEWABLE / HOLD**

## Outcome

SOLREIGN continues to reject an FX cue when its entity anchor cannot be resolved on the client.
The one expected lossy case—an allowlisted `Broadcast` effect whose supplied entity anchor is
genuinely absent locally—now enters a dedicated bounded diagnostic bucket logged at `Info`.
All ambiguous, confidential, malformed, and other validation failures retain warning severity.

No RobustToolbox, game-content, prototype, map, CVar, launcher, hub, deployment, or production file
changed.

## Security acceptance matrix

| Case | Render | Diagnostic |
|---|---:|---|
| Known `Broadcast`, validation says unresolvable anchor, entity absent locally | no | informational expected-loss bucket |
| Known `DetailOnly`, entity absent locally | no | warning |
| Unknown effect/classification | no | warning |
| Anchor resolves but transform/nullspace/coordinates are invalid | no | warning |
| Non-finite or out-of-range payload | no | warning |
| Missing anchor for a coordinate-only effect | no | warning |
| Any other validation failure | no | warning |

Severity changes only when the recorded reason exactly equals
`ValidateExpectedLossy:AnchorEntityUnresolvable`; prefix or suffix variants remain warnings.
Aggregation still retains the drop count and a bounded correlation-ID sample.

## Test evidence

### Red

The new policy test was introduced before implementation. Compilation failed with `CS0246`
because `SolreignFxDropDiagnosticPolicy` did not exist.

### Green

- Focused policy + existing drop aggregator + wire-validator fuzz band:
  **41 passed / 0 failed / 0 skipped**.
- Complete `Content.Tests._Solreign.FX` namespace:
  **322 passed / 0 failed / 0 skipped**.
- Real connected client/server integration fixture:
  **1 passed / 0 failed / 0 skipped**.
  The fixture observed one raw `dust` wire event, zero accepted `dust` cues, and:
  `[SolreignFx] ValidateExpectedLossy:AnchorEntityUnresolvable: 1 cue(s) dropped ...`
  at informational severity. Since the integration harness treats unexpected warnings as test
  failures, the pass is end-to-end evidence that this exact race is no longer warning-fatal.

The first fixture attempts exposed unrelated setup traffic (`impact_light`). The final fixture uses
the otherwise quiet `dust` effect and scopes accepted-cue assertions to that effect, avoiding a
false claim that the connected test world emits no other FX.

### Known sibling debt, not changed here

`SolreignFxEffectsPrototypeConsistencyTest` was separately observed at
**2 passed / 1 failed / 0 skipped** because its default-off activation assertion expects zero
prototypes while current master exposes 198. That stale activation-fixture contract is addressed by
the independent activation-reconciliation lane, commit
`b7d81bc20b415bf7d91ce494ed8982f74991f005`; this production lane does not rewrite it.

Offline `NU1900` vulnerability-feed warnings occurred during restore/build. One sandboxed run
aborted because VSTest could not bind its loopback socket; the identical isolated test was rerun
outside the filesystem/network sandbox and passed.

## Combined-master qualification

After master integrated the Saltern repair, activation reconciliation, and earlier FX qualification,
a detached synthetic commit combined:

- current master `03588948f194ddabaf1e394b517e59d439e621db`;
- this branch at `957f9e47d9b9667c580148193820fbc143049970`;
- merge tree `feb6bc46763a442139ad8f52b7e6623736bb6be3`;
- ephemeral validation commit `e346e4dcd4e561fe44352d114272700ec0af9650`.

No source was edited in the detached worktree. Serialized evidence:

| Gate | Result |
|---|---|
| exact changed-fixture band plus the new stale-anchor fixture | **48 passed / 0 failed / 0 skipped** |
| FX + activation unit contracts | **334 passed / 0 failed / 0 skipped** |
| Saltern map load | **1 passed / 0 failed / 0 skipped** |
| Saltern-dependent `AntagGhostRoleTest` | **28 passed / 0 failed / 0 skipped** |
| broad `FullyQualifiedName~Solreign` band | **465 passed / 1 failed / 4 skipped / 470 total** |

The prior activation receipt measured 437 passed / 29 failed / 3 skipped / 469 total. The combined
tree therefore removed 28 failures while adding this branch's one new integration test. No
warning-fatal or failing `AnchorEntityUnresolvable` outcome or Saltern cascade remained; expected
lossy missing-anchor summaries were still counted and logged at `Info`.

The sole broad failure was
`FirstDeathSceneIntegrationTest.PostRoundDeath_DoesNotClaim_TheSceneSurvivesForARealRound`.
It failed again in a fresh isolated 1-case rerun during `GameTest.DoTeardown()` at
`Pair.RunUntilSynced()`, so it is reproducible current-head test/lifecycle debt and is not solely
dependent on preceding broad-test order. The harness replaces the underlying synchronization
exception with a bare `Assert.Fail`, so this receipt does not invent a lower-level product cause.

This materially improves integration confidence but does **not** make the tree release-qualified:
the broad band remains red, and map/package/release gates were not run after that hard stop.

## Review and integration gate

Independent final confidentiality/fail-closed review: **PASS, no actionable P0-P3 findings**.
The reviewer confirmed that the exact validation reason, sealed allowlist classification, local
entity absence, fixed diagnostic string, pre-validation receive cap, and bounded aggregator
prevent this severity change from becoming a confidentiality, malformed-input, or log-spoofing
bypass. The accepted residual tradeoff is that an arbitrary nonexistent anchor for a known
`Broadcast` effect is informational; it is still rejected, capped, counted, and aggregated.

A post-commit synthetic merge preview and the combined qualification above were cleanly composed
against local and `origin/master` commit `03588948f194ddabaf1e394b517e59d439e621db`.
This is collision and test evidence, not integration approval.

Claude remains the integrator. Preview this commit with the activation-test reconciliation
(`b7d81bc20b415bf7d91ce494ed8982f74991f005`) and Saltern YAML repair
(`e22d1b2b41db33e41d2955611e79325abe671905`), then rerun the exact changed-fixture integration
band. Keep the earlier RobustToolbox disconnect queue-clear work independent.

This receipt grants no merge, push, deploy, package publication, restart, activation, launcher,
hub, or live authority.
