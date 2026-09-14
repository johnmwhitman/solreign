# Season 1 Beat 3 exact-map census and next decision — 2026-07-26

## Strategic finding

Beat 3's delivery mechanism is real on all seven maps. Its player outcome is
not yet reliable.

The rule broadcasts three A.U.D.I.T. lines over 50 seconds and hides one static
archived-transcript folder in any station-owned storage accepted by
`EntityStorageSystem.CanInsert`. Exact-map testing observed 44 to 437 candidate
destinations per map. At SOLREIGN's target 2–8 player population, a single clue
selected from that surface can easily remain unseen.

The next epic should improve discovery without turning the mystery into a GPS
marker or pretending the static transcript is actual retained player chat.

## Current truth

- `SolreignGhostCabinets` is manual/admin-triggered and `weight: 0`.
- It has no dedicated CVar, automatic scheduler, Showrunner descriptor, map
  marker, records-terminal connection, Ledger write, or Chronicle consequence.
- `LastObservation` is transient server-local diagnostic state only.
- The transcript is curated fiction from a four-value localized dataset.
- The rule does not spawn spectral filing cabinets despite its fiction.
- The folder persists after the 50-second rule ends.
- Existing Shift Archive, Compliance, Directive, Memorial, Corporate Project,
  and S-07 surfaces do not consume or reveal Beat 3 state.
- `SolreignRecordsTerminal` is default-on in configuration but absent from all
  seven authored maps, so it has no map-authored player reachability.

## Exact-map discovery surface

| Map | Candidates | Open | Low-pop interpretation |
| --- | ---: | ---: | --- |
| Leviathan | 437 | 8 | Very large search surface despite min population 0 |
| Meridian | 274 | 8 | Not normally low-pop, but still an unbounded search |
| Nocturne | 44 | 2 | Compact, but has no map-authored filing cabinets |
| Oasis | 172 | 1 | Moderate low-pop search surface |
| Perihelion | 225 | 4 | Large search surface at 2–8 |
| Terminus | 374 | 8 | Worst if emergency map fallback selects it below band |
| Verdant | 225 | 4 | Large search surface at 2–8 |

These are runtime census observations, not stable map-content APIs.

## Options for the next player-value slice

### A. Department-scale diegetic hint — recommended first

After selecting a destination, announce or print a coarse, curated clue such
as the department or records wing. Never expose an entity ID, coordinate,
player identity, or exact locker.

Why first:

- smallest runtime and content surface;
- preserves searching and crew conversation;
- works on all current maps without seven map edits;
- directly targets low-pop discoverability; and
- can be CVar-gated and previewed independently of Showrunner automation.

Acceptance should measure whether 2–8 players can locate the folder within a
bounded window, not merely whether the hint renders.

### B. Records-hub preference

Prefer storage near a map's records/HR/command surface, with a bounded fallback
to current station storage. This improves fiction and search density but needs
a trustworthy area/anchor contract. Do not infer departments from arbitrary
prototype names.

### C. Dedicated spectral-cabinet anchor

Place one reviewed anchor on every map, spawn a distinctive cabinet/VFX there,
and contain the folder inside it. This best matches the story and creates a
strong visual signature, but it is a seven-map plus art/VFX epic and should
follow a deliberate design pass.

### D. Chronicle consequence

Only after players reliably find and discuss the clue should an approved,
aggregate Beat 3 result feed the Chronicle. Do not persist real chat, private
messages, identities, locations, moderation data, secret roles, or arbitrary
folder text.

## Recommended sequence

1. Run a 2–8-player rehearsal with the current rule and measure time-to-find.
2. If discovery is weak, ship option A behind a default-off Beat 3 discovery
   CVar with a curated fallback.
3. Rehearse again and require a shared-story outcome, not only test success.
4. Decide whether the stronger visual identity of option C justifies its map
   and asset cost.
5. Add Chronicle persistence only after moderated consequence semantics exist.
6. Keep Showrunner preview-only until a trusted capability verifier can prove
   map, population, cooldown, safety, and operator controls.

## Adjacent roadmap findings from the read-only census

These are not part of this implementation:

- Providence reactive comments say the feature ships default-off, while
  `CCVars.SolreignProvidenceReactive.cs` currently defaults
  `solreign.providence.reactive_enabled` to `true`. This needs an explicit
  truth/intent decision, not a silent code flip.
- Several Providence sound comments still call acid, death, and welcome audio
  unwired even though production call sites now exist.
- Cross-season WorldState prototypes and storage are registered, but no
  production producer records a decision and no consumer applies a policy.
- The Showrunner planner remains deliberately inert with no production caller,
  catalog, command, CVar, prototype, or execution adapter.
- Historical documentation refers to a `startgamerule` command that is absent
  from this tree; `addgamerule` and the admin game-rule UI are the verified
  operator paths.

These should enter future collision-free truth-repair or architecture lanes;
none should be bundled into Beat 3 discovery work.
