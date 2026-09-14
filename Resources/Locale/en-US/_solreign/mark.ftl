# "The Mark" copy pack (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §8, VERBATIM), wave MG-W2.
# Key contract lives in Content.Server/_Solreign/PlayerDelight/Mark/MarkCopy.cs; both directions
# (key existence + no orphans) plus the closed variable vocabulary {$name $kind $cm $days} and the
# no-$name-on-public-surfaces denylist are enforced by Content.Tests/_Solreign/MarkCopyTests.cs.
# $name renders ONLY in the C3 owner-examine suffixes (rail 5 — the character name never appears
# on any public surface). PG-13, corporate-sinister deadpan throughout.

# --- §8A stage examine lines (PUBLIC: kind + stage only, no variables) ------------------------

solreign-mark-stage-sapling-0 = A bare sapling in a company pot. The soil is still stamped with tray marks.
solreign-mark-stage-sapling-1 = New leaves have unfurled. Someone has been keeping the soil damp.
solreign-mark-stage-sapling-2 = The stem is thicker; growth rings show in the bark like quiet attendance.
solreign-mark-stage-sapling-3 = A small tree now. It looks permanent. The pot does not.

solreign-mark-stage-lamp-0 = A new garden lamp, cold white, still sealed at the base.
solreign-mark-stage-lamp-1 = The light has warmed a shade; dust has not yet found the glass.
solreign-mark-stage-lamp-2 = A steady amber cast. It looks like it has kept watch through several shifts.
solreign-mark-stage-lamp-3 = Soft, settled light. The kind that implies someone is expected back.

solreign-mark-stage-plate-0 = A new brass plate, edges sharp, finish still factory-bright.
solreign-mark-stage-plate-1 = Finger-polish has begun; the surface softens under repeated notice.
solreign-mark-stage-plate-2 = A warm sheen from years of glances. The plate has learned to be looked at.
solreign-mark-stage-plate-3 = Deep, honest patina. It reads less like inventory and more like a permanent record.

# --- Kind display words (render $kind in generic templates) -----------------------------------

solreign-mark-kind-sapling = sapling
solreign-mark-kind-lamp = lamp
solreign-mark-kind-plate = name-plate

# --- Planting verbs (spec §2: the three fixed alt-click choices) ------------------------------

solreign-mark-verb-sapling = Plant a sapling
solreign-mark-verb-lamp = Light a lamp
solreign-mark-verb-plate = Seat a name-plate

# --- C1 placement confirmations (private, at plant) -------------------------------------------

solreign-mark-plant-confirm-generic-1 = Recorded. Your {$kind} is now part of the garden's continuity ledger. Try not to die before it matters.
solreign-mark-plant-confirm-generic-2 = Placement accepted. The object is fixed, catalogued, and already older than your doubts.
solreign-mark-plant-confirm-sapling = Your sapling is planted. Growth will be measured. Neglect will be… managed.
solreign-mark-plant-confirm-lamp = Your lamp is lit and locked to its fixture. Brightness accrues with time, not with requests.
solreign-mark-plant-confirm-plate = Your name-plate is seated. It will not take custom text. It will take age.

# --- The already-claimed quiet refusal (private; W2 addition, spec §2's "already claimed" line) -

solreign-mark-plant-already = The ledger shows one mark for you already. One is the number. It is doing fine.

# --- C2 return-visit lines (private, on stage advance; kind-true unit words) — wired by MG-W4 --

solreign-mark-return-sapling = Your sapling has grown {$cm} cm. I have been watering it. You are welcome.
solreign-mark-return-lamp = Your lamp is up {$cm} lumens. I have been keeping the circuit honest. You are welcome.
solreign-mark-return-plate = Your name-plate gained {$cm} sheen. I have been polishing the record. You are welcome.
solreign-mark-return-generic-1 = Your {$kind} advanced {$cm} since last contact. {$days} days of quiet work. I kept the books.
solreign-mark-return-generic-2 = Welcome back. Your {$kind} is {$cm} further along after {$days} days. Continuity is a mutual project.

# --- C3 owner-examine suffixes (the ONLY $name surfaces in the pack) --------------------------

solreign-mark-examine-owner-1 = This one is yours, {$name}. The ledger agrees.
solreign-mark-examine-owner-2 = Registered to {$name}. You may look as long as you like. Leaving is optional; returning is encouraged.

# --- C4 stranger-examine suffixes (PUBLIC: no variables, no name) -----------------------------

solreign-mark-examine-stranger-1 = A crew mark. Not yours. Hands off; history is load-bearing.
solreign-mark-examine-stranger-2 = Employee Continuity asset. Owned. Observed. Not available for reassignment.

# --- C5 the garden fixture (PUBLIC; mirrored inline on the SolreignMarkGarden prototype) -------

solreign-mark-garden-name = Continuity Garden
solreign-mark-garden-desc = Company exhibit for long-horizon personnel retention. Fixed fixtures only. Objects here outlive rounds, excuses, and several middle managers. Do not rearrange. Do not ask for Task 4.

# --- C6 overflow (record exists, no physical slot this round; private) — wired by MG-W4 --------

solreign-mark-overflow-1 = Your mark is on file. Garden capacity is full this cycle; the object will resume physical duty when a slot opens.
solreign-mark-overflow-2 = Continuity preserved in the ledger. Visual representation deferred for space — not for lack of care.

# --- C7 the once-ever first-return nudge (private) — wired by MG-W4 ---------------------------

solreign-mark-nudge-1 = If you have a moment: the Continuity Garden still has your {$kind}. No requirement. Just a fact.
solreign-mark-nudge-2 = Personnel note: something you left is still growing. The garden does not issue summons. It issues… options.
