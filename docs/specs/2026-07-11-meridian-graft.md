# Solreign Meridian — research-sprawl graft plan + room census

**Date:** 2026-07-11/12
**Lane:** Maps program (task #21), large-chassis wave — lane 3 of 3 concurrent lanes (lane 1 = the
biggest candidate, lane 2 = second, this lane = third), running alongside Waves 15/16 (feedback-
polish C#, tests, config/map_pool — files this lane never touches).
**Status:** Chassis + identity content LANDED in this pass — `Resources/Prototypes/Maps/_Solreign/
solreign_meridian.yml` (gameMap prototype), `Resources/Maps/_Solreign/solreign_meridian.yml` (grid,
copied from upstream Marathon + 49 new appended entities, uids 900001-900049), `Resources/
Prototypes/_Solreign/Entities/meridian_identity.yml` (bespoke prototypes), `Resources/Locale/
en-US/_solreign/meridian-lore.ftl` (new lore text), `map_pool.yml` updated to include
`SolreignMeridian`. `Content.IntegrationTests` `GameMapsLoadableTest` run against this map — see
this lane's final report for the pass/fail evidence.

## 0. Chassis pick

Ranked candidates from `Resources/Prototypes/Maps/*.yml` by BOTH minPlayers and raw grid-file byte
size (the same two metrics this fork's earlier chassis picks — Oasis/Saltern, Verdant/Packed,
Perihelion/Packed — already used and agreed on):

| Map | minPlayers | Grid size | Note |
|---|---|---|---|
| Fland | 70 | 5.08 MB | largest — lane 1 |
| Box | 50 | 3.91 MB | second — lane 2 |
| **Marathon** | **35** | **3.15 MB** | **third — this lane** |
| Serpentcrest | 50 | 3.84 MB | passed over: 3-grid pirate-wreck POI (Gorlex/Interdyne/Donk Co./ Cybersun faction dressing baked into room names — "Gorlex command", "Gorlex office" — not a clean `StandardNanotrasenStation` hull) |
| Exo | 40 | 2.70 MB | below Marathon on both metrics |
| Bagel | 35 | 3.41 MB | below Marathon on grid size |
| Plasma | 30 | 3.63 MB | below Marathon on minPlayers |

Marathon verified as a genuine single-grid `StandardNanotrasenStation` (`grids: [30]`, not
multi-grid) before picking it, with a 61-76 job roster and a department layout — Science, Toxins,
Sci Server Room, RD Office, Xenoarch, Robotics, Research Server all clustered in one wing, a
Library, a detached "Chapelroid" chapel satellite reached over its own bridge — that reads as a
"research sprawl" without fighting the base building's own fiction.

## 1. Room census (APC-anchor technique, same method `2026-07-11-map-graft-plan.md` used for Oasis)

54 named-APC rooms grep-verified against `Resources/Maps/marathon.yml` (now byte-identical in
`Resources/Maps/_Solreign/solreign_meridian.yml` except the renamed `BecomesStation` line + the
appended entities). Selected anchors used by this pass:

| Room | Anchor (x,y) | Use |
|---|---|---|
| Xenoarch | (41.5,10.5) | Zoo campus entrance + centerpiece arena |
| Disposals | (48.5,20.5) | Zoo campus far end |
| (unnamed corridor, verified `WindowDirectional` at 44.5,15.5 / 44.5,11.5 + `AirlockExternalLocked` at 45.5,16.5) | ~(44,13) | Planetarium observation dome |
| Library | (-9.5,-35.5) | All 20 handbook fragments |
| RD Office | (28.5,17.5) | Showpiece secret |
| Chaplain Office (Chapelroid satellite) | (-76.5,-42.5) | Heaven/Hell-chain entry gate + Crimson Keycard |
| Bridge | (-33.5,54.5) | Final memo addendum |
| North East Maints | (32.5,44.5) | Hidden labcoat-effigy secret |

**Honest caveat (same one every prior Solreign map-authoring doc in this repo carries, because the
constraint is the same): this was not verified in the RT mapping editor or a running client** — no
map-editor access exists in this session, only text-level YAML authoring plus the
`GameMapsLoadableTest` integration test (this lane's one build exception). Coordinates were chosen
by proximity to grep-verified named anchors (APCs, windows, airlocks) and reasonable offsets (2-8
tiles), the same rigor level as `2026-07-11-map-graft-plan.md`'s own "walk here and confirm, not
paint blind at this pixel" standard — NOT a guarantee that every placement lands on open floor
clear of the existing (fairly dense) Xenoarch-area machinery (`RandomArtifactSpawner`,
`CrateArtifactContainer` x2, canisters, a `PortableGeneratorJrPacman`, several `Catwalk`/cable
runs all sit within a few tiles of the anchors used). A follow-up walk-through pass in the actual
client is the natural next step, exactly as every earlier Solreign map-graft doc has recommended
for its own placements.

## 2. What was built (see meridian_identity.yml + the appended grid entities for the real content)

- **Zoo campus, 13 exhibits + 1 centerpiece** (Oasis's own wing tops out at 3 habitat-divider
  pens) — 3 reused Oasis zoo-fauna reskins (Mothroach/Crab/Bat), 8 plain upstream animal-spawner
  markers (Goat, Sheep, Monkey, Parrot, Carp, Frog, Lizard, Penguin), 2 non-antagonist ghost-role
  mobs (`MobMoproach`, `MobEmotionalSupportScurret` — both grep-verified `NpcFactionMember:
  [Passive]` / nonantagonist `GhostRole`, no `SoloAntagonist` mind role, unlike `MobRatKing`/
  `Behonker` which were deliberately NOT used here), and the `SolreignMeridianSpecimenZero`
  containment-arena centerpiece (see below).
- **Specimen Zero** — reskin of `MobGorilla` (the biggest upstream animal body that already ships
  the "won't throw the first punch, will finish it" idiom via its own `NPCRetaliation` +
  `FactionException` + `NpcFactionMember: [Passive]`), acid-green tint, no new mechanics.
  **Note:** this prototype was originally authored as `SolreignSpecimenZero` and was renamed to
  `SolreignMeridianSpecimenZero` after a real ID collision was found and independently verified
  against a pre-existing, unrelated prototype at `Resources/Prototypes/_Solreign/GameRules/
  minibosses.yml:183` (`id: SolreignSpecimenZero`, a GameRule entity for an existing miniboss
  system, `SolreignSpecimenZeroRule`) — both the grid placement and the entity definition were
  confirmed consistent under the new name before this doc was written.
- **Planetarium observation dome** — sited at the one place in this wing with grep-verified
  external windows (`WindowDirectional` x2 + `AirlockExternalLocked`), 2 `BenchComfy`, signage,
  and the "fused star-glass shard" secret.
- **Chapel gate** — `SolreignSublevelHGate` (the existing, previously-unplaced Sublevel H/Heaven-
  chain entry prototype from `Resources/Prototypes/_Solreign/Entities/Markers/hellzone_gates.yml`)
  placed in the Chapelroid Chaplain Office, plus a `SolreignCrimsonKeycard` nearby so the gate is
  actually reachable — same chain Perihelion/Oasis leave for "the mapping wave" to close.
- **Library** — all 20 `SolreignPaperHandbookPage{N}` fragments (Section 4 of the narrative bible)
  placed together in the Library, per this task's explicit brief (Oasis's own plan scattered them
  across many rooms; Meridian's brief asked for all 20 in one place instead).
- **4 extra memo papers** — `SolreignMeridianMemo{1-4}`, explicit lore continuation of the 8-step
  Oasis Auditor's Memo trail (new locale file `meridian-lore.ftl`, cross-references
  `lore-papers.ftl`'s existing trail without renumbering it).
- **4 secrets total** (old labcoat effigy, Specimen Zero field log, star-glass shard, "the null
  result" showpiece) — all obey the never-announce rule (`2026-07-11-showpiece-placement.md` §1):
  no `AnnounceOnUse`, no comms ping, no admin alert on discovery.
- **Station mood** — `SolreignStationMood` component: gentle day/night cycle (2400s full lerp,
  white↔soft-acid-green), `SolreignSporeDrift` weather preference (spore weather fits a station
  whose zoo/xenoarch wing studies live specimens, unlike Perihelion's solar-flare pick).
- **Changeling-friendly density note** (documentation only, no new mechanic — the brief asked for
  a note, not a build): Marathon's own roster is already dense in several departments — Security
  alone fields 12 roundstart slots (8 SecurityOfficer + Warden + Detective + 4 SecurityCadet
  interns + 2 Lawyers), Medical 8, Command 7 — real crowd cover for a blend-in changeling round on
  this chassis without any new content needed.

## 3. Not done here (deliberately, out of scope or deferred)

- No raw tile-chunk edits of any kind — every new placement is an entity (spawner, mob, sign,
  bench, paper, prop) added at a fresh uid, never a tile paint. RT map tile data is a per-file
  base64-packed chunk array (verified by inspection — not human-editable by hand safely), so floor
  variety in the zoo/dome relies on the chassis's own existing floor + new entities/props, not new
  tile types, even though this file's own tilemap dictionary already registers `FloorGrass`/
  `FloorDirt`/`FloorAsteroidSand` etc.
- No new parallax/skybox art for Meridian specifically (the existing 3 Solreign parallaxes —
  Nocturne/Perihelion/Verdant — are all AI-generated art assets; a 4th one is a follow-up for
  whoever owns the sprite-factory pipeline, not this lane).
- `GameMapsLoadableTest` filtered to this map only is this lane's one build exception — see the
  final report for its actual result.
