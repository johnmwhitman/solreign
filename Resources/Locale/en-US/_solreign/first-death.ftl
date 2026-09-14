# "The Authored First Death" copy pack (docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §8 —
# grk draft 2026-07-16, verified & PG-13-screened; adopted VERBATIM). PROVIDENCE's once-per-
# account-EVER authored death scene: public eulogy by name, private ghost line, and the next-spawn
# "Reinstatement Processing" rehire beat. Cosmetic flavor only — never affects gameplay, revival,
# scoring, or Standing (the fee is FICTION, spec §4.4).
#
# CLOSED VOCABULARY (spec §3.3, enforced by FirstDeathCopyTests): the only variables in this file
# are $name / $tours / $title / $fee. There is deliberately NO attacker variable and no template
# ever names, describes, or counts the attacker — a PA line naming the killer is a free antag
# reveal and a metagrief vector.

## PA sender label for the eulogy announcement.

solreign-first-death-sender = PROVIDENCE

## Eulogies (spec §8A) — public PA, 2-3 sentences, each includes $name. E3-E7 are cause-matched
## (VIOLENCE/VACUUM/BURN/MISADVENTURE/UNKNOWN — always selected in v1); E1/E2/E8 are the generic
## pool, staged per the pack for a later variety pass (see FirstDeathCopy.GenericEulogyKeys).

solreign-first-death-eulogy-generic-1 = Assets, a moment. { $name } has concluded their first terminal performance review. Their file remains open. Their locker will be honored, then inventoried, then reassigned with care.
solreign-first-death-eulogy-generic-2 = Attention. { $name } is no longer available for assignment. Solreign notes the absence. Productivity continues; respect is not optional and is being measured.
solreign-first-death-eulogy-generic-3 = Good afternoon, Assets. { $name } will not be clocking out with you today. They were one of ours. Resume work when you are ready; grief is permitted on company time for exactly ninety seconds.
solreign-first-death-eulogy-violence = Station-wide notice: { $name } has exited following a workplace dispute classified as a third-party liability event. No further parties will be named. Their contribution ledger is frozen pending ceremonial review.
solreign-first-death-eulogy-vacuum = Environmental alert resolved. { $name } has been reconciled against hull integrity metrics they did not personally approve. Solreign regrets the inconvenience to their continued respiration. A plaque is already being drafted.
solreign-first-death-eulogy-burn = Safety bulletin closed. { $name } has completed a thermal compliance event ahead of schedule. Their enthusiasm for energy transfer is noted. Remains of equipment are being depreciated; { $name } is not.
solreign-first-death-eulogy-misadventure = Incident closed without prejudice. { $name } has been removed from the active roster via misadventure — the category reserved for machinery, materials, and bad luck with excellent branding. They will be missed between KPI cycles.
solreign-first-death-eulogy-unknown = Personnel update: { $name } has transitioned to inactive status. Root cause: pending. Sentiment: genuine. The Board extends condolences and a reminder that uncertainty is still a billable outcome.

## The death-moment private line (spec §4.2) — sent to the dead player's session (a ghost, watching
## chat). Sets up the rehire payoff on their next spawn.

solreign-first-death-private-line = Do not be alarmed, { $name }. Your file remains open. We will discuss reinstatement when you are… available.

## The Reinstatement Processing Fee — rendered as a literal string, never deducted from anything
## (the ledger's ALWAYS-CUMULATIVE covenant makes the fee flavor-text by law, spec §4.4).

solreign-first-death-fee = 45cr

## Rehire processing lines (spec §8C) — private popup + chat on the claimed account's next spawn.
## R2 (rehire-2, the waiver) is ALWAYS used when tours_at_death == 0: a day-one death earns the
## warmest beat — the first failure must become a keepsake, not a bill.

solreign-first-death-rehire-1 = Welcome back, Asset. Your first cessation has been processed. Reinstatement Processing Fee: { $fee }. Receipt follows. We are glad you are breathing on company property again.
solreign-first-death-rehire-2 = Personnel notice: this is your first recorded terminal event. The Reinstatement Processing Fee has been waived as a courtesy. Please try to make it your last first. Compliance is its own reward; so is still being here.
solreign-first-death-rehire-3 = Rehire packet complete. Itemized: Continuity of Consciousness Surcharge ({ $fee }), Locker Re-key (complimentary), Condolence Template License (bundled), Oxygen Restart (included). Sign here. Mentally.
solreign-first-death-rehire-4 = { $title } status restored pending probationary warmth. Fee assessed: { $fee }. Your prior shift count ({ $tours }) has been retained; your interruption has not been held against you. Much.
solreign-first-death-rehire-5 = You were missed in the metrics. You are rehired. Administrative friction: { $fee }. Emotional friction: absorbed by the house. Report to work when ready; the station kept your seat warm in the spreadsheet sense.
solreign-first-death-rehire-6 = Solreign does not delete people. It reprocesses them. Fee: { $fee }. Badge reprint: automatic. Welcome home, Asset. Die interestingly only once; the sequel is just onboarding.
