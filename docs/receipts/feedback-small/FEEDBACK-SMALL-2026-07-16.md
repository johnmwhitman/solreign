# SOLREIGN feedback-small — 2026-07-16

Branch: `feat/feedback-small` (worktree `~/AI/solreign-trees/feedback-small`), off `master` @
`de07ac3ec6`. Implements the SMALL + approved BALANCE items from
`FEEDBACK-TRIAGE-2026-07-16.md`'s orchestrator-adopted recommendations (the triage's numbers, not
the players' raw asks). No push/merge — commits banked on the branch only.

Concurrent lanes coordinated with: `fix/feedback-bugs` (entity bug fixes — the 6 BUG-classified
items) and `feat/fx-primitives-v1` (FX dirs). See "Collision notes" below.

---

## Per-item results

### 1. Energy katana — non-guaranteed find (triage #2)

**Done.** Was a 100%-guaranteed single-locker placement (`solreign_oasis.yml` uid 550,
`EntityTableContainerFill` with a hard `id: EnergyKatana` entry) — functionally identical to
guaranteed loot despite the "showpiece" doctrine's intent. Per the triage's option (a): rolled into
a weighted pool spread across **5** `ClosetMaintenanceFilledRandom` instances (uid 550, 7789, 9572,
10646, 11244), each independently rolling a new `SolreignEnergyKatanaRareRoll` entityTable
(`_Solreign/Entities/oasis_identity.yml`): 1/20 chance (~5%) per locker, `NoneSelector` otherwise.
Stays on Oasis, stays a maintenance-closet find, no `AccessReader` — just no longer deterministic.

### 2. Botanist greenery → mega seed servitor (triage #5)

**Done.** New seed line `solreignAntiviralHerb` (`_Solreign/Botany/seeds.yml`), grow data cloned
verbatim from upstream `ambrosiaVulgaris` (same RSI, same chemicals), `productPrototypes: [
SolreignEgg18 ]` (the existing "antiviral botanist's greenery" easter egg). Packet entity
`SolreignAntiviralHerbSeeds` (`_Solreign/Botany/seed_packets.yml`, same clone-the-RSI idiom as
`SolreignWardGarlicSeeds`), added to `MegaSeedServitorInventory`
(`Catalog/VendingMachines/Inventories/seeds.yml`). Locale strings in
`Locale/en-US/_solreign/botany.ftl`. This also resolves item #4 ("antiviral," vague on its own) —
the triage's own grounding: #4 fully resolves via #5's context, no separate action needed.

### 3. Engi lockers + nitrogen tank (triage #13)

**Done.** One line: `NitrogenTankFilled` added to `FillEngineerHardsuit`
(`Catalog/Fills/Lockers/engineer.yml`), alongside the existing `OxygenTankFilled`. Prototype already
existed (used in cargo/salvage/emergency crate fills) — standard upstream-style fill addition.

### 4. Oasis randomized spawns (triage #12, #14)

**Done**, split into two mechanisms:

- **Trash (#12):** the two hardcoded `TrashBag`/`TrashBananaPeel` entities (same tile, same item,
  every round) replaced with 3 `RandomSpawner` instances at the same 3 tiles, merged into the
  map's existing `RandomSpawner` proto block (not a new block — kept the file's one-block-per-proto
  convention). `RandomSpawner`'s own default table (`GenericTrashItems`,
  `Markers/Spawners/Random/trash.yml`) is the established weighted litter table used elsewhere,
  just never wired to Oasis before.
