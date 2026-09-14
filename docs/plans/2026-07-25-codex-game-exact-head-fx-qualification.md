# GAME exact-head FX qualification plan

Date: 2026-07-25
Branch: `codex/solreign-game-exact-head-qualification-fx-20260725`
Base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`
RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`

## Purpose

Qualify the current GAME exact head after the FX render-pool startup fix and deliberate
`solreign.fx.cue_v1 = true` activation. Preserve fail-closed release gates: a focused or full
integration failure stops map and package qualification.

## Sequence

1. Verify exact Git and submodule identities.
2. Build and run the YAML validator.
3. Run release guards and the full unit band.
4. Run the focused FX integration band.
5. Diagnose and correct only exact-head regressions with reproduced red/green evidence.
6. Run the full serialized SOLREIGN integration band.
7. Stop before map/package qualification on any full-band failure.
8. Commit a factual receipt and handoff. Do not merge, push, deploy, or activate.

## Decision gates

- The 2026-07-25 activation commit is the authority for default-on feature posture.
- Raw-wire confidentiality tests must isolate their intentionally produced stream from unrelated
  default-on producers.
- Test isolation may not hide a production lifecycle defect.
- A green focused band does not override a red full band.
