# Solreign onboarding — guide entry name for FirstShift.xml
# (Resources/Prototypes/_Solreign/Guidebook/FirstShift.yml). Guide names are LocId-required,
# so this key must exist. Signage flavor text (Resources/Prototypes/_Solreign/Entities/signage.yml)
# is authored inline on Name/Description per the fork's existing entity convention and needs no
# Loc entry of its own — see that file's header comment for why.

guide-entry-firstshift = Your First Shift at Solreign

# First Assignment interface. These keys are intentionally colocated with the finite card deck.
first-shift-tab-title = First Assignment
wingmates-tab-title = Wingmates
first-shift-heading = PROVIDENCE Misfiled Orientation
first-shift-disabled = First Assignments are not available right now.
first-shift-idle-intro = Choose a department for one private, harmless orientation card.
first-shift-suggested-department = Suggested from your current assignment: { $department }
first-shift-department-label = Department
first-shift-department-engineering = Engineering
first-shift-department-medical = Medical
first-shift-department-science = Science
first-shift-department-cargo = Cargo
first-shift-department-service = Service
first-shift-department-universal = General orientation
first-shift-start = Start assignment
first-shift-card-why = Why: { $instruction }
first-shift-card-orient = Orient: { $instruction }
first-shift-card-try = Try: { $instruction }
first-shift-card-if-stuck = If stuck: { $instruction }
first-shift-card-safety-stop = Safety stop: { $instruction }
first-shift-providence-omen-heading = Optional PROVIDENCE paperwork
first-shift-plain-instructions = Plain instructions
first-shift-open-map = Open private marker
first-shift-skip = Skip this step
first-shift-another-card = Another card
first-shift-open-guide = Open guide
first-shift-find-crewmate = Find a crewmate
first-shift-ahelp = AHelp
first-shift-end = End assignment
first-shift-stage-next-orient = I am oriented
first-shift-stage-next-try = I found it
first-shift-stage-next-debrief = I tried it
first-shift-stage-next-mark = Leave a mark
first-shift-stage-complete = Complete assignment
# ALIVENESS P0 #4 — completion acknowledgment. The toast is deliberately terse; the handoff
# arrives as a private chat line (it outlives the popup) and points at the Contracts Board,
# the actual pop-1 economy spine. Never touches any card's observation-only safety text.
first-shift-complete-popup = Orientation logged.
first-shift-complete-handoff = PROVIDENCE: Orientation logged. The Company now considers you oriented. Your first paid work order is waiting on the Contracts Board — deliver it to the fulfillment dropbox and Standing follows.
first-shift-reroll-cooldown = Please wait a moment before choosing another card.

## P3.2 OTHER SILENT DROPS — first-shift reroll stale-snapshot feedback (audit fix 1 of 4).
## Reroll is the only "unlucky department" recourse the player has; when the click races a
## snapshot Publish the silent drop is indistinguishable from a broken button. This pops a
## one-shot stale-snapshot line specifically for the Reroll path; the other three
## (Advance/Complete/End) keep their silent drop per the audit's reasoning — the UI re-renders
## the same stage so the player has some signal, AND a popup on every click would be spam.

first-shift-reroll-stale-snapshot-popup = Your state has already updated. The next card will pick up on refresh.
first-shift-marker-unknown = station marker
first-shift-marker-unavailable = No private station marker is available. Return to Arrivals or use the guide.
first-shift-marker-arrivals-fallback = Department marker unavailable; private marker points to Arrivals: { $label }
first-shift-marker-department = Private department marker: { $label }
first-shift-marker-continuity-garden = Private marker: { $label }
first-shift-map-title = Private First Assignment marker
first-shift-map-arrivals-marker = Arrivals fallback
first-shift-map-department-marker = Assignment destination

# --- The Mark kishōtenketsu restructure (MARK-SPEC-2026-07-17-DRAFT.md §3.6/§8B, MG-W3) ---------
# The numbered task-list overview (presentation-only; Tasks 1-3 re-frame the existing card fields
# above, content untouched). 3 rows dormant, 5 once solreign.mark.enabled is live.
first-shift-task-1-orient = 1. Orient
first-shift-task-2-try = 2. Try
first-shift-task-3-debrief = 3. Debrief
first-shift-task-4-list = 4. [REASSIGNED]
first-shift-task-5-mark = 5. The Mark