- **Founder's idol / ID card / Sublevel H gate (#14):** Oasis never got the identity-prop treatment
  every later station got (`leviathan_identity.yml`/`meridian_identity.yml`/`perihelion_identity.yml`
  precedent) — new `_Solreign/Entities/oasis_identity.yml` adds `SolreignOasisFoundersIdol` (reuses
  `Objects/Specific/Xenoarchaeology/artifact_fragments.rsi` state `ancientball3`, tinted, same
  no-mechanics doctrine as the other stations' showpieces — no announce, no admin alert). Both the
  idol and the existing `SolreignCrimsonKeycard` (hellzone access card,
  `Markers/hellzone_gates.yml`) are merged into a new `SolreignOasisIdentityRareRoll` entityTable
  (1/30 each, per locker) and spread across **6** different `ClosetMaintenanceFilledRandom`
  instances (uid 7058, 7518, 9465, 9793, 9862, 10221) — a genuinely randomized spawn *location* each
  round, not a single hardcoded placement.

  `SolreignSublevelHGate` itself is a physical Gateway-idiom **structure**, not
  container-spawnable — it needs a fixed `Transform` like every other Gateway-idiom entity in this
  fork, and true per-round position randomization for a placed structure would be a new C# system
  (out of SMALL scope, not requested). Placed it once, deterministically, at the south
  deep-maintenance pocket (`pos: -3.5,-39.5`, uid 900803) per the file's own stated follow-up plan
  ("hand-placing one instance ... is the follow-up mapping step"). What *is* randomized is whether/
  where you find the Crimson Keycard needed to get through it — the functional "will you find your
  way in this round" experience is still randomized. Flagging per the showpiece doctrine's own
  caveat: this coordinate is grep-verified against the map file, not walked/confirmed in a running
  client — treat it as "confirm the exact tile," same disclaimer the original showpiece-placement
  doc carries for all six of its own coordinates.

### 5. Mandatory fun deployment tube + foam bullets (triage #30)

**Done.** One line: `BoxDonkSoftBox` (existing foam-dart box, `Item.size` inherits `BaseItem`'s
`Small`, fits the tube's `maxItemSize: Small`) added to `SolreignFireworksLauncher`'s
`EntityTableContainerFill` list alongside the existing fireworks.

### 6. Navy officer's sabre (triage #18)

**Done.** New entity `SolreignNavyOfficerSabre`
(`_Solreign/Entities/navy_officer_sabre.yml`), 18 Slash (not 23 — the triage's own comparable table:
cutlass 16, machete 15, katana 15, **Captain's Sabre 17** deliberately tuned to be the melee-tier
ceiling, claymore/throngler 20, `EnergyKatana` 30 antag-only). 18 sits alongside the Captain's Sabre
without outclassing it, no new mechanics (no reflect chance, no charge ability — none requested).
Placeholder sprite: reuses `captain_sabre.rsi`/`captain_sabre_storage_64x.rsi` wholesale, noted
in-file for the sprite-factory wave to replace with bespoke Navy-branded art. No "Navy Officer"
role/job exists in this fork (triage's own note), so — to keep it actually reachable rather than a
silent orphan — it rides the same `SolreignOasisIdentityRareRoll` pool as the founder's idol/keycard
(item #4 above) instead of sitting unplaced or fake-allowlisted as "by-design admin-only."

### 7. Nanoware trench coat armor (triage #7 armor half only)

**Done** (armor numbers only — the sprite half of #7 is item #8's SPRITE-factory-wave audit,
explicitly out of this task's item list). `SolreignNanowareTrenchCoat`
(`_Solreign/Entities/solreign_easter_eggs.yml`) previously had `Armor.modifiers.coefficients: {
Slash: 0.95 }` only (5% reduction, armor in name only). Added `Blunt/Slash/Piercing: 0.88` across
the board — sub-vest (standard `ClothingOuterArmorBase` is 0.70) because this item also carries free
storage a security vest doesn't, and it's an unrestricted easter-egg find, not Security-gated. No
Heat/Explosion bonus added, per the triage's own caveat against power creep.

### 8. Chef CQC (triage #3) + antiviral (triage #4)

- **Chef CQC: implemented.** Triage classified it SMALL with a clear, grounded mechanism ("reuse
  the existing combo-core framework... comparable effort to the ~120-190 line Judo Belt/Carp Scroll
  files"). Built "Chef CQC": a new martial-arts toy following the Judo Belt's wear-conditioned grant
  idiom (apron on = certified) with the Way-of-the-Ornamental-Carp's two-input pairwise combo shape
  (own pure-logic copy, `ChefComboRules.cs`, deliberately not shared with Carp/Judo's own copies, per
  their own stated "stay independently removable" discipline):
  - Swat, Swat → **Heat Check** (stamina jolt)
  - Swat, Toss → **86'd** (knockdown)
  - Toss, Toss → **Order Up** (disarm throw)
  New files: `Content.Server/_Solreign/MartialArts/{ChefComboRules,SolreignChefApronComponent,
  SolreignChefApronWearerComponent,SolreignChefApronSystem}.cs`,
  `Content.Tests/_Solreign/ChefComboRulesTests.cs` (23 tests, mirrors `CarpComboRulesTests.cs`
  exactly), `_Solreign/Entities/chef_cqc.yml` (`SolreignChefApron`, parents the existing
  `ClothingOuterApronChef` — already has its own worn sprite, no sprite gap — no new art needed),
  `Locale/en-US/_solreign/chef-apron.ftl`. **Wiring corrected after grk review** (see below): NOT
  forced via `ChefGear.startingGear` (that would silently strip whatever outer-clothing loadout the
  player picked, every spawn — see grk finding #1). Instead, the existing `ChefApron` loadout option
  (`Loadouts/Jobs/Civilian/chef.yml`, one of three: Apron/Jacket/Wintercoat) now points at
  `SolreignChefApron` instead of plain `ClothingOuterApronChef` — picking Apron in the loadout menu
  grants Chef CQC, picking Jacket/Wintercoat doesn't, and the player's own choice is respected
  either way.
  - `DisarmedEvent` hook note: Carp already owns `(MobStateComponent, DisarmedEvent)`, Judo owns
    `(HumanoidProfileComponent, DisarmedEvent)`, the shove-knockback system owns
    `(MovementSpeedModifierComponent, DisarmedEvent)` — Chef hooks a fourth distinct pair,
    `(DamageableComponent, DisarmedEvent)`, to keep each toy's own (component, event) pair distinct
    per that established discipline.
- **Antiviral (#4): not separately implemented** — per the triage's own grounding, this item fully
  resolves via #5 (mega seed servitor), which is done above. No vague/bigger-scope remainder to
  skip; nothing left to do under this label.

### 9. Explicitly NOT implementing (recorded, not built)

- **Shove determinism (#19):** triage recommends against. `CombatModeComponent.BaseDisarmFailChance
  = 0.75f` is a deliberate upstream anti-spam safeguard, and Solreign's own
  `SolreignShoveSystem` already layers a full knockback throw + bonus stamina damage on top of every
  *successful* shove. Making it land 100% of the time would stack a guaranteed stun + guaranteed
  knockback-throw + bonus stamina damage with zero counterplay — a large combat-balance shift, not a
  bugfix. Left untouched. Tune-the-constant candidate for the owner: `BaseDisarmFailChance` itself
  (e.g. ~0.55-0.60), or surface the existing Judo Belt/`DisarmMalus` counters better.
- **Reload-while-walking (#29):** triage recommends against. Ballistic reloads use `DoAfter` with
  `BreakOnMove = true` — a standard, deliberate SS14 anti-kite safeguard. Removing it fleet-wide is a
  real balance change, not a bugfix, and (per the task's framing) fights the same intentional
  anti-spam family Solreign's knockback additions already stack on top of. Left untouched. Tune-the-
  constant candidate for the owner: the movement-tolerance threshold on the DoAfter, or a
  perk/training gate for specific roles/belts rather than a blanket removal.

Both left for the owner as tuning candidates, not silently dropped — recorded here per the task's
explicit instruction.

---

## UID ledger (map-file entity additions, `solreign_oasis.yml`)

Per UID LAW: fresh range **900800-900899**, ascending, zero pre-existing collisions (verified — grep
for `uid: 900[89][0-9][0-9]` across every `Resources/Maps/_Solreign/*.yml` returned nothing before
this branch touched it).

| uid | proto | replaces |
|---|---|---|
| 900800 | `RandomSpawner` | old `TrashBag` uid 8951 (same tile) |
| 900801 | `RandomSpawner` | old `TrashBananaPeel` uid 7351 (same tile) |
| 900802 | `RandomSpawner` | old `TrashBananaPeel` uid 8267 (same tile) |
| 900803 | `SolreignSublevelHGate` | new placement, no prior instance on Oasis |

(Rare-roll pool references on the 10 `ClosetMaintenanceFilledRandom` lockers — uid 550, 7058, 7518,
7789, 9465, 9572, 9793, 9862, 10221, 10646, 11244 — are `EntityTableContainerFill` component
overrides on *existing* locker uids, not new map entities; they consume no new uids.)

## Collision notes (concurrent lanes)

- `solreign_easter_eggs.yml` is shared with `fix/feedback-bugs` (item #10, the liquid-flame-thermos
  BUG fix, lines ~83-96). This wave only touched the separate `SolreignNanowareTrenchCoat` block
  (armor coefficients, ~15 lines further down) — different entity, different lines, but same file.
  Low collision risk (line-based diff, non-adjacent blocks), flagged for the merge step regardless.
- No other item in this wave touches any of the other 5 BUG-classified items' files
  (`easter_eggs.yml` egg12/egg20, `portal_props.yml`, `weapons_chaos_tier.yml`).
- No FX directories (`Content.{Client,Server}/_Solreign/FX/`) touched anywhere in this wave.

---

## Verification (this worktree, `feat/feedback-small`)

```
dotnet build Content.Server/Content.Server.csproj -p:UseSharedCompilation=false -m:1
  -> 0 errors (165 pre-existing warnings)

dotnet build Content.Tests/Content.Tests.csproj -p:UseSharedCompilation=false -m:1
  -> 0 errors (283 pre-existing warnings)

dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -p:UseSharedCompilation=false -m:1
  -> 0 errors (4 warnings, all pre-existing NU1510)

dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -p:UseSharedCompilation=false
  -> No errors found (39.5 s)  [caught + fixed one real mistake: Armor.coefficients needed to
     nest under Armor.modifiers.coefficients — fixed before this run]

dotnet test Content.Tests/Content.Tests.csproj --filter "FullyQualifiedName~_Solreign" --no-build
  -> Passed! Failed: 0, Passed: 1733, Skipped: 2, Total: 1735  (baseline ~1708/2skip + 23 new
     ChefComboRulesTests + net new passing tests from other already-landed lanes since the
     baseline was quoted)
     [First run caught + fixed one real gap: SolreignOrphanReachabilityTest failed because
      SolreignNavyOfficerSabre wasn't reachable from anywhere — fixed by adding it to the
      SolreignOasisIdentityRareRoll pool rather than allowlisting it as fake admin-only.]

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignMapContentSeedIntegrationTest" --no-build
  -> Passed! Failed: 0, Passed: 7, Skipped: 0, Total: 7  (all 7 maps, including Oasis)

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~PostMapInitTest.GameMapsLoadableTest" --no-build
  -> Passed! Failed: 0, Passed: 26, Skipped: 0, Total: 26

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignMapHealthScorecardIntegrationTest" --no-build
  -> Passed! Failed: 0, Passed: 11, Skipped: 0, Total: 11

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignMapPoolIntegrationTest|FullyQualifiedName~SolreignWingmateBeaconMapPlacementIntegrationTest" --no-build
  -> Passed! Failed: 0, Passed: 22, Skipped: 0, Total: 22

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~StartingGearStorageTests|FullyQualifiedName~ChameleonJobLoadoutTest" --no-build
  -> Passed! Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

All green. Two real mistakes were caught and fixed during verification (not hidden): the
`Armor.modifiers` nesting, and the sabre's reachability gap.

## grk review

Ran foreground against the full diff (`grk` / Grok, prompted with the change summary + review
focus areas: C# correctness, YAML/entity-table mistakes, balance vs. stated intent, reachability).

**Two findings, both triaged:**

1. **Chef starting-gear clobbers loadout choice (real bug, fixed).** grk traced
   `Content.Server/Station/Systems/StationSpawningSystem.cs` spawn order (loadout equipped first,
   then `startingGear` via `EquipStartingGear` → `InventorySystem.TryEquip(..., force: true)`,
   `Content.Shared/Station/SharedStationSpawningSystem.cs:118`) and found my original
   `ChefGear.equipment.outerClothing: SolreignChefApron` would silently overwrite whatever the
   player picked from the existing 3-way `ChefApron`/`ChefJacket`/`ChefWintercoat` loadout group on
   every single spawn. Verified the claim directly (read the spawn-order code, confirmed
   `force: true`, confirmed the pre-existing loadout group in
   `Loadouts/Jobs/Civilian/chef.yml`) — **confirmed correct, not a false positive.** Fixed:
   reverted the `ChefGear` edit, re-pointed the `ChefApron` loadout option itself at
   `SolreignChefApron` instead. Re-ran the full Solreign unit filter (1733/1735, unchanged),
   `StartingGearStorageTests`/`ChameleonJobLoadoutTest`, `SolreignMapContentSeedIntegrationTest`,
   and `PostMapInitTest.GameMapsLoadableTest` after the fix — all still green (see numbers above,
   captured post-fix).
2. **Navy sabre "18 outclasses/should be ≤16-17" (reviewed, kept as-is).** grk read this as a
   violation because 18 > Captain's Sabre's 17. This is **not** a bug: the task's explicit,
   orchestrator-adopted instruction was "implement at 18 slash (NOT 23)," and the triage document's
   own recommendation explicitly names a **17-18** range as acceptable ("cap at 17-18 to sit
   alongside the Captain's Sabre, not above it"). 18 is the top of that stated range, chosen
   deliberately by the orchestrator, not a coding mistake — kept as instructed. Noted here as a
   reviewed-and-overruled dissent rather than silently discarded.

## Files changed

```
NEW:
Content.Server/_Solreign/MartialArts/ChefComboRules.cs
Content.Server/_Solreign/MartialArts/SolreignChefApronComponent.cs
Content.Server/_Solreign/MartialArts/SolreignChefApronWearerComponent.cs
Content.Server/_Solreign/MartialArts/SolreignChefApronSystem.cs
Content.Tests/_Solreign/ChefComboRulesTests.cs
Resources/Locale/en-US/_solreign/chef-apron.ftl
Resources/Prototypes/_Solreign/Entities/chef_cqc.yml
Resources/Prototypes/_Solreign/Entities/navy_officer_sabre.yml
Resources/Prototypes/_Solreign/Entities/oasis_identity.yml

MODIFIED:
Resources/Locale/en-US/_solreign/botany.ftl
Resources/Maps/_Solreign/solreign_oasis.yml
Resources/Prototypes/Catalog/Fills/Lockers/engineer.yml
Resources/Prototypes/Catalog/VendingMachines/Inventories/seeds.yml
Resources/Prototypes/Loadouts/Jobs/Civilian/chef.yml   (post-grk-review fix, see above)
Resources/Prototypes/Roles/Jobs/Civilian/chef.yml       (net no-op after the grk-review revert)
Resources/Prototypes/_Solreign/Botany/seed_packets.yml
Resources/Prototypes/_Solreign/Botany/seeds.yml
Resources/Prototypes/_Solreign/Entities/delighters/fireworks.yml
Resources/Prototypes/_Solreign/Entities/solreign_easter_eggs.yml
```
