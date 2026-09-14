# GAME Contracts explainer parity receipt

Date: 2026-07-26
Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
Implementation: `e205e7454a089c5588806cf68bceafe8c072b5fd`
Verdict: **HANDOFF-READY — changed surfaces proven; broad gate baseline-blocked**

## Result

Nocturne and Verdant were the only two maps in the seven-map rotation without
`SolreignSignContractsHowTo`. Both now carry exactly one existing explainer prototype near their
existing Contracts Board:

| Map | Board | Explainer | Distance | Placement evidence |
|---|---:|---:|---:|---|
| Nocturne | `0.5,10.5` | `4.5,9.5` | 4.123 tiles | nearest clean `WallShuttle`; closer wall tiles have an AirAlarm, Directive Terminal, APC, or poster |
| Verdant | `10.5,18.5` | `10.5,19.5` | 1 tile | next clean `WallReinforced` tile |

The first draft placed each sign directly on its board. Independent review correctly identified
that as a wall-sprite and click-target overlap. The final placements separate them. The test rejects
zero-distance placement on every map except the explicitly named pre-existing Leviathan legacy
exception.

## Test-first evidence

Before map changes, the new integration test produced the intended behavioral red:

```text
Failed: 2, Passed: 5, Skipped: 0, Total: 7
SolreignNocturne: expected exactly one Contracts How-To sign ... found 0
SolreignVerdant: expected exactly one Contracts How-To sign ... found 0
```

Exact final working-tree gates, run serially:

| Gate | Result |
|---|---|
| `SolreignContractsSignMapPlacementIntegrationTest` | 7 passed / 0 failed / 0 skipped, 29s |
| `SolreignMapContentSeedIntegrationTest` + `SolreignMapHealthScorecardIntegrationTest` | 18 passed / 0 failed / 0 skipped, 48s |
| `Content.Tests --filter FullyQualifiedName~_Solreign` | 2,461 passed / 0 failed / 2 Windows-only skips, 4s |
| `Content.YAMLLinter` | `No errors found in 21836 ms.` |
| `git diff --check` | clean |

The first linter attempt used `--no-restore` before that project had an assets file and failed
closed with `NETSDK1004`. The rerun restored/builds locally and passed; the exact final tree was
then linted again with `--no-restore` and passed as reported above.

## Broad-gate truth

The full `Content.IntegrationTests --filter FullyQualifiedName~_Solreign` command was attempted on
this branch. Unrelated tests failed during pooled-client teardown on:

```text
[SolreignFx] Validate:AnchorEntityUnresolvable
```

The run was intentionally cancelled after the failure repeated across unrelated Directives,
EchoGarden, Feedback, FirstDeathScene, HotPotato, Library, and Mark tests. This is the known
current-master FX fail-soft defect already isolated in the separate parked candidate `a83bcd2`;
the existing FX + Station Audit synthetic composition has previously measured 389 passed / 0
failed / 4 skipped. No aggregate from the cancelled run is claimed here.

Claude should land/reconcile the FX fail-soft candidate before requiring an exact-head full
`_Solreign` green. The focused seven-map test, neighboring map band, unit/prototype band, and YAML
gate above all exercised the Contracts-map change successfully.

## Independent review

The read-only reviewer initially reported:

- P2: board/sign sprite and click-target overlap in the first placement.
- P3: the test allowed zero-distance regression.

Both were repaired. Final review: **PASS, no remaining P0-P3 findings**.

## Compatibility and rollback

This adds two instances of an existing content prototype and one test. It changes no RobustToolbox
code, protocol, serialization schema, database, persistence, CVar, build pin, launcher discovery,
hub registration, or production state. Rollback is removal of the two map entries and test.

This receipt is evidence, not merge, deploy, activation, or release authorization.
