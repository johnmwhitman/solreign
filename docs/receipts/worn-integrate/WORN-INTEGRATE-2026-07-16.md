# Worn-sprite integration (production batch 10) — receipt

**Date:** 2026-07-16 · **Branch:** `feat/worn-integrate` (worktree
`~/AI/solreign-trees/worn-integrate`, base `master` @ `c8e5a492d6`) · **Status:
HELD at merge gate, pending owner review.** No push, no merge.

Source: `~/AI/SUCCESSION/staged/ccbync-swap/production-batch-10-worn/` (staging
area, not part of this repo) — the player-demanded "ALL WORN ITEMS NEED
SPRITES" wave: 16 audited worn-state items, 1 zero-art YAML fix, 1 deferred
item, plus 6 new-art assets (navy sabre, Oasis Founder's Seal, Greenshield
uniform + armor, foam crossbow green, Halcyon hoverpack, Verge courier
satchel — the last two also close two of the 16 audited items' own
placeholder-art gaps).

## 1. Re-verification against current master (step 1)

Batch-10 was gated (`gates.py`, alignment ≥0.85) against a checkout from
earlier in the day. Master moved twice since then (`feat/feedback-small`,
`fix/feedback-bugs`, both already merged). Re-ran `gates.py` fresh before
touching anything: **PASS: 18/18 assets, alignment 132/132 directions ≥0.85,
game repo untouched** — no drift in the staged art itself. Re-verifying
against the LIVE prototypes turned up real drift in three places (all
reconciled, not silently overwritten):

- **`SolreignEgg20` (omni gauntlet):** `fix/feedback-bugs` (already on master)
  had added a quick-fix `inhand-left/right` (rigged from the icon, not the
  parent rig) to unblock a pickup crash, and its own YAML comment explicitly
  said the real `equipped-HAND` art was "left for the sprite-factory wave."
  This lane closes exactly that gap. Kept master's animated `icon` (4-frame
  `delays`, which batch-10's own meta.json omitted even though the pixels are
  byte-identical), replaced `inhand-left/right` with batch-10's properly
  parent-rig-measured versions, added `equipped-HAND`.
