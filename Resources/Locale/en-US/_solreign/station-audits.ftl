# "Station Audit" — PROVIDENCE's end-of-shift deadpan corporate assessment (v14 wave-1 #3, council
# C1 deltav-008 lineage — CLEAN-ROOM build, no upstream text or code read/reused; only the public
# "a paper station report reaches the round-end screen" description informed the shape). Closed
# template vocabulary throughout: every line here is picked by StationAuditComposer from real
# locally-observed round state, never assembled from free text. Same evil-megacorp HR voice as the
# Corporate Ladder / Station Directive.

solreign-station-audit-header = === PROVIDENCE STATION AUDIT — SHIFT #{$round} ===

solreign-station-audit-shift-duration = Shift Duration: {$minutes} {$minutes ->
    [one] minute
   *[other] minutes
}

solreign-station-audit-crew-count = Crew Complement: {$count} {$count ->
    [one] asset
   *[other] assets
}

## Corporate Directive

solreign-station-audit-directive-present = Corporate Directive on file: {$title}
solreign-station-audit-directive-absent = Corporate Directive on file: None this shift.
solreign-station-audit-directive-outcome-fulfilled = Directive Outcome: Quota Met.
solreign-station-audit-directive-outcome-unfulfilled = Directive Outcome: Quota Missed.
solreign-station-audit-directive-outcome-unreported = Directive Outcome: Not On Record.

## Fatalities

solreign-station-audit-deaths-none = Fatalities: None recorded. Head Office is cautiously pleased.
solreign-station-audit-deaths-some = Fatalities: {$count}
solreign-station-audit-deaths-commemorated = A formal commemoration was recorded for this shift.
solreign-station-audit-deaths-uncommemorated = No formal commemoration is on file for this shift.

## Stipends / Liability Board

solreign-station-audit-stipends-disabled = Stipend Program: Not active this shift.
solreign-station-audit-stipends-processed = Stipend Program: {$count} {$count ->
    [one] crew asset
   *[other] crew assets
} processed for career-rank salary.

solreign-station-audit-bounties-disabled = Liability Board: Not active this shift.
solreign-station-audit-bounties-count = Liability Board: {$count} {$count ->
    [one] claim
   *[other] claims
} adjudicated.

## Notable events

solreign-station-audit-notable-events = Notable Events Logged: {$count}

## Commendation of the Shift

solreign-station-audit-commendation-present = Commendation of the Shift: {$name} — Highest Confirmed Productivity Output.
solreign-station-audit-commendation-absent = Commendation of the Shift: No standout performance recorded this shift.

## Item of Concern (closed set — deliberately FICTIONAL flavor, never tied to a real metric)

solreign-station-audit-item-of-concern-header = Item of Concern:
solreign-station-audit-item-of-concern-unrepaired-breach = An unrepaired hull breach remains open on the maintenance backlog.
solreign-station-audit-item-of-concern-unpaid-cafeteria-tabs = Several cafeteria tabs remain unpaid. Head Office is filing a strongly worded memo.
solreign-station-audit-item-of-concern-supply-requisition-backlog = Janitorial supply requisitions remain unfulfilled for the third consecutive shift.
solreign-station-audit-item-of-concern-vending-machine-under-investigation = A vending machine on the concourse has been marked "under investigation" since last quarter.
solreign-station-audit-item-of-concern-incident-paperwork-backlog = Incident paperwork backlog remains above the comfort threshold.
solreign-station-audit-item-of-concern-unlogged-maintenance-tunnel = A maintenance tunnel remains unlogged in the current station schematic.

solreign-station-audit-footer = — Filed by PROVIDENCE on behalf of Solreign Corporate.

## Inspection layer — mandatory same-session PROVIDENCE consequence (v14 gap-closure pass)

solreign-station-audit-hr-sender = Solreign HR

solreign-station-audit-inspection-header = \[PROVIDENCE INSPECTION — ASSIGNED THIS SHIFT]
solreign-station-audit-inspection-verdict-pass = {$name}: PASS
solreign-station-audit-inspection-verdict-fail = {$name}: FAIL
solreign-station-audit-inspection-verdict-na = {$name}: NOT APPLICABLE

solreign-station-audit-inspection-assigned-announcement = PROVIDENCE has assigned {$count} inspection {$count ->
    [one] criterion
   *[other] criteria
} for this shift: {$names}.

solreign-station-audit-checkpoint-commendation = PROVIDENCE issued a commendation at the {$minutes}-minute mark.
solreign-station-audit-checkpoint-escalation = PROVIDENCE issued a formal escalation at the {$minutes}-minute mark.
solreign-station-audit-checkpoint-quietly-corrected = PROVIDENCE quietly corrected an error at the {$minutes}-minute mark.
solreign-station-audit-checkpoint-concluded-at-round-end = PROVIDENCE's inspection concluded at shift's end.

solreign-station-audit-checkpoint-pa-commendation = Head Office is pleased. PROVIDENCE commends the crew's performance this shift.
solreign-station-audit-checkpoint-pa-escalation = PROVIDENCE has flagged a standards failure this shift. Corrective attention is expected.
solreign-station-audit-checkpoint-pa-quietly-corrected = PROVIDENCE has noted a minor discrepancy and quietly corrected the record.

## Inspection criteria (closed vocabulary, StationAuditCriterionCatalog)

solreign-station-audit-criterion-zero-fatalities-name = Zero Fatalities
solreign-station-audit-criterion-zero-fatalities-pass = No fatalities recorded — standard maintained.
solreign-station-audit-criterion-zero-fatalities-fail = Fatalities recorded — standard not maintained.

solreign-station-audit-criterion-event-responsiveness-name = Event Responsiveness
solreign-station-audit-criterion-event-responsiveness-pass = Crew responded to at least one notable event.
solreign-station-audit-criterion-event-responsiveness-fail = No notable-event response logged this shift.

solreign-station-audit-criterion-full-complement-name = Full Complement
solreign-station-audit-criterion-full-complement-pass = Station was crewed this shift.
solreign-station-audit-criterion-full-complement-fail = Station was not crewed this shift.

solreign-station-audit-criterion-payroll-discipline-name = Payroll Discipline
solreign-station-audit-criterion-payroll-discipline-pass = Stipend processing kept pace with crew complement.
solreign-station-audit-criterion-payroll-discipline-fail = Stipend processing fell behind crew complement.

solreign-station-audit-criterion-directive-compliance-name = Directive Compliance
solreign-station-audit-criterion-directive-compliance-pass = This shift's Corporate Directive was fulfilled.
solreign-station-audit-criterion-directive-compliance-fail = This shift's Corporate Directive was not fulfilled.

solreign-station-audit-criterion-memorial-protocol-name = Memorial Protocol
solreign-station-audit-criterion-memorial-protocol-pass = The shift's first death was formally commemorated.
solreign-station-audit-criterion-memorial-protocol-fail = The shift's first death was not formally commemorated.

## Lifetime citation (async PROVIDENCE follow-up, nyanopark-019 fold)

solreign-station-audit-lifetime-count = This is shift #{$count} in the station's audit history.
solreign-station-audit-lifetime-commendation-tours = {$name} has now logged {$tours} tours station-wide.
