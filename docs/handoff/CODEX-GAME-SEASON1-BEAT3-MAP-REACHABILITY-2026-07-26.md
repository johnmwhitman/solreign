# Claude handoff — Season 1 Beat 3 exact-map reachability

## Parked candidate

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-season1-beat3-map-reachability-v1-20260726`
- Branch:
  `codex/game-season1-beat3-map-reachability-v1-20260726`
- Tested code base:
  `70eb235834d1e43e2e7500e9b8dbbbeb64273df0`
- Implementation commit:
  `8b55bf1fcb2dd7833401129380d507369cf8c26d`
- Implementation tree:
  `3fdcaa4b4777aab60ca15751d10dc53f823a913f`
- Strict-build repair dependency:
  `79fb230c10e695f97daea7055fdc9b34703135df`
- Tested composition tree:
  `697b79a3ba67a62ca007e6ee30a901b229c04b8b`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Receipt:
  `docs/receipts/CODEX-GAME-SEASON1-BEAT3-MAP-REACHABILITY-2026-07-26.md`
- Census/strategy:
  `docs/research/SEASON1-BEAT3-EXACT-MAP-CENSUS-2026-07-26.md`
- Kill-date: 2026-08-09

## Outcome

One new integration fixture closes the explicit production-map evidence gap
left by the synthetic Beat 3 delivery tests:

- all seven canonical rotation maps load through the real map loader;
- all storage candidates the production rule may choose are exact-map local;
- manually starting the real Beat 3 rule delivers one contained, curated folder
  on every map; and
- the pre-existing synthetic test covers the bounded uncontained open-storage
  outcome, while the new fixture validates that outcome's map/station
  provenance if a future random selection realizes it.

Exact-composition evidence:

- strict build: 355 warnings / **0 errors**;
- focused map fixture: **7/0/0**;
- full units: **2,935/0/3**; and
- full SOLREIGN integration: **494/0/4**, 498 total.

No runtime C#, map, prototype, localization, content, CVar, Showrunner, Ledger,
web, Director, or engine code changed.

## Claude landing procedure

1. Revalidate the then-current GAME canonical head. Cached `origin/master`
   advanced after testing to docs-only `14a3c7a01...`.
2. Land or compose the separate strict-build repair
   `79fb230c10e695f97daea7055fdc9b34703135df`.
3. Review and cherry-pick
   `8b55bf1fcb2dd7833401129380d507369cf8c26d`.
4. Review and retain the separate documentation commit immediately following
   the implementation commit on this branch, or copy the dated evidence files
   into Claude's integration history.
5. Build `Content.IntegrationTests` with analyzers and normal warning policy
   enabled.
6. Rerun the focused seven-map fixture, full `Content.Tests`, and complete
   `FullyQualifiedName~Solreign` integration band before landing.

Do not describe this as player discoverability, Showrunner readiness, a full
upstream integration run, or a warning-free repository. No push, canonical
merge, package, deploy, restart, publication, CVar activation, event
activation, launcher/hub change, credential use, live/player data access, or
production mutation occurred. Claude remains the integrator.
