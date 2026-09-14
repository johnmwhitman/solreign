# SOLREIGN pre-swap recon — 2026-07-26

Three read-only recon agents swept GAME / OPS / DAEMON / portfolio state before a chat swap.
**Nothing here was executed** — John called the swap after the plan was approved. This is the
verified ground truth the next chat should start from, plus `INTEGRATION-PLAN.md` beside it.

**Live at time of writing:** artifact `Solreign_update_20260726-183547Z` · GAME master
`3e46722b82` · OPS main `ac737e0b` · DAEMON main `f8060c76` · round 122 · 36/50 switches ON.
⚠️ The live artifact was built from `14a3c7a01b` (master~2); the commits above it are docs +
a C# test fix, so no runtime content diverges. Rollback pin remains `…20260726-012036Z`.

---

## 🔴 FINDING 1 — the entire 2026-07-26 wave is UNPUSHED. This is the top risk.
`git ls-remote` on the GAME origin returns 174 heads and **zero** `codex/*`, `agx/*`, `cdx/*` or
`art/*` refs. Same story in OPS: 51 remote heads, and **no 07-26 codex/agx branch exists on the
remote at all**. Roughly **27 branches of new work (7 GAME + 14 OPS + 6 daemon) live only on this
disk.** The risk is loss, not staleness. There is nothing to fetch anywhere.

⚠️ **Do NOT blind-push OPS branches.** Commit `450593ae` (a ~109MB `node_modules` blob) is
reachable in `rel-ops`; the documented consequence is that a large family of OPS branches is
**unpushable** without history surgery, which was decided against. Bundles are the archive route
(`~/AI/scratch/git-bundles/`). GAME and DAEMON branches have no such blocker.

## FINDING 2 — the GAME wave is 4 effective branches, not 7
Three are cherry-pick/rebase duplicates, proven by **identical patch-ids**, not by eyeballing:
* `test(solreign): restore strict integration build` appears on 3 branches, patch-id `c047be62fd`.
* `evidence-providence-strict-composition` = `game-providence-reactive-truth-qualification` minus docs.
* `evidence-game-season1-beat3-map-qualified`'s test file is **byte-identical** to the `game-…` twin.

All five red-flag checks came back clean across the whole wave: no binaries/db/scratch, no
RobustToolbox pointer change, no `Resources/Maps/**` edits, no `Prototypes/Species|Body/` files,
and every prototype id the new tests reference already resolves on master.

**Recommended order:** `game-strict-integration-build-repair` (base fix the others carry) →
`game-providence-reactive-truth-qualification` → `game-season1-beat3-map-reachability-v1` →
`game-season1-beat3-capability-observation-v1`. Drop the two `evidence-*` duplicates.

## 🔑 FINDING 3 — SR-BUG-003 was a FALSE POSITIVE. I was wrong to hold that branch.
`codex/solreign-htn-nocturne-lifecycle-20260726` was held out because its test referenced
`SolreignNocturneLifecycleTestCompound`, "defined nowhere in Resources/". **The prototype does
exist** — declared inline via `[TestPrototypes]`, so it is present at test runtime.

