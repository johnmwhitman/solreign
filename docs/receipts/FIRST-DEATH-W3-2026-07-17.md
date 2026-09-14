# FIRST-DEATH FD-W3 — `first-death-crypt` build receipt (game side)

**Date:** 2026-07-17 · **Lane:** FD-W3 (spec §10) · **Branch:** `feat/first-death-w3` off `origin/master` @ `481bcc212e` (FD-W1/W2 merged)
**Spec:** `docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md` §5 (crypt entry: game→daemon→website), §9 Q2 built with the EXISTING pseudonym law (GUID `[:6]` short handles; real-name option John-gated, NOT built)
**Companion:** daemon repo `~/AI/solreign-director` branch `feat/first-death-crypt` (no-LLM `handle_first_death` path + `tests/test_crypt_first_death.py` — see `docs/FIRST-DEATH-CRYPT-2026-07-17.md` there)

## What this lane adds (game)

The additive wire extension of the crypt death report (spec §5.1): when — and only when — a death
wins the once-per-account-EVER atomic first-death claim (`SeasonLedgerStore.TryClaimFirstDeathAsync`,
FD-W1), the game composes and fires ONE extended `POST /api/crypt/death` carrying the
game-templated epitaph (rendered plate text), the §8D closed-map cause label, `first_death: true`,
character name, tours, and title. The per-death T+0 telemetry report
(`SolreignDeathTelemetrySystem` → `ReportDeath`) keeps firing for every death exactly as today.

