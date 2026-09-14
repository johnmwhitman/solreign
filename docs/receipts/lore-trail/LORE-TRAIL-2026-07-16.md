# Season-1 Lore Trail Rollout — 2026-07-16

**Branch:** `feat/lore-trail` (worktree `~/AI/solreign-trees/lore-trail`, off `master` @ `a98700c0a2`).
No push, no merge (per instruction) — this is the owner's read.

---

## SPOILER-SAFE SUMMARY (safe to relay/publish a pitch from this section only)

**Mission:** the map-seed audit (`docs/receipts/mapseed/MAPSEED-AUDIT-AND-FIXES-2026-07-16.md`, Gap 1)
found the Season-1 "Ledger Wakes" lore trail — 8 Auditor's Memos + 20 Employee Handbook pages + the
Vault Reward Crate — was complete on Oasis only. Meridian had the 20 handbook pages but no memos and
no vault; Leviathan had 2 of 8 memos and nothing else; Nocturne, Perihelion, Verdant, and Terminus had
none of it at all. On a random-map round, most rounds on most maps never exposed this content. This
wave closes that gap: **every one of the 7 `SolreignMapPool` rotation maps now carries the complete
trail** — exactly 8 memos, all 20 handbook pages, and 1 vault reward crate, each.

**Design-intent finding:** checked the lore-queue docs and the trail's own source comment
(`lore_papers.yml`: *"the mapping wave places memos along the trail... and scatters the handbook
pages"*) before deciding scope. Nothing in the source material implies per-map subsets are the
intended design — Oasis's own placement is a full 8/20/1 set, and the audit explicitly frames the
other maps' partial/zero counts as an unfinished rollout, not a deliberate map-specific trim. Fixed
scope: **full trail, every map**, matching Oasis's own precedent exactly.

**Placement counts (this wave's new fragments only):**

| Map | Memos added | Handbook pages added | Vault added | Resulting total |
|---|---|---|---|---|
| Oasis | 0 (already complete) | 0 (already complete) | 0 (already complete) | 8 / 20 / 1 |
| Nocturne | 8 | 20 | 1 | 8 / 20 / 1 |
| Perihelion | 8 | 20 | 1 | 8 / 20 / 1 |
| Verdant | 8 | 20 | 1 | 8 / 20 / 1 |
| Meridian | 8 | 0 (already complete) | 1 | 8 / 20 / 1 |
| Leviathan | 6 (memos 7+8 already present) | 20 | 1 | 8 / 20 / 1 |
| Terminus | 8 | 20 | 1 | 8 / 20 / 1 |

**Placement method:** every new fragment is anchored 1-2 tiles from an existing, already-verified-safe
department landmark (a job `SpawnPoint*` marker, or a `DefaultStationBeacon*` navigation marker where
a map ran out of distinct job types) — the same "known landmark Transform" idiom this fork's own map
files already document using (e.g. Nocturne's own header: *"exact prop/light coordinates were chosen
from known landmark Transforms... rather than a full visual pass"*). Each candidate tile was checked
against every other entity already on that map and rejected if anything blocking (walls, doors,
closets, consoles, machines, furniture) already occupied it — only tiles that were empty, or held
only non-blocking infrastructure (cable/pipe/light/sign, the same "infra-only occupants" idiom the
concurrent mapseed-fixes wave used for its own placements), were accepted. This spreads every map's 29
fragments across roughly 20-38 distinct departments per map (exact figures vary by chassis size),
rewarding exploration the same way Oasis's own hand-placed scatter does.

**Open Bunk (the concurrent "Subject 07" mystery arc) coexistence:** Meridian's and Leviathan's Open
Bunk marker tiles (`SpawnSolreignS07PersonnelWing` / `SpawnSolreignS07RecordsWing`) were identified and
a 2-tile exclusion radius was carved out around each before any lore-trail placement ran, so nothing
in this wave shares a tile with, or crowds, that arc's anchors.

**Vending-reachability sweep (mission's secondary ask):** re-ran the audit's egg-reachability script
against the post-mapseed-fixes state and confirmed only 3 gaps remained fixable at the prototype level
(vendor-pack content, not new map machines): `SolreignEgg08` (medical), `SolreignEgg09`/`11`/`13`
(botanical), and `SolreignNanowareTrenchCoat`/`SolreignFlightJacket` (winter clothing) on Meridian.
All 3 were closed by adding the item to a *different, universally-present* vendor pack already
carrying an in-theme catalog (details below). Nocturne's remaining department-shaped gaps (Curator/
Cargo/Detective/Robotics/Lawyer/Chapel-locked novelty items) were deliberately **not** force-relocated
— Nocturne is by design a compact, ~7-max-player chassis without those departments, and stuffing their
locked novelty items into unrelated universal packs would be scope creep past "prototype-level fix"
into altering unrelated stations' vending catalogs project-wide for a department that map never had.

**Verification:** YAML linter clean, zero duplicate uids across all 6 touched map files (uid range
900700-900799, this lane's assigned range), `GameMapsLoadableTest` + the extended
`SolreignMapContentSeedIntegrationTest` (now asserting per-map trail completeness) both green — see
Verification section below for exact numbers.

---

## SPOILERS BELOW — exact placement table, uid ranges, anchor list

### Design process

Placement anchors were selected programmatically (not eyeballed) to guarantee tile safety and
department spread at this scale (six maps × up to 29 new fragments = ~148 placements): a script
(`parse_map.py`/`assign.py`/`gen_yaml.py`, this session's scratch tooling) parsed each map's existing
`SpawnPointJob*` entities (one per job/department, already covered by SR-W-012's own per-job-spawn and
spawn-tile-atmosphere-safety checks) plus `DefaultStationBeacon*` navigation markers as a fallback pool
when a chassis ran short on distinct job types (Nocturne, the smallest chassis at ~20 jobs, needed 4
beacon anchors to reach 29). For each fragment, the script walked a ring of candidate offset tiles
(1-2 tiles from the anchor) and picked the first one where every entity already occupying that exact
tile matched a non-blocking allowlist (cable/pipe/wire/light/APC/sign/decal/vent/sensor — the same
"infra-only occupants" standard the mapseed-fixes wave's own commit message used), or where nothing at
all occupied it. Anchors were consumed round-robin by distinct job type first (maximizing department
spread) before reusing a second instance of any job with multiple spawn points.

### Uid ranges (this lane's assigned range: 900700-900799 per map)

| Map | Uid range used | Count | Parent grid uid |
|---|---|---|---|
| Nocturne | 900700-900728 | 29 (8 memo + 20 handbook + 1 vault) | 2 |
| Perihelion | 900700-900728 | 29 (8 memo + 20 handbook + 1 vault) | 2 |
| Verdant | 900700-900728 | 29 (8 memo + 20 handbook + 1 vault) | 2 |
| Terminus | 900700-900728 | 29 (8 memo + 20 handbook + 1 vault) | 2 |
| Meridian | 900700-900708 | 9 (8 memo + 1 vault) | 30 |
| Leviathan | 900700-900726 | 27 (6 memo + 20 handbook + 1 vault) | 13329 |

Zero duplicate uids confirmed per map (full-file `uid:` scan, not just the new range) after insertion.
Oasis was **not** touched (already complete; source of the pattern being replicated).

### Open Bunk exclusion zones honored

- Meridian: excluded a 2-tile radius around `(-21.5, 20.5)` (parent 30) — the `SpawnSolreignS07PersonnelWing`
  marker, uid 900565.
- Leviathan: excluded a 2-tile radius around `(-18.5, -22.5)` (parent 13329) — the
  `SpawnSolreignS07RecordsWing` marker, uid 900614.

No lore-trail fragment landed inside either radius; nearest lore-trail fragment to each marker is
several tiles away (checked programmatically, not just by eye).

### Anchor department spread (by map)

Every fragment's trailing YAML comment records its anchor prototype and department label (e.g. `#
Atmos dept anchor (SpawnPointAtmos); infra-only occupants: EmergencyLight, GasPipeBend`) — see the
raw diffs in `Resources/Maps/_Solreign/solreign_{nocturne,perihelion,verdant,terminus,meridian,leviathan}.yml`
for the full per-fragment list. Summary of distinct anchor departments used per map:

- **Nocturne** (29 fragments, only ~20 distinct jobs available on this compact chassis): Atmos,
  Bartender, Botanist, Captain, CargoTechnician (×2), Chef, Chemist, Clown, HeadOfSecurity, Janitor,
  Latejoin (×6), MedicalDoctor (×2), Musician, Observer, Passenger, SalvageSpecialist, Scientist,
  SecurityOfficer, StationEngineer, plus 4 `DefaultStationBeacon` fallbacks (Arrivals, Atmospherics,
  Bar, Botany) for the last 4 fragments.
- **Perihelion / Verdant** (same base chassis, 29 fragments each, 38+ distinct jobs available — no
  beacon fallback needed): Atmos, Bartender, Borg, Botanist, Captain, CargoTechnician, Chaplain, Chef,
  Chemist, ChiefMedicalOfficer, Clown, Detective, HeadOfPersonnel, HeadOfSecurity,
  HeadOfSecurityWeapon, Janitor, Latejoin, Lawyer, Librarian, MedicalDoctor, MedicalIntern, Mime,
  Musician, Observer, Paramedic, Passenger, Quartermaster, ResearchAssistant, ResearchDirector.
- **Terminus** (29 fragments, largest job roster of the 7): Atmos, Bartender, Borg, Botanist, Captain,
  CargoTechnician, Chef, Chemist, ChiefEngineer, ChiefMedicalOfficer, Clown, Detective,
  HeadOfPersonnel, HeadOfSecurity, HeadOfSecurityWeapon, Janitor, Latejoin, Lawyer, Librarian,
  MedicalDoctor, MedicalIntern, Mime, Musician, Observer, Parametic, Passenger, Psychologist,
  Quartermaster, Reporter.
- **Meridian** (9 fragments — memos + vault only, handbook already complete): Atmos, Bartender, Borg,
  Botanist, Captain, CargoTechnician, Chaplain, Chef, Chemist.
- **Leviathan** (27 fragments — memos 1-6 + handbook + vault, memos 7/8 already present): Atmos,
  Bartender, Borg, Botanist, Captain, CargoTechnician, Chaplain, Chef, Chemist, ChiefEngineer,
  ChiefMedicalOfficer, Detective, HeadOfSecurity, HeadOfSecurityWeapon, Janitor, Latejoin, Lawyer,
  Librarian, Observer, Paramedic, Passenger, Quartermaster, Reporter, ResearchAssistant,
  ResearchDirector, SalvageSpecialist, Scientist.

### Vending-sweep prototype edits (3 files, content-only, no map surgery)

| Egg(s) | Was gated behind | New home pack | File | Machines now carrying it | Maps fixed |
|---|---|---|---|---|---|
| `SolreignEgg08` | `NanoMedInventory` (`VendingMachineWallMedical`, 2/7 maps) | `NanoMedPlusInventory` (added) | `Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml` | `VendingMachineMedical` (universal, 7/7) | Oasis, Nocturne, Perihelion, Verdant, Meridian |
| `SolreignEgg09`/`11`/`13` | `GetmoreChocolateCorpInventory` (`VendingMachineSnack`, 3/7 maps) | `NutriMaxInventory` (added) | `.../nutri.yml` | `VendingMachineNutri` (universal, 7/7) | Perihelion, Verdant, Meridian, Terminus |
| `SolreignNanowareTrenchCoat`, `SolreignFlightJacket` | `WinterDrobeInventory` (`VendingMachineWinter`, 6/7 maps) | `AutoDrobeInventory` (added, contraband tier — same emag-gated novelty idiom this pack already uses for 2 other Solreign eggs) | `.../theater.yml` | `VendingMachineTheater` (universal, 7/7) | Meridian |

Re-ran the audit's own `audit_egg_reachability.py` (patched locally to track *all* packs an egg lives
in, not just the last one found, since these fixes are additive to a second pack rather than a
replacement) after the edits: **30/32 reachable on every map except Nocturne (21/32, the pre-existing
department-shaped gaps left as documented scope, see above) and the unaffected-by-design `SolreignEgg01`/
`SolreignSodaCan` (2 items with no map in the pool where both are unreachable, unchanged by this
wave).** Before this sweep: Oasis 29, Nocturne 20, Perihelion 26, Verdant 26, Meridian 24, Leviathan 30,
Terminus 27 (post-mapseed-fixes baseline). After: Oasis 30, Nocturne 21, Perihelion 30, Verdant 30,
Meridian 30, Leviathan 30, Terminus 30.

### Scorecard extension

`Content.IntegrationTests/Tests/_Solreign/SolreignMapContentSeedIntegrationTest.cs` extended (same
file the mapseed-fixes wave added) with per-map assertions: exactly one of each of the 8
`SolreignPaperAuditorMemo{1-8}`, exactly one of each of the 20 canonical `SolreignPaperHandbookPage{N}`
ids, and exactly one `SolreignVaultRewardCrate` — all scoped to the station-owned grid the same way
the existing front-door/mood assertions already are, and all counting only entities whose Transform is
parented *directly* to the grid (excluding the vault crate's own bonus copy of
`SolreignPaperAuditorMemo8`, which its `EntityTableContainerFill` spawns as a nested reward item — see
`lore_papers.yml` — so that pre-existing intentional duplicate doesn't false-positive the "exactly one
findable-in-the-world copy" assertion). This closes the exact recurrence risk the mapseed audit
flagged for Gap 1: a future edit that silently drops a fragment on any map now fails CI instead of
drifting back to a partial trail.

### Verification

```
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -p:UseSharedCompilation=false
  -> No errors found (ran twice: once after map placements, once after the vending-pack edits)

Zero-duplicate-uid check (custom script, full-file uid scan per touched map):
  solreign_nocturne.yml:    total_uids=2827  distinct=2827  duplicates=0
  solreign_perihelion.yml:  total_uids=16438 distinct=16438 duplicates=0
  solreign_verdant.yml:     total_uids=16463 distinct=16463 duplicates=0
  solreign_terminus.yml:    total_uids=31223 distinct=31223 duplicates=0
  solreign_meridian.yml:    total_uids=23951 distinct=23951 duplicates=0
  solreign_leviathan.yml:   total_uids=37981 distinct=37981 duplicates=0

dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -p:UseSharedCompilation=false
  -> 0 errors, 94 warnings (pre-existing, unrelated to this change)

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~GameMapsLoadableTest|FullyQualifiedName~SolreignMapContentSeedIntegrationTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
  -> Passed: 253, Failed: 0, Skipped: 0, Total: 253, Duration: 2m 49s
     (matches the mission-stated 253/253 master baseline exactly -- includes all 7
     GameMapsLoadableTest cases plus all 7 SolreignMapContentSeedIntegrationTest cases, now with
     the lore-trail assertions added, still green on every map.)
```

### Files changed

```
Resources/Maps/_Solreign/solreign_nocturne.yml    (+29 entities, uid 900700-900728)
Resources/Maps/_Solreign/solreign_perihelion.yml  (+29 entities, uid 900700-900728)
Resources/Maps/_Solreign/solreign_verdant.yml     (+29 entities, uid 900700-900728)
Resources/Maps/_Solreign/solreign_terminus.yml    (+29 entities, uid 900700-900728)
Resources/Maps/_Solreign/solreign_meridian.yml    (+9 entities,  uid 900700-900708)
Resources/Maps/_Solreign/solreign_leviathan.yml   (+27 entities, uid 900700-900726)
Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml   (+1 line: SolreignEgg08)
Resources/Prototypes/Catalog/VendingMachines/Inventories/nutri.yml     (+3 lines: SolreignEgg09/11/13)
Resources/Prototypes/Catalog/VendingMachines/Inventories/theater.yml   (+2 lines: TrenchCoat/FlightJacket)
Content.IntegrationTests/Tests/_Solreign/SolreignMapContentSeedIntegrationTest.cs  (extended)
docs/receipts/lore-trail/LORE-TRAIL-2026-07-16.md  (this file)
```
