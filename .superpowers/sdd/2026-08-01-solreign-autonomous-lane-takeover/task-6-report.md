# Task 6 — Manifest-authoritative dormant-terminal removal

Base: `1156253b69f3be803aaa52fbc0fc5cf48aed067f`

## RED

1. Added `ProductionManifestContractTests.test_each_production_manifest_matches_its_owned_generated_blocks`, a real subprocess contract that invokes `map_patch.py --check` for each production manifest against the repository maps.
   - Command: `python3 -m unittest Tools._Solreign.MapPatch.tests.test_production_manifest_contract`
   - Result: expected failure: `community-fixtures-v1.json` rejected its edited owned blocks; `records-terminal-v1.json` reported all seven maps pending.
2. Ran only `SolreignCommunityFixtureMapPlacementIntegrationTest` before changing that test.
   - Result: expected failure, 7/7 map cases asserted `expected exactly one station-grid SolreignNoticeboard`, but every map had zero.

## Repair

- Retired `records-terminal-v1.json`, whose seven placements were intentionally removed and whose empty replacement would be rejected by MapPatch.
- Removed only the Noticeboard placement from each `community-fixtures-v1.json` map entry. Library Annex placement, tile, occupant, UID, and anchor metadata remain intact except for the authorized Leviathan anchor X reconciliation below.
- Updated the runtime fixture to require exactly one `SolreignLibraryAnnex` and zero dormant `SolreignNoticeboard` instances on each station grid, while retaining the real seven-map load, floor, anchoring, and beacon-distance checks.
- Corrected the Noticeboard allowlist prose to attribute the former placement to `community-fixtures-v1`.
- Reconciled stale map integrity directly exposed by the required runtime surface:
  - Leviathan beacon `900612` had a stale Y position (`-23.5`) inconsistent with its A9 arrival-path documentation and manifest; restored it to `-8.5` while retaining the later wall-adjacent X adjustment (`-35.5`). The Library Annex remains at `(-34.5,-7.5)`, now Manhattan distance 2.
  - Perihelion and Verdant beacon entity children had malformed YAML indentation introduced before this task; corrected only their `components` nesting, matching the prior Oasis repair pattern.
  - MapPatch record parsing now permits valid nested entity indentation, allowing its real production check to recognize the already-valid Oasis beacon block.

## GREEN

1. `python3 -m unittest Tools._Solreign.MapPatch.tests.test_production_manifest_contract` — PASS, 1 test.
2. `python3 Tools/_Solreign/MapPatch/map_patch.py --manifest Tools/_Solreign/MapPatch/manifests/community-fixtures-v1.json --check` — PASS, exit 0.
3. `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false --filter "FullyQualifiedName~SolreignCommunityFixtureMapPlacementIntegrationTest" -- NUnit.ConsoleOut=0` — PASS after rebuild, 7 passed / 0 failed.
4. `git diff --check` — PASS.

No broad integration suite, CVar, OPS, watchdog, deployment, or parallel-test change was run or made.

## Follow-up P1 — mixed-indentation MapPatch moves

Independent review found that the relaxed entity-record parser accepted nested indentation while
`plan_moves` still rewrote every moved `pos:` line with six spaces. Added
`MoveCoreTests.test_apply_preserves_nested_entity_pos_indentation` first; it failed RED because
the move flattened an eight-space `pos:` to six spaces. The mover now captures and reuses the
matched `pos:` indentation.

GREEN evidence:

1. The new regression test passes.
2. `python3 -m unittest Tools._Solreign.MapPatch.tests.test_map_move Tools._Solreign.MapPatch.tests.test_production_manifest_contract` — PASS, 10 tests.
3. `git diff --check` — PASS.

No runtime fixture was rerun because this parser-only repair does not change map content.
