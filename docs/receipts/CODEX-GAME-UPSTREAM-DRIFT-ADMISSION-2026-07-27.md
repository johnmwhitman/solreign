# Codex GAME upstream-drift admission receipt

## Disposition

`PARKED / PASS-AS-LOCAL-INCOMPLETE-HISTORY-EVIDENCE`

This receipt is evidence for a read-only local inventory. It is not a complete
upstream-sync rehearsal, merge recommendation, compatibility result, security
verdict, build result, launcher/hub result, release approval, or deploy
authorization.

## Lane

- Repository: `Kolton-SS14/server`
- Branch: `codex/game-upstream-drift-admission-20260727`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-upstream-drift-admission-20260727`
- Base: `a4fbb1758644d5827a4d92348c0767e94481ff62`
- Implementation commit: `4dac5ebf484764e5f08ce10e34ed10adc3dace8b`
- Owned implementation:
  `Tools/_Solreign/UpstreamDrift/**`

The historical `auto-value/sr-w-006-upstream-sync` rehearsal was inspected only
as a predecessor. It fetches, changes branches, attempts a merge, invokes
`dotnet test`, and can exit before cleanup. It was not executed, edited,
cherry-picked, or used as this lane's development base.

## Implemented contract

The new standard-library Python 3.9 tool:

- compares two already-present full refs or object IDs using `/usr/bin/git`;
- disables lazy fetch, replacement objects, prompts, pagers, global/system
  configuration, signatures, and optional locks;
- performs no fetch, checkout, merge, worktree mutation, submodule recursion,
  build, package, launcher, hub, deploy, or live-system operation;
- rejects nested repository roots, symlinked repository paths, partial clones,
  unsafe refs, and shallow history unless incomplete-history mode is explicit;
- records exact divergence, upstream-only commits and paths, conservative path
  overlap, versioned review categories, review flags, and parent-repository
  RobustToolbox gitlink pointers;
- bounds commits, cumulative file changes, Git output, per-child time,
  audit-wide time, rendered reports, Git error detail, and structured error
  output;
- kills the owned process group on timeout or output overflow and keeps forced
  termination bounded;
- renders deterministic timestamp-free/path-free JSON and Markdown from one
  canonical model; and
- explicitly refuses any positive history-completeness claim.

## Measured local inventory

Inputs:

- Fork/base: `a4fbb1758644d5827a4d92348c0767e94481ff62`
- Local upstream object:
  `0007d22d8d03700b19f46abf78c74a6ae25a26cd`
- Repository: shallow
- Comparison basis: `reachable-local-objects-only`
- Completeness claim: `not-made`

Results:

- Base-only commits: 680
- Upstream-only commits: 243
- Upstream-only paths: 2,066
- Conservative changed-path overlap: 33
- Unclassified paths: 0
- Review flags:
  `COLLISION_CANDIDATES`, `INCOMPLETE_HISTORY`,
  `ROBUSTTOOLBOX_POINTER_CHANGED`, `SENSITIVE_SURFACE_PATHS`,
  `UPSTREAM_DRIFT_PRESENT`

Category counts:

| Category | Paths |
|---|---:|
| `content_maps_prototypes` | 1,020 |
| `database_schema` | 1 |
| `engine_render_audio` | 4 |
| `gameplay_source` | 986 |
| `launcher_package` | 0 |
| `protocol_network` | 0 |
| `robusttoolbox_pointer` | 1 |
| `security_auth` | 39 |
| `tooling_tests_docs` | 59 |
| `unclassified` | 0 |

RobustToolbox parent-repository pointers:

- Base: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Upstream: `2c5cd424167aad2997c448e5cd1ab2d9d0eea8c8`
- Changed: yes
- Analysis scope: parent-repository pointer only; engine commits were not
  inspected.

Deterministic render evidence:

- JSON: 762,928 bytes,
  SHA-256 `b811386e70e7860834c7f9e1ddb7b6ab81d1913265e2adbc20594305a15e53f1`
- Markdown: 257,009 bytes,
  SHA-256 `644c84c2df5b937a88be5ed88b86d08be084fe321771fa35947a68168c5bc980`

## Conservative overlap paths

These are paths changed on both locally reachable sides. They are review
candidates, not predicted merge conflicts:

- `.github/workflows/validate-rgas.yml`
- `.github/workflows/validate-rsis.yml`
- `.github/workflows/validate_mapfiles.yml`
- `.gitignore`
- `Content.Client/Options/UI/EscapeMenu.xaml`
- `Content.Client/UserInterface/Systems/Ghost/GhostUIController.cs`
- `Content.Server/GameTicking/Rules/GameRuleSystem.Utility.cs`
- `Content.Server/Materials/MaterialStorageSystem.cs`
- `Resources/Audio/Lobby/attributions.yml`
- `Resources/Locale/en-US/job/job-description.ftl`
- `Resources/Locale/en-US/tips.ftl`
- `Resources/Prototypes/AmbientMusic/rules.yml`
- `Resources/Prototypes/Catalog/uplink_catalog.yml`
- `Resources/Prototypes/Entities/Clothing/Head/soft.yml`
- `Resources/Prototypes/Entities/Clothing/OuterClothing/suits.yml`
- `Resources/Prototypes/Entities/Clothing/Shoes/misc.yml`
- `Resources/Prototypes/Entities/Mobs/Customization/Markings/scars.yml`
- `Resources/Prototypes/Entities/Mobs/NPCs/pets.yml`
- `Resources/Prototypes/Entities/Mobs/NPCs/regalrat.yml`
- `Resources/Prototypes/Entities/Mobs/NPCs/space.yml`
- `Resources/Prototypes/Entities/Mobs/Player/changeling.yml`
- `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cups.yml`
- `Resources/Prototypes/Entities/Objects/Devices/pda.yml`
- `Resources/Prototypes/Entities/Stations/base.yml`
- `Resources/Prototypes/GameRules/cargo_gifts.yml`
- `Resources/Prototypes/GameRules/events.yml`
- `Resources/Prototypes/GameRules/meteorswarms.yml`
- `Resources/Prototypes/Roles/Antags/changeling.yml`
- `Resources/Textures/Objects/Weapons/Melee/fireaxe.rsi/meta.json`
- `Resources/Textures/Objects/Weapons/Melee/fireaxeflaming.rsi/meta.json`
- `Resources/Textures/Structures/Piping/Atmospherics/directionalfan.rsi/icon.png`
- `Resources/Textures/Structures/Piping/Atmospherics/directionalfan.rsi/meta.json`
- `Resources/Textures/Structures/Piping/Atmospherics/tinyfan.rsi/meta.json`

## Verification

- Fixture/unit suite:
  `/usr/bin/python3 -B -m unittest discover -s Tools/_Solreign/UpstreamDrift/tests`
  — 44 passed, 0 failed.
- `git diff --cached --check` — passed.
- Gitleaks over `Tools/_Solreign/UpstreamDrift` — no leaks.
- The final actual local audit completed within its bounds and reproduced both
  render hashes above.
- Final actual-audit non-mutation proof:
  - HEAD remained
    `a4fbb1758644d5827a4d92348c0767e94481ff62`.
  - refs digest remained
    `1cbd325e67495d4f9381a52f6326ae1ac0c7d69772e6e68ce3e5683d58d0736b`.
  - status digest remained
    `7e96e9ea30e35fdf33d01d9263ac7ce96ed7751397d13358c1ea1a2e70c167d4`.
  - staged-diff digest remained
    `a5d9a6eee5d93d5a7a8388b4d4ca034aea8bb3edf70f5c106708413bc111a9f2`.
  - unstaged-diff digest remained the empty SHA-256
    `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- Three independent local final reviews (code, tests/evidence, threat model)
  reported no remaining P0-P3 findings.
- Sanitized Grok review findings were closed before the final suite. Two
  narrower MiniMax final-review attempts were unavailable (provider timeout);
  no MiniMax verdict is claimed.

No .NET/MSBuild command ran. No live box, panel, credentials, player data,
RoutePlane, backup scheduler, ledger data, Crashpad, production service, remote
branch, merge, push, deploy, or restart was touched.

## Remaining gate

This advances SR-W-006, SR-W-007, and SR-W-008 but closes none of them. The
next acceptable evidence step is a clean disposable full-history clone (or an
explicitly approved history-completion operation), followed by:

1. the same audit without incomplete-history mode;
2. human classification of sensitive and overlapping commits;
3. independent asset/license review where applicable;
4. a separate disposable merge rehearsal;
5. the normal one-heavy-build-at-a-time game battery; and
6. explicit integration authority.
