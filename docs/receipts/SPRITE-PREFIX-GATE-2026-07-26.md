# Item and Clothing Prefix Sprite Gate Receipt — 2026-07-26

## Verdict

**IMPLEMENTATION GREEN; PACKAGE/RELEASE HOLD.**

This branch is a reviewable test-only SR-W-069 reliability slice. It is not merged, pushed,
deployed, activated, or package-qualified. The full SOLREIGN integration gate is red.

## Identity

- Branch: `codex/solreign-item-clothing-sprite-gate-20260726`
- Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Changed production code/prototypes/assets: none
- Changed test: `Content.Tests/_Solreign/SolreignSpriteStateExistsTests.cs`
- Plan: `docs/plans/2026-07-26-codex-item-clothing-prefix-sprite-gate.md`

## Red-to-green evidence

Initial synthetic proof, before implementation:

```text
Detector_ActuallyFires_OnAMissingHeldPrefixState       FAILED
Detector_ActuallyFires_OnAMissingEquippedPrefixState   FAILED
Failed: 2, Passed: 0, Total: 2
```

Receipt:
`/private/tmp/codex-solreign-item-clothing-sprite-gate-20260726/item-clothing-prefix-red.trx`

The first repository pass then found 78 real pre-existing held-prefix misses:

- 70 inherited upstream misses;
- 8 misses across SOLREIGN Easter eggs 09, 11, 13, and 18.

Three proposed icon fallbacks were rejected during independent review: a one-direction inventory
icon is not a four-direction hand rig and would likely float over the holder. All prototype
experiments were removed. No content change remains.

Final focused result:

```text
Passed: 21, Failed: 0, Skipped: 0
```

Receipt:
`/private/tmp/codex-solreign-item-clothing-sprite-gate-20260726/sprite-state-class-pocket-final.trx`

Measured gate coverage:

```text
2209 prototype files
11133 entity prototypes
21515 state references resolved
1586 held-prefix references
174 equipped-prefix/equipped-state references
78 exact legacy misses quarantined
```

Coverage floors retain about ten percent churn margin:

- held-prefix: 1,420;
- equipped-prefix/state: 156;
- all state references: 19,300.

## Independent-review corrections

Three read-only reviewers challenged the first green draft. The branch now:

- binds each legacy debt entry to exact prototype ID, RSI, state, and runtime context;
- compares the complete expected and observed debt sets, making partial fixes attributable;
- rejects an allowlisted prototype rebound to another RSI;
- fails closed when a prefix-driven default has no resolvable RSI;
- fails closed on an unknown worn-slot flag instead of silently skipping it;
- table-tests all current visual `SlotFlags` translations;
- expands `POCKET` to both current runtime slots, `pocket1/POCKET1` and `pocket2/POCKET2`;
- gives the held and equipped slices independent anti-silence floors;
- states that prefix-free defaults, explicit visual layers, arbitrary inventory templates, and
  middle-hand visuals remain out of scope.

## Verification

| Gate | Result |
|---|---|
| final sprite-state class | PASS — 21 passed / 0 failed |
| final `_Solreign` Content.Tests band | PASS — 2,479 passed / 0 failed / 2 Windows-only skipped |
| final YAML executable | PASS — `No errors found in 29631 ms` |
| `git diff --check` | PASS |
| SOLREIGN integration band | **FAIL — 441 passed / 26 failed / 2 skipped / 469 total, 10m07s** |

Integration receipt:
`/private/tmp/codex-solreign-item-clothing-sprite-gate-20260726/solreign-integration-band.trx`

The integration run repeatedly reported the already-documented
`Validate:AnchorEntityUnresolvable` FX teardown warning across otherwise unrelated tests. It also
observed an HTN planning `NullReferenceException`/`[FATL]` during station-audit setup, plus other
independent assertion failures. This branch changes only `Content.Tests`; it cannot correct or
cause production integration behavior. The result is nevertheless an exact readiness HOLD and
must not be hidden by the focused green tests.

No map, full-suite, package, launcher, hub, or live-player gate was run after the integration
failure.

## Follow-up

1. Create a separate sprite-art lane for proper four-direction `inhand-left/right` rigs for
   SOLREIGN Easter eggs 09, 11, and 13; Egg 18 already owns suitable rigged states.
2. Extend the gate to explicit `inhandVisuals`/`clothingVisuals` layer specs.
3. Extend it again to prefix-free Item/Clothing defaults.
4. Resolve current-master integration blockers before citing any branch as package-qualified.

This receipt is evidence for Claude's review. It grants no merge, deploy, activation, or release
authority.
