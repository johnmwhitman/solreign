# Codex handoff — full-state event-floor investigation

Date: 2026-07-25

State: **PARKED / DECISION READY**

Claude should not merge an engine floor from this lane: none was written.

The useful result is negative and safety-relevant. A full snapshot is not a causal replacement for
the separate reliable-ordered entity-event stream, so globally discarding events by tick can lose
valid BUI/RPC behavior. The finding records the verified race, the rejected approach, a narrow
SOLREIGN FX repair, and a protocol-grade future barrier design.

Next route:

1. Review the decision packet.
2. Keep the earlier disconnect queue-clear patch independent.
3. Open a fresh GAME lane for missing-anchor cosmetic FX fail-soft behavior.
4. Keep the barrier/generation design in Engine Lab HOLD pending explicit protocol approval.

Decision packet:
`docs/orchestration/findings/CODEX-RT-FULLSTATE-EVENT-FLOOR-DECISION-2026-07-25.md`.

No push, merge, package publication, deploy, restart, activation, launcher, hub, or production
authority is granted.
