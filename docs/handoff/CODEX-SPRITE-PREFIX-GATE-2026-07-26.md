# Codex Handoff — Item and Clothing Prefix Sprite Gate

## State

Ready for Claude review as a committed, unmerged test-only branch. Package/release remains HOLD
because the SOLREIGN integration band is red.

## Review order

1. `docs/plans/2026-07-26-codex-item-clothing-prefix-sprite-gate.md`
2. `Content.Tests/_Solreign/SolreignSpriteStateExistsTests.cs`
3. `docs/receipts/SPRITE-PREFIX-GATE-2026-07-26.md`

## What changed

- Added prefix-driven Item and Clothing state synthesis to the permanent sprite-state gate.
- Added red/green detector fixtures and adversarial fail-closed mutations.
- Added exact legacy-debt quarantine and separate anti-silence coverage floors.
- Documented the deliberately unimplemented explicit/default visual slices.

## What did not change

- No game system, component, prototype, sprite, map, CVar, RobustToolbox file, website, Director,
  deployment helper, or live state.
- No merge, push, deployment, restart, activation, launcher, or hub change.

## Landing caveat

The test slice is locally green, but the repository is not package-qualified:

```text
SOLREIGN integration: 441 passed / 26 failed / 2 skipped
```

Claude should review/land this only as a test-hardening slice. It must not be bundled with or cited
as resolution of the separate FX lifecycle, HTN, localization, map/job-spread, or other integration
failures.

## Queued visual work

The scan proves four SOLREIGN Easter eggs currently inherit missing `produce-inhand-left/right`
states. Do not repair eggs 09, 11, or 13 by reusing their one-direction inventory `icon` state.
They require actual four-direction hand rigs. Egg 18 already has suitable in-hand art and can be
rewired in the later explicit-visual lane where that mapping will be validated.
