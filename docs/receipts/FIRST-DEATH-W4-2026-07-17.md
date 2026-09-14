# FIRST-DEATH FD-W4 — `first-death-obituary` build receipt (game side, SHIPS INERT)

**Date:** 2026-07-17 · **Lane:** FD-W4 (spec §10) · **Branch:** `feat/first-death-w4` off `origin/master` @ `d61b8939e9` (FD-W1/W2/W3 merged)
**Spec:** `docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md` §6 (the Discord obituary — designed, gated) + §8D (the embed) + §9 Q1 (go-live is John's standing gate; manual paste is the recommended opening-weeks mode)

## What this lane adds

The Discord obituary leg of the authored first death — **built inert by design**. When a death wins
the once-per-account-EVER atomic claim (FD-W1's `TryClaimFirstDeathAsync`), the game composes the
§8D obituary from exactly the claimed snapshot and runs a fail-closed gate stack. With the shipping
defaults **nothing ever egresses**: the webhook CVar ships EMPTY (the BugReport "empty = disabled"
precedent), and every claimed first death instead appends a ready-to-paste obituary block to
`data/first_deaths.jsonl` — the opening-weeks mode where John pastes the good ones into Discord by
hand (spec §6.2's John gate; setting `solreign.first_death.webhook` on the live box IS the go-live
decision, and this lane does not make it).

### The gate stack (all must pass before any send, in order)

| # | Gate | Semantics |
|---|---|---|
| 0 | The atomic first-death claim (FD-W1) | No claim → no obituary of any kind. Once per account, EVER — the volume ceiling on everything below. |
| 1 | `solreign.first_death.webhook` non-empty + parseable | SHIPS EMPTY = inert (BugReport-precedent semantics). Parsed **locally, zero egress at config time** (see D1). Unparseable = fail closed to manual-paste mode, logged WITHOUT the value (CVar is CONFIDENTIAL). |
| 2 | `solreign.first_death.min_players` (default 2) vs **connected players at death time** | The recap-law analogue (spec §6.2): never advertise an empty station. Count snapshotted synchronously on the death tick, never re-read after the await. |
| 3 | Own rate-limit channel `first_death_discord` | The FD-W3 review lesson (grk r1 #1) applied as lane law: any new egress gets its OWN channel — distinct from BOTH crypt channels, pinned by a constants test. Consumed only after gates 1–2 pass (a non-send never burns the window). |

**Write-before-dispatch:** the JSONL line — carrying the gate decision
(`webhook-empty` / `below-player-gate` / `rate-limited` / `dispatched`) and the ready-to-paste §8D
text — is appended BEFORE any send is attempted, on **every** claimed first death (spec §6.2:
below the player gate "the obituary goes to the JSONL file only"; when the webhook fires, the file
is still the durable ledger). The send itself is fire-and-forget (BugReport threading contract),
and its error path logs the exception TYPE only — an `HttpClient` message can embed the request
URI, i.e. the webhook token.

### Files

| File | Change |
|---|---|
| `Content.Server/_Solreign/Providence/FirstDeathObituary.cs` | NEW — the composed snapshot record (plain data; **no GUID field exists**, closed vocabulary by construction) + the `FirstDeathObituaryDispatch` decision enum |
| `Content.Server/_Solreign/Providence/FirstDeathDiscordPayload.cs` | NEW — pure §8D composer (title/body/footer templates, acid-green `BugReportDiscordPayload.EmbedColor` reuse, closed cause-label map via FD-W3's `FirstDeathCopy.CauseLabelFor`, empty allowed-mentions), pure player-gate math, pure zero-egress webhook-URL parser |
| `Content.Server/_Solreign/Providence/FirstDeathObituaryJsonl.cs` | NEW — pure `data/first_deaths.jsonl` line shaping (the `BugReportJsonl` idiom): closed GUID-free property set + the paste block byte-identical to the embed text |
| `Content.Server/_Solreign/Providence/FirstDeathObituarySystem.cs` | NEW — ECS glue: CVar subscriptions, the gate stack, the locked JSONL append (`IResourceManager.UserData` dir — the bug_reports.jsonl idiom; runtime server-local data, never in shipped artifacts), the one egress path, test seams |
| `Content.Server/_Solreign/Providence/ProvidenceFirstDeathSystem.cs` | the call site (the lane's only existing-file edit, FD-W3's D2 precedent): synchronous `PlayerCount` snapshot on the death tick + `_obituary.Record(...)` immediately after a WON claim, in its OWN try/catch — a failure here costs the obituary, never the eulogy (fate-isolation law, grk r1 #5) |
| `Content.Tests/_Solreign/FirstDeathDiscordPayloadTests.cs` | NEW — 9 unit tests: §8D embed shape from fixed inputs, nobody-pinged, closed-label-map round-trip (raw enum never leaks), all-slots-filled, attacker + PG-13 vocabulary denylists over every composed surface, handle-only identity (structural no-GUID pin), player-gate below/at/above, webhook-parser accept/reject batteries |
| `Content.Tests/_Solreign/FirstDeathObituaryJsonlTests.cs` | NEW — 5 unit tests: single-line law, snapshot+decision field correctness, paste block == embed text, closed GUID-free property set, dispatch-name totality/uniqueness |
| `Content.Tests/_Solreign/FirstDeathObituaryChannelTests.cs` | NEW — 1 test: `first_death_discord` distinct from BOTH crypt channels (the rate-limit-channel law, pinned) |
| `Content.IntegrationTests/Tests/_Solreign/FirstDeathSceneIntegrationTest.cs` | +3 scenarios (and the shared clean-slate helper now redirects the JSONL to a per-test temp file — the SeasonLedgerDbPath law — and zeroes the egress counter): **defaults → jsonl-only, dispatch `webhook-empty`, ZERO send attempts, once-per-claim across repeat deaths and rounds** (the inert proof); **webhook armed + below player gate → jsonl-only (`below-player-gate`), zero sends, mock confirms**; **both gates met → send path invoked exactly ONCE on the mock with the §8D embed, jsonl line still lands, repeat death never re-sends**. No test performs any real egress — the send seam is mocked before any armed scenario. |

## Verification (this branch, all foreground, Release)

| Command | Result |
|---|---|
| `dotnet build -c Release` | 50 projects, **0 errors** (warnings pre-existing; zero in new files) |
| `dotnet test Content.Tests -c Release --no-build` | **2344 passed / 3 skipped (known)** — baseline 2329 + 15 new |
| `dotnet test Content.IntegrationTests -c Release --no-build --filter FullyQualifiedName~Solreign` | **316 passed / 1 skipped (known)** — baseline 313 + 3 new |
| `dotnet run --project Content.YAMLLinter -c Release` | **"No errors found"** |

## Deviations / decisions (recorded, with rationale)

1. **D1 — webhook identifier parsed locally, not via the BugReport `GetWebhook` HTTP probe.** The
   BugReport clone resolves its webhook by an HTTP GET at CVar-set time. This lane's law is zero
   egress until every send gate passes, so configuration must not probe the network: the id/token
   pair is parsed from the URL shape (`https` + discord.com/discordapp.com + `.../webhooks/{id}/{token}`,
   fail-closed on anything else) with a pure, unit-tested parser. The send itself is the
   validation. Side benefit: no boot-time dependency on Discord being reachable to arm the webhook.
2. **D2 — JSONL lands on EVERY claimed first death, including below the player gate.** The lane
   brief's test shorthand read "min_players unmet → neither", but the authoritative spec §6.2 is
   explicit — "A solo player's first death still gets [its keepsakes]… Below the gate, the
   obituary goes to the JSONL file only" — and the John-gate paragraph makes the JSONL the
   canonical record of every first death for hand curation. Built to spec; the integration test
   pins `below-player-gate` → jsonl line present, zero sends.
3. **D3 — §8D embed text lives in C# constants, not first-death.ftl.** The FD-W2 copy pack's
   closed-variable law (`$name/$tours/$title/$fee` only) is pinned by tests; the embed additionally
   needs the cause label. Following the precedent `FirstDeathCopy.CauseLabelFor` set for §8D
   display text, the embed templates are C#-side constants — byte-testable, and the .ftl law stays
   untouched. (No YAML/loc changes anywhere in this lane.)
4. **D4 — claim-time composition (FD-W3's D1 placement), not T+4s.** The obituary composes and
   decides beside the crypt report, immediately after the won claim — outside the beat's accepted
   swallow window, so the JSONL ledger records every claimed first death even if the in-game beat
   is later dropped by a round-end race.
5. **Error-log hygiene:** the send failure path logs `e.GetType().Name` only (never the message),
   because `HttpClient` exception text can embed the full request URI — which for a Discord webhook
   IS the secret. The unparseable-CVar path likewise logs without the value.

## Notes for the orchestrator

- **Ships inert; go-live is §9 Q1 (John's standing gate).** Setting `solreign.first_death.webhook`
  on the live box is the entire arming action; `min_players` (default 2) and the dedicated
  rate-limit channel are already live behind it. Recommended opening-weeks mode remains manual
  paste from `data/first_deaths.jsonl` (John: the `paste` field of each line is the whole block).
- **WORKTREE-MAP registration pending:** `~/AI/solreign-trees/first-death-w4` (GAME,
  `feat/first-death-w4`, this lane) needs its row in the OPS-lane `docs/WORKTREE-MAP.md` —
  orchestrator-owned; builder lanes do not edit it.
- **MERGE LAW: only Claude merges.** No merge or push performed by this lane.
- `data/first_deaths.jsonl` grows one line per NEW account's first death, forever, on the live
  box's data dir (runtime `UserData`, never in shipped artifacts — the bug_reports.jsonl idiom).
  Bounded by the account population; no rotation needed at SOLREIGN scale.
