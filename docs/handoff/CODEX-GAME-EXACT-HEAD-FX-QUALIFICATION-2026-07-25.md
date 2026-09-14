# Codex GAME exact-head FX qualification handoff

## State

- Branch: `codex/solreign-game-exact-head-qualification-fx-20260725`
- Base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`
- Merge/push/deploy: none
- Verdict: **HOLD**

## What is ready for review

- Exact-head FX activation tests reflect the deliberate default-on posture.
- The two-client raw-wire confidentiality fixture is isolated from default-on World Feedback and
  recycled queued events.
- Focused FX integration is green: 9/9.
- The full qualification evidence is preserved in
  `docs/receipts/GAME-EXACT-HEAD-FX-QUALIFICATION-2026-07-25.md`.

## What is not closed

- Full SOLREIGN integration is red: 433 passed / 29 failed / 7 skipped.
- Default-on FX lifecycle crosses round/test-pair teardown and produces unresolved-anchor warnings.
- Non-FX activation tests retain stale dormant-default contracts.
- A late antag ghost-role setup hit a distinct pooled map/YAML parser failure that still needs a
  minimal reproducer.
- Map and package qualification were intentionally not attempted.

## Integrator guidance

Do not merge this branch as release authorization. The narrow test corrections are reviewable, but
GAME remains package-blocked. Land only through the normal Claude integration gate after the
successor lifecycle fix and full green battery prove compatibility.
