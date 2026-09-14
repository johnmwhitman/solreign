# Solreign build-hosting go-card

**Status:** staged and verified, LOCAL ONLY. One gated step remains (upload),
then one gated step to apply config to the live server. Nothing has been
uploaded, no bucket has been created, no live-server file has been touched.

## The problem

`http://142.132.139.111:1316/info` currently reports `"acz": true` and an
empty `download_url`. That means every connecting launcher is forced to
stream the ~243MB client build through the game server's own custom
`/download` endpoint (`RobustToolbox/Robust.Server/ServerStatus/StatusHost.Acz.cs:120-306`,
`HandleAczManifestDownload`). That stream is breaking mid-transfer
(`Broken pipe` in `StatusHost.Acz.cs:109` `HandleAczManifest` per the crash
report), so nobody can finish joining. It's also the ceiling on the
20-player public goal — every one of those 243MB streams competes with game
traffic on the same process/socket.

The fix: host the zip on external storage (Cloudflare R2) and tell the
server (via CVars) to point launchers there instead. Once `build.download_url`
is non-empty, `/info` switches to the external-build code path
(`StatusHost.Handlers.cs:70-147`, `HandleInfo` → `GetExternalBuildInfo`) and
compliant launchers fetch the zip over plain HTTPS from R2 — they never call
`/manifest.txt` or `/download` on the game server again.

## Build-identity verdict

**MATCHED — no rebuild needed.** See `staged-cdn/ACZ-VERIFICATION.txt` for
the full derivation. Short version: the live server's reported build
version (`A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F`)
is not a hash of a zip file — it's a BLAKE2B-256 hash over a sorted manifest
of per-file BLAKE2B-256 hashes, computed live by
`StatusHost.Acz.Sources.cs::PrepareAczInner`/`CalcManifestData`/
`AssetPassAczWriter`. A faithful re-implementation of that exact algorithm
(same crypto library + version RobustToolbox itself pins — SpaceWizards.Sodium
0.3.0 / libsodium 1.0.20.1) run against local
`server/release/SS14.Client.zip` produced the **identical** hash. That
confirms this local zip's 6,651 files are byte-for-byte what the live
server is packaging right now — safe to host externally as-is.

- File: `SS14.Client.zip`, 254,386,692 bytes (242.6 MiB)
- File SHA-256 (for `build.hash`): `2dedc3a2122291027e2aaac5ed18099e0341d5d2677a150c83e7dc7334bb872d`
- ACZ manifest hash (matches live `version`): `A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F`

## What's staged

```
/Users/johnwhitman/AI/solreign-trees/build-hosting/staged-cdn/
  SS14.Client.zip        242.6 MiB, verified above
  SHA256SUMS              file checksum
  ACZ-VERIFICATION.txt    full identity-check derivation
  UPLOAD.sh               JOHN-GATED, not run — R2 upload via wrangler
```

No separate "manifest files" are needed. This plan uses the plain
`build.download_url` + `build.hash` contract (single zip, launcher verifies
via SHA-256) rather than the `build.manifest_url` contract (which would
require hosting the manifest.txt + a matching blob-download format
externally too — much more moving parts for no benefit here).

## wrangler / R2 finding

`wrangler whoami` → logged in as `johndw@gmail.com`
(account `33c970f9bc6406c8ce2368a571557f21`). Read-only check:
`wrangler r2 bucket list` **succeeded** (returned the 6 existing buckets:
ai-portfolio-backup, assets, fleetopus-evidence, routeplane-downloads,
solreign-ledger-offsite, thumbprinted-backups) — this token has at least R2
read/list. Bucket create + object put were **not** attempted (that would be
a live write); `UPLOAD.sh` carries a GitHub-Release fallback in a trailing
comment block in case `r2 bucket create` 403s when John runs it.

Cloudflare Pages was ruled out per your brief: Pages caps individual assets
at 25MB, this zip is ~243MB. R2 has no such cap.

## John's ordered steps

1. **Upload (gated):** review and run
   `/Users/johnwhitman/AI/solreign-trees/build-hosting/staged-cdn/UPLOAD.sh`
   from a terminal. It creates bucket `solreign-client-cdn` (edit the name
   in the script first if you want something else), uploads the zip to key
   `ss14-client/A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F/SS14.Client.zip`,
   and enables the free `r2.dev` public dev URL (a custom-domain option is
   included but commented out). It prints the final public URL at the end.

