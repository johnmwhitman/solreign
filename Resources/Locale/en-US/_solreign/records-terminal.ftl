# Personnel Records Terminal copy pack (wave-2 item einstein-016, mine rank #8). CLOSED template
# rendering: every string a player can see from this console is one of the fixed templates below,
# or an already-shipped key reused verbatim from another Season Ledger copy pack (the social-first
# "-reason" milestone names in social-cheap-adds.ftl; the Mark kind word and stage examine line in
# mark.ftl). House voice: corporate-sinister-but-warm, PG-13, deadpan throughout — Content restriction
# lifted 2026-07-17; ships dormant behind solreign.records_terminal.enabled (default FALSE).

solreign-records-terminal-window-title = PROVIDENCE Personnel Records
solreign-records-terminal-preamble = RETRIEVING YOUR FILE. The Company already knows. This is a courtesy.

solreign-records-terminal-section-title = LEDGER TITLE
solreign-records-terminal-section-tours = TOURS SERVED
solreign-records-terminal-section-standing = CAREER STANDING
solreign-records-terminal-section-social = SOCIAL FIRSTS
solreign-records-terminal-section-first-death = FIRST DEATH
solreign-records-terminal-section-mark = CONTINUITY GARDEN MARK

## Honest empty states — never a fabricated number or invented sentence.

solreign-records-terminal-social-none = None recorded yet.
solreign-records-terminal-first-death-on-file = Statement of Record: on file.
solreign-records-terminal-first-death-none = None. Keep it that way.
solreign-records-terminal-mark-none = No mark on file. The garden remembers who plants.

## The Mark line — $kind is MarkCopy's kind word (sapling/lamp/name-plate), $stage is the SAME
## stage-examine sentence the physical object already shows on examine (solreign-mark-stage-*),
## reused rather than duplicated so the terminal never contradicts the object in the world.

solreign-records-terminal-mark-line = A {$kind} of yours is on file. {$stage}

## Defensive fallback for an unexpected social-first flag id (should be unreachable — the plan only
## ever contains flags from RecordsTerminalRenderer's closed CelebratedFlagOrder).

solreign-records-terminal-social-first-unknown = An unlisted milestone is on file.

solreign-records-terminal-refresh-button = Refresh
