# Station Audit round-end markup repair plan

Date: 2026-07-25

Branch: `codex/solreign-firstdeath-teardown-repro-20260725`

Base: `afb6b5cfbde7e24cf4b679f12dccf7e8e71e6b15`

State: **IMPLEMENTED / VERIFIED / HOLD FOR CLAUDE**

## Problem

The broad SOLREIGN integration band had one remaining failure:
`FirstDeathSceneIntegrationTest.PostRoundDeath_DoesNotClaim_TheSceneSurvivesForARealRound`.
Generic teardown replaced the underlying connected-client exception with a bare assertion failure,
so the test name did not identify the product fault.

## Test-first sequence

1. Reproduce the exact First Death test on the clean branch base.
2. Synchronize the connected client immediately after `RestartRound`, while the fixture still owns
   the operation and can expose the real stack.
3. Use the exposed stack to identify the emitting product path.
4. Add a focused assertion that the real Station Audit round-end event text parses as Robust
   rich-text markup.
5. Repair only the malformed localization value.
6. Run the focused tests, affected integration fixtures, and the broad SOLREIGN band on the
   synthetic stack that also contains the independent FX stale-anchor repair.
7. Obtain independent source archaeology and fail-closed review.

## Implementation boundary

- Keep the explicit client synchronization in the connected First Death fixture as a diagnostic
  contract.
- Escape the literal Station Audit inspection-header opening bracket before it reaches the
  round-end rich-text surface.
- Parse the complete Station Audit event text in the enabled real-round integration fixture.
- Do not change game rules, audit assignment, rewards, CVars, prototypes, maps, RobustToolbox,
  deployment, or production state.

## Acceptance

- Exact First Death reproduction exposes the original parser exception rather than a teardown-only
  failure.
- Focused Station Audit markup assertion is red before the localization repair and green after it.
- First Death and Station Audit focused tests pass together.
- Combined broad SOLREIGN integration band passes with zero failures.
- Current-master composition is conflict-free.
- Independent review finds no actionable P0-P3 issue.
