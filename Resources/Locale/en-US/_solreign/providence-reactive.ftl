# PROVIDENCE event-reactive copy pack (feat/providence-event-reactive, PROVIDENCE-VOICE-DESIGN.md).
# Unlike the timer-driven idle-musings voice pack, every line here is dispatched because something
# specific just happened (a death, a shift ending) — see ProvidenceEventReactiveSystem.
#
# CLOSED VOCABULARY, same discipline as first-death.ftl: death-generic lines take only $name; the
# repeat line additionally takes $count; the memory lines additionally take $cause/$title (both
# closed-vocabulary values sourced from the existing first-death record, never free text); round-end
# takes only $count. There is no $attacker anywhere. $name always arrives pre-sanitized through
# ProvidenceNameSanitizer before it ever reaches Loc.GetString.

## PA sender label for every event-reactive line.

solreign-providence-reactive-sender = PROVIDENCE

## Death, generic — no Ledger memory available, not yet a repeat pattern this shift.

solreign-providence-reactive-death-generic-1 = Asset { $name }'s death has been logged. The Ledger updates accordingly.
solreign-providence-reactive-death-generic-2 = Asset { $name } has entered non-productive status. Cause: pending review. Noted, as ever.

## Death, repeat — this account's third-or-later death THIS shift (no Ledger memory available).

solreign-providence-reactive-death-repeat = Asset { $name }'s death this shift — number { $count } — has been logged. A pattern is forming, and the Ledger has opinions about it.

## Death, Ledger memory — a genuinely prior-round first-death record exists for this account. "It
## never forgets," demonstrated rather than claimed.

solreign-providence-reactive-death-memory-1 = Asset { $name } has died again. The Ledger recalls the first time too — { $cause }, back when they held the title of { $title }. Some things it does not let go of.
solreign-providence-reactive-death-memory-2 = Asset { $name }'s file has been updated. This Ledger's earliest entry for this Asset was also a death — { $cause }. History, it seems, is a repeat offender.

## Round-end summary — the shift's death toll, once, as the round concludes.

solreign-providence-reactive-roundend-zero = Shift concluded. Zero Assets lost. Head Office finds this unusual, and is choosing not to investigate why.
solreign-providence-reactive-roundend-some = Shift concluded. { $count } Asset(s) did not survive to see it. The Ledger has updated accordingly.
