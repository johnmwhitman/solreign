# Codex handoff — GAME Station Audit round-end markup repair

Date: 2026-07-25

State: **PARKED / HOLD FOR CLAUDE INTEGRATION**

Branch: `codex/solreign-firstdeath-teardown-repro-20260725`

Base: `afb6b5cfbde7e24cf4b679f12dccf7e8e71e6b15`

Implementation commit: `9b9bd6e31d11047c31f7bba96c2be9cd2de51a9b`

## What changed

- Escaped the Station Audit inspection header's literal opening bracket before rich-text parsing.
- Added a production-path assertion that parses the complete Station Audit round-end event text.
- Made the connected First Death integration fixture synchronize immediately after round restart,
  exposing future client faults in the owning test rather than generic teardown.

## Evidence

- Clean-base First Death reproduction: **0/1**, teardown-masked.
- Explicit sync exposed `Pidgin.ParseException` in the client round-end markup path.
- Focused Station Audit markup contract was **RED** before the locale fix.
- Focused Station Audit plus original First Death: **2/2 passed** after the fix.
- Independent fail-closed review: **PASS, no P0-P2 findings**; its sole P3 durability suggestion
  was incorporated by parsing the explicitly forced inspection fixture too.
- Final normal audit + forced inspection + First Death band: **3/3 passed**.
- Combined with FX repair `a83bcd2eec508fd09092cb00a2ba28190c5715a4`,
  broad SOLREIGN integration: **466 passed / 0 failed / 4 skipped / 470 total**.
- Independent source archaeology confirmed the full client crash path and found no second
  unescaped literal bracket in the Station Audit localization file.
- Current `origin/master` `6368757ee6b980db829ad06c7c21b08a9f61c6f1` composes without conflict; its
  only post-base change is the Oasis map.
- RobustToolbox remains exactly `960edb32c4dd417496e4667177625d8c3cb14f7e`.

Full receipt:
`docs/receipts/GAME-STATION-AUDIT-ROUND-END-MARKUP-2026-07-25.md`.

## Claude route

1. Review this branch and the independent FX repair branch
   `codex/solreign-fx-stale-anchor-failsoft-20260725`.
2. Preview both against the actual landing head. They are independent and the current-master
   structural preview is conflict-free.
3. Reproduce the focused Station Audit + First Death tests and the relevant combined integration
   band on the actual landed commit.
4. Do not drop the explicit connected-client sync: it is the diagnostic step that exposed the
   previously hidden product exception.
5. Treat the synthetic commits as evidence objects, not branches to merge.

No merge, push, package publication, deploy, restart, activation, launcher, hub, or production
authority is granted.
