# Open-source launch — John handoff

Lane finished the clean snapshot, secret scan, license pass, and README.
Two gates stay yours. The fleet does not rotate keys, flip visibility, or post.

Workspace twin (same text): `JOHN-HANDOFF.md` beside the kanban workspace.

## What is already done

- New **private** GitHub repo: https://github.com/johnmwhitman/solreign
- Snapshot SHA: `882ca8217c61ca0b1c4e70d8fbb0adf459e820e8` (then this docs commit)
- Source: game pack `Kolton-SS14/server` `origin/master`
  `183a6dd4f8d25a09442c5efe17f7cfa85799cfd5` (2026-08-15). Not a history rewrite.
- 12 unknown-provenance concept PNGs dropped. See `SNAPSHOT-EXCLUSIONS.md`.
- gitleaks 8.30.1: history 0, `--no-git` 0, planted `sk-cp-` control 1 (trusted zero).
- MIT + `NOTICE` + `LICENSES.md`. SOLREIGN-original assets default CC-BY-NC-SA 4.0.
- Item 6 (W37 arrivals) already receipted by `t_4c721fd5`. Not redone.
- Live box, hub listing, website landing, Discord: **untouched**.

## G1 — rotate the MiniMax key (you)

History, fingerprints only:

- A MiniMax key sat in `Kolton-SS14/generate_audio.py:7` from ~2026-07-20.
  The 08-19 ops commit still had the literal. Later `origin/main` replaced it
  with `os.environ.get("MINIMAX_API_KEY")`. KEY_MASTER only recorded
  **relocations**, not a rotation.
- That file is on the **ops overlay**, not this game snapshot. This repo does
  not contain `generate_audio.py` and does not contain a MiniMax literal.
- Keychain slot: `minimax-api-key`.
- Load order that must not reverse: update Keychain **and** both `.env.local`
  copies **first**, then revoke the old MiniMax key. Reverse order breaks
  `mmx` + imagegen.

You run:

1. Mint a new MiniMax key in the MiniMax console.
2. `security add-generic-password -U -s minimax-api-key -a $USER -w '<new>'`
   (and the matching `.env.local` lines).
3. Confirm ops `generate_audio.py` still reads env/Keychain, never a literal.
4. Revoke the old key.
5. Optional: rewrite ops `generate_audio.py` to Keychain-first
   (`security find-generic-password -s minimax-api-key -w`, then env, then
   `~/AI/Tools/.env.local`).

Do not paste the new key into chat, YAML, or git.

## G2 — flip this repo public (you)

When G1 is done (or you accept residual risk on the **ops** key, which is not
in this repo):

```bash
gh repo edit johnmwhitman/solreign --visibility public --accept-visibility-change-consequences
```

Confirm:

```bash
gh repo view johnmwhitman/solreign --json isPrivate,url,visibility
```

Expect `"visibility":"PUBLIC"`, `"isPrivate":false`.

Do **not** flip `kolton-ss14`, `solreign-ops`, `solreign-director`, or
`solreign-director-console`. Those stay private.

## Post-flip checklist — solreignweb (`t_f70799ba`)

After G2 reads PUBLIC, solreignweb (not this lane) owns the site increment:

1. `gh repo view johnmwhitman/solreign --json visibility` — **block if not PUBLIC**.
   Do not link a private repo.
2. Add an "Open source" section above the fold on solreign.net linking
   `https://github.com/johnmwhitman/solreign`.
3. One-click join block: `ss14://142.132.139.111:1316`, live player count from
   `http://142.132.139.111:1316/status`.
4. Room render (this tree: `docs/media/room-render.png`).
5. Receipt = live URL diff + HTTP 200. No identity posts from the fleet.

Sibling content increment `t_ab5d2241` (blog #10) is a separate card; it does
not wait on G2, only on this head closing.

## First outward post — DRAFT (attach, never post)

Splash contract: this is a draft for John to rewrite in his own phrasing.
I did not sample Discord or r/ss14 this cycle. r/ss14 has a written rule
removing AI content; the last SOLREIGN ad card got a coordinated "AI EVRYTHNG"
callout. Do not post this as-is. Do not post it at all until you decide the
room.

Tweak this:

> I open-sourced the SOLREIGN content pack. It is a Space Station 14 fork
> where the station AI stays after the round. Live server:
> ss14://142.132.139.111:1316 — one click from the launcher. Code:
> github.com/johnmwhitman/solreign (after you flip it public). Site:
> solreign.net. Discord: discord.gg/f2dxBpFUY4.

One next-action only: the connect URL. No hub listing edit from the fleet.

## Operations parking

Park operations after one month unless arrivals say otherwise.

- Box left running as of W37 (round 196, Secret, autopause-on-empty).
- Arrivals baseline: `t_4c721fd5`, snapshot `20260913-201259`, 7d distinct
  non-admin = 5. Next comparison is W38+.

## Out of scope for the fleet

Identity broadcasts, spend, credential rotation/exposure, CarMart, live-box
mutation, force-push, loosening gates.