# Task 4 — the permanent, non-interactive row (the *ten* beat: zero mechanics, the unbreakable
# joke — no system ever reads these lines). Row + deflection variants, deterministic per-round pick.
first-shift-task-4-heading = Task 4
first-shift-task4-row-1 = Task 4 has been… reassigned. Do not ask about Task 4.
first-shift-task4-row-2 = This item is no longer on your ticket. Do not ask about Task 4.
first-shift-task4-row-3 = Task 4 was reassigned before you arrived. Do not ask about Task 4.
first-shift-task4-deflection-1 = Task 4 is closed. Your curiosity has been noted and not actioned.
first-shift-task4-deflection-2 = That request falls outside your current compliance surface. Proceed to Task 5.
first-shift-task4-deflection-3 = I am not authorized to discuss Task 4 with you. I am authorized to change the subject.

# Task 5 — Leave a Mark (the *ketsu* beat: the PROVIDENCE brief, static, no variables).
first-shift-task-5-heading = Task 5 — Leave a Mark
first-shift-task-5-brief = The checklist was never the point. Shifts end. Hulls forget. Choose one object from the fixed menu and place it at the Continuity Garden. It will be waiting when you return — older, quieter, and still yours. Leave something that stays.

# First Assignment deck. Literal instructions remain separate from optional PROVIDENCE omens.
first-shift-card-engineering-threshold-title = Find Engineering
first-shift-card-engineering-threshold-why = Engineering keeps the station safe, powered, and breathable.
first-shift-card-engineering-threshold-orient = Follow your private marker to Engineering's public entrance.
first-shift-card-engineering-threshold-try = From the threshold, identify one department sign or public equipment label.
first-shift-card-engineering-threshold-stuck = Open the Engineering guide, request a crewmate, choose another card, or return to Arrivals.
first-shift-card-engineering-apc-title = Read an APC from the outside
first-shift-card-engineering-apc-why = APCs distribute power to a local area, and their exterior indicators help engineers understand it.
first-shift-card-engineering-apc-orient = Find an APC visible from an ordinary crew area near Engineering.
first-shift-card-engineering-apc-try = Observe its exterior label and indicator without opening or operating it.
first-shift-card-engineering-apc-stuck = Use the Engineering guide to see what an APC looks like, ask a crewmate, reroll, or return to Arrivals.
first-shift-card-engineering-tool-title = Match a tool to its label
first-shift-card-engineering-tool-why = Knowing common tools by sight makes future instructions easier to follow.
first-shift-card-engineering-tool-orient = Look for a public tool display, belt, or equipment label near Engineering.
first-shift-card-engineering-tool-try = Identify one ordinary tool by name; leave scarce or stored equipment where it is.
first-shift-card-engineering-tool-stuck = Open the Engineering guide, ask a crewmate to point out a common tool, reroll, or return to Arrivals.
first-shift-safety-engineering = Do not open equipment, touch wires, or change power or atmospherics. Observation is enough.

first-shift-card-medical-threshold-title = Find Medbay
first-shift-card-medical-threshold-why = Medical provides safe assessment and care when crewmembers are hurt.
first-shift-card-medical-threshold-orient = Follow your private marker to Medbay's public entrance.
first-shift-card-medical-threshold-try = Identify the reception area or one public department sign from the threshold.
first-shift-card-medical-threshold-stuck = Open the Medical guide, request a crewmate, choose another card, or return to Arrivals.
first-shift-card-medical-labels-title = Read an emergency-supply label
first-shift-card-medical-labels-why = Clear supply labels help crew find the right help during an emergency.
first-shift-card-medical-labels-orient = Find emergency supplies that are visible from a public Medbay area.
first-shift-card-medical-labels-try = Read one exterior label without removing or using its contents.
first-shift-card-medical-labels-stuck = Open the Medical guide, ask a crewmate where labels can be viewed safely, reroll, or return to Arrivals.
first-shift-card-medical-reference-title = Find the first-aid reference
first-shift-card-medical-reference-why = The Medical guide explains basic first aid before practical care is attempted.
first-shift-card-medical-reference-orient = Stay in a public Medbay area or any safe crew area.
first-shift-card-medical-reference-try = Open the Medical guide and locate its non-invasive first-aid section; no practical treatment is requested.
first-shift-card-medical-reference-stuck = Ask a crewmate to point out the guide section, choose another card, or return to Arrivals.
first-shift-safety-medical = Do not treat anyone, remove supplies, inject, or consume anything. Reading and observation are enough.

