# Codex H3.4 Tier-2 Role Recovery — 2026-07-27

## State

- Repository: GAME (`johnmwhitman/kolton-ss14`)
- Branch: `codex/game-horizon3-recovery-ship-20260727`
- Fresh base: `fa12e6ee0295a42b8efb97ce2633f2816bbb92ef` (`origin/master`, re-fetched after verification)
- Feature commit: `3b3cec93127` (`feat(solreign): wire tier-two expedition roles`)
- Prerequisite test repair: `06b3a9b9cb2` (`fix(tests): type compliance hunter rule id`)
- Review-hardening commit: `0d2380bdf1c` (`test(solreign): harden tier-two role contracts`)
- Authority: review and PR only. No merge, deployment, restart, live configuration, or website change was performed.

## Delivered

The clean H3.4 recovery slice adds two bounded, one-slot Cargo roles:

- `SolreignSectorFreightMaster`
- `SolreignExpeditionSpecialist`

Both roles have:

- exact `Cargo`, `Salvage`, `Maintenance`, and `External` access;
- no privileged job specials;
- a role-defining EVA hardsuit and department-appropriate field equipment;
- a Cargo playtime requirement and Cargo as their sole primary department;
- a secondary non-primary Solreign department entry for branded character-editor visibility;
- one round-start slot and one station-grid spawn on every map in the seven-map `SolreignMapPool`.

The map edits add exactly two nonblocking spawn markers per map, colocated with
existing Cargo Technician and Salvage Specialist markers, with fresh UIDs and
matching `entityCount` updates.

## Review corrections incorporated

Independent review found and closed two P1 defects before the final battery:

1. `GroupTankHarness` occupied Vox `outerClothing` before StartingGear, which
   could displace the required hardsuit. Both role loadouts now omit the group,
   and a real `StationSpawningSystem` regression test verifies the exact equipped
   hardsuit for a default Vox profile.
2. The jobs initially existed only in the non-primary Solreign department.
   Both now belong to primary Cargo as well, so playtime progression,
   department coloring, and department-aware systems classify them correctly.
   Tests require Cargo to be the jobs' one and only primary department.

Review also corrected all seven supply-count comments and made the map-pool test
name explicit about its configuration boundary.

The final `/ship` pass closed two additional coverage gaps by executing the
Cargo playtime requirements immediately below and at their two- and three-hour
boundaries, and by pinning every field-kit slot rather than only the hardsuit.
It also verifies player-visible localization and files Expedition Specialist
under Cargo, matching its supervisor, access, progression, and sole primary
department.

The final adversarial review returned `APPROVE` with no P0-P2 findings. The
maintainability review's two findings—the stale receipt and Engineering/Cargo
path mismatch—were both closed before publication.

## Verification receipts

All .NET work ran serially (`-m:1`, `-nodeReuse:false`,
`-p:UseSharedCompilation=false`). No concurrent or retry-loop build was used.

| Gate | Result | Receipt SHA-256 |
| --- | --- | --- |
| Tier-2 source contracts | 5 passed, 0 failed | `68d037c32e1198f2c9ce137e8d50b8b2c267728b1431a9de7f084a16688987ad` |
| Tier-2 engine integrations | 11 passed, 0 failed | `26dbdc11cd02bfe7ba2144089de20717b63d25f34806ec43be2fcfdfcb767eb4` |
| Full `Content.Tests` | 2,997 passed, 0 failed, 3 platform skips | `9be514c765e1342024c8da05bd357b74cd62d9ac14519f88ed723a963fcc81a4` |
| SOLREIGN integration band | 519 passed, 0 failed, 4 intentional skips | `a0bc6890b1f0bdec494ae166b012c7eae7d5fad2c1f91bfaf371b5f03869ba4b` |
| YAML/prototype linter | `No errors found` | command output, 28.647 seconds |
| Whitespace/conflict checks | pass | `git diff --check`; fresh base remained unchanged |

The first source-test invocation compiled successfully but its sandboxed test
host could not bind a loopback socket. The already-built binary was run once
with normal loopback access and passed 5/5; this was an environment denial, not
a test failure or retry loop.

## Honest reachability boundary

The branch proves that, when `game.map_pool` selects `SolreignMapPool`, the
runtime manager resolves exactly the seven intended maps, each map loads, both
jobs have exact `[1, 1]` availability, and each has exactly one matching
station-grid spawn.

It does **not** claim that checked-in GAME defaults activate that pool. The
default CVar is still `DefaultMapPool`, and the checked-in Solreign rotation
preset remains explicitly unwired. Live selection is an OPS/deployment
configuration responsibility and was not changed in this Game-only lane.

## Held follow-ups

Do not recover the dirty autonomous H3 chain by cherry-picking it wholesale.
The following need separate clean-room branches:

1. Shuttle registry/deeds: extend the existing vessel-identity domain, attach
   from real station/shuttle lifecycle events, and persist asynchronously and
   atomically through the Season Ledger.
2. Sector navigation: use real `ShuttleSystem` FTL, docking, map loading,
   beacons, and destination lifecycle; do not ship a parallel timer simulation.
3. Material export: adapt the vanilla material silo/station resource and Cargo
   economy boundaries; do not ship the duplicate custom inventory model.
4. Map Forge: an engine-backed deterministic recipe/patch compiler that emits
   ordinary SS14 map YAML, validates load/save/load, renders previews, and
   promotes maps only through an explicit reviewed commit.

## Integrator sequence

1. Review commits `06b3a9b9cb2` and `3b3cec93127` in order.
2. Confirm the target branch still contains the typed compliance-hunter test
   repair or retain `06b3a9b9cb2`.
3. Re-run the focused Tier-2 integration and the SOLREIGN integration band
   after any conflict resolution.
4. Treat map-pool activation as a separate OPS verification.
5. Merge only under the designated integrator's authority; deployment remains
   a later explicit action.