The linter failure is unrelated to that YAML. Root cause traced to
`PrototypeManager.ValidateFields.cs:74` via `ValidateStaticFields` — a **reflection walk over
static C# fields**, not YAML validation. Two things combine: `Content.YAMLLinter.csproj`
project-references `Content.IntegrationTests` (so the test class is in `FindAllTypes()` for both
client and server passes), and `Program.cs` filters *YamlErrors* by client/server visibility but
concatenates **FieldErrors with no such filter**. The test's `private static readonly
ProtoId<HTNCompoundPrototype>` fields therefore fail on the client pass (kind lives in
`Content.Server`, unregistered client-side) and again on the server pass (the id isn't in on-disk
`Resources/`, which is all the linter indexes).

**Proof it's placement, not content:** `Content.Server/Clothing/Systems/CursedMaskSystem.cs:32`
uses the identical `static readonly ProtoId<HTNCompoundPrototype>` pattern and passes today —
only because it sits in `Content.Server`, which the client instance never loads.

**Smallest fix: remove `static` from those two fields.** `ValidateStaticFieldsInternal` only walks
`BindingFlags.Static`, so both errors vanish with zero behaviour change; `protoManager.Index(...)`
is unaffected. Bare `const string` neighbours need no change. **Mutation-prove red→green before
merging.** The correct long-term repair — filter `FieldErrors` by client/server visibility the way
`YamlErrors` already are — is its own lane.

## FINDING 4 — OPS: 9 drainable, 6 that must not all land
**Class A, merge-ready** (clean, small, non-overlapping `docs/` + `deploy/`): `web-muster-calendar-v1`,
`ops-ledger-swap-integrity`, `ops-ledger-restore-admission`, `solreign-landing-rehearsal`,
`v143-release-truth-reconciliation`, `outcome-proof-admission-contract`, `web-exact-head-verifier`,
`sr-w-001-artifact-identity-gate`, `evidence-ops-release-gates-composition`. Three touch
`deploy/*.py` — review those by hand; that's the deploy pipeline.

**Class B — one lane forked six ways.** `solreign-web-pixellab-design`,
`evidence-ops-visual-runtime-qualification` (superset, 111 commits / 253 files),
`ops-visual-cumulative-hygiene`, `evidence-ops-visual-browser-qualification`,
`evidence-ops-visual-release-composition`, `solreign-final-preview-20260724` — **plus**
`agx/visual-conversion`. All hit the same ~160 `website/public` + `website/src` files. Each merges
clean *individually*; merging any two sequentially collides hard. **Pick one, discard the rest as
ancestors.** This is the website lane's own sitting, not an integration chore.

`agx/visual-conversion` remeasured: local ref `146d5efe` is **4 commits stale**; `origin/…` is
`01bd544e`. Divergence is main **+41** / branch **+28** (the branch hasn't moved since 07-25 — main
advanced). Branch-side change set unchanged at 116 files / +4054 −294.

## FINDING 5 — Antigravity's content is NOT committed anywhere
`~/AI/Kolton-SS14/server` (on `auto-value/sr-horizon-execution`) holds **34 uncommitted entries**:
8 modified `Resources/Prototypes/_Solreign/` YAMLs, `CCVars.Solreign.cs`, and 25 untracked
additions including whole new `Content.Server/_Solreign/{Cargo,Exploration,Shuttles}/` trees and
**14 new `.rsi` sprite directories** (compliance unit, go-kart, boostpad, mudpatch, contracts
board, heaven chime, pedestals, columns, glow plants, changeling armblade).

**That is another lane's live checkout — do not work in it** (the documented `git reset --hard`
scar). It needs a handoff *from its owner*. Flagging, not touching.

## FINDING 6 — the overlay hazard, quantified
**66 unmerged branches carry a stale `Body/Species/shadow.yml` (`ef36553b`) and
`Species/shadow.yml` (`03c71b05`)** — byte-identical across all 66, and **older than main's**
(`369e0fb9` / `8db32433`). Merging any of them regresses the lungs fix. Git flags most as
CONFLICT, so it won't land silently — but **never `-X ours/theirs` or force-resolve the
`auto-value/sr-w-*` family.** Same pattern smaller on `delighters.yml` (6 divergent branches).

The build path is now guarded both ways (post-overlay species gate + species/body `DIFFERS`
fail-closed in `cmd_apply_overlay`), but the merge path is guarded only by attention.

## FINDING 7 — two mutually-exclusive engine targets (a NEW owner decision)
`codex/solreign-fx-round-lifecycle-20260725` bumps RobustToolbox `960edb32c4 → bae90c907c`
(submodule pointer + 3 docs, **no C#** — nothing in-tree consumes it, so it can't be validated
from content code). `chore/rt-upgrade-v283` moves it to a **third** SHA `b7fb6cd70d`. Both cannot
land. **Someone must pick the engine target before either merges.** No 07-26 branch depends on
either bump — every one still points at `960edb32c4`.

## FINDING 8 — DAEMON is the cleanest repo, and has real value sitting idle
`main == origin/main == f8060c76` (no stale-local disease here). Merge-ready today, both unpushed
and purely additive: `test/codex-director-route-inventory-v1` (3f/+459) and
`feat/codex-director-action-audit-verifier-v1` (3f/+1146). `wf/market-hmac-failclosed` is a
**one-file conflict in `orchestrator/market.py`** carrying an HMAC fail-closed **security** fix —
worth rebasing, not dropping. `feat/codex-director-public-privacy` is 70 behind with 19-file
conflicts: **dead, reimplement.**

## FINDING 9 — the lock is a backlog, not contention
~25 SOLREIGN rows are PARKED/HANDOFF-READY awaiting integration, all `Land slot: Claude integrator`,
all kill-dated **2026-08-09**. Only 2 rows are ACTIVE. ⚠️ Both ACTIVE timestamps are ~1–1.5h in the
*future* vs wall clock — clock skew; don't read them literally. Two 07-24 rows carry no
PARKED/ACTIVE marker at all (`bestiary-v2`, `visual-identity-pass`) — `~/AI/Tools/lock-triage`
candidates.

Also dirty and mid-flight elsewhere: `daemon-library`, `wf-poll-time-safety`,
`wf-deploy-process-guard` (that last one's entire feature is untracked), `Kolton-SS14-cdx`,
`agx-work`. The two `wf-*` daemon trees sit on the same base, both dirty, both unpushed — a
collision waiting to happen.

## FINDING 10 — the 07-23 board needs re-baselining before its decisions mean anything
`~/AI/SUCCESSION/solreign-codex-game-roadmap-2026-07-23/` — PROPOSED, dual DO-NOT-SHIP absorbed,
**6 decisions pending** (D1 ratify-liquidation · D2 write off 102 auto-value branches · D3 Packet
Law · D4 power-hacking chore horizon · D5 force 17 packets to binary disposition by 09-10 ·
D6 land `test/codex-sr-w084-license-regression-gate`). Its own 07-24 postscript says counts must be
re-baselined — and that postscript is itself now stale. **Check D6 first: its branch may already
be merged.** A sibling agx-presentation board is also PROPOSED with its own D1–D3.

---

## What I did NOT do, and why (auditable scope)
No merges, no pushes, no deploy artifact (John's call — new chat owns the deploy cycle), no
website-lane merges, no touch of `Kolton-SS14/server`'s uncommitted work, no engine bump, no
`auto-value/*` archaeology, no box writes / flyctl / publishing / token rotation.
