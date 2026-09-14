# Contracts explainer seven-map parity plan

Date: 2026-07-26
Repository: GAME
Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
Authority: implementation and local verification only; Claude integrates

## Player outcome

Every map in `SolreignMapPool` gives a first-shift player a discoverable explanation beside the
Contracts Board. The slice does not alter contracts, Standing, rewards, persistence, or feature
activation.

## Scope

- Add one existing `SolreignSignContractsHowTo` to Nocturne.
- Add one existing `SolreignSignContractsHowTo` to Verdant.
- Add a live-loaded seven-map regression test requiring one board, one explainer, a shared station
  grid, a bounded distance, and no new board/sign sprite overlap.
- Preserve Leviathan's pre-existing co-located placement as one named legacy exception rather than
  silently broadening this slice.

## Method

1. Write the seven-map test before the map edits.
2. Capture the expected 5-pass/2-fail red result.
3. Use fresh UIDs above each map's previous maximum.
4. Select verified wall tiles, avoiding other wall decoration and avoiding the board's own tile.
5. Run focused, neighboring map, unit/prototype, and YAML gates serially.
6. Obtain independent read-only review and repair all P0-P3 findings.
7. Commit and park the branch for Claude; do not push, merge, deploy, or activate.

## Rollback

Remove UIDs `901003` and `901007` plus the regression test. There is no database, engine, network,
CVar, launcher, or hub state to unwind.