first-shift-card-science-threshold-title = Find Science
first-shift-card-science-threshold-why = Science studies unusual phenomena and develops useful station technology.
first-shift-card-science-threshold-orient = Follow your private marker to Science's public entrance.
first-shift-card-science-threshold-try = Identify one department sign or public-facing research label from the threshold.
first-shift-card-science-threshold-stuck = Open the Science guide, request a crewmate, choose another card, or return to Arrivals.
first-shift-card-science-inert-title = Observe an inert research object
first-shift-card-science-inert-why = Careful exterior observation comes before any scientific procedure.
first-shift-card-science-inert-orient = Find an inactive object or display visible from a public Science area.
first-shift-card-science-inert-try = Describe one exterior feature to yourself without activating, moving, or operating the object.
first-shift-card-science-inert-stuck = Open the Science guide, ask a crewmate for a safe example, reroll, or return to Arrivals.
first-shift-card-science-surface-title = Recognize a research surface
first-shift-card-science-surface-why = Recognizing work surfaces helps distinguish public orientation from active experiments.
first-shift-card-science-surface-orient = Look from a public Science threshold for a labeled console, desk, or research surface.
first-shift-card-science-surface-try = Identify what kind of surface it is using signs or the Science guide; no experiment is requested.
first-shift-card-science-surface-stuck = Open the Science guide, ask a crewmate to name a visible surface, reroll, or return to Arrivals.
first-shift-safety-science = Do not activate equipment, run experiments, or handle hazardous materials. Exterior observation is enough.

first-shift-card-cargo-threshold-title = Find Cargo
first-shift-card-cargo-threshold-why = Cargo receives deliveries and coordinates ordinary station supplies.
first-shift-card-cargo-threshold-orient = Follow your private marker to Cargo's public reception or bay entrance.
first-shift-card-cargo-threshold-try = Identify the reception counter, delivery area, or one public department sign.
first-shift-card-cargo-threshold-stuck = Open the Cargo guide, request a crewmate, choose another card, or return to Arrivals.
first-shift-card-cargo-appraisal-title = Make a harmless appraisal
first-shift-card-cargo-appraisal-why = Appraisal teaches how ordinary objects are described without changing station inventory.
first-shift-card-cargo-appraisal-orient = Choose a harmless item you already personally hold, or an ordinary public item that can stay in place.
first-shift-card-cargo-appraisal-try = Read its name and make a playful guess about its value without taking, selling, or moving it.
first-shift-card-cargo-appraisal-stuck = Open the Cargo guide, ask a crewmate for a harmless example, reroll, or return to Arrivals.
first-shift-card-cargo-delivery-title = Recognize the delivery surface
first-shift-card-cargo-delivery-why = Cargo's public delivery surfaces show where requests and parcels are coordinated.
first-shift-card-cargo-delivery-orient = Find a public delivery counter, request console, or contract display near Cargo.
first-shift-card-cargo-delivery-try = Identify the surface and read its exterior label without accepting, spending, or moving anything.
first-shift-card-cargo-delivery-stuck = Open the Cargo guide, ask a crewmate to identify the public surface, reroll, or return to Arrivals.
first-shift-safety-cargo = Do not accept orders, spend funds, or move station property. Observation and your own belongings are enough.

