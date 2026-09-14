# SOLREIGN → Standalone Steam Release Plan

**Status:** PLANNING ONLY — not activated. John opened this lane 2026-07-15; work happens in a separate thread.
**Repo:** `~/AI/Kolton-SS14-v9` (fork of `space-wizards/space-station-14`, fork point upstream master 2026-07-13)
**Legal basis (verified 2026-07-15):** content code + RobustToolbox engine are MIT — commercial, closed-source release is permitted. Fork contains NO code from AGPL forks (Delta-V/Frontier/Goob/Einstein Engines — scanned; only false-positive "DeltaV" physics vars). **Hard rule: never port code or assets from AGPL forks into this repo — it would create a source-disclosure obligation and kill the closed commercial option.**

What MIT does NOT cover: assets (CC licenses, some non-commercial), the "Space Station 14" trademark/branding, and Wizard's Den infrastructure (auth/hub/launcher).

---

## Phase 1 — Asset replacement (the licensing gate)

Audit numbers from 2026-07-15 scan of `Resources/`:

| Class | Count | Commercial? | Action |
|---|---|---|---|
| Sprite bundles CC-BY-NC-SA-3.0 / CC-BY-NC-4.0 / CC-BY-NC-SA-4.0 | **107** of 3,103 | ❌ | Replace or remove |
| Audio dirs with CC-BY-NC entries | **34** attribution entries | ❌ | Replace or remove |
| Audio marked `license: Custom` | **13** entries | ⚠️ unknown | Review each; replace by default |
| Sprites CC-BY-SA 3.0/4.0, CC-BY | ~2,860 | ✅ with attribution | Keep; attribution manifest (Phase 2) |
| CC0 sprites/audio | ~125 + ~170 | ✅ | Keep |

**The list of files to replace: [`NC-ASSET-MANIFEST.txt`](NC-ASSET-MANIFEST.txt)** (same directory, machine-generated). Regenerate any time:

```bash
cd ~/AI/Kolton-SS14-v9/Resources
grep -rlE '"license"\s*:\s*"CC-BY-NC' Textures --include=meta.json   # sprites
grep -rlE 'license:\s*"?CC-BY-NC' Audio --include=attributions.yml   # audio
grep -rlE 'license:\s*"?Custom'   Audio --include=attributions.yml   # custom
```

**⚡ Head start — SR-W-084 already exists.** The v13 ops backlog has a CC-BY-NC replacement lane, state `ready`: 115 assets triaged (2 delete / 4 swap / 109 regen), two pilot batches run (4/5 pass), regen pipeline de-risked. 17 swap candidates are staged UNMERGED with placeholder CC0 art awaiting John's curation (they fail the Solreign-authored rail until curated). **The triage artifact: `~/AI/solreign-trees/orch-ops/docs/CCBYNC-INVENTORY-2026-07-12.md`** — full per-asset table with license + provenance; it confirms all NC assets are upstream space-station-14 inheritances (mostly sprites ported from SS13 codebases: goonstation/tgstation/civstation), **zero Solreign-authored NC assets**. Start from that triage, not from scratch — reconcile its 115-item list against this manifest's current counts (upstream merges since triage may have shifted numbers; 2026-07-15 scan = 107 sprite bundles).

Steps:
1. **Triage the 107 sprite bundles**: adopt SR-W-084's delete/swap/regen triage; re-verify against the current manifest. For regens: AI-generate matching sprite sheets — the .rsi `meta.json` defines exact frame sizes and state names, replacements must match 1:1 so prototypes don't break.
2. **AI regeneration pipeline**: use existing SOLREIGN art pipeline (MiniMax image-01 / Vertex Imagen per Tools memory) → pixel-downscale to .rsi frame dims → drop into .rsi, update `meta.json`: `"license": "CC0-1.0", "copyright": "SOLREIGN original, AI-generated 2026"`. Honest metadata matters — it feeds Phase 5's AI disclosure.
3. **Audio**: replace the 34 NC entries with CC0 sources (freesound CC0, or generated); resolve the 13 `Custom` entries individually — assume replace.
4. **Full-tree license sweep** (catch what the two greps miss): fonts checked 2026-07-15 — NotoSans/NotoSansDisplay (OFL), RobotoMono (Apache, LICENSE.txt present), Boxfont-round (credits.txt — verify terms), **`Animal Silence.otf` = loose font with no license file in-tree, resolve or replace**. Still to sweep: lobby art, `Resources/Textures/Logo`, maps with embedded art credits, any `attributions.yml` elsewhere, `Resources/Credits/` files.
5. **CI guard**: add a test/lint that fails the build if any `meta.json`/`attributions.yml` contains `CC-BY-NC` or unknown license strings — prevents regressions from future upstream merges.
6. **Verify in-game**: boot server, spawn-check a sample of replaced entities (sprite states resolve, no pink error textures), play the replaced audio cues.

Definition of done: NC/Custom greps return zero; CI guard green; visual spot-check passed.

