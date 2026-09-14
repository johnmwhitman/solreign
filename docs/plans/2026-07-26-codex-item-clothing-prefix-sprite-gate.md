# Item and Clothing Prefix Sprite Gate Plan — 2026-07-26

## Status

Implemented and verified on an isolated, unmerged branch. Three independent re-reviews found no
remaining blocker for the deliberately narrow prefix-only contract. Package/release remains HOLD
because the full SOLREIGN integration band is red.

## Immutable lane

- Branch: `codex/solreign-item-clothing-sprite-gate-20260726`
- Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Backlog: narrow SR-W-069 content-dependency and asset-coverage slice
- Production behavior: none
- Activation, merge, push, deploy, restart: prohibited

## Problem

The existing sprite-state gate validates entity `Sprite` and `DamageStateVisuals`, but the SS14
client also synthesizes state names from:

- `Item.heldPrefix` as `<prefix>-inhand-left/right`;
- `Clothing.equippedPrefix` as `<prefix>-equipped-<slot>`;
- `Clothing.equippedState` as a complete state override.

The normal YAML linter does not reject a missing synthesized state. The client fails soft by
omitting the held or worn layer, so players see an item disappear rather than a load failure.

## Test-first sequence

1. Add synthetic missing-held-prefix and missing-equipped-prefix fixtures.
2. Record the real red result before implementation.
3. Mirror the narrow client synthesis rules, including independent prototype-field inheritance,
   Item/Clothing RSI precedence, explicit-visual suppression, and legacy clothing-slot names.
4. Run the detector over the full repository.
5. Preserve pre-existing misses as an exact debt set keyed by prototype, RSI, state, and context;
   reject growth, rebinding, and silent shrink.
6. Add fail-closed mutants for missing RSI, debt rebinding, and unknown worn-slot flags.
7. Add separate held/equipped coverage floors after measuring the final implementation.
8. Run focused, SOLREIGN unit, YAML, and integration gates; record red gates without weakening the
   detector.

## Deliberate boundary

This is a prefix-only regression gate. It does not claim full Item/Clothing runtime parity:

- prefix-free default `inhand-*` and `equipped-*` states remain queued;
- explicit `inhandVisuals` and `clothingVisuals` layer specs remain queued;
- `HandLocation.Middle` is excluded because normal humanoid item art is left/right and existing
  extra-hand content intentionally suppresses overlapping visuals;
- clothing slots are mapped from current `SlotFlags` conventions, not discovered from arbitrary
  custom inventory templates.

Independent review rejected using inventory `icon` art as a shortcut for three unrigged SOLREIGN
Easter eggs. This branch therefore contains no production prototype or asset changes. Those items
need proper four-direction hand rigs in a separate art lane.

## Acceptance

- Synthetic held and equipped misses fail before the implementation and pass after it.
- Debt waivers cannot follow a prototype onto a different RSI.
- Prefix-driven visuals with no resolvable RSI fail closed.
- Unknown visual slot flags are named failures, not silent skips.
- Exact legacy debt cannot grow or shrink without an explicit ledger edit.
- The full sprite-state class, SOLREIGN unit band, and YAML executable are green.
- Any integration red remains a visible package/readiness HOLD.