first-shift-card-service-threshold-title = Find Service
first-shift-card-service-threshold-why = Service keeps daily station life welcoming, supplied, and clean.
first-shift-card-service-threshold-orient = Follow your private marker to a public Service, Kitchen, or Bar entrance.
first-shift-card-service-threshold-try = Identify one department sign or public service counter from the threshold.
first-shift-card-service-threshold-stuck = Open the Service guide, request a crewmate, choose another card, or return to Arrivals.
first-shift-card-service-pantry-title = Read a pantry label
first-shift-card-service-pantry-why = Pantry labels help staff distinguish ready-to-eat items from ingredients that require preparation.
first-shift-card-service-pantry-orient = Find a food label visible in a public Service or Kitchen area.
first-shift-card-service-pantry-try = Identify one ready-to-eat label without heating, cutting, taking, or tasting the item.
first-shift-card-service-pantry-stuck = Open the Service guide, ask a crewmate to point out a label, reroll, or return to Arrivals.
first-shift-card-service-cleaning-title = Recognize a cleaning surface
first-shift-card-service-cleaning-why = Knowing where routine cleaning begins makes it easier to ask for the right supplies safely.
first-shift-card-service-cleaning-orient = Find a public sink, cleaning sign, or janitorial surface without entering a restricted room.
first-shift-card-service-cleaning-try = Identify the surface and leave it unchanged; no spill or chemical use is requested.
first-shift-card-service-cleaning-stuck = Open the Service guide, ask a crewmate to name a safe surface, reroll, or return to Arrivals.
first-shift-safety-service = Do not use heat, knives, chemicals, or consume anything. Labels and observation are enough.

first-shift-card-universal-title = Reorient at Arrivals
first-shift-card-universal-why = Arrivals is a dependable place to regain your bearings and choose what comes next.
first-shift-card-universal-orient = Follow your private marker back to Arrivals and locate the station map or a public sign.
first-shift-card-universal-try = Open the New Player guide, or optionally greet one nearby crewmember.
first-shift-card-universal-stuck = Stay at Arrivals, request a crewmate, choose another card, or use AHelp when you need staff assistance.
first-shift-safety-universal = Do not enter restricted areas, handle weapons, make arrests, investigate antagonists, or leave the station. Staying at Arrivals is always valid.

# Curated deterministic PROVIDENCE omens. These never replace literal instructions.
first-shift-omen-engineering-1 = PROVIDENCE requests confirmation that gravity remains directionally compliant.
first-shift-omen-engineering-2 = Auxiliary paperwork predicts a heroic future for one correctly identified label.
first-shift-omen-engineering-3 = The station has provisionally approved the continued existence of corners.
first-shift-omen-medical-1 = PROVIDENCE reminds all organs to remain within their assigned administrative region.
first-shift-omen-medical-2 = Today is an excellent day for supplies to remain clearly labeled.
first-shift-omen-medical-3 = Bedside manner form 7-B may be completed entirely through respectful observation.
first-shift-omen-science-1 = The hypothesis has been peer reviewed by three unusually confident clipboards.
first-shift-omen-science-2 = PROVIDENCE reports that causality is operating within acceptable tolerances.
first-shift-omen-science-3 = An inactive machine is still achieving several important bureaucratic outcomes.
first-shift-omen-cargo-1 = PROVIDENCE has assigned this parcel a value between sentimental and adequately taped.
first-shift-omen-cargo-2 = The quarterly logistics forecast calls for a 12 percent increase in pointing at labels.
first-shift-omen-cargo-3 = No crate has filed an objection to remaining exactly where it is.
first-shift-omen-service-1 = Hospitality metrics improve whenever a counter is correctly recognized.
first-shift-omen-service-2 = PROVIDENCE certifies this pantry as theoretically capable of containing lunch.
first-shift-omen-service-3 = Cleanliness paperwork is already 40 percent cleaner than the previous paperwork.
first-shift-omen-universal-1 = Arrivals has once again arrived exactly where records predicted.
first-shift-omen-universal-2 = PROVIDENCE welcomes all crew, including those currently consulting a map.
first-shift-omen-universal-3 = Orientation remains valid even when performed in a tasteful circle.

# --- First-spawn push prompt (FirstShiftSpawnPromptSystem, 2026-08-02): the one private pointer
#     a brand-new arrival gets toward the First Shift beacon. Popup is the glanceable line; chat is
#     the screenshot-surviving copy with the actual instruction.
solreign-first-shift-spawn-prompt-popup = PROVIDENCE has prepared your orientation.
solreign-first-shift-spawn-prompt-chat = PROVIDENCE: Welcome aboard. Find the glowing First Shift beacon near Arrivals and press it. Choose a department, and PROVIDENCE will prepare one private, harmless starter assignment.
