# SOLREIGN Game production recovery — 2026-07-28

## Outcome

Production was restored by failing forward to the current v284 release. The server is running,
answering the stock launcher surface, and listed on the official Space Station 14 hub as:

- address: `ss14://142.132.139.111:1316`
- name: `[EN][MRP] 🚀SOLREIGN | Persistent, sinister AI`
- engine: `284.0.2`
- source commit: `72f45029e91f1e2384f2e5d6392926f54e3c4e77`

No rollback archive was applied. The mixed failed release and damaged config remain preserved in
`/.incident-quarantine-20260728-171824` on the host.

## Root cause and repair

The v284 merge retained upstream-deleted prototype files and stale custom prototype contracts.
The boot blockers were repaired by:

- deleting the obsolete monolithic uplink catalog;
- deleting the obsolete ambient-music rules file;
- migrating Solreign action icons to Sprite components;
- replacing a missing/non-compatible rainbow clothing texture reference;
- replacing the removed `BaseGateway` dependency with a local visual-only
  `BaseSolreignZoneGate`, without inheriting vanilla teleporter/linking behavior.

An independent final diff review reported no introduced P0/P1 blocker.

The second production issue was operational: Oxy's `SERVER_ADVERTISE` startup variable was off.
That variable rewrote `hub.advertise=false` on each start even though the recovered config
declared it true. The startup toggle is now persistently on. After one controlled restart, the
console logged successful advertisement to both configured hubs and the official hub feed
returned the canonical SOLREIGN entry.

## Artifact identity

- artifact: `Solreign_update_20260728-171824Z.zip`
- SHA-256: `f487046ca9f2e5d30ebd956b8b24a706b27b47c3fa6690b91653818c115d6473`
- size: `308594311` bytes
- entries: `3937`
- duplicate entries: `0`
- embedded `Content.Client.zip`: `262611844` bytes
- prohibited packaged paths: no `server_config.toml`, `data/`, or `logs/`

The uploaded artifact was downloaded again and compared byte-for-byte with the local artifact.
It was decompressed only inside an isolated staging directory. Every extracted path and size
matched the ZIP manifest. Oxy's decompressor stripped the executable bit from `Robust.Server`;
the staged file was repaired to `0755` before promotion.

## Config recovery

The recovered config was derived from the pre-v14.1 production snapshot and the verified current
hostname. It:

- removes the stale external `[build]` pin so packaged Hybrid ACZ is authoritative;
- migrates movement bob to the current table-shaped configuration;
- preserves the established status, map pool, hub, Director, and game settings;
- enables runtime logging;
- contains `hub.advertise=true`.

Recovered pre-promotion config SHA-256:
`371bfeb44e2b03e3e97df7fc2bd537f52db46d36df8f1836da87eb9600ece9a4`.

The damaged config is preserved in quarantine with SHA-256:
`56d5ae03041fa629afdff82adfd7a70c03bc4c15abc1cffb7540d0fe870cc640`.

Oxy legitimately rewrites its managed hostname, player-limit, and hub values on start. A
post-restart parse verified the persisted hub address/advertising value, migrated movement-bob
shape, and absence of an external build pin. No secret values were printed into this receipt.

## Data safety

The release promotion did not move or overwrite `data/` or `logs/`.

- Season Ledger SHA-256 immediately before and immediately after file promotion:
  `08f849fabfb70c8b37d3562074d1240f0c77282517315743d44ae710ec409fc4`
- `preferences.db` size before and after: `330436608` bytes
- latest pre-recovery Ledger snapshot was restore-tested separately before this promotion

After two normal server starts, the Ledger file-level hash changed because SQLite opened and
processed its WAL/checkpoint state. A post-start pull of the database plus WAL/SHM proved:

- `PRAGMA integrity_check`: `ok`
- schema `user_version`: `6`
- tables: `23` including SQLite metadata, identical before/after
- no application table lost rows
- every application table row count was identical before/after

This is a physical SQLite-file change with no logical data loss.

## Verification

Source/build evidence:

- YAML/prototype linter: no errors
- solution/package graph build: 0 errors
- Content.Tests: 3109 passed, 0 failed, 3 skipped
- `git diff --check`: pass
- exact-diff gitleaks scan: pass
- full Solreign integration sweep: held on a pre-existing current-master
  `AlertSpriteView`/FX-anchor teardown signature; the recovery did not claim that gate green

Production evidence:

- panel state: `running`
- native console: `Server Version 284.0.2.0 -> Ready`
- runtime `cvar hub.advertise`: `True`
- official hub feed: one matching canonical entry
- `/status`: pass
- `/info`: pass, engine `284.0.2`, auth required, ACZ enabled
- engine resolution: pass for Windows/Linux/macOS on the official Robust CDN
- ACZ manifest: 6725 entries, advertised hash matched, `/download` reachable
- privacy probe: pass
- root `Content.Client.zip` size: matched packaged client pin
- root `Robust.Server` mode: `0755`

The compatibility probe's UDP LAN-discovery subcheck remains `LIMITED` by design on the WAN
host; TCP status on the same port passed. This is not a full authenticated player join proof.
Receipt:
`/private/tmp/solreign-incident-20260728/compat-receipts-final/receipt-20260728T182946Z.json`.

## Preserved recovery topology

- staged archive: `/.incident-stage-20260728-171824/Solreign_update_20260728-171824Z.zip`
- quarantined failed release: `/.incident-quarantine-20260728-171824`
- crash artifact: `/core.22`
- historical release archives: left in the production root
- incident-local evidence: `/private/tmp/solreign-incident-20260728`

Do not delete these until a separate housekeeping pass confirms the evidence has been copied to
durable storage and the new release has survived the desired observation window.
