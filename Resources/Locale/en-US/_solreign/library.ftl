# STATION-LIBRARY (wave-2 item) — the Station Archive ("PROVIDENCE Records Annex"), a bookshelf
# where players submit a written work that persists in the Season Ledger and re-materializes as a
# readable book every round (Content.Server/_Solreign/Library/SolreignLibrarySystem.cs). Voice is
# plain and archival — a librarian's placard, not a Directive surveillance surface. PG-13
# throughout (John's standing ruling).

## Verbs

solreign-library-verb-submit = Submit a Written Work
solreign-library-verb-report = Report to PROVIDENCE

## Submission window

solreign-library-window-title = STATION ARCHIVE — SUBMISSION
solreign-library-window-title-label = Title
solreign-library-window-body-label = Text
solreign-library-window-hint = Submitted works are screened, then bound and shelved for the rest of the season. There's no time limit on how long a work stays — reporting one takes it down immediately.
solreign-library-ui-submit-button = Submit
solreign-library-ui-char-count = {$current}/{$max}

## Submit outcomes (server-driven popups)

solreign-library-submit-success-popup = Your work has been submitted for binding.

## The honest, non-lore "the daemon is not answering" outcome — channel unready, or the
## classifier round trip itself failed/timed out/was unverifiable. Distinct from a genuine
## classifier rejection below.
solreign-library-not-accepting-popup = The Archive is not accepting submissions right now.

## A classifier rejection. Generic and private on purpose — never echoes the submitted title or
## text, never states the specific reason.
solreign-library-submit-withheld-popup = This submission could not be accepted.

solreign-library-quota-popup = You've already submitted a work this round. The Archive accepts one per author per round.

## Rate-limit deny on submit — never left the station, distinct wording from the daemon-didn't-
## answer copy above so a busy Archive never reads as an outage.
solreign-library-submit-busy-popup = The Archive is processing an earlier submission. Try again in a moment.

## Report — the one-tap containment action. Quiet, no drama, no confirmation of outcome beyond
## "it's handled."
solreign-library-report-confirm-popup = Reported. The work is withdrawn from the shelf.

## Book flavor (spawned book item description, PaperSystem content is the work's own body)

solreign-library-book-description = Bound in the Station Archive. Credited to {$author}.

## PROVIDENCE-authored seed works (this lane's design brief §4): every seed still passes the same
## fail-closed classifier round trip as a player work — authoring them in-house does not exempt
## them (defense in depth). None reference a specific player, report, or moderation decision.

solreign-library-seed-handbook-title = SOLREIGN Employee Handbook — Page 3
solreign-library-seed-author-hr = PROVIDENCE ARCHIVES — Solreign Human Resources

solreign-library-seed-safety-title = The Pre-Shift Safety Scroll
solreign-library-seed-author-safety = PROVIDENCE ARCHIVES — Solreign Safety Office
solreign-library-seed-safety-body =
    THE PRE-SHIFT SAFETY SCROLL
    (unroll before duty; reroll after)

    Article the First: a suit is not a substitute for a door. Check both before trusting either.

    Article the Second: fire is a chemical process that would like very much to become a
    bigger chemical process. Do not encourage it. Extinguishers are found near "IN CASE OF
    FIRE" signage, which Solreign Corporate regrets was, for one printing run in 2019, itself
    printed on flammable paper. That batch has been recalled. Mostly.

    Article the Third: atmospheric alarms are not background music. If your suit begins
    beeping in a rhythm you find catchy, you are already breathing vacuum and should stop
    enjoying it.

    Article the Fourth: the crowbar is a tool, a lever, and, in one still-unresolved 1998
    incident, a named godparent. Treat it with proportional respect.

    Article the Fifth, and Final: should this scroll ever catch fire, refer to Article the
    Second. Should this scroll ever catch atmosphere, refer to Article the Third. Should this
    scroll ever achieve sentience, refer it to Human Resources, who have a form for that.

    — filed under Solreign Safety Office, undated, laminated twice

solreign-library-seed-poetry-title = Sonnets for Compliance: A Chapbook
solreign-library-seed-author-compliance = PROVIDENCE ARCHIVES — Solreign Compliance Division
solreign-library-seed-poetry-body =
    SONNETS FOR COMPLIANCE
    a chapbook, Solreign Compliance Division, author withheld pending Form 12-C review

    I.
    Oh Form 12-C, in triplicate you bloom,
    one white, one yellow, one a ghostly blue.
    I filled you out alone, in this small room,
    and somewhere, someone filed you. Is that you?

    II.
    The quota does not love you back, and yet
    each quarter still you meet it, mostly, near.
    Compliance is a debt we never let
    ourselves forget we somehow volunteer.

    III.
    To the Auditor, Unmet
    You came, you counted, and you left a note.
    I do not know your name. I know your hand.
    Somewhere a ledger balances by rote,
    and somewhere, briefly, someone understands.

    — bound here by request of the Compliance Division, who insist this counts as documentation

solreign-library-seed-opening-title = Notice: The Annex Opens
solreign-library-seed-author-command = PROVIDENCE ARCHIVES — Station Command
solreign-library-seed-opening-body =
    NOTICE: THE ANNEX OPENS

    This shelf exists so that what is written here outlives the round it was written in.

    Everything else on this station resets, restocks, or is quietly swept up by the next
    shift. This is the one exception, by design. Write something worth keeping, or write
    something silly worth keeping anyway — both belong here in equal standing.

    The Archive does not judge what is submitted beyond the ordinary screening every written
    word on this station already passes through. It only remembers.

    — Station Command, on the occasion of the Annex's first shelving

## libraryunhide console command (moderator-only correction tool)

cmd-libraryunhide-desc = Reverses a hidden library work (correcting a bad-faith report).
cmd-libraryunhide-help = Usage: libraryunhide <workId>
cmd-libraryunhide-usage = Usage: libraryunhide <workId>
cmd-libraryunhide-success = Work {$id} unhidden.
cmd-libraryunhide-notfound = Work {$id} was not hidden or does not exist.
cmd-libraryunhide-error = Could not unhide work: {$error}
