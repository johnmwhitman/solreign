# R1 Station Points — 2026-08-10

## Intent
First-session path certainty: latejoin sees First Shift beacon, can reach records, wallmounts not floating.

## Landed (branch ship/r1-station-points-20260810 → PR #16)
- A9: beacon Chebyshev ≤2 from SpawnPointLatejoin on all 7 rotation maps
- Records terminals placed (in-document YAML) on all 7 maps; orphan allowlist cleaned
- Lobby noun "First Shift beacon" (master via PR #15)
- Wallmount quality: KnownFloating/Collisions/ArcOrphans emptied via re-anchor
- Contracts How-To kept ≤5 tiles from boards after board moves
- Content-seed test: economy boards absent; one records terminal; lore trail intact

## Gates
- Tools/solreign_gate.sh: 2733 unit + NetId + YAML
- Integration: MapContentSeed, WingmateBeacon, CommunityFixture, MapHealth, ContractsSign, WingmateSystem

## Client eyeball still owed before deploy claim
