# Solreign Oasis map-graft plan — zoo wing + lore trail + cake case + fauna

**Date:** 2026-07-11
**Lane:** Maps program (task #21) / Oasis station heavy customization (task #8)
**Status:** Chassis START landed (`Resources/Prototypes/Maps/_Solreign/solreign_oasis.yml` +
`Resources/Maps/_Solreign/solreign_oasis.yml`, a scoped identity-only copy of upstream Saltern).
This document is the graft plan for the NEXT session that opens the map in the RT mapping editor
and does the actual tile placement. Nothing described below has been placed yet.

## 0. How this doc was built (and its one real limitation)

Every room name, APC label, and tile coordinate below was pulled with `grep`/a small Python pass
over `Resources/Maps/_Solreign/solreign_oasis.yml` (identical grid to upstream `saltern.yml` today)
— specifically the `name: <Room> APC` entities, which sit inside their room and give a reliable
anchor point. Coordinates are the `pos: x,y` of that APC (or named door), `parent: 31` (Saltern's
one grid). **This was not verified by opening the map in-editor or in a running client** (ground
rules for this task forbid `dotnet build`) — so treat coordinates as "walk to here and confirm,"
not "paint blind at this pixel." Every entity ID referenced (spawners, structures, papers) was
grep-verified to exist in-repo before being listed; none are invented.

Verified room census on this chassis (name + anchor coordinate), for reference during the walk:

| Room | Anchor (x,y) | | Room | Anchor (x,y) |
|---|---|---|---|---|
| Arrivals | (-38.5, -8.5) | | Bar | (-5.5, -7.5) |
| Dorms | (-26.5, -4.5) | | Kitchen | (-11.5, 2.5) |
| Botany | (-18.5, -3.5) | | Chemistry | (16.5, 2.5) |
| Tool Room | (-30.5, 9.5) | | Chapel | (-39.5, 11.5) |
| Cargo Bay | (14.5, 13.5) | | Library | (13.5, -23.5) |
| Engineering | (43.5, 10.5) | | Theatre | (-15.5, -9.5) |
| North Maints | (-20.5, 15.5) | | Brig | (-12.5, 12.5) |
| South Maints | (-8.5, -36.5) | | Security | (-4.5, 15.5) |
| East Maints | (20.5, -23.5) | | Medical | (13.5, -8.5) |
| Xenoarch | (-12.5, -27.5) | | Robotics | (1.5, -30.5) |
| Vault (door) | (0.5, 17.5) | | Bridge | (-2.5, 27.5) |
| Salvage | (24.5, 18.5) | | Science | (-11.5, -17.5) |

No standalone "Atmospherics" or "Tech Storage" room exists on this compact chassis — both are
folded into Engineering / Tool Room respectively (confirmed: no `Atmos*` room name and no
`AtmosMonitoringConsole`-adjacent named room in the grid). The plan below substitutes accordingly
and says so at each step, rather than inventing a room that isn't there.

---

## 1. Lore-paper trail + treasure vault (Section 3 + 4 of the narrative bible)

Source entities: `Resources/Prototypes/_Solreign/Entities/lore_papers.yml` (already built, waves
1-5 — 8 `SolreignPaperAuditorMemo{1-8}`, 20 `SolreignPaperHandbookPage{N}`, and the vault reward
set `SolreignGoldenParachutePack` / `SolreignPlatinumShare` / `SolreignVaultRewardCrate`). That
file explicitly defers all map placement to "the mapping wave" — this is that wave's instructions.

### 1a. The 8-step Auditor's Memo trail

The lore file's header names an 8-step trail (Arrival Hub → Crew Quarters → Hydroponics → Tech
Storage → Cargo Bay → Atmospherics → Maintenance B → AI Core Duct → hidden vault). Mapped onto
this chassis's real rooms, in walking order:

| Step | Trail beat | Real room (anchor) | Entity to place |
|---|---|---|---|
| 1 | Arrival Hub | Arrivals (-38.5,-8.5) | `SolreignPaperAuditorMemo1` |
| 2 | Crew Quarters | Dorms (-26.5,-4.5) | `SolreignPaperAuditorMemo2` |
| 3 | Hydroponics | Botany (-18.5,-3.5) | `SolreignPaperAuditorMemo3` |
| 4 | Tech Storage | Tool Room (-30.5,9.5) | `SolreignPaperAuditorMemo4` |
| 5 | Cargo Bay | Cargo Bay (14.5,13.5) | `SolreignPaperAuditorMemo5` |
| 6 | Atmospherics | Engineering (43.5,10.5) — no standalone atmos room on this chassis; place near whichever atmos-pipe alcove is inside Engineering | `SolreignPaperAuditorMemo6` |
| 7 | Maintenance B | East Maints (20.5,-23.5) — closest maint loop to Engineering | `SolreignPaperAuditorMemo7` |
| 8 | AI Core Duct | a maintenance duct tile adjacent to the Vault door (0.5,17.5) — lore text says the vault is "behind the AI core wall panel," so the AI-core duct and the vault entrance should be the same neighborhood | `SolreignPaperAuditorMemo8` |
| 9 | Hidden vault | Vault room, behind the `HighSecCommandLocked` door already named "Vault" at (0.5,17.5) | `SolreignVaultRewardCrate` (which already contains the Golden Parachute, Platinum Share, and Memo 8 as loot — do not ALSO place a loose Memo 8 outside the crate, or double it) |

