# CI Readiness — 2026-07-18

**Question answered:** Once John clears the GitHub Actions billing lock, will CI on
`johnmwhitman/kolton-ss14` go green?

**Verdict: GREEN-READY = YES**, *after* the one attributions fix on branch
`chore/ci-readiness` lands (10 files, staged in this branch). Without that fix, one job
(the RGA schema validator) goes red the moment billing is restored — for a real content
reason, not a billing one.

- **Root cause of the red CI (since 2026-07-13):** GitHub Actions **billing lockout** on
  the `johnmwhitman` account. Every run since `2026-07-13T01:42Z` is refused at the
  scheduler before checkout. This is tracked as **J3** (`docs/ROADMAP-PHASE3.md`) and is
  John-gated. No workflow YAML change fixes it. Full trace: `docs/receipts/CI-TRIAGE-2026-07-17.md`.
- **One real content defect** would still fail CI after billing is restored: 10
  `attributions.yml` files put in-house-generation provenance prose in the `source`
  field, which the RGA schema requires to be a URL or `"NA"`. **Fixed on this branch.**
- Everything else (build, unit tests, Solreign integration, map loads, YAML linter, RSI,
  map schema, orphan-reachability) passes the local battery and has no billing-independent
  defect.

---

## (a) John's billing fix — the one action

**One-liner:** On GitHub, go to **Settings → Billing and licensing → Payment
information** for the **`johnmwhitman`** account and clear the failed payment / raise the
spending limit so Actions can schedule again.

Exact steps (~2 min):

1. Sign in as **johnmwhitman** (the personal account that owns `kolton-ss14`).
2. Open **https://github.com/settings/billing** (avatar → **Settings** → **Billing and
   licensing**).
3. Look for the banner/error that Actions is emitting on every blocked run:
   > "The job was not started because recent account payments have failed or your
   > spending limit needs to be increased. Please check the 'Billing & plans' section in
   > your settings."
4. Fix whichever applies:
   - **Payment method failed** → **Payment information** → update/replace the card, then
     settle any past-due balance.
   - **Spending limit hit** → **Plans and usage** (Actions & Packages usage) → raise the
     **Actions monthly spending limit** above the current spend (default is often $0,
     which blocks all paid minutes once the free tier is exhausted).
5. Confirm Actions is unblocked: **Actions** tab → re-run any recent workflow (or push a
   trivial commit). The run should now start instead of being refused at the scheduler.

No repo/config change is required for the billing fix — it is entirely account-side.

---

## (b) What runs after billing is restored, and what should pass

Triggers below are for push to `master`/`staging`/`stable` and PRs (per each workflow's
`on:`). All results are from the local battery (`docs/receipts/TRIPLE-MERGE-BATTERY-2026-07-16.md`)
plus per-merge branch-green receipts, since Actions itself cannot run while blocked.

| Workflow (file) | Trigger | Expected once billing restored |
|---|---|---|
| **Build & Test Debug** (`build-test-debug.yml`) | push/PR | PASS — local: 0 build errors, `Content.Tests` 2216/0 (+3 skip), `Content.IntegrationTests` Solreign 302/0. |
| **Build & Test Map Renderer** (`build-map-renderer.yml`) | push/PR | PASS — builds + runs map renderer; map-load suite 246/246 locally. |
| **YAML Linter** (`yaml-linter.yml`) | push/PR | PASS — local `Content.YAMLLinter` "No errors found". |
| **Test Packaging** (`test-packaging.yml`) | push (path-filtered) / PR | PASS — builds `Content.Packaging`, packages server+client; no defect. |
| **Solreign CI — Content Checks** (`solreign-ci-content-checks.yml`) — 4 jobs: | push/PR | See below. |
| ├ RSI Validator | | PASS — local `validate_rsis.py` clean. |
| ├ **YAML RGA schema validator** | | **PASS only with this branch's fix** (else FAIL on 10 files — see (c)). Local yamale: 88/88 after fix. |
| ├ YAML map schema validator | | PASS — 220/220 map files locally. |
| └ Solreign orphan-reachability | | PASS — local `dotnet test` 1/1. |
| **CRLF Check** (`check-crlf.yml`) | PR only | PASS — no CRLF introduced. |
| **RSI Diff** (`rsi-diff.yml`) | PR only | Informational (posts a diff comment); not a gate. |

Scheduled/dispatch/publish workflows (`publish.yml` cron is commented out,
`publish-testing.yml` daily cron, `update-credits.yml` weekly cron, `heisendetector.yml`
now dispatch-only, all six `labeler-*.yml`, `close-master-pr.yml`, `no-submodule-update.yml`)
are not part of push-gating CI and are out of scope for green-readiness. `publish*` and
`update-credits` depend on repo secrets (`PUBLISH_TOKEN`, `CHANGELOG_RSS_KEY`,
`CHANGELOG_DISCORD_WEBHOOK`) — expected, unrelated to the billing lock.

---

## (c) Residual real defects that would still fail CI after billing

### 1. RGA schema: 10 attributions files — **FIXED on this branch**

