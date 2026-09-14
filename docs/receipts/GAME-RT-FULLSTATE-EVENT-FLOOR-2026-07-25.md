# GAME / RobustToolbox full-state event-floor receipt

Date: 2026-07-25

Parent branch: `codex/solreign-rt-fullstate-event-floor-20260725`

Nested branch: `codex/solreign-fullstate-event-floor-20260725`

Verdict: **PARKED / NO-CODE DECISION**

The proposed blanket entity-event floor was rejected before implementation. Read-only code
archaeology confirmed the observed order; independent protocol red-team showed that the proposed
fix would silently lose valid transient events. The nested RobustToolbox tree remains unchanged at
`960edb32c4dd417496e4667177625d8c3cb14f7e`.

No production, engine, test, game-content, map, CVar, launcher, or deployment file changed. No
build or test result is claimed for this no-code decision lane.

Canonical finding:
`docs/orchestration/findings/CODEX-RT-FULLSTATE-EVENT-FLOOR-DECISION-2026-07-25.md`.

Recommended next implementation is a new, content-scoped test-first lane that makes an
unresolvable anchor on the explicitly lossy `SolreignFxCueV1` path a quiet bounded drop while
retaining strong diagnostics for malformed or unauthorized payloads.

Claude remains the integrator. This receipt grants no merge, push, deploy, package publication,
restart, activation, launcher, hub, or live authority.