| File | Change |
|---|---|
| `Content.Server/_Solreign/Providence/FirstDeathCryptReport.cs` | NEW — pure wire record + `Build(...)`, snake_case `JsonPropertyName`s = the daemon's `CryptDeathEvent` contract; `attacker_guid` always empty (redaction law §3.3) |
| `Content.Server/_Solreign/Providence/FirstDeathCopy.cs` | `CauseLabelFor(FirstDeathCause)` — the §8D closed display map (reused by FD-W4's embed later) |
| `Content.Server/Administration/Systems/SolreignCryptSystem.cs` | `ReportFirstDeath(FirstDeathCryptReport)` — IDENTICAL gating to `ReportDeath` (fail-closed `DirectorChannel.TryGetReadyToken` on `solreign.crypt.enabled` + master + token, same `"crypt"` rate-limit channel, same signed fire-and-forget POST via the extracted shared `PostDeathReport` sender). Daemon absent/off → silently skipped, scene unaffected (rail 4) |
| `Content.Server/_Solreign/Providence/ProvidenceFirstDeathSystem.cs` | the call site: composes the report in `ClaimAndSchedule` immediately after a WON claim, from exactly the values persisted into the claim row; `_cryptReportsComposed` test seam (reset with round state) |
| `Content.Tests/_Solreign/FirstDeathCryptReportTests.cs` | NEW — 5 unit tests: Build snapshot correctness, serialized property-set/byte contract, §8D map verbatim, map totality + attacker-vocabulary denylist, Build↔map round-trip |
| `Content.IntegrationTests/Tests/_Solreign/FirstDeathSceneIntegrationTest.cs` | +4 assertions across the existing scenarios: claimed first death composes exactly ONE report (with `solreign.crypt.enabled` verified default-OFF — the offline law, zero outbound HTTP via the `TryGetReadyToken` short-circuit); repeat death same round → still 1; claim-refused death next round → 0; `first_death.enabled=false` → 0 |

**Gating law verified:** the existing crypt hook is telemetry TO the daemon gated by
`solreign.crypt.enabled` + master Director CVar + token (all fail-closed, default OFF) — this
lane's report rides the identical gate. No daemon→player text exists anywhere in this feature, so
the broadcasts-gate law (`solreign.director.broadcasts.enabled`) is not in play (spec rail 3).

**Website: ZERO changes — verified.** `~/AI/Kolton-SS14/website/src/components/CryptFeed.tsx`
(client-side `useEffect` fetch of `/api/public/crypt`) renders `victim_id`, `final_standing`, and —
when present — quoted `obituary_text` (lines 69/74/78-80). First-death entries ride `obituary_text`
verbatim through the existing card. (The spec's `website-v2/website/src/...` pointer is stale; the
live path is `website/src/` under the Kolton-SS14 parent, outside this server repo.)

## Verification (this branch, all foreground)

| Command | Result |
|---|---|
| `dotnet build -c Release` | 50 projects, **0 errors** (warnings pre-existing) |
| `dotnet test Content.Tests -c Release --no-build` | **2303 passed / 3 skipped (known)** — includes the 5 new wire-contract tests |
| `dotnet test Content.IntegrationTests -c Release --no-build --filter FullyQualifiedName~Solreign` | **307 passed / 1 skipped (known)** |
| `dotnet run --project Content.YAMLLinter -c Release` | **"No errors found"** |

Daemon branch verification: `pytest tests/` → **514 passed** (8 new in
`tests/test_crypt_first_death.py`).

## Deviations from the spec (recorded, with rationale)

1. **D1 — POST fires at claim time (~T+0.2s), not with the T+4s beat** (spec §2 item 4 / §5.3
   "The POST fires at T+4s"). The T+4s placement exists for in-game pacing; the crypt POST has no
   in-game surface. Firing it in `ClaimAndSchedule` right after the won claim ties the permanent
   public memorial to the exactly-once linchpin itself and removes the beat's accepted swallow
   window (round-end race / mid-flight CVar flip — review F4's path), where the claim row would be
   burned but the plaque never minted. "Created live" (§5.3) holds: the plaque is up within the
   same shift either way. Independent gating (§2 "each independently gated") is preserved — the
   report does NOT re-check `first_death.enabled` or InRound at send time; it rides the crypt
   channel's own gates.
2. **D2 — file boundary wider than §10's "one method edit".** §10 was drafted before FD-W2 landed;
   sourcing the payload "from FD-W2's composed scene" requires the call site in
   `ProvidenceFirstDeathSystem.ClaimAndSchedule` plus the §8D label map in `FirstDeathCopy`. Both
   edits are additive; no existing behavior changed (the W1/W2 test batteries stayed green
   untouched).
3. **D3 — daemon stores no `character_name`/`tours`/`title`** (wire-accepted, deliberately
   unstored): zero schema change, and the public-feed name exposure is the John-gated §9 Q2 policy
   delta. `cause_label` rides the existing `weapon_used` column (not selected by the public feed).
4. **Moderation inheritance (spec-required gate, §5.2):** the epitaph passes
   `moderate_output(..., "obituary")` daemon-side — fail-closed, so with no classifier configured
   the canned obituary fallback is stored instead of the epitaph (the entry still mints). The
   no-LLM rail holds regardless: `minimax_client.chat_text` is trap-asserted never to run on this
   path.

## Notes for the orchestrator

- **WORKTREE-MAP registration pending:** `~/AI/solreign-trees/first-death-w3` (GAME,
  `feat/first-death-w3`, this lane) needs its row in the OPS-lane `docs/WORKTREE-MAP.md` — that
  file lives in the orchestrator-owned `orch-ops` canonical lane, which builder lanes do not edit.

- **Rare double-mint possibility (accepted, additive-only):** a first death that is ALSO legendary
  (standing ≥50 / nemesis kill) produces two crypt entries — the legacy LLM obituary from the T+0
  telemetry report plus this lane's plate. The legacy path is untouched by spec law; suppressing it
  would be an existing-flow change.
- Q7 (Director/crypt gates ON at opening?) remains a live-box decision — everything here ships
  dormant behind the default-OFF crypt gates.
- The council kill test (memo test 4: ≥3/5 second sessions, neutral-or-positive mentions) is an
  operational measurement to collect after go-live — flagged per spec §7.
