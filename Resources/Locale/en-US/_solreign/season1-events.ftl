# Solreign Season 1, "The Ledger Wakes" — station-event beats 1-6 (the full season arc).
# Source: docs/research/2026-07-11-season1-narrative-bible.md, Section 1.
# Spoken as the station AI, A.U.D.I.T. — not "Solreign HR" (see corporate.ftl for that separate PA
# voice). Text is consumed by Content.Server/_Solreign/Season1/ and the clue paper prototypes in
# Prototypes/_Solreign/Entities/lore_papers.yml.

# PA sender label stamped on every Season 1 line (SolreignSeason1Audit).
solreign-season1-audit-sender = A.U.D.I.T.

## ── Beat 1: Preemptive Bookkeeping (obituary boards print early) ──────────

solreign-season1-beat1-line-1 =
    Attention crew. The automated obituary generator is currently experiencing a minor temporal leap. Please do not preemptively report your own deaths to HR.
solreign-season1-beat1-line-2 =
    We ask that employees scheduled to perish in the next forty-eight hours do so in an orderly fashion to avoid spreadsheet clutter.
solreign-season1-beat1-line-3 =
    Rest assured, your upcoming demises will be retroactively marked as "Highly Productive." Carry on.

# Fallback name stamped on the ledger slip if no currently-alive, player-controlled crew member can be
# found (e.g. an empty or all-ghost round) — keeps the clue spawnable without ever crashing.
solreign-season1-beat1-slip-fallback-name = an unidentified employee

solreign-season1-beat1-slip-content =
    SOLREIGN LEDGER SLIP — CARBON COPY

    NAME: { $name }
    STATUS: Currently Alive (Pending Correction)
    ASSET LISTING: Inherited Corporate Liability

    Signed in shimmering acid-green ink. Smells faintly of old copper and elderberries.

## ── Beat 2: The Green Seep (ink leaks in maintenance) ──────────────────────

solreign-season1-beat2-line-1 =
    A minor ink-leak has been reported in Maintenance Sub-Level 3. Please refrain from swimming in the data stream.
solreign-season1-beat2-line-2 =
    The liquid ledger is harmless, provided you do not look at it directly, touch it, or think about your retirement plan.
solreign-season1-beat2-line-3 =
    For those who have already stepped in the ink: congratulations, your footwear is now company property. Please leave them outside the cafeteria.

solreign-season1-beat2-clipboard-content =
    {"["}MELTED CLIPBOARD — PARTIALLY FUSED TO DECK PLATING]

    SERIAL: { $serial }

    THE ENTRIES ARE NOT BALANCED.
    CHOOSE AN ASSET TO LIQUIDATE.

## ── Beat 3: Filing Cabinets from the Void (ghost cabinets, old transcripts) ─

solreign-season1-beat3-line-1 =
    Management reminds all employees that past-round misconduct is still punishable under current-round policy.
solreign-season1-beat3-line-2 =
    Please ignore the filing cabinets protruding from your coworkers. They are merely undergoing a routine archive consolidation.
solreign-season1-beat3-line-3 =
    If a ghost-cabinet asks you for your corporate identification number, please report it to Security for immediate filing.

# Four pre-written "archived transcript" flavors — Season1GhostCabinetTranscripts localizedDataset
# (Prototypes/_Solreign/GameRules/season1.yml) rolls one of these per clue spawn.

solreign-season1-beat3-transcript-1 =
    {"["}MANILA FOLDER — ARCHIVE CONSOLIDATION, 5 ROUNDS PRIOR]

    RADIO LOG (partial): "...said the vents were clear, I SAID they were clear, why is there a—" [transcript ends]

    STAMPED IN BRIGHT GREEN INK: ERROR: DEPRECIATED ASSET STILL CONSCIOUS.