2. **Paste the config.** Add a `[build]` section to
   `Content.Server/server_config.toml` (there isn't one today — grep
   confirmed no `[build]` table currently exists) with the URL from step 1:

   ```toml
   [build]
   download_url = "https://<bucket>.<account>.r2.dev/ss14-client/A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F/SS14.Client.zip"
   hash = "2dedc3a2122291027e2aaac5ed18099e0341d5d2677a150c83e7dc7334bb872d"
   version = "A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F"
   fork_id = "custom"
   ```

   `version` is deliberately set to the *same* string the ACZ path was
   already reporting — launchers that already cached this exact build under
   that version tag won't need to re-download at all. `fork_id` **must**
   be set explicitly to `"custom"` here: unlike the ACZ code path
   (`StatusHost.Handlers.cs:156-160`, which falls back to `"custom"` when
   `build.fork_id` is empty), the external-build path
   (`GameBuildInformation.GetBuildInfoFromConfig`,
   `RobustToolbox/Robust.Shared/Utility/GameBuildInformation.cs:16-51`) has
   **no such fallback** — an unset `build.fork_id` would report an empty
   string, not `"custom"`, which could confuse the launcher's per-fork
   local-file bookkeeping. `engine_version` doesn't need setting; it
   already defaults to the assembly version (`282.0.0`), matching live.

   **If the oxy panel's Startup tab only takes CLI args/exec commands
   instead of editing the toml directly**, either form works (CVars are
   read from `--cvar` startup flags or `+cvar` exec commands the same as
   from the config file — `RobustToolbox/Robust.Server/CommandLineArgs.cs:55-74`,
   `ConfigurationCommands.cs:84-135`):

   ```
   --cvar build.download_url=https://<bucket>.<account>.r2.dev/ss14-client/A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F/SS14.Client.zip --cvar build.hash=2dedc3a2122291027e2aaac5ed18099e0341d5d2677a150c83e7dc7334bb872d --cvar build.version=A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F --cvar build.fork_id=custom
   ```

   or as post-init exec commands (`+cvar <name> <value>`, one per flag):

   ```
   +cvar build.download_url https://<bucket>.<account>.r2.dev/ss14-client/A38A2ADC.../SS14.Client.zip
   +cvar build.hash 2dedc3a2122291027e2aaac5ed18099e0341d5d2677a150c83e7dc7334bb872d
   +cvar build.version A38A2ADC9C1580B214AD66A75F31DB4014A00C3D33B696C73917DA7D455F9E2F
   +cvar build.fork_id custom
   ```

   One more check before applying: confirm there's no `build.json` sitting
   next to the live server executable that already sets these fields —
   `StatusHost.cs:181-197` (`RegisterCVars`) only applies `build.json`
   values to CVars that are still at their empty default, so it won't
   clobber a config/CLI value, but if `build.json` already has *something*
   in these fields it's worth knowing about before you add the config.

3. **Stop → Start** the server (CVars in the `build.*` group aren't
   hot-reloadable the way some are — a restart is the safe way to be sure
   `RegisterCVars()` re-reads the new config cleanly).

4. **Verify:** `curl http://142.132.139.111:1316/info` and confirm
   `build.acz` is now `false` and `build.download_url` is populated with
   the R2 URL (not empty). This is the signal that launchers will stop
   calling `/download` on the game server.

5. **Connect test:** join with the SS14 launcher and confirm the client
   download now comes from the R2/`r2.dev` URL (fast, resumable, no more
   `Broken pipe`), not from the game server.

## Rollback

If anything looks wrong after Stop→Start, blank the four CVars back out
(`build.download_url=""` etc., or just remove the `[build]` block and
restart) — the server falls straight back to today's Hybrid-ACZ behavior
(`HandleInfo` re-takes the `string.IsNullOrEmpty(downloadUrl)` branch,
`StatusHost.Handlers.cs:81-85`). No data migration, no client-side state to
undo — this is a pure server-side CVar flip either direction.

## What changes for the player

Today: launcher connects → sees `acz: true` → streams the 243MB build
through the game server's `/download` POST endpoint (competing with live
game traffic, currently breaking pipe mid-stream).

After: launcher connects → sees `acz: false` + a `download_url` → downloads
the 243MB zip directly from Cloudflare R2 over plain HTTPS (CDN-backed,
resumable, doesn't touch the game server's socket at all) → then makes its
normal UDP game connection to `142.132.139.111:1316` exactly as before.
Nothing about the actual connect/auth/gameplay path changes — only where
the client bytes come from.
