# Game Exact-HEAD Qualification Plan — 2026-07-25

## Status

Executed through the Solreign integration gate. The gate failed, so the ladder stopped.

**Verdict: HOLD — NOT PACKAGE QUALIFIED.**

This is historical qualification evidence for `57340d8e57260c85b3cc600c746821fc66b1a520`, not a current-branch verdict. `origin/master` subsequently advanced to `04f3f96cc7b76bdd862603f21fa7200e717f179c`; that successor state requires its own exact-HEAD qualification.

## Immutable scope

- Game HEAD: `57340d8e57260c85b3cc600c746821fc66b1a520`
- RobustToolbox pin: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- OPS helper pin: `0c7d73dea287cc0409c800832dcfc9f5208eb2cd`
- Branch/worktree: `codex/solreign-game-exact-head-qualification-20260725`
- Purpose: qualify this exact source state through the release-oriented game ladder.
- Excluded: code changes, merge, deployment, live-state inference, activation, or package publication.

## Stop-on-failure ladder

1. Confirm exact game, RobustToolbox, and OPS helper identities.
2. Build Content.Tests in DebugOpt.
3. Run the focused release guards.
4. Run the complete Content.Tests unit band.
5. Build and execute the YAML validation band.
6. Run the Solreign integration band.
7. Only if all prior gates pass: run map, full-suite, and package qualification.

## Recorded execution

| Gate | Result |
|---|---|
| Content.Tests build, DebugOpt | PASS — 0 errors, 1,133 warnings, 2m10.58s |
| Focused release guards | PASS — 2 passed, 0 failed |
| Full unit band | PASS — 2,865 passed, 0 failed, 3 skipped |
| YAML build | PASS — 0 errors, 99 warnings, 5.57s |
| YAML executable | PASS — `No errors found in 32546 ms` |
| Solreign integration band | **FAIL — 354 passed, 10 failed, 8 skipped, 8m13s** |
| Map qualification | NOT RUN — stopped at failed integration gate |
| Full-suite qualification | NOT RUN — stopped at failed integration gate |
| Package qualification | NOT RUN — stopped at failed integration gate |

## Exit criteria not met

Qualification remains blocked until:

1. The exact integration failures are reconciled with the changed source defaults.
2. `FxCueTwoClientConfidentialityTest.ArmBladeExtend_WithRealPvsCulling_ClientOutsidePvsRangeReceivesZeroCuesOnTheWire` is reproduced in isolation and classified.
3. Source comments, default-posture tests, and actual defaults agree.
4. Fleet Operations and Shift Archive have enabled-path coverage adequate for release qualification.
5. The integration band is green at the same immutable pins.
6. The deferred map, full-suite, and package gates then pass.
7. The successor `04f3f96cc7b76bdd862603f21fa7200e717f179c` line is qualified independently rather than inheriting this historical result.

Passing source tests alone must not be interpreted as deployment, runtime activation, package publication, or live-state evidence.
