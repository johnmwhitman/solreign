# First-session orientation vertical-slice plan

Date: 2026-08-09

Queue item: `FIRST-SESSION-ORIENT`

Player value: a newcomer should recognize the nearby orientation beacon, begin one safe
assignment, understand any rejection, and leave with an explicit next step.

## Current product truth

The live path already contains the useful machinery: the lobby newcomer hint, a once-ever
first-spawn private prompt, one `SolreignWingmateBeacon` on each rotation map, the First
Assignment UI, department-specific safe cards, private markers, and explicit rejection
feedback. The beacon prototype carries both `WingmateBeacon` and `FirstShiftBeacon`, so it
is one physical destination with two player-facing names.

Historical commit `f6a301a84c66102f967e3e938f8f92ba836579f2` on
`codex/restore-solreign-integration-gate-20260802` is rejected as a source: its noticeboard
orientation and directive additions are unwired static logic, produce no player-visible
result, and depend on a dormant/removed front door.

## Smallest coherent change

Use the existing First Shift path. Make newcomer-facing copy consistently name the physical
destination as the **First Shift beacon**, while retaining Wingmates as the social tab inside
that beacon. The delayed spawn chat should state the immediate action and outcome in one line:
find the glowing First Shift beacon near Arrivals, press it, choose a department, and receive
a private starter task. The lobby hint should use the same noun and promise only behavior the
live default-on path provides.

Do not add a subsystem, persistence, metric, noticeboard, map placement, CVar, or operator
instrument. Do not alter assignment rules, card safety text, antagonist logic, or dormant
Market/Bounties/Records surfaces.

Expected production files:

- `Resources/Locale/en-US/_solreign/lobby-newcomer.ftl`
- `Resources/Locale/en-US/_solreign/first-shift.ftl`

## Test-first implementation

1. Reread the raw lock and work only in the existing successor based on the exact current
   PR #14 head. Stop if either head or scope changed.
2. Add the smallest localization contract assertion to the existing First Shift test surface:
   both newcomer messages name `First Shift beacon`; the spawn chat names the press,
   department choice, and private starter-task outcome; neither advertises dormant terminals.
3. Run that focused test and observe RED against current mixed `FIRST-SHIFT`/`Wingmate` copy.
4. Change only the two localization values above, then rerun the focused test to GREEN.
5. Review the rendered English at lobby-line and private-chat length. If the promised action
   is not true in the current UI, stop instead of widening implementation.

## Acceptance and proportional proof

Acceptance is textual and behavioral, not a new instrumentation project:

1. A newcomer sees the same destination name before and after spawn.
2. The prompt tells them exactly where to go and what interaction starts the flow.
3. The prompt truthfully previews department choice and one private harmless assignment.
4. Existing First Shift success/rejection feedback and next-step controls remain unchanged.

Run only the focused localization/First Shift unit band while developing. Once the two-file
candidate and its contract assertion are coherent, run `Tools/solreign_gate.sh` at most once,
then `git diff --check`, a diff-scoped secret scan, and one nonempty independent introduced-
defect review. Reserve six integration shards, Release/artifact builds, and deployment proof
for the actual release candidate.

## Stop and rollback

Stop on lock collision, changed PR #14 head, a need for new runtime wiring or CVar activation,
two consecutive implementation failures, or evidence that the copy promises unavailable
behavior. Before merge, rollback is the owned localization/test commit only. PR #14, master,
production, map placements, and dormant systems remain untouched.