The `fix/rga-schema` merge (`affde1f976`, "88/88 schema pass") cleaned the original 8
files the CI-triage lane found. A **later** audio merge — `87a7c91665` ("audio: the full
NC-closure arc — free-swaps… 10 Lyria lobby commissions"), which is on `master` *after*
the rga fix — **re-introduced the same class of defect**: 22 new attribution entries
across 10 files put provenance prose (e.g. `"In-house generation, 2026-07-17. See
docs/receipts/AUDIO-FREESWAP-INTEGRATE-2026-07-17.md."`) in the `source` field, which the
schema (`RobustToolbox/Schemas/rga.yml` → `source: url()`) requires to be a URL or `"NA"`.

Verified by reimplementing the exact schema+validators (`RobustToolbox/Schemas/rga.yml`,
`rga_validators.py`) with `yamale` — the library `PaulRitter/yaml-schema-validator@v1`
wraps — against all 88 `Resources/**/attributions.yml`:

- **Before fix (current `master`): 78/88 PASS, 10/88 FAIL.**
- **After this branch's fix: 88/88 PASS.**

Files fixed (all under `Resources/Audio/`): `Ambience/Antag`, `Animals`,
`Effects`, `Effects/Grenades/SelfDestruct`, `Items`, `Items/Anomaly`, `Items/Artifact`,
`Lobby`, `Mecha`, `Voice/Talk`.

Fix follows John's own `fix/rga-schema` convention exactly and is lossless: the source
prose is appended verbatim to the `copyright` field (which the schema leaves
unconstrained) as `(Source provenance: …)`, and `source` is set to `"NA"`. 44 lines
changed, 2 lines per entry, no incidental edits, comments preserved.

> Note: the entries that already put provenance prose in `copyright` (e.g.
> `Resources/Audio/_Solreign/`, `Resources/Textures/_Solreign/`) already pass — a raw
> `grep "In-house generation"` overcounts because it also matches the legitimately-placed
> `copyright` prose. The authoritative signal is the yamale run, which flags only the 10
> files where the prose sits in `source`.

**To carry this fix to master:** merge `chore/ci-readiness` into `master` (John-gated
merge per the Solreign merge law).

### 2. `heisendetector.yml` pins `actions/upload-artifact@v7` — low priority, not fixed

`heisendetector.yml` line 52 uses `actions/upload-artifact@v7` while
`build-test-debug.yml` (line 60) uses `@v4` for the same action. This is a version
inconsistency and `v7` may not resolve. **Left unfixed deliberately** because: (a) the
workflow is now `workflow_dispatch`-only (no push/schedule trigger), and (b) the
upload-artifact step is gated `if: failure()`, so it never runs on a green build and
cannot affect push-gating CI health. If John ever runs Heisentest manually against a
failing suite and the artifact upload errors on the version, pin it to `@v4` to match the
rest of the repo. Not verified against the live Actions registry (no network); flagged as
an observation, not a confirmed break.

No other billing-independent defects found: all other pinned actions
(`actions/checkout@v7`, `actions/setup-dotnet@v5`, `actions/upload-artifact@v4`,
`space-wizards/submodule-dependency@v0.1.5`, `PaulRitter/yaml-schema-validator@v1`) are
used consistently across the push-gating workflows; `secrets.GITHUB_TOKEN` is
auto-provided; the `publish*`/`update-credits` secrets are expected repo secrets.

---

## (d) The 96-bounce-email context (Heisentest schedule → dispatch)

The single largest source of CI noise during the outage was **`heisendetector.yml`
("Hunt for Heisentests")**. Upstream (space-wizards) runs it on a `schedule: */15 min`
cron to catch flaky tests across a large multi-contributor mainline. On this
single-maintainer fork that cron fired **up to 96 times/day**, and — because Actions was
billing-locked — **every one of those 96 daily runs failed at the scheduler and emailed
John a failure notification**, on top of fully duplicating `build-test-debug.yml` (which
already runs the same `Content.Tests` + `Content.IntegrationTests` on every push).

The CI-triage lane (`fix/ci-triage`, merged `56642d81b6`) **removed the `schedule`
trigger and left the workflow as `workflow_dispatch` only** — killing the ~96/day bounce
stream while keeping the ability to run it manually later. That change is already on
`master`. After billing is restored, John can re-add a (lighter) scheduled maintenance
trigger if he wants periodic flaky-test hunting; it is intentionally off by default now.

---

## Verification performed (local — Actions cannot run while billing-locked)

- Reimplemented `RobustToolbox/Schemas/rga.yml` + `rga_validators.py` with `yamale`
  (v6.1.0) + `validators`; ran against all 88 `Resources/**/attributions.yml`.
  Before fix: 78/88. After fix (this branch): **88/88**.
- Confirmed the 10 failing files are on `master` itself (not just a downstream branch)
  via `git show master:<file>` and merge-ancestry checks (`87a7c91665` is an ancestor of
  `master`, landed after the `fix/rga-schema` merge `affde1f976`).
- Battery trust: `docs/receipts/TRIPLE-MERGE-BATTERY-2026-07-16.md` (build 0 errors;
  units 2216/0/3; Solreign integration 302/0/1; map loads 246/0; YAML linter clean; NC
  census 0) + per-merge branch-green receipts on subsequent master merges.
- Working tree clean; fix isolated to branch `chore/ci-readiness` (worktree
  `~/AI/solreign-trees/ci-readiness`). No change to `master`, no push.

**Bottom line for John:** clear the Actions billing lock (Settings → Billing), then merge
`chore/ci-readiness` so the RGA validator stays green. With both done, CI is green-ready.
