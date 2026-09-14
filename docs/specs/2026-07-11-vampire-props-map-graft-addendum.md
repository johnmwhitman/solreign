# Vampire props map-graft addendum — coffin, garlic ward, chapel ground

**Date:** 2026-07-11
**Lane:** Phase-2 Track A1 (docs/plans/2026-07-11-ROADMAP-PHASE2.md) — closing the "Vampire has a
coffin, chapel ground, and garlic ward placed and reachable on Oasis" success criterion.
**Status:** Prototypes landed this pass (`Resources/Prototypes/_Solreign/Entities/Antags/vampire.yml`
— `SolreignVampireCoffin`, `SolreignVampireGarlicWard`, `SolreignVampireChapelGround`). Nothing
described below has been placed on the map yet — this addendum hands the placement request to
whoever next opens `Resources/Maps/_Solreign/solreign_oasis.yml` in the RT mapping editor, same
division of labor as `2026-07-11-map-graft-plan.md` (that document's own header: "this task owns
prototypes only, NOT the map").

Room anchors below are copied from `2026-07-11-map-graft-plan.md`'s own verified room census
(grep-derived `pos:` of each room's named APC/door, `parent: 31`) — not re-verified independently
this pass, same one-real-limitation caveat that document states: "walk to here and confirm," not
"paint blind at this pixel."

---

## 1. Coffin — "Executive Recharge Pod" (`SolreignVampireCoffin`)

**Placement: North Maints, anchor (-20.5, 15.5).** Reasoning: the spec (§4.5 rule 3) wants the
coffin "a visible, findable, campable object: the vampire's power has an address" — a maintenance
corner is exactly that register (off the main crew path, but not hidden behind a locked door,
so a crew member who goes looking for it during Vampire Night can actually find it without special
access). North Maints sits adjacent to Dorms (-26.5,-4.5) and Tool Room (-30.5,9.5), giving the
Nocturnal Acquisitions Specialist a short, plausible walk from crew-quarters-adjacent space to
their pod without crossing the whole station. South Maints (-8.5,-36.5) is the alternative if
North Maints turns out to be too cramped once the mapper is looking at the actual tilemap — either
maintenance loop satisfies the "findable but not central" brief equally well.

Place one `SolreignVampireCoffin` instance. No new furniture/room needed — it's a freestanding
crate-shaped prop, same footprint as the vanilla `CrateCoffin` it's built on.

---

## 2. Garlic ward — kitchen-grown (`SolreignVampireGarlicWard`)

**Placement: Kitchen (-11.5, 2.5), with a second instance in Botany (-18.5, -3.5).** Reasoning:
spec §4.2/§4.5 rule 4 says "the kitchen grows it round-start" and the counter must need "zero antag
knowledge" — placing it in both the Kitchen (where a chef would naturally have it on a prep counter
or shelf) and Botany (where it could sit as an already-harvested sample next to the hydroponics
trays, alongside whatever seed/plant setup Botany already has) covers the two rooms a crew member
would look in without needing to be told this is a counter-item at all. Two loose instances, not a
pile — same "don't over-place" convention `2026-07-11-map-graft-plan.md` used for its own lore
papers.

---

## 3. Chapel ground — consecrated flooring (`SolreignVampireChapelGround`)

**Placement: Chapel, anchor (-39.5, 11.5).** The Chapel room already exists on this chassis (part
of the verified room census) — no new room needed. Since `SolreignVampireChapelGround` is a
`CarpetChapel`-based floor decoration (non-colliding, `Structures/Furniture/Carpets/chapel_carpet.rsi`),
place it as the chapel's actual floor covering near the altar, replacing or supplementing whatever
placeholder flooring is there now — placing it IS placing the room's flooring, not adding an extra
invisible marker underfoot. One instance covering the altar/kneeling area is enough; the component's
own `Radius` (default 6 tiles) already reaches the room's working footprint from a single central
placement — confirm on foot once in the editor that 6 tiles actually spans the room's chapel-proper
floor (not the whole room including the entry vestibule), and add a second instance only if the room
turns out to be larger than that on this specific chassis.

---

## 4. Execution checklist for the mapping session

1. Open `Resources/Maps/_Solreign/solreign_oasis.yml` in the RT mapping editor (same file
   `2026-07-11-map-graft-plan.md` targets — fold this into the same mapping pass if convenient,
   rather than opening the file twice).
2. Walk North Maints (-20.5,15.5); confirm it reads as "findable back corner," not a dead end no
   one ever walks through. Place one `SolreignVampireCoffin`. Fall back to South Maints
   (-8.5,-36.5) only if North Maints doesn't work out on foot.
3. Place one `SolreignVampireGarlicWard` in Kitchen (-11.5,2.5) and one in Botany (-18.5,-3.5).
4. Place one `SolreignVampireChapelGround` in the Chapel (-39.5,11.5), sited near the altar; confirm
   the default 6-tile radius covers the room's working floor before deciding whether a second
   instance is warranted.
5. YAML-lint the edited grid file the same permissive-loader way `2026-07-11-map-graft-plan.md`'s
   own checklist item 9 describes (plain `yaml.safe_load` will error on RT's `!type:Foo` tags even
   on an unmodified upstream map — expected, not a real failure).
6. Leave in-engine load/spawn verification (`dotnet build` / running the server) to whoever picks
   this up next — this addendum, like the plan it extends, does not run the engine per this task's
   ground rules.
