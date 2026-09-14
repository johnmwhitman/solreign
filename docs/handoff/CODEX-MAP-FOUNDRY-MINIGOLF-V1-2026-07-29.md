# Codex handoff — Map Foundry mini-golf v1

Date: 2026-07-29

## Lane

- Repository: SOLREIGN Game
- Branch: `codex/game-map-foundry-minigolf-v1-20260729`
- Worktree: `/Users/johnwhitman/AI/solreign-trees/codex-game-map-foundry-minigolf-v1-20260729`
- Stacked base: `f3d61c87004e2c15783904ad629354e8f04dca67`
- Dependency: land `codex/game-map-foundry-v0-20260729` before this branch.
- Merge/deploy state: not merged, not pushed, not deployed.

## Delivered

This is the first content-producing consumer stacked on the read-only Map
Foundry v0 lane.

1. `MapPatch` placements may now omit a map-level component override when the
   prototype already contains all required behavior.
2. `MapPatch --check` and an idempotent re-apply now preserve the byte order of
   multiple owned patch blocks on the same map.
3. `minigolf-v1.json` deterministically places a ball, putter, and hole on
   Terminus in the existing Grand Prix park.
4. The generated block is applied to `solreign_terminus.yml`.
5. The resolved ball, putter, and hole are removed from the orphan allowlist;
   the long-distance driver remains intentionally admin-only.

## Exact placement

Anchor: `SolreignSignGrandPrixWelcome`, uid `90005`, position `-1.5,6.5`,
parent grid `2`.

| Key | Prototype | UID | Position | Expected tile | Expected occupants |
| --- | --- | ---: | --- | --- | --- |
| ball | `SolreignGolfBall` | 901200 | `-3.5,9.5` | `FloorMowedAstroGrass` | none |
| putter | `SolreignGolfClubPutter` | 901201 | `-3.5,8.5` | `FloorMowedAstroGrass` | none |
| hole | `SolreignGolfHole` | 901202 | `-6.5,9.5` | `FloorMowedAstroGrass` | none |

The ball-to-hole lane is three clear mowed-astrograss tiles beside the existing
Grand Prix recreation area. The driver is deliberately absent because its
15-tile throw tuning is inappropriate for this short course.

## TDD and verification

Observed red before the optional-component implementation:

```text
map_patch: ERROR: placement 0 component must be an object
```

Observed red before the multi-block order repair:

```text
map_patch: PENDING: Resources/Maps/_Solreign/alpha.yml
```

Focused green commands:

```sh
python3 -m unittest discover -s Tools/_Solreign/MapPatch/tests -p 'test_*.py'
python3 Tools/_Solreign/MapPatch/map_patch.py \
  --manifest Tools/_Solreign/MapPatch/manifests/minigolf-v1.json --check
python3 Tools/_Solreign/MapPatch/map_patch.py \
  --manifest Tools/_Solreign/MapPatch/manifests/community-fixtures-v1.json --check
python3 Tools/_Solreign/MapFoundry/map_foundry.py inventory
git diff --check
```

Final focused receipt:

- MapPatch tests: `22 passed`, `0 failed`.
- `minigolf-v1 --check`: pass.
- `community-fixtures-v1 --check`: pass with the mini-golf block following it.
- Map Foundry inventory: seven maps, zero errors.
- `git diff --check`: pass.

MiniMax produced the initial bounded implementation. Grok's first independent
review returned HOLD on two issues: the multi-block order defect and a generated
filesystem regex test that both misrepresented itself as runtime proof and could
not parse its own sentinels/prototypes. The order defect was reproduced red and
fixed; the misleading test was removed rather than patched into a weaker claim.
Grok's bounded final re-review re-ran the focused receipt and returned
`NO FINDINGS` / `COMMIT: PASS`.

## Honest remaining gate

No C# or `dotnet` command was run in this lane. A generated filesystem/regex
test that called itself a runtime map-load test was reviewed and removed before
commit because it did not boot the engine or decode the map tiles. The next
agent should add one real `Content.IntegrationTests` map-load case using the
existing `SolreignCommunityFixtureMapPlacementIntegrationTest` idiom, then run
that single filtered case once. Do not restore the discarded regex test.

Map Foundry v0 still treats every manifest as full seven-map coverage.
`minigolf-v1.json` is intentionally a one-map MapPatch manifest, so a future
Foundry slice should add an explicit, fail-closed partial-scope contract before
claiming that `validate-manifest` accepts one-map content packs. Do not weaken
the existing complete-coverage default.

## Successor sequence

1. Inspect this branch and its dependency commit.
2. Run the five focused commands above.
3. Add and run the single real engine map-load integration test.
4. Request independent review of the combined two-commit stack.
5. Land Foundry first, then this mini-golf consumer.
6. Keep merge, push, deploy, and production activation separately gated.

## Fleet note

MiniMax and Grok were used under the existing private-source disclosure grant.
Kimi K3 is installed and agentic, but the current safety gate did not interpret
"available for build" as authorization to transmit nonpublic SOLREIGN source to
Kimi. No SOLREIGN source was sent to Kimi in this lane.
