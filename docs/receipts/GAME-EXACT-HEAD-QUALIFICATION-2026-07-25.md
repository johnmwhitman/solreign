# Game Exact-HEAD Qualification Receipt — 2026-07-25

## Verdict

**HOLD — NOT PACKAGE QUALIFIED.**

The stop-on-failure ladder reached the Solreign integration band and stopped on 10 failures. Map, full-suite, and package qualification were not run. This receipt makes no deployment, activation, package-publication, or live-state claim.

This receipt is historical and applies only to game HEAD `57340d8e57260c85b3cc600c746821fc66b1a520`. It does not qualify the successor `origin/master` state `04f3f96cc7b76bdd862603f21fa7200e717f179c`.

## Immutable identities

- Game HEAD: `57340d8e57260c85b3cc600c746821fc66b1a520`
- RobustToolbox pin: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- OPS helper pin: `0c7d73dea287cc0409c800832dcfc9f5208eb2cd`

## Measured evidence

| Band | Measured result | Gate |
|---|---|---|
| Content.Tests build, DebugOpt | 0 errors; 1,133 warnings; 2m10.58s | PASS |
| Focused release guards | 2 passed; 0 failed | PASS |
| Full Content.Tests unit band | 2,865 passed; 0 failed; 3 skipped | PASS |
| YAML build | 0 errors; 99 warnings; 5.57s | PASS |
| YAML executable | `No errors found in 32546 ms` | PASS |
| Solreign integration band | 354 passed; 10 failed; 8 skipped; 8m13s | **FAIL** |
| Map qualification | Not run after integration failure | STOPPED |
| Full-suite qualification | Not run after integration failure | STOPPED |
| Package qualification | Not run after integration failure | STOPPED |

## Exact 10 observed integration failures

1. `DirectivesFaxSystemIntegrationTest.Disabled_ByDefault_NeverStartsTheRule`
2. `EchoGardenIntegrationTest.Dormant_ShipPosture_IsZeroBehavior_AndDoesNotDisturbMark`
3. `FirstDeathSceneIntegrationTest.PostRoundDeath_DoesNotClaim_TheSceneSurvivesForARealRound`
4. `MarkBeatsIntegrationTest.Dormant_MasterCVarOff_IsZeroBehaviorRegardlessOfSubGate`
5. `MarkBeatsIntegrationTest.Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence`
6. `MarkGardenIntegrationTest.Dormant_ShipPosture_IsZeroBehavior`
7. `MovementBobIntegrationTest.ShipsDormant_DefaultFalse_OnServerAndClient`
8. `RecordsTerminalIntegrationTest.Dormant_ShipPosture_OpeningForceClosesAndNeverReadsTheLedger`
9. `SolreignWorldFeedbackIntegrationTest.WorldFeedback_RequiresBothDormantByDefaultGates`
10. `FxCueTwoClientConfidentialityTest.ArmBladeExtend_WithRealPvsCulling_ClientOutsidePvsRangeReceivesZeroCuesOnTheWire`

Failures 1–2 and 4–9 directly expose stale dormant/default assumptions after source-default changes. Failure 3 was observed as collateral teardown after a Station Audit side effect. Failure 10 is a separate PVS confidentiality failure: it requires isolated reproduction and must not be attributed to the default changes without evidence.

## Activation-audit reconciliation inventory

This is the audit’s inventory of 10 tests whose dormant/default expectations require reconciliation. It is not a claim that all 10 failed in this run:

1. `DirectivesFaxSystemIntegrationTest.Disabled_ByDefault_NeverStartsTheRule`
2. `DirectivesFaxSystemIntegrationTest.DirectiveOutcomeQuery_WhileDormant_IsNeverReported`
3. `EchoGardenIntegrationTest.Dormant_ShipPosture_IsZeroBehavior_AndDoesNotDisturbMark`
4. `MarkGardenIntegrationTest.Dormant_ShipPosture_IsZeroBehavior`
5. `MarkBeatsIntegrationTest.Dormant_MasterCVarOff_IsZeroBehaviorRegardlessOfSubGate`
6. `MarkBeatsIntegrationTest.Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence`
7. `MovementBobIntegrationTest.ShipsDormant_DefaultFalse_OnServerAndClient`
8. `ProvidenceEventReactiveSystemIntegrationTest.Disabled_ByDefault_RoundEndProducesNoDispatch`
9. `RecordsTerminalIntegrationTest.Dormant_ShipPosture_OpeningForceClosesAndNeverReadsTheLedger`
10. `SolreignWorldFeedbackIntegrationTest.WorldFeedback_RequiresBothDormantByDefaultGates`

In the observed run, the Directives outcome-query test and Providence disabled-by-default test appeared skipped rather than failed. The inspection-disabled test also appeared skipped. Those skips do not resolve the source-default reconciliation requirement.

## Source/default discrepancies and coverage gaps

- Fleet Operations defaults enabled, but lacks direct enabled-path qualification coverage. Its round initialization creates three null-prototype entities and clears the tracking list without deleting previously spawned nodes.
- Directives Fax defaults enabled while comments still describe dormant posture and human activation authority; direct rule tests can bypass the round-start CVar path.
- Echo, Mark, Movement Bob, Station Audit, and related integration tests/comments still contain dormant or ships-false assumptions inconsistent with enabled defaults.
- FX Cue remains default false after its revert, while its CVar comment says enabled. Its earlier default-on state exposed a release/YAML entity-allocation failure that manual positive tests did not catch.
- World Feedback defaults enabled while comments and one integration expectation still describe dormant posture; the master FX Cue gate remains false and can mask behavior.
- Providence reactive behavior defaults enabled while comments still describe false, unreleased, or dark posture.
- Records Terminal defaults enabled while system comments still say it ships false and activation remains reserved to John.
- Shift Archive defaults enabled while comments/tests describe default-false or canary posture; it lacks enabled CVar/UI/ledger integration coverage.
- Station Audit inspection defaults enabled while comments describe an explicit future flip; its fallback can fire independently of the base Station Audit gate.

## Required next evidence

1. Reconcile comments, expected defaults, and dormant tests against the intended source posture.
2. Add an exact CVar-manifest test for release-relevant defaults.
3. Add Fleet Operations enabled-path coverage and Shift Archive enabled CVar/UI/ledger coverage.
4. Isolate and reproduce the FX Cue two-client PVS confidentiality failure.
5. Keep FX Cue default false until its release/YAML lifecycle failure is fixed and the relevant release band is green.
6. Rerun the affected integration band at the same exact pins.
7. If and only if integration is green, continue to map, full-suite, and package qualification.
8. Run a separate exact-HEAD qualification for successor `04f3f96cc7b76bdd862603f21fa7200e717f179c`; do not carry this receipt’s verdict forward as evidence for that state.

Source defaults are not evidence of live configuration or successful runtime activation.