- **`SolreignEgg07` (pale king's needle):** master's RSI carries an `icon1`
  state (SolutionContainerVisuals fill layer) that batch-10's own rebuilt RSI
  silently dropped (out of its state-list scope). Preserved `icon1` by merging
  file-by-file rather than overwriting the directory wholesale.
- **Foam crossbow:** `fix/feedback-bugs` had already applied a runtime
  `color: "#9dfd39"` tint hack to the plain vanilla `foam_crossbow.rsi` as a
  stopgap. Batch-10's real art (a proper procedural recolor RSI) supersedes
  that hack outright.

All other 9 of the 14 "simple" fix items (trench coat, necktie, tactical
lenses, flight jacket, feathered mask, fowl mask, hazard exosuit, red
pendant, antiviral herb) plus the crowbar had byte-identical `icon.png`
between master and staged (md5-verified) — safe wholesale RSI-directory
replacement, no merge needed.

## 2. Items wired

**14 worn-sprite fixes (RSI placed + `Clothing.sprite` override added):**
`SolreignEgg20` (omni gauntlet, + `equipped-HAND`), `SolreignNanowareTrenchCoat`,
`SolreignCrowbar`, `SolreignNecktie`, `SolreignTacticalLenses`,
`SolreignFlightJacket`, `SolreignFeatheredMask`, `SolreignFowlMask`,
`SolreignEgg07` (pale needle — Clothing.sprite added explicitly for
robustness even though Spear's own Clothing has no sprite set and would have
fallen back to the entity's base Sprite RSI anyway), `SolreignEgg10` (red
pendant), `SolreignEgg16` (hazard exosuit), `SolreignEgg18` (antiviral herb),
`SolreignClothingBackHalcyonHoverpack`, `SolreignClothingBackVergeCourierSatchel`
(last two: Sprite AND Clothing both repointed off the vanilla placeholder
path onto the new dedicated RSIs).

**1 zero-art YAML fix:** `SolreignVestmentOfAscension` — added
`- type: Clothing / sprite: Clothing/Neck/Cloaks/cap.rsi`, matching the
already-shipping `SolreignHaloOfFinalAuditor` precedent exactly. No new art.

**1 deferred (unchanged):** `SolreignKnockoutBat` — matches the triage's own
"low priority, not Solreign-specific" call. No fix shipped, no regression.

**4 placeholder→real art swaps:**
- `SolreignNavyOfficerSabre` — Sprite repointed from `captain_sabre.rsi` to
  the new `_Solreign/navy_sabre.rsi`. This entity had **no `Clothing`
  component anywhere in its chain** (`BaseSword`/`BaseMajorContraband` are
  held-only) — added one fresh (`slots: [Belt]`, `quickEquip: false`) to
  actually realize the `equipped-BELT` state the new art delivers. The 64x64
  bag-storage icon (`captain_sabre_storage_64x.rsi`) was outside batch-10's
  delivered scope, so it's left as a documented placeholder, not silently
  dropped.
- `SolreignOasisFoundersIdol` ("the Founder's Idol") — Sprite repointed from
  a tinted vanilla `artifact_fragments.rsi` state to the new, original
  `_Solreign/founders_seal.rsi`. Note: the entity ID is `...FoundersIdol`, not
  "...FoundersSeal" — the "seal" naming is the art's own name, not the entity
  ID; the RUN-REPORT's claim that this and the Greenshield items already
  existed as placeholder-sprite entities held for the sabre and this one, but
  NOT for Greenshield (see below).
- `SolreignFoamCrossbow` — Sprite repointed from the `fix/feedback-bugs`
  runtime-tint hack to the new `_Solreign/foam_crossbow_solreign.rsi`; added
  matching `Item.sprite` override so held/inhand rendering also uses the new
  RSI's `inhand-left/right` states instead of falling back to vanilla.

**Greenshield uniform + armor — corrected premise, both wired:** re-verifying
against current master found that `Resources/Prototypes/_Solreign/Roles/
greenshield.yml`'s `GreenshieldOfficerGear` startingGear referenced the
**vanilla** `ClothingUniformJumpsuitDetective` and `ClothingOuterArmorBasicSlim`
directly — no dedicated Solreign entity existed for either (unlike the sabre
and seal, which did exist with placeholder sprites). Created two new entities
in `greenshield.yml` (`SolreignGreenshieldOfficerUniform`, parent
`ClothingUniformJumpsuitDetective`; `SolreignGreenshieldOfficerArmor`, parent
`ClothingOuterArmorBasicSlim`), wired their Sprite/Item/Clothing to the new
`_Solreign/greenshield_jumpsuit.rsi` / `_Solreign/greenshield_armor.rsi`, and
repointed `GreenshieldOfficerGear`'s `jumpsuit`/`outerClothing` fields at them.

## 3. Owner-eye flags (apply-but-flag, per batch-10's own disclosure)

- **`SolreignEgg18` equipped-HELMET** — shipped intentionally 2x the measured
  anchor size (a genuinely ~9x2px sprig slot on `ambrosia_vulgaris.rsi`)
  for legibility; a disclosed trade-off on this low-priority item. Worth a
  5-second in-game look.
- **Greenshield jumpsuit** — reads correctly as a charcoal uniform with green
  piping at 8x, but is quite dark at true 32px scale (comparable to the
  vanilla detective jumpsuit it's rigged from). Worth confirming it reads as
  "Greenshield" and not "generic dark uniform" at gameplay zoom.
- **(Minor, pre-existing, not introduced by this lane)** grk's review flagged
  that `SolreignVestmentOfAscension`'s Sprite carries a `color` tint that its
  new Clothing override does not restate — but this is the exact same shape
  as the already-shipping `SolreignHaloOfFinalAuditor` two entities above it
  in the same file, which this fix deliberately mirrors verbatim per the
  batch-10 RUN-REPORT's own instruction. Not a regression from this
  integration; flagged for completeness only.

## 4. Collisions / deferrals

**None.** Checked the two named concurrent lanes explicitly:
`feat/regen-integrate` (regen RSIs) and `feat/fx-changeling-armblade` (W4,
changeling files) — this lane's entire diff is confined to
`Resources/Prototypes/_Solreign/Entities/*.yml`,
`Resources/Prototypes/_Solreign/Roles/greenshield.yml`, and
`Resources/Textures/_Solreign/**` (RSI assets + `attributions.yml`). No file
overlap with either lane. `fix/feedback-bugs` (already merged to master, not
concurrent) touched three of the same RSIs/entities this lane also touches
(egg20, egg12-unrelated, foam crossbow) — reconciled explicitly, see §1, not
deferred.

## 5. Licensing

All new/modified RSIs: `CC-BY-SA-3.0`, "Solreign (AI-generated,
human-reviewed)". Worn-state derivations inherit the license of the item
they're derived from (all already CC-BY-SA-3.0/CC0 Solreign or vanilla-parent
art per the RUN-REPORT's per-item license notes). 7 new attribution entries
added to `Resources/Textures/_Solreign/attributions.yml` (crossbow, sabre,
founders_seal, gs_jumpsuit, gs_armor, hoverpack, satchel), matching the
existing registry format.

## 6. Verification (step 4)

- **Build:** `dotnet build SpaceStation14.slnx -c Debug` → **0 errors**, 1247
  warnings (all pre-existing obsolete-API warnings, unrelated to this change).
- **YAML linter:** `dotnet run --project Content.YAMLLinter -c Debug` →
  **exit 0, "No errors found in 30987 ms."**
- **Solreign unit filter:** `dotnet test Content.Tests -c Debug --filter
  FullyQualifiedName~_Solreign` → **1796 passed, 0 failed, 2 skipped, total
  1798** — matches the ~1796 baseline exactly.
- **Inhand/worn regression + all-items-have-sprites:** `dotnet test
  Content.IntegrationTests -c Debug --filter "FullyQualifiedName~
  InhandSpriteRegressionTest|FullyQualifiedName~PrototypeSaveTest"` → 5
  passed, 1 failed, 6 total. The 1 failure (`UninitializedSaveTest`, 18
  sub-assertions) is entirely about **vanilla, non-Solreign** medical items
  (`MedicatedSuture`, `Ointment1`, `Brutepack1`, `HealingToolbox`,
  `OintmentAdvanced1`, `Bloodpack1`, `MaterialCloth`, `Brutepack`,
  `RegenerativeMesh`) hitting a pre-existing float-precision quirk
  (`delay: 2.6999999` vs `2.7`) in their `Healing` component on spawn — zero
  Solreign entities appear in the failure list, and this test method shares a
  file with (but is unrelated to) the sprite test I actually needed.
  `AllItemsHaveSpritesTest` (the one that actually validates every item
  entity — including all newly-wired Solreign ones — resolves a real,
  visible sprite) **passed**, confirming no broken RSI/state reference was
  introduced.
- **GameMapsLoadableTest:** `dotnet test Content.IntegrationTests -c Debug
  --filter "FullyQualifiedName~GameMapsLoadableTest|FullyQualifiedName~
  NonGameMapsLoadableTest"` → **246 passed, 0 failed, 246 total.**
- **grk review** (foreground, `docs/receipts/worn-integrate/grk-review.md`):
  verdict "looks correct" — no YAML errors, no duplicate component types, no
  Sprite/Clothing desync, no unintended balance/mechanic change from the new
  sabre belt-slot or the Greenshield entities. One non-blocking note (the
  VestmentOfAscension color-tint parity gap, see §3) already addressed above.

## 7. Files touched

```
Resources/Prototypes/_Solreign/Entities/heavenzone_props.yml       (VestmentOfAscension)
Resources/Prototypes/_Solreign/Entities/solreign_easter_eggs.yml   (crowbar, necktie, trench, lenses, flight, feathered, fowl)
Resources/Prototypes/_Solreign/Entities/easter_eggs.yml            (egg07, egg10, egg16, egg18, egg20)
Resources/Prototypes/_Solreign/Entities/gear.yml                   (hoverpack, satchel)
Resources/Prototypes/_Solreign/Entities/oasis_identity.yml         (Founder's Idol/Seal)
Resources/Prototypes/_Solreign/Entities/navy_officer_sabre.yml     (sabre + new Clothing component)
Resources/Prototypes/_Solreign/Entities/weapons_chaos_tier.yml     (foam crossbow)
Resources/Prototypes/_Solreign/Roles/greenshield.yml               (2 new entities + startingGear rewire)
Resources/Textures/_Solreign/attributions.yml                      (7 new entries)
Resources/Textures/_Solreign/EasterEggs/*.rsi/                     (11 replaced/merged RSI dirs)
Resources/Textures/_Solreign/{foam_crossbow_solreign,navy_sabre,founders_seal,
  greenshield_jumpsuit,greenshield_armor,halcyon_hoverpack,verge_courier_satchel}.rsi/  (7 new RSI dirs)
```

## Merge status

**HELD pending owner review.** Committed on `feat/worn-integrate`, not
pushed, not merged. Orchestrator relays to John for the go/no-go, in
particular the two owner-eye flags in §3.
