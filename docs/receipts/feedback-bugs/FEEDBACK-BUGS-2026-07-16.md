# Player-feedback batch 2026-07-16 — bugfix wave

Source: `FEEDBACK-TRIAGE-2026-07-16.md` (John's player-feedback triage against master
@ `de07ac3ec6`). This wave implements the triage's BUG-class diagnoses for 6 items, re-verifying
each against the live source rather than trusting the write-up blind. Branch `fix/feedback-bugs`,
worktree `~/AI/solreign-trees/feedback-bugs`. No push/merge — commits only, per instructions.

Concurrent lane note: `feat/fx-primitives-v1` is active in `Content.*/_Solreign/FX` — this wave
touches zero files under that path, confirmed by `git status`/diff scope below.

---

## 1. Omni gauntlet pickup error — FIXED

**Root cause** (confirmed, matches triage): `SolreignEgg20` (`Resources/Prototypes/_Solreign/Entities/easter_eggs.yml`)
overrides only `Sprite`. Its parent `ClothingHandsGlovesColorBlack` sets an explicit
`Item.InhandVisuals` dict (`{state: inhand-left}` / `{state: inhand-right}`, no RSI path), which
takes the *explicit* branch in `Content.Client/Items/Systems/ItemSystem.cs`'s `OnGetVisuals` —
bypassing the existence check that the *default*-visuals path (`TryGetDefaultVisuals`) performs.
`Content.Client/Hands/Systems/HandsSystem.cs`'s `UpdateHandVisuals` then falls back to the item's
own `SpriteComponent.BaseRSI` (since `Item.RsiPath` is null) with **no state-existence check**, and
calls `LayerSetData` with state `inhand-left`/`inhand-right` — which `egg20_omni_gauntlet.rsi` did
not have (only `icon`). Confirmed this asymmetry (explicit-visuals path skips the check that the
default path has) by reading both code paths side by side.

**Fix**: added `inhand-left`/`inhand-right` (4-dir) states to `egg20_omni_gauntlet.rsi`, procedurally
rigged from the existing icon via `docs/receipts/feedback-bugs/rig_inhand_states.py` (same rig
idiom as `docs/receipts/probe-img2img/inhand_rig.py`, already used in-repo for `beach_ball.rsi`).
Frame 0 of the gauntlet's 4-frame animated icon is used as the static held pose.

