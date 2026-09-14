# Solreign showpiece weapons — hidden-placement manifest

**Date:** 2026-07-11
**Lane:** Weapons variety (task #7 — martial arts + Sleeping Carp + weapons variety), roadmap
drip-feed doctrine (surprising finds spread across the station over time, not dumped in one crate).
**Status:** SPEC ONLY. Nothing in this document has been placed on any map. This is a manifest for
whichever session next opens the map in the RT mapping editor / edits `Resources/Maps/_Solreign/`
(a path this session does not touch — ground rules for this task scope this session to
`Resources/Prototypes/_Solreign/Entities/`, `docs/specs/`, and two `Catalog/VendingMachines`
inventory files only).

## 0. Audit methodology (how the 6 showpieces below were chosen)

Cross-checked every entity under `Resources/Prototypes/Entities/Objects/Weapons/**` against three
places an entity could already be "live": `Resources/Prototypes/Catalog/**` (vendors, crate fills),
`Resources/Prototypes/Entities/Markers/Spawners/**` (random loot spawners), and `Resources/Maps/**`
(every shipped map, hand-placed). An entity that turns up in none of the three is dormant — built,
functional, never seen by a player. Six dormant/under-used exotic melee weapons were selected for
this manifest because their flavor text and power level make them good "someone find this and tell
their friends" showpieces rather than vendor filler (that's what `weapons_chaos_tier.yml`, the
sibling deliverable in this PR, is for):

| Showpiece | Upstream prototype ID | Defining file | Live-usage audit result |
|---|---|---|---|
| Sword-cane | `Cane` + `CaneBlade`, stored in `CaneSheathFilled` | `Entities/Objects/Weapons/Melee/cane.yml` | `CaneSheathFilled`/`CaneBlade` only appear in `Catalog/uplink_catalog.yml` (traitor purchase) — never a physical find on any map |
| Ninja's edge | `EnergyKatana` | `Entities/Objects/Weapons/Melee/sword.yml` | Only referenced by `Roles/Antags/ninja.yml` (an antag's starting belt slot) and `Actions/ninja.yml` — zero vendor/loot/map hits |
| The Liability | `HyperEutacticBlade` | `Entities/Objects/Weapons/Melee/e_sword.yml` | Zero hits anywhere outside its own defining file — fully dormant, and per its own flavor text ("envisioned to cleave the very fabric of space and time itself") the single most powerful entity audited |
| Unsanctioned Miracle | `UnholyHalberd` | `Entities/Objects/Weapons/Melee/cult.yml` | Zero hits outside its own defining file — the cult-melee file is otherwise only reachable via the Cult antag's own spawn logic, not a vendor/loot/map placement |
| Unmanifested Cargo | `EldritchBlade` | `Entities/Objects/Weapons/Melee/cult.yml` | Same file, same result: zero hits outside its own defining file |
| Redundant Deterrence | `EnergySwordDouble` | `Entities/Objects/Weapons/Melee/e_sword.yml` | Zero hits — only its Cyborg-only variant (`CyborgEnergySwordDouble`) is referenced elsewhere (borg loadouts), the player-holdable base entity itself is unplaced |

None of these six IDs are touched, renamed, or re-parented by this PR — `weapons_chaos_tier.yml`
only reskins separate *kid-safe* bases. The "reflavor" for the two cult-melee showpieces
(`UnholyHalberd`, `EldritchBlade`) named in the ask happens entirely **in the discovery fiction
below** (the Ledger title + in-world dressing a player encounters), not as a new renamed prototype
— that keeps this deliverable data-only inside `docs/specs/`, per this session's assigned paths.
If a future session wants an actual Solreign-branded reskin entity for either (own name/description/
sprite tint, following the `weapons_chaos_tier.yml` idiom), that's a follow-up, not this doc.

## 1. THE NEVER-ANNOUNCE RULE (read this before placing anything)