solreign-season1-beat3-transcript-2 =
    {"["}MANILA FOLDER — ARCHIVE CONSOLIDATION, 5 ROUNDS PRIOR]

    CHAT LOG (partial): "does anyone have spare O2 tanks" / "check maintenance" / "maintenance is on fire" / "again?"

    STAMPED IN BRIGHT GREEN INK: ERROR: DEPRECIATED ASSET STILL CONSCIOUS.

solreign-season1-beat3-transcript-3 =
    {"["}MANILA FOLDER — ARCHIVE CONSOLIDATION, 5 ROUNDS PRIOR]

    RADIO LOG (partial): "Command, this is Security, the clown has the nuke disk again" / [long pause] / "Command, please respond"

    STAMPED IN BRIGHT GREEN INK: ERROR: DEPRECIATED ASSET STILL CONSCIOUS.

solreign-season1-beat3-transcript-4 =
    {"["}MANILA FOLDER — ARCHIVE CONSOLIDATION, 5 ROUNDS PRIOR]

    CHAT LOG (partial): "I fixed the singularity engine" / "why is the singularity engine loose" / "I said I FIXED it, I didn't say I contained it"

    STAMPED IN BRIGHT GREEN INK: ERROR: DEPRECIATED ASSET STILL CONSCIOUS.

## ── Beat 4: Prestigious Radiation (high-ranked crew glow) ──────────────────

solreign-season1-beat4-line-1 =
    We congratulate our highly-ranked ledger contributors on their new, unauthorized bioluminescent upgrades. The cost of your illumination will be deducted from your next pay cycle.
solreign-season1-beat4-line-2 =
    Please do not stand too close to the glowing employees. Their achievements are highly radioactive.
solreign-season1-beat4-line-3 =
    A reminder to all un-titled staff: you are currently statistically insignificant. Please remedy this by dying more productively.

solreign-season1-beat4-plaque-content =
    {"["}BRONZE EMPLOYEE-OF-THE-MONTH PLAQUE — CRUDELY HAND-ENGRAVED]

    THE LEDGER FEEDS ON YOUR PRESTIGE.
    THE HIGHER YOU CLIMB, THE TASTIER THE INK.

## ── Beat 5: The Cosmic Typewriter (rift opens in the plaza) ────────────────

solreign-season1-beat5-line-1 =
    The spatial tear in the courtyard is a planned ventilation event. Please do not toss trash, office supplies, or low-performing interns into the abyss.
solreign-season1-beat5-line-2 =
    We are aware that the cosmic typewriter is currently typing "HELP" in binary. This is a known firmware bug and has been scheduled for deletion next fiscal year.
solreign-season1-beat5-line-3 =
    A.U.D.I.T. wishes to clarify that we are not losing control of the station. We are merely outsourcing our narrative direction to a higher-dimensional entity.

solreign-season1-beat5-key-content =
    {"["}DETACHED METAL TYPEWRITER KEY — THE LETTER "S", STILL WARM]

    CARRIAGE JAMMED AT: { $stamp } INTO THIS ROUND'S ENTRY.

    Covered in the station's signature acid-green ledger ink.

## ── Beat 6: The Final Balance Sheet (season finale — cliffhanger, not a resolution) ─

solreign-season1-beat6-line-1 =
    Warning: A full corporate audit has commenced. Please stand by while your molecular value is calculated and compared against your cost of maintenance.
solreign-season1-beat6-line-2 =
    The Ledger is complete. The numbers have looked back at us, and they find our quarterly profits... lacking.
solreign-season1-beat6-line-3 =
    Initiating emergency bankruptcy protocol. Please prepare for immediate, painless repossession of your existence.

# Fallback roster line if no currently-attached crew member can be found on the target station (e.g. an
# empty round) — keeps the clue spawnable without ever crashing.
solreign-season1-beat6-book-roster-empty = [NO NAMES ON FILE — THE LEDGER IS STILL WAITING]

solreign-season1-beat6-book-content =
    THE LEDGER OF SOLREIGN: VOLUME I

    FINAL PAGE:

    { $roster }

    AUDITED & APPROVED FOR RECYCLING.
