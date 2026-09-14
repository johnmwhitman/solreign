# SOLREIGN Game: six-week product roadmap

Updated: 2026-08-09

## Product result

A newcomer should complete an understandable, useful first session and leave with a
reason to return. Work is ordered by the player's journey: arrive and orient, act and
connect, then remember and return. A new framework, metric, or operator instrument is
not roadmap progress unless it is the smallest requirement for one of those outcomes.

## Current release truth

- PR #14 is the only active release candidate. It is exact head
  `4e0d1842c5c7a0fd1b09e767a3fd2613e2c3a47f` over `origin/master`
  `637d102a00d6599b328e34b49cd5db4c61440871`.
- Its local product and focused proof are complete. Hosted GitHub Actions currently
  refuse before runner allocation because of the account payment/spending limit. This
  is infrastructure refusal, not code evidence, and it may not be bypassed by this lane.
- The only permitted dependent work is one local successor based on that exact PR head.
  No second PR, master push, merge, deployment, or dormant-CVar activation occurs while
  PR #14 is blocked.
- The held arrivals fix `8500070bd0f2f373eb634bcc5db0763dc36e6d39` does not need
  replaying: it is already an ancestor of both fetched `origin/master` and PR #14. Its
  two changed files are byte-identical in the held commit, master, PR #14, and the local
  successor. The queue records this as verified repository state, not production proof.

## Weeks 1-2: make arrival safe and legible

Ship PR #14 when its gate is available. Preserve the already-landed arrivals repair: it
keeps established arrivals-shuttle re-boarders alive without granting
`PendingClockInComponent`, while retaining legitimate first-arrival behavior, normal
off-grid exit, and the no-spawn fallback. Do not manufacture a replay commit for code
already present on master.

After that correctness repair, make one newcomer path understandable in play: arrival,
one nearby useful action, and an explicit next step. Reuse existing First Shift,
Wingmates, contracts, and map affordances before creating another subsystem. Historical
branch `codex/restore-solreign-integration-gate-20260802` is archaeology only. Its
orientation additions are unwired static logic with no player-visible output, so none of
that commit is a candidate for salvage. Extend the live First Shift path instead.

Exit: a bounded first-session scenario proves the newcomer can arrive safely, identify
one useful action, complete or reject it with truthful feedback, and know what to do next.

## Weeks 3-4: make the first session social and consequential

Connect the first useful action to one other player or department through already-live
Wingmate, First Shift, or contract surfaces. Prefer reducing dead clicks, ambiguity, and
large-map loneliness over adding content breadth. Each accepted action must have visible
success or a specific private rejection; unavailable systems must not advertise a dead
front door.

Exit: the first-session scenario includes one real cooperative handoff, its success and
failure paths are understandable, and low population does not strand the player on an
impossible objective.

## Weeks 5-6: give the player a reason to return

Create one restrained cross-round callback from already-shipped durable state such as
the Season Ledger, First Death, or Shift Archive. It should recall a meaningful prior
choice or relationship without inventing progression currency, grinding, or a new data
platform. The callback must be visible to the returning player and safe when history is
missing.

Exit: a returning-player scenario observes one truthful prior-round callback, while a
fresh player receives a coherent fallback and no stale identity or private-player data is
exposed.

## Release rhythm and proof budget

Target one coherent player-value bundle every two weeks. Freeze scope when the bundle's
acceptance scenario is satisfied; gates that miss the release window roll forward rather
than being waived.

For each implementation wake: run the changed fixture or focused unit band. Run
`Tools/solreign_gate.sh` once only after the dependent candidate is coherent. Reserve the
six integration shards, artifact build, and deployment proof for the actual release
candidate. Hosted compute remains off until a human-approved zero-cost or funded path
exists.

WIP is capped at PR #14 plus one local dependent successor. The authoritative execution
order and machine state live in `docs/roadmap/EXECUTION-QUEUE.yaml`.