**No showpiece may ever trigger a station announcement, admin/system alert, combat-log broadcast,
round-start briefing line, or any other mechanically-visible signal when it is placed, picked up,
moved, or used.** No `AnnounceOnUse`, no comms console ping, no "Central Command has detected an
anomaly," nothing. These exist to be found by rumor and foot-traffic, the same way a real hidden
object in a building gets discovered — one person notices it, tells two friends, and the story
outlives any individual round. A system message the moment someone opens the cabinet kills that
completely; it turns a discovery into a notification. If a future placement wants an in-fiction
detection story (e.g. "the compliance division suspects something is missing"), that has to be a
**delayed, decoupled** signal — a Ledger stat, a contract hook days later — never a synchronous
announcement tied to the pickup event. This rule is non-negotiable for all six entries below and
should be treated as a standing rule for any future showpiece added under this doctrine.

## 2. Anchors used

All coordinates below were `grep`-verified against `Resources/Maps/_Solreign/solreign_oasis.yml`
(the one Solreign-identity map on disk at the time of writing — grid inherited from upstream
Saltern; `parent: 31` throughout) using each room's named APC or an existing filled locker/altar as
a stable anchor, the same technique used in `docs/specs/2026-07-11-map-graft-plan.md`. **This was
not verified in the mapping editor or a running client** (no `dotnet build` / no map-file edits in
this session's scope) — treat every coordinate as "walk here and confirm the exact tile," not
"paint blind at this pixel." If the live rotation has since moved to a different map, re-anchor
using the same named-APC/named-locker technique on that map instead of trusting these numbers
verbatim.

One collision to avoid: the Vault room (`HighSecCommandLocked` door, `pos: 0.5,17.5`) already holds
`SolreignVaultRewardCrate` per the lore-paper trail (`docs/specs/2026-07-11-map-graft-plan.md`,
section 1). **None of the six showpieces below go in that crate or that room** — six separate
locations, on purpose, so the "drip-feed" stays spread out instead of clustering every good find in
one place.

## 3. The six placements

### 3.1 — Sword-cane (`Cane` / `CaneBlade` / `CaneSheathFilled`)

- **Location:** Head of Personnel's Office. Anchor: `HoP's Office APC`, `pos: 9.5,22.5`. The
  existing `LockerHeadOfPersonnelFilled` (`uid 6021`, `pos: 9.5,21.5`) is right there — add
  `CaneSheathFilled` to that locker's contents rather than introducing new furniture.
- **Lock/access:** Whatever `LockerHeadOfPersonnelFilled` already requires (HoP's personal locker —
  no additional `AccessReader` needed; the container itself is the lock).
- **Ledger title:** *"Distinguished Gentleman"* — awarded the first time a player draws `CaneBlade`
  from its sheath.
- **Discovery fiction:** No plaque, no note. It's just sitting among the HoP's personal effects like
  it's always been there. Corporate deniability is the joke — nobody signed off on it, nobody asks.

### 3.2 — Ninja's edge (`EnergyKatana`)

- **Location:** A maintenance stash, not a display. Anchor: existing `ClosetMaintenanceFilledRandom`
  at `pos: -19.5,12.5`, in the loop right off `North Maints APC` (`pos: -20.5,15.5`). Add
  `EnergyKatana` to that closet's contents.
- **Lock/access:** None. No `AccessReader`. This is deliberately the loosest of the six — a
  maintenance tech's job is to be in maintenance, so anyone who does the legwork of actually
  checking every closet in North Maints (there are ~20 `ClosetMaintenanceFilledRandom` instances on
  this chassis — see the grep list in this doc's working notes) earns the find through patience, not
  clearance. Matches the ask's own example ("maintenance walls").
- **Ledger title:** *"Shadow Requisition."*
- **Discovery fiction:** Wrapped in a rag, wedged behind the mundane closet clutter. No ninja ever
  came back for it. Nobody asks why.

### 3.3 — The Liability (`HyperEutacticBlade`)

- **Location:** Captain's Quarters. Anchor: `Captain's Quarters APC`, `pos: 8.5,27.5`; existing
  `LockerCaptainFilledHardsuit` sits at `pos: 10.5,26.5`. Mapper's call: either nest it inside that
  locker's contents, or — preferred, given the power level — add one new small wall-mounted safe in
  the same room and put it there instead, so it doesn't read as "one of the Captain's regular
  hardsuit-locker items."
- **Lock/access:** `AccessReader access: [["Captain"]]` at minimum. Given the flavor text ("envisioned
  to cleave the very fabric of space and time itself"), consider stacking a second physical barrier
  (a `Weldable` panel over the safe, or nesting the safe inside the hardsuit locker as a locked
  sub-container) so a single stolen Captain ID isn't a one-step unlock. Exact mechanism is the
  mapper's call — the requirement is "hardest of the six to reach," not a specific component.
- **Ledger title:** *"Corporate Overreach Award."*
- **Discovery fiction:** No cutscene, no warning. It's a company asset that should never have shipped
  on a civilian station, filed under Captain's personal effects because nobody could agree on a
  better place to put it.

### 3.4 — Unsanctioned Miracle (`UnholyHalberd`, reflavored as a found relic — no new entity)

- **Location:** Chapel back area, at the altar. Anchor: `AltarSpawner`, `pos: -36.5,13.5` (right by
  `Chapel APC`, `pos: -39.5,11.5`). Add a small sealed reliquary/cabinet next to the altar and place
  `UnholyHalberd` inside it — do not put it on open display on the altar itself.
- **Lock/access:** `AccessReader access: [["Chapel"]]`.
- **Ledger title:** *"Unsanctioned Miracle."*
- **Discovery fiction:** In-world, this is never called "the unholy halberd" to a player who finds
  it — the Chaplain's own paperwork (a `Paper` prototype, mapper's call on exact wording) refers to
  it only as "the Instrument," logged as a donation nobody can trace, and asks that it not be
  discussed with Central Command. That's the "reflavor": a new fictional identity wrapped around the
  unmodified upstream entity, not a renamed prototype.

### 3.5 — Unmanifested Cargo (`EldritchBlade`, reflavored as a found relic — no new entity)

- **Location:** Cargo/Salvage. Anchor: `Salvage APC`, `pos: 24.5,18.5` (`Cargo APC` at
  `pos: 14.5,13.5` is the fallback if Salvage doesn't have a sensible sealed container on the actual
  tilemap). Add one new locked crate among the ordinary salvage-yield crates — visually
  indistinguishable from a normal salvage haul crate until opened.
- **Lock/access:** `AccessReader access: [["Salvage"]]` (or `[["Cargo"]]` if placed nearer the Cargo
  APC instead — pick one access group to match whichever room it actually lands in).
- **Ledger title:** *"Unmanifested Cargo."*
- **Discovery fiction:** No note at all. It's just in the crate, mixed in with a normal haul, like
  someone's salvage run turned up something the manifest doesn't account for. The mystery is the
  point — nobody explains where it came from because nobody in-universe knows either.

### 3.6 — Redundant Deterrence (`EnergySwordDouble`)

- **Location:** Security wing. Anchor: `LockerHeadOfSecurityFilledHardsuit`, `pos: -9.5,22.5`
  (`Brig APC` at `pos: -12.5,12.5` as the room-level anchor). Add to that locker's contents, or a
  small adjacent secure locker if the mapper wants it separated from the HoS's regular hardsuit kit.
- **Lock/access:** `AccessReader access: [["HeadOfSecurity","Armory"]]` (both required — this is
  meant to be harder to reach than the standard Armory locker, not equivalent to it).
- **Ledger title:** *"Redundant Deterrence."*
- **Discovery fiction:** No inventory tag, no manifest line. It reads like excess Armory stock nobody
  requisitioned back out — because it is.

## 4. Summary table (Ledger titles, for whoever wires up the actual unlock hook)

| Showpiece | Ledger title | Room | Access |
|---|---|---|---|
| Sword-cane | Distinguished Gentleman | HoP's Office | HoP locker (existing) |
| Ninja's edge | Shadow Requisition | North Maints | none (buried, unlocked) |
| The Liability | Corporate Overreach Award | Captain's Quarters | `Captain` (+ second barrier) |
| Unsanctioned Miracle | Unsanctioned Miracle | Chapel | `Chapel` |
| Unmanifested Cargo | Unmanifested Cargo | Salvage/Cargo | `Salvage` or `Cargo` |
| Redundant Deterrence | Redundant Deterrence | Security/Brig | `HeadOfSecurity` + `Armory` |

Ledger-title wiring (actually granting a `SeasonTitleComponent`-style title on first pickup/use) is
`Content.Server/_Solreign/SeasonLedger/` territory, out of this session's scope — this table is the
handoff artifact for whichever session owns that wiring next.