Notes for the mapper:
- Steps 1→5 walk roughly west-to-east across the crew/public wings, which is a sane discovery
  order for a first-time player. Steps 5→9 range further and re-cross the map (Cargo → Engineering
  → East Maints → Vault) — that's an intentional escalation (the trail gets harder to follow as it
  nears the reward), but confirm on foot that there's an actual maintenance-tunnel path connecting
  those three without forcing a public-corridor detour; if not, it's fine to swap step 6/7 rooms
  for whichever atmos/maint alcove is actually contiguous once you're looking at the tilemap.
- Place each memo as a loose `Paper` entity on a desk/floor/shelf already in that room — no new
  furniture needed, these are small paper items.
- The Vault door (`HighSecCommandLocked`, Command access) is unchanged; the crate inside needs
  its own `AccessReader` (already set to `["Command"]` in `SolreignVaultRewardCrate`), so a Command
  player (or someone who breaches the door) reaching the crate is the intended difficulty curve.
  Don't relax the crate's access to match a lower-sec room.

### 1b. The 20 handbook fragments

`lore_papers.yml` says these "tell the Solreign Corp origin story" in page-number order but are
individually collectible finds, not a directed trail — scatter them across public/semi-public
rooms distinct from the 9 Auditor-trail rooms above, so finding a handbook page doesn't spoil or
duplicate a memo beat. Suggested distribution (2-3 per room, ~20 total):

- Bar (-5.5,-7.5) + Kitchen (-11.5,2.5): pages 3, 12, 27 (early pages — origin-story cold open,
  fits a break-room read)
