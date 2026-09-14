# Snapshot exclusion list

Clean one-commit snapshot of the SOLREIGN game content pack for
`johnmwhitman/solreign`. Built 2026-09-14T05:39:53Z.

## Source of truth

| Field | Value |
|---|---|
| Source repo (read-only) | `/Users/johnwhitman/AI/Kolton-SS14/server` |
| Source ref | `origin/master` |
| Source SHA | `183a6dd4f8d25a09442c5efe17f7cfa85799cfd5` |
| Source date | 2026-08-15 11:02:39 -0500 |
| Source subject | Merge merge/upstream-286-refresh-20260814 |
| Method | `git archive origin/master` (no history rewrite; one new commit) |
| Outer ops overlay | **not** included (`Kolton-SS14` `origin/main` `2942d054` is ops, not game) |
| Live working tree | **not** used (dirty, wrong branch) |

The 08-19 live-box build is a descendant of this SHA. This ancestor is the
card-approved snapshot source.

## Never entered this tree (absent from `origin/master`)

These were named in the publication brief. They are not in the game content
pack at `origin/master`, so they were not copied and are not present here:

- `.env` files / live MiniMax key material
- `generate_audio.py` (lives on the **ops** overlay, not the game pack; the
  08-19 committed literal was later replaced with `os.environ.get`; rotation
  remains John-gated G1)
- `backups/` (ledger sqlite dumps, box configs)
- `releases/` zip artifacts
- ledger directories (`backups/ledger/`)
- `agents/` / Hermes profile state
- Atlas PII containment **docs** from the July audit (`docs/architecture/PUBLIC-PLANE-PRIVACY-CONTAINMENT.md`
  and siblings live on the ops overlay, not this pack)
- website / Discord bots / director daemon / watchdog sources
- `solreign-ops`, `solreign-director` (already separate private GitHub repos)

`.envrc` **is** in `origin/master`. It is a 5-line nix-direnv bootstrap
(`use flake`, no assignments of secrets). Kept.

## Dropped from the archive before the snapshot commit

| Path | Reason |
|---|---|
| `Resources/Textures/_Solreign/EasterEggs/_concepts/*.png` (12 files) | no `meta.json`, unknown provenance — card rule: do not ship |

RobustToolbox is a gitlink (`160000` `af2a7d0406b410c4d960f0f4b34513651c6dc742`)
and is **not** vendored. Clone with `git submodule update --init`.

## Intentionally kept (triaged)

- Upstream CC-BY-NC* sprites (67 `meta.json`) — original license preserved; mapped in `LICENSES.md`
- `docs/receipts/**/mmx_worker*.py` — Keychain reads only, no literals
- `Resources/ConfigPresets/server_config.toml` — `pg_password` empty
- `Resources/ConfigPresets/Build/development.toml` — stock `postgres` placeholder
- `Content.Server/Administration/Systems/SolreignLiveMapPrivacyRules.cs` and tests — code, not PII dumps
- `docs/media/{chapel,moonlord,website-home,room-render}.png` — added for README (CC-BY-NC-SA 4.0)

## What this snapshot is not

- Not a history rewrite of `kolton-ss14` or `solreign-ops`
- Not a deploy, hub-listing edit, or live-box change
- Not a visibility flip (G2, John)
- Not a MiniMax key rotation (G1, John)
