# GAME Station Audit round-end markup receipt

Date: 2026-07-25

Branch: `codex/solreign-firstdeath-teardown-repro-20260725`

Base: `afb6b5cfbde7e24cf4b679f12dccf7e8e71e6b15`

Implementation commit: `9b9bd6e31d11047c31f7bba96c2be9cd2de51a9b`

Verdict: **PARKED / REVIEWABLE / HOLD**

## Outcome

The remaining broad SOLREIGN integration failure was not a First Death product defect. An
unescaped literal `[` in the Station Audit inspection header entered the client round-end
`RichTextLabel.SetMarkup` path and raised `Pidgin.ParseException`. Generic integration teardown
then hid that exception behind a bare `Assert.Fail`.

The header now escapes its literal opening bracket. A production-path integration assertion parses
the entire Station Audit `RoundEndTextAppendEvent` text, and the connected First Death fixture
explicitly synchronizes the replacement round before returning.

No game rule, CVar, prototype, map, RobustToolbox, launcher, hub, deployment, or production state
changed.

## Confirmed causal path

Independent source archaeology traced:

`station-audits.ftl inspection header`
→ `RenderInspectionLines`
→ `RenderLines`
→ `RoundEndTextAppendEvent`
→ `RoundEndMessageEvent`
→ client `RoundEndSummaryWindow`
→ `RichTextLabel.SetMarkup`

The inspection header was the only Station Audit localization value with an unescaped literal
opening bracket. Other brackets in that file are Fluent select-arm syntax and are not rendered.

The new assertion parses all event text emitted in its real-round invocation. It is a strong
production-path regression contract. The explicitly forced inspection-render fixture also parses
its complete event text, preserving direct coverage of this localization key if inspection
defaults or normal-round assignment conditions change later. Neither assertion claims exhaustive
coverage of every other conditional Station Audit rendering branch.

## Test-driven evidence

| Gate | Result | Evidence |
|---|---:|---|
| clean-base exact First Death reproduction | **0 passed / 1 failed** | `/private/tmp/codex-solreign-firstdeath-repro-20260725/firstdeath-clean-red.trx` |
| explicit connected-client sync diagnostic | **0 passed / 1 failed** with exact `Pidgin.ParseException` | `/private/tmp/codex-solreign-firstdeath-repro-20260725/firstdeath-explicit-sync.trx` |
| focused Station Audit markup assertion before repair | **RED** | `/private/tmp/codex-solreign-firstdeath-repro-20260725/station-audit-markup-red.trx` |
| focused Station Audit + original First Death after repair | **2 passed / 0 failed** | `/private/tmp/codex-solreign-firstdeath-repro-20260725/station-audit-and-firstdeath-green.trx` |
| final normal audit + forced inspection + First Death band | **3 passed / 0 failed** | `/private/tmp/codex-solreign-firstdeath-final-20260725/station-audit-final-strengthening.trx` |

The parser reported the em dash after the opening bracket as unexpected while expecting `/` or
`]`, matching interpretation of the literal inspection header as a markup tag.

## Branch-alone affected band

Affected fixtures plus component parity on this branch measured:
**18 passed / 3 failed / 1 skipped / 22 total**.

The three failures were all the already-isolated FX
`Validate:AnchorEntityUnresolvable` warning-fatal condition:

- `RoundEndFallback_FiresTheCheckpoint_WhenTheRoundEndsBeforeTheTimerElapses`
- `Commendation_NoResolvedAccount_PaysNothing_ButStillFiresWithoutThrowing`
- `Enabled_RealRoundEnd_AppendsTextPrintsPaperAndPersistsLedgerRow`

This branch intentionally does not contain the independent FX fail-soft repair. There was no
`ParseException` after this repair.

Evidence:
`/private/tmp/codex-solreign-firstdeath-repro-20260725/affected-fixtures-and-parity.trx`.

## Combined-stack qualification

A detached synthetic commit combined:

- game base `afb6b5cfbde7e24cf4b679f12dccf7e8e71e6b15`;
- FX repair tip `a83bcd2eec508fd09092cb00a2ba28190c5715a4`;
- this repair `9b9bd6e31d11047c31f7bba96c2be9cd2de51a9b`;
- merge tree `c862e292e662d09a0bceaca2256ed64ab4445e4f`;
- validation commit `132ebbe6d0fe62e7f382e6aeef87024a3d1c0feb`.

The broad `FullyQualifiedName~Solreign` integration band passed:
**466 passed / 0 failed / 4 skipped / 470 total** in 11 minutes 45 seconds.

Evidence:
`/private/tmp/codex-solreign-fx-stationaudit-stack-results-20260725/solreign-fx-stationaudit-combined-broad.trx`.

This closes the prior combined result of 465 passed / 1 failed / 4 skipped. The sole prior failure
was the now-exposed Station Audit round-end parser fault. No test or diagnostic was suppressed.

## Current-master drift preview

After qualification, `origin/master` advanced to
`6368757ee6b980db829ad06c7c21b08a9f61c6f1`. Its only change from this branch's base is
`Resources/Maps/_Solreign/solreign_oasis.yml`.

A conflict-free synthetic composition of current master, the FX repair, and this repair produced:

- FX/current-master tree `8551f6ce9a339673f6c860194c8d8c675d2744c3`;
- intermediate commit `0d5633c74f86f19798d808548dadaa8f96ae3214`;
- final tree `2ce9fbae71812020d75f44e5f446cd332b70896d`;
- final commit `a1d031fc144f5c6b45431da69041061916e9f5b8`.

Compared with the fully tested combined commit, the final synthetic composition differs only in
the Oasis map file. This is structural collision evidence; the exact 466/0/4 broad run belongs to
the earlier immutable tested commit and is not relabeled as a current-master rerun.

RobustToolbox remains pinned at
`960edb32c4dd417496e4667177625d8c3cb14f7e`.

## Review and integration gate

Independent fail-closed review: **PASS, no P0-P2 findings**. Its sole P3 durability recommendation
was to parse the output of the explicitly forced inspection fixture as well as the normal enabled
real-round fixture; that strengthening is included in this branch. The reviewer also confirmed
that the explicit connected-client synchronization surfaces errors and does not suppress logs,
exceptions, state, or production behavior.

Claude remains the integrator. The FX repair and this repair are independent, conflict-free
branches; the zero-failure broad proof includes both. Preview them against the actual landing head,
then reproduce the relevant integration band on the resulting landed commit.

This receipt grants no merge, push, package publication, deploy, restart, activation, launcher,
hub, or production authority.