**Worn/Clothing look**: per the task's own conditional, checked whether overriding `Clothing` too
was cheap. It is not — no procedural shortcut exists in this codebase for worn/body-slot
(`equipped-HAND`) states (only in-hand rigging has a precedent); doing this convincingly needs real
art. Left un-fixed and explicitly commented in the prototype for the sprite-factory wave (already
tracked as one of the 16 items in the triage's item-8 audit).

**Test**: `Content.IntegrationTests/Tests/Sprite/InhandSpriteRegressionTest.cs`,
`InhandLayersResolveToExistingRsiStates("SolreignEgg20")`.

## 2. Liquid flame thermos open error — FIXED

**Root cause** (confirmed): `SolreignLiquidFlameThermos` parents `DrinkFlaskBar` → `FlaskBase`,
which carries `DrinkVisualsOpenable` (`GenericVisualizer` mapping `OpenableVisuals.Opened` to state
`icon_open`/`icon`). `liquid_flame_thermos.rsi` had only `icon` — opening it errored. Separately, no
ancestor in the flask chain sets `Item.InhandVisuals` or `Item.sprite`, so (unlike item 1, which
inherits an *explicit* InhandVisuals entry and skips the existence check) this entity takes the
*checked* default-visuals path in `ItemSystem.TryGetDefaultVisuals` — a missing inhand state there
resolves to silently-nothing-shown-in-hand, not a crash. Still worth the same fix (real states).

**Fix**: added `icon_open` (procedurally derived — cap-disc rows stripped per the icon's own alpha
mask, exposed neck pushed toward a warm amber glow, matching "warm, glowing yellow soup") +
`inhand-left`/`inhand-right` (procedurally rigged) to `liquid_flame_thermos.rsi`. Generator:
`docs/receipts/feedback-bugs/rig_inhand_states.py` (`make_icon_open` + `rig_target`).

**Test**: `InhandLayersResolveToExistingRsiStates("SolreignLiquidFlameThermos")` +
`LiquidFlameThermosHasOpenState` (both in `InhandSpriteRegressionTest.cs`).

## 3. Transit capsule shows beachball in hand — FIXED

**Root cause** (confirmed): `SolreignEgg12` parents `BeachBall`, which sets `Item.sprite:
Objects/Fun/Balls/beach_ball.rsi` **explicitly** (non-null `Item.RsiPath`). Per `HandsSystem.cs`,
an explicit `Item.RsiPath` wins over the entity's own `Sprite.BaseRSI` for the default-visuals
fallback, so held rendering kept showing the parent's beach ball art even though ground/inventory
correctly showed the custom orb.

**Fix**: added `Item.sprite: _Solreign/EasterEggs/egg12_transit_orb.rsi` override on `SolreignEgg12`,
plus `inhand-left`/`inhand-right` (procedurally rigged, slightly larger grow factor for the sphere)
on `egg12_transit_orb.rsi`.

**Caught by the regression test itself**: first pass forgot to update `egg12_transit_orb.rsi`'s
`meta.json` after generating the PNGs — `InhandLayersResolveToExistingRsiStates("SolreignEgg12")`
failed with "no in-hand layers resolved at all" until the states were declared. Left that in this
receipt on purpose as evidence the test is a real check, not a tautology (also independently
verified by reverting item 1's meta.json fix and confirming the corresponding test case fails).

**Test**: `InhandLayersResolveToExistingRsiStates("SolreignEgg12")`.

## 4. Punpun lights — INVESTIGATED, NO CODE BUG FOUND; regression test added

Triage item 22 flagged this as low-confidence, unconfirmed mechanism, explicitly recommending a
live repro over more static reading. Did the live repro
(`Content.IntegrationTests/Tests/_Solreign/PunpunHandheldLightIntegrationTest.cs`), using the real
`MobMonkeyPunpun` prototype (single `HandLocation.Left` hand, `ComplexInteraction` present) and a
real `FlashlightLantern`:

- Pickup into Pun Pun's one hand: **succeeds** (`SharedHandsSystem.TryPickup`).
- "E" on the held light via `SharedInteractionSystem.InteractionActivate` (the exact production
  call path for a real E-press — `FlashlightLantern` has no `ItemToggleComponent`, it uses the
  legacy `HandheldLightSystem` + `ActivateInWorldEvent`): **returns true, not blocked**.
- `HandheldLightComponent.Activated` and (server-side) `PointLightComponent.Enabled` both flip to
  **true** — the light does **not** stay dark.
- Client-side, after the state replicates: both the base in-hand layer and the
  `ToggleableVisuals` glow-overlay layer (`inhand-left-light`) resolve to real states in
  `flashlight.rsi` for `HandLocation.Left` — `flashlight.rsi` already ships every state needed
  (`inhand-left`, `inhand-right`, `inhand-left-light`, `inhand-right-light`), so this is **not**
  the same missing-state class as items 1-3.

Also traced (and ruled out) every species-gating candidate in the codebase: `CanComplexInteract`
(Pun Pun has `ComplexInteraction`), `ItemToggleSystem.OnUseInHand` (no gate at all), `UseAttemptEvent`
subscribers (MobState/Cuffable/Stun/Ghost/AdminFrozen — none species-specific), `Eye`/`ContentEye`
defaults (`DrawLight = true`, unmodified anywhere in the monkey ancestor chain), and confirmed no
Solreign-specific override of Punpun exists on any map (plain vanilla `MobMonkeyPunpun` is placed).

**Conclusion**: no reproducible bug exists in the toggle/light mechanism itself at the state/logic
level — every candidate the triage named (toggle blocked, dark point light, missing render state)
is directly falsified by this test. If the original report is real, it can only be a pixel-level
GPU/shader rendering symptom in some specific circumstance, which is outside what a headless
integration test can observe and needs an actual visual QA pass to pin down. No code change
accompanies this item; the test is added as a permanent regression lock on the mechanism so a
future change that reintroduces a real block will fail CI instead of waiting for the next report.

## 5. Companion cube size -25% — FIXED

`SolreignCompanionCubeStructure` (`Resources/Prototypes/_Solreign/Entities/portal_props.yml`):
`scale: 1.4, 1.4` → `scale: 1.05, 1.05` (1.4 × 0.75 = 1.05, exactly -25%). One-line change, single
prototype, applies to all 7 station maps that place it (no per-map edits needed).

## 6. Foam crossbow Solreign edition wrong/green sprite — FIXED (the in-scope part)

Per the task's conditional: the triage found the intended fix here (`color: "#9dfd39"` acid-green
tint, matching every sibling reskin in the same file and the exact pattern already used on
`SolreignKnockoutBat`) is a trivial one-field convention fix, not new art — so implemented it,
rather than deferring to the sprite wave. Added a `Sprite` override with the tint to
`SolreignFoamCrossbow` (`Resources/Prototypes/_Solreign/Entities/weapons_chaos_tier.yml`), matching
the file's own documented "acid-green Sprite tint" idiom.

---

## Verification

- **Build**: `dotnet build SpaceStation14.slnx -c Debug` → **0 errors** (51 projects).
- **YAML linter**: `dotnet run --project Content.YAMLLinter -c Debug` → exit 0, "No errors found."
- **Solreign unit filter**: `dotnet test Content.Tests -c Debug --filter FullyQualifiedName~_Solreign`
  → **1710 total, 1708 passed, 2 skipped** — matches the master baseline exactly.
- **New regression tests** (`Content.IntegrationTests`):
  - `Tests/Sprite/InhandSpriteRegressionTest.cs` — 4 tests, all passing; verified to genuinely
    catch the bug class by reverting each fix in turn and confirming the corresponding test fails.
  - `Tests/_Solreign/PunpunHandheldLightIntegrationTest.cs` — 1 test, passing.
  - Full `Tests._Solreign` namespace: 199 total, 198 passed, 1 skipped (pre-existing skip,
    unrelated) — no regressions from this wave's changes.
  - Full `Tests.Sprite` namespace: 5/5 passed.
- **GameMapsLoadableTest** (map-adjacent change: companion cube structure placed on 7 station
  maps): filter `GameMapsLoadableTest` → **246/246 passed** (includes `NonGameMapsLoadableTest`
  by substring match).
- **grk review**: see `docs/receipts/feedback-bugs/grk-review.md`.

## Files changed

- `Resources/Prototypes/_Solreign/Entities/easter_eggs.yml` (items 1, 3)
- `Resources/Prototypes/_Solreign/Entities/solreign_easter_eggs.yml` (item 2)
- `Resources/Prototypes/_Solreign/Entities/portal_props.yml` (item 5)
- `Resources/Prototypes/_Solreign/Entities/weapons_chaos_tier.yml` (item 6)
- `Resources/Textures/_Solreign/EasterEggs/egg20_omni_gauntlet.rsi/{meta.json,inhand-left.png,inhand-right.png}`
- `Resources/Textures/_Solreign/EasterEggs/liquid_flame_thermos.rsi/{meta.json,icon_open.png,inhand-left.png,inhand-right.png}`
- `Resources/Textures/_Solreign/EasterEggs/egg12_transit_orb.rsi/{meta.json,inhand-left.png,inhand-right.png}`
- `Content.IntegrationTests/Tests/Sprite/InhandSpriteRegressionTest.cs` (new)
- `Content.IntegrationTests/Tests/_Solreign/PunpunHandheldLightIntegrationTest.cs` (new)
- `docs/receipts/feedback-bugs/rig_inhand_states.py` (new, generator)

## Credit

Player feedback batch 2026-07-16, root-caused via `FEEDBACK-TRIAGE-2026-07-16.md`, implemented this
wave per-item as above.