## Phase 2 — Attribution manifest (keeping the CC-BY-SA art legal)

CC-BY-SA art may ship commercially but requires attribution, and **modified CC-BY-SA sprites remain CC-BY-SA** (share-alike applies to the art, not the code — code stays closed).

1. Script: walk all `meta.json` + `attributions.yml` → generate `CREDITS-ASSETS.md` (license, copyright holder, source) — an afternoon of scripting; keep it as a build step, not a one-off.
2. Ship the manifest in the game (credits screen or bundled file) + on the store page's legal section.
3. Include MIT notices: Space Wizards Federation copyright (LICENSE.TXT) + RobustToolbox license in credits.
4. Decide policy for SOLREIGN-modified CC-BY-SA sprites: publish just those art files publicly (e.g. a small assets-only public repo) to satisfy share-alike cleanly.

## Phase 3 — Rebrand / trademark scrub

MIT grants no trademark rights. Zero "Space Station 14" / "SS14" / Space Wizards branding in the shipped product or store presence.

1. Grep sweep: `Space Station 14`, `SS14`, `spacestation14`, `Space Wizards`, `wizards.dev` across `Resources/`, client strings, window titles, `Resources/Textures/Logo/`, default MOTD, changelog templates. **Baseline measured 2026-07-15: 243 files in `Resources/` (yml/ftl/json) + 3 C# files** contain SS14/Space Wizards strings — mostly locale text and attribution comments; scriptable sweep, but each hit needs a keep-as-credit vs replace decision (attribution mentions in `meta.json` copyright fields are legally *required* to stay).
2. Replace logo/lobby art with SOLREIGN branding (AI pipeline).
3. Safe factual line for store page: "Built on the open-source RobustToolbox engine."
4. Rename solution-level branding where user-visible (window title, server browser name); avoid churning internal namespaces (`Content.Server` etc.) — invisible to players, breaks future upstream merges.

## Phase 4 — Infrastructure decoupling

The stock game leans on Wizard's Den services; a standalone Steam game must not.

1. **Auth**: replace WizDen central auth with Steamworks auth (Steam ticket → your session) or self-hosted auth. This is the largest engineering item — spike it early.
2. **Hub/server browser**: remove hub advertising to WizDen; direct-connect + own server list (or Steam server browser).
3. **Client packaging**: standalone client build (no SS14 launcher / ACZ-from-hub flow); RobustToolbox supports standalone packaging — `Content.Packaging` is the starting point.
4. **Updates**: Steam depots become the update channel; wire CI to build + upload via SteamPipe.
5. **Hosting**: decide official-servers vs player-hosted (player-hosted = shipping server binaries; fine under MIT).

## Phase 5 — Steamworks / store

1. Steamworks account + $100 app fee per app.
2. **AI content disclosure (mandatory, non-optional)**: Valve requires disclosure of AI-generated content; it appears on the store page. SOLREIGN is full-AI — file it honestly and completely. If any content is AI-generated *live at runtime*, the disclosure has a second section with guardrail requirements — check whether any live LLM features ship in the game build.
3. Store assets: capsules, trailer, screenshots (AI pipeline + real gameplay capture).
4. Age/content survey (SS14-genre violence, drugs, etc. — answer per actual content).
5. Steam builds via SteamPipe from CI; playtest via Steam Playtest or closed beta branch.

## Phase 6 — Legal & business hygiene

1. Entity/tax setup for Steam payouts (Valve needs a tax identity).
2. EULA + privacy policy (multiplayer = you process player data; auth choice from Phase 4 determines scope).
3. Note: purely AI-generated art has thin-to-no copyright protection — you can sell it, but you can't stop others copying those specific assets. Acceptable trade; just known.
4. Keep `LICENSE.TXT` (MIT notice) in the shipped distribution.

## Phase 7 — Positioning (the community reality)

Legal fight is won before it starts; the community fight cannot be won, only routed around.

- Target audience = players who've never heard of SS14, not the existing community. Market it as its own game.
- Expect hostility from the SS13/14 ecosystem (this is the exact scenario that drove the fork world to AGPL). Don't engage on their turf; don't hide the lineage either — "built on RobustToolbox" + honest AI disclosure removes the "gotcha" surface.
- Review-bombing risk on launch is real; Steam has an off-topic review flagging mechanism — know it exists.

---

## Suggested order of attack for the next thread

1. Phase 1 steps 1–3 (asset replacement — the long pole and pure grind, ideal for fleet/Codex lanes with Claude verify)
2. Phase 4 step 1 spike (Steamworks auth — the biggest technical unknown; de-risk early)
3. Phases 2+3 (scriptable, quick)
4. Phases 5–7 when a build exists

**Constraints that carry over:** MERGE LAW — only Claude merges (2026-07-12). No AGPL-fork code, ever. Kolton's live server keeps running from the existing lane; Steam work should branch (`steam/*`) and not destabilize v13 lanes.
