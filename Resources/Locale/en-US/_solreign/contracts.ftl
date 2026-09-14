# Solreign Contracts (quest spine, Milestone 1). Voice: unsettlingly upbeat internal comms —
# sinister-but-premium acid-green megacorp, PG throughout. Compliance is its own reward.

## Contracts Board — examine listing (the M1 browse surface)

solreign-contracts-board-examine-header = [color=#9acd32]SOLREIGN WORK ORDERS — Compliance is its own reward.[/color]
solreign-contracts-board-examine-empty = All contracts are fulfilled. Leadership is thrilled. Leadership is watching.
solreign-contracts-board-examine-entry = [color=#9acd32]{$id}[/color] {$name} — +{$standing} Standing — {$status}
solreign-contracts-status-open = OPEN. Alt-click to claim.
solreign-contracts-status-claimed = assigned to {$claimant} ({$done}/{$total} received)
solreign-contracts-status-recruiting = SALVAGE RAID — recruiting, {$count} { $count ->
        [one] asset
       *[other] assets
    } registered. Alt-click to join, then launch.
solreign-contracts-status-raid-launched = SALVAGE RAID underway, {$count} assets ({$done}/{$total} received)

## Contracts Board — verbs

solreign-contracts-verb-claim = Claim work order {$id}: {$name}
solreign-contracts-verb-join = Join salvage raid {$id}: {$name}
solreign-contracts-verb-launch = Launch salvage raid {$id}: {$name}
solreign-contracts-verb-skip = Decline {$id}: {$name}

## Contracts Board — popups

solreign-contracts-popup-claimed = Work order {$id} is yours. The Company has logged your enthusiasm.
solreign-contracts-popup-claim-taken = That work order is already assigned. Initiative noted. Twice.
solreign-contracts-popup-claim-rank = This opportunity is reserved for more senior assets. Keep climbing.
solreign-contracts-popup-claim-cap = You hold {$cap} active work orders. Delegation is a leadership skill.
solreign-contracts-popup-raid-joined = Registered on raid {$id}. Crew size: {$count}. Synergy detected.
solreign-contracts-popup-raid-already = You are already on this roster. Your commitment has been noted once.
solreign-contracts-popup-raid-full = This raid roster is at capacity. Consider founding your own synergy.
solreign-contracts-popup-raid-closed = This raid is no longer recruiting.
solreign-contracts-popup-raid-not-member = Only registered crew may launch a raid. Register first. Forms matter.
solreign-contracts-popup-raid-launched = Raid {$id} is GO with {$count} assets. Quota scaled. Payout scaled: +{$standing} Standing each. Bring it all back.
solreign-contracts-popup-skip-cooldown = Contract reassignment is rate-limited. Patience is a metric.
solreign-contracts-popup-skip-access = Your credentials cannot decline this contract. The contract remains optimistic.
solreign-contracts-popup-skip-not-yours = That work order belongs to a colleague. Eyes on your own quarterlies.
solreign-contracts-popup-skipped = Work order {$id} has been archived as "strategically deprioritized."

## P3.2 OTHER SILENT DROPS — contracts no-mind fall-throughs (audit fix 3 of 4). The Deny()
## helper covers ~10 popup paths across claim/join/raid/skip, but the no-mind branches
## (TryGetUser failures) all fall through to bare `return` — a player who pressed Claim
## while mid-ghost got nothing. One fallback key covers all three call sites (claim/join/skip).

solreign-contracts-popup-no-mind = The Company cannot locate your account on this terminal. Step away from the console and try again from a regular crew body.

## Contracts Board — client window (Milestone 2 BUI)

solreign-contracts-window-title = SOLREIGN WORK ORDERS
solreign-contracts-window-empty = All contracts are fulfilled. Leadership is thrilled. Leadership is watching.
solreign-contracts-window-flavor-left = Compliance is its own reward.
solreign-contracts-window-flavor-right = Solreign Fulfillment Terminal v2.4 — smiling since install
solreign-contracts-ui-claim-button = Claim
solreign-contracts-ui-join-button = Join
solreign-contracts-ui-launch-button = Launch
solreign-contracts-ui-skip-button = Decline
solreign-contracts-ui-title-label = [bold]{$name}[/bold]
solreign-contracts-ui-id-label = [color=#9acd32]{$id}[/color]
solreign-contracts-ui-reward-label = Compensation: [color=#9acd32]+{$standing} Standing[/color] per participating asset
solreign-contracts-ui-manifest-label = Deliverables: {$items}
solreign-contracts-ui-manifest-entry = {$item} ({$done}/{$total})
solreign-contracts-ui-description-label = {$description}
solreign-contracts-ui-status-open = [color=#9acd32]OPEN[/color] — awaiting one motivated asset.
solreign-contracts-ui-status-claimed = Assigned to {$claimant}. Their enthusiasm has been logged.
solreign-contracts-ui-status-recruiting = [color=#9acd32]SALVAGE RAID[/color] — recruiting, {$count} { $count ->
        [one] asset
       *[other] assets
    } registered. Quota and payout scale with the crew.
solreign-contracts-ui-status-launched = [color=#9acd32]SALVAGE RAID[/color] underway — {$count} { $count ->
        [one] asset
       *[other] assets
    } committed. Bring it all back.

## Fulfillment Dropbox — popups

solreign-contracts-popup-deposit-accepted = Received: {$item} ({$done}/{$total}). The Company thanks you for your compliance.
solreign-contracts-popup-deposit-rejected = This item matches none of your open work orders. It is, however, a lovely item.
solreign-contracts-popup-contract-complete = Work order {$id} FULFILLED. +{$standing} Corporate Standing. Your file grows warmer.
solreign-contracts-popup-raid-payout = Raid {$id} FULFILLED by a teammate. +{$standing} Corporate Standing, credited without you lifting a finger this time. Synergy.

## UX-SIMPLE FIX 4 — reason text fed into the shared SolreignAwardPopup helper (produces
## "+N Standing — [reason]"), replacing the two bespoke strings above at their call sites.
solreign-contracts-award-reason-complete = Work order {$id} FULFILLED. Your file grows warmer.
solreign-contracts-award-reason-raid = Raid {$id} FULFILLED by a teammate. Synergy.

## Printed work order (paper manifest)

solreign-contracts-work-order-header = [font size=14][bold]SOLREIGN WORK ORDER[/bold][/font] ({$id})
solreign-contracts-work-order-title = [bold]{$name}[/bold]
solreign-contracts-work-order-list-start = Deliverables (deposit in any Fulfillment Dropbox):
solreign-contracts-work-order-entry = {$amount}x {$item}
solreign-contracts-work-order-reward = Compensation: +{$standing} Corporate Standing per participating asset.
solreign-contracts-work-order-footer = [italic]Compliance is its own reward. This document loves you.[/italic]

## Contract names, descriptions and deliverable names (launch content, spec §9)

solreign-contract-cola-audit-name = Beverage Compliance Audit
solreign-contract-cola-audit-desc = Quarterly metrics indicate a 12% shortfall in refreshment throughput. Deposit 5 units of cola. Hydrate the numbers. The numbers are thirsty.
solreign-contract-item-cola = cola

solreign-contract-pen-recovery-name = Asset Recovery Initiative: Writing Implements
solreign-contract-pen-recovery-desc = Company pens keep "walking away." Pens do not have legs. This has been escalated. Return 10 pens to the chute and no further questions will be asked (about the pens).
solreign-contract-item-pen = pen

solreign-contract-mandatory-fun-name = Mandatory Fun Initiative, Phase 1
solreign-contract-mandatory-fun-desc = Morale is at 61%. Target: 62%. Deposit one (1) plush toy for redistribution to a workstation of the Company's choosing. Fun will be had. This is a directive.
solreign-contract-item-plush = plush toy

solreign-contract-signage-1-name = Rival Signage Remediation
solreign-contract-signage-1-desc = Unauthorized Nanotrasen materials have appeared in OUR hallways. Remove 3 posters and deposit them for "archival." Smile while you do it. Cameras appreciate smiles.
solreign-contract-signage-2-name = CONTINUITY: Rival Signage Remediation, Phase 2
solreign-contract-signage-2-desc = The posters came back. This is now personal (professionally). Remove 5 more and deposit them for "aggressive archival."
solreign-contract-item-rival-poster = archived rival poster

solreign-contract-facilities-sweep-name = Facilities Excellence Sweep
solreign-contract-facilities-sweep-desc = Somewhere, a floor tile is imperfect. This keeps Leadership up at night. Deposit 10 floor tiles, condition: gleaming, so Facilities can achieve Excellence (mandatory).
solreign-contract-item-floor-tile = floor tile

solreign-contract-incident-report-name = Incident Documentation Protocol
solreign-contract-incident-report-desc = An Incident occurred. The Company requires one written report for the newsletter. The newsletter is called "Everything Is Fine."
solreign-contract-item-incident-report = incident report (any paperwork accepted)

solreign-contract-nutrition-audit-name = Preventative Nutrition Audit
solreign-contract-nutrition-audit-desc = Legal has advised that employees require vitamins. Deposit 10 units of fruit so Medical can confirm fruit still works. Findings will be celebrated quietly.
solreign-contract-item-fruit = fruit

solreign-contract-botany-yield-name = Quarterly Botanical Yield Review
solreign-contract-botany-yield-desc = The Board has seen a picture of wheat and would like more. Deposit 15 wheat bundles. Growth is not optional; it is quarterly.
solreign-contract-item-wheat = wheat bundle

solreign-contract-onboarding-1-name = Onboarding Excellence Program
solreign-contract-onboarding-1-desc = Welcome, Asset! Your first deliverable: one (1) donut, unbitten, as tribute— as *training*. As training.
solreign-contract-onboarding-2-name = CONTINUITY: Onboarding Excellence, Step 2
solreign-contract-onboarding-2-desc = File one (1) signed onboarding form. Any paper is a form if you believe in it. The Company believes in you.
solreign-contract-onboarding-3-name = CONTINUITY: Onboarding Excellence, Final Step
solreign-contract-onboarding-3-desc = Surrender two (2) training pens to prove you no longer need training. You are now 4% less probationary.
solreign-contract-item-donut = donut (unbitten)
solreign-contract-item-onboarding-form = signed onboarding form
solreign-contract-item-training-pen = training pen

solreign-contract-executive-lunch-name = Executive Lunch Procurement
solreign-contract-executive-lunch-desc = A Director is having A Day. Assemble the Recovery Meal: one burger, one donut, one cake. Speed is appreciated. Discretion is required. The cake is a business expense.
solreign-contract-item-burger = burger
solreign-contract-item-cake = cake

## Salvage raids (group-scaled team-ups — objectives and per-head payout scale with the crew)

solreign-raid-scrap-reclamation-name = Deep Void Equity Reclamation
solreign-raid-scrap-reclamation-desc = The void is holding Company property and has ignored three invoices. Assemble a crew, launch the raid, and bring back the scrap. Quota scales with crew size. So does the payout. Synergy!
solreign-contract-item-scrap = salvaged scrap

solreign-raid-ore-futures-name = Mineral Futures Acceleration Program
solreign-raid-ore-futures-desc = The Board bought ore futures. The ore does not know this yet. Register a crew, launch, and deposit raw ore until the futures become presents.
solreign-contract-item-ore = raw ore

solreign-raid-waste-revaluation-name = Waste Stream Revaluation Sprint
solreign-raid-waste-revaluation-desc = One asset's trash is the Company's underperforming asset class. Crew up, sweep the halls, and deposit litter for revaluation. Cleanliness is a team sport with a scoreboard.
solreign-contract-item-trash = reclaimed litter

## Executive Vendor (Contracts M4, "the ladder buys things" — rank-gated vending machine)

solreign-executive-vendor-popup-denied = ACCESS DECLINED. This vendor is reserved for more senior assets. Keep climbing.
solreign-executive-vendor-stamp-name = EXECUTIVE APPROVED

## Contracts Streak (v14 low-pop quest-board extension, spec §3 — consecutive-shift favor, gated by
## CCVars.SolreignContractsQuestBoardEnabled). Same escalating-deadpan voice as the Directives Fax
## compliance streak.

solreign-contracts-streak-milestone-3 = Work Order Streak: 3 Shifts. Noted.
solreign-contracts-streak-milestone-5 = Work Order Streak: 5 Shifts. Head Office has begun a file.
solreign-contracts-streak-milestone-10 = Work Order Streak: 10 Shifts. The file has a tab now.
solreign-contracts-streak-milestone-25 = Work Order Streak: 25 Shifts. You have been externally benchmarked.
solreign-contracts-streak-milestone-50 = Work Order Streak: 50 Shifts. Head Office would like you to know it is watching, warmly.
solreign-contracts-streak-milestone-chat = PROVIDENCE COMPLIANCE NOTICE: { $reason }