- Library (13.5,-23.5): pages 34, 45, 58, 62 (the Library already invites sit-and-read behavior)
- Chapel (-39.5,11.5): pages 79, 83 (the handbook's tone go-to for the game's "compliance is
  itself a virtue" corporate-cult humor lands well next to the chaplain's captive audience)
- Medical (13.5,-8.5) + Robotics (1.5,-30.5): pages 94, 101, 115 (mid-book, technical-department
  flavor)
- Security (-4.5,15.5) + Brig (-12.5,12.5): pages 128, 134, 147 (HR-adjacent compliance chapters)
- Xenoarch (-12.5,-27.5) — see §2 below, this room is being converted into the zoo wing, so its
  2-3 fragments (160, 172, 189) should read as the "field notes" chapter about specimen handling,
  planting them IN the new zoo wing once built rather than the old xenoarch shelving
- Theatre (-15.5,-9.5): page 195 (dramatic flourish, matches a stage prop shelf)
- Bridge (-2.5,27.5): page 200 (the final page belongs somewhere command-adjacent — a fitting
  capstone next to where the vault trail also ends)

Scatter each on a shelf, desk, or floor tile already furnished in that room; no new furniture.

---

## 2. Zoo wing

**Siting: convert/expand the Xenoarch room (anchor -12.5,-27.5).** Reasoning: Xenoarch (a
xeno-archaeology/specimen-study lab) is the one existing room on this chassis whose theme already
overlaps "alien creatures on display" — reusing it means the wing inherits a plausible in-fiction
reason to exist (Solreign's research arm studying live specimens, not just fossils) instead of
needing a bolted-on room with no narrative excuse. It also sits in the map's south quadrant next to
South Maints (-8.5,-36.5) and East Maints (20.5,-23.5), i.e. already a low-traffic back corner —
expanding it outward into adjacent maintenance space won't cut through a main crew corridor.

**Scope for this compact (8-20 pop) chassis:** keep it small — one viewing room plus 3-4 small
habitat nooks, not a sprawling exhibit hall. Rough footprint target: 10-14 tiles wide, reusing
Xenoarch's existing walls on at least two sides and pushing out toward the South Maints corner for
the rest.

**Structure (all verified upstream prototypes, zero new C#):**
- Perimeter/exhibit dividers: `ReinforcedWindow` (see-through, matches "zoo viewing glass") backed
  by `Grille` where a wall segment needs to be walkable-through for keeper access but still caged.
- Wing entrance: `AirlockGlass` (glass airlock, so the wing reads as public-viewable from the
  Maintenance corridor threading past it) — no access restriction needed, this is a public
  attraction, not a secure area (unlike the Vault).
- Habitat floor dressing: this chassis's tilemap already registers `FloorGrass`, `FloorDirt`, and
  `FloorAsteroidSand` (grep-verified in `Resources/Maps/_Solreign/solreign_oasis.yml`'s tilemap
  header) — paint 2-3 small habitat nooks with a different one of these each, so each fauna group
  gets a visually distinct enclosure instead of one undifferentiated pen.

**Fauna spawners — PG/kid-safe cut only.** Deliberately excludes every hostile/xeno spawner
(`SpawnMobXeno*`, `SpawnMobBear`, `SpawnMobHellspawn`, `SpawnMobPurpleSnake`, etc. from
`Entities/Markers/Spawners/Mobs/hostile.yml` and `xenos.yml`) — those are combat/antag content, not
appropriate for a family zoo exhibit, and per the memory ledger a separate "monster nest" is
already tracked as its own (adult-content-adjacent, not this ticket) lane. All of the below are
plain animal-spawner markers already in the repo (`Entities/Markers/Spawners/Mobs/animals.yml` /
`carp.yml`), drop-in with zero new components:

| Habitat nook | Spawner entity ID | Notes |
|---|---|---|
| Savanna/dirt nook | `SpawnMobGoat`, `SpawnMobSheep` | calm herbivores, safe to let roam loose behind grille |
| Jungle/grass nook | `SpawnMobMonkey`, `SpawnMobGorilla`, `SpawnMobParrot` | the "primate house" beat |
| Aquatic tank (small reinforced-glass pool, if room allows) | `SpawnMobCarp`, `SpawnMobFrog` | reuse `SpawnMobCarp` rather than `SpawnMobShark`/`SpawnMobCarpMagic` — keep the tank harmless, no combat-carp lore baggage |
| Small critters (walk-up terrarium row) | `SpawnMobLizard`, `SpawnMobCrab`, `SpawnMobPenguin` | one per small glass terrarium along the entrance wall, cheap visual variety |

Place one spawner marker per habitat nook (not a pile of duplicates) — vanilla animal mobs are
docile and roam on their own, no cage-behavior AI needed.

**Lore tie-in:** put handbook fragments 160/172/189 (see §1b) on a shelf just inside the wing
entrance, framed as the zoo's own "field notes" — ties the new wing into the existing lore trail
without touching the Auditor's Memo sequence.

---

## 3. Cake case ("the cake is a lie")

Entity: `SolreignCakeShowcaseFilled` (`Resources/Prototypes/_Solreign/Entities/portal_props.yml`)
— already a complete, self-contained prop (locked `GlassBox`, `AccessReader: [["Command"]]`,
pre-filled with `SolreignHolographicCake`). Nothing to build, just place one instance.

**Placement: Bar (-5.5,-7.5).** The Bar is the station's public social hub — every crew member
passes through it, which is the whole point of the "visible reward, permanently locked to Command,
you will never eat it" gag (`description` on the case literally promises nothing). Bridge
(-2.5,27.5) is the thematically "correct" Command-adjacent alternative if the mapper would rather
keep it out of the casual social space, but Bar gives it far more foot traffic and payoff.

Use `SolreignCakeShowcaseFilled` directly (not the empty `SolreignCakeShowcase` base) — this is a
permanent fixture gag, not something a Contract or event is expected to fill later. One instance is
enough; this is explicitly singular in the task brief.

---

## 4. Execution checklist for the mapping session

1. Open `Resources/Maps/_Solreign/solreign_oasis.yml` in the RT mapping editor (NOT `saltern.yml`
   — that's still the untouched upstream file).
2. Walk the 9 Auditor-trail rooms in order (§1a), confirm the coordinate table still matches what
   you see, adjust step 6/7/8 room choice only if the maintenance topology doesn't actually connect
   the way this doc assumes.
3. Drop the 8 `SolreignPaperAuditorMemo{N}` + `SolreignVaultRewardCrate` (crate already carries its
   own loot table — don't add a duplicate loose Memo 8).
4. Scatter the 20 `SolreignPaperHandbookPage{N}` per §1b's room list (skip the 3 destined for the
   zoo wing until step 6).
5. Build the zoo wing on the Xenoarch footprint per §2 (walls/airlock/floor dressing first, then
   spawners).
6. Place the 3 zoo-wing handbook fragments (160/172/189) just inside the entrance.
7. Place one `SolreignCakeShowcaseFilled` in the Bar.
8. Optional but recommended while already in the file: swap the leftover `PosterMapSaltern` decal
   (flagged in `Resources/Prototypes/Maps/_Solreign/solreign_oasis.yml`'s header comment) for any
   other `PosterMap*`/`PosterContraband*` prototype — cosmetic, five-minute fix, not required to
   ship this graft but free to knock out in the same pass.
9. YAML-lint the edited grid file the same way this chassis copy was validated (a permissive
   loader that treats RT's `!type:Foo` tags as opaque nodes — plain `yaml.safe_load` will error on
   those tags even on an unmodified upstream map, that's expected and not a real failure).
10. Leave the actual in-engine load/spawn verification (`dotnet build` / running the server) to
    whoever picks this up next — this planning pass did not run the engine per the task's ground
    rules.
