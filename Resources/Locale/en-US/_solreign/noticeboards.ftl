# Crew Noticeboards (v14 wave-1 #2; spec docs/council/2026-07-17-player-text-safety.md — the C2
# posture memo). Voice is plain and crew-facing, deliberately NOT the acid-green corporate voice
# of bounties.ftl/contracts.ftl — this is meant to read as an actual community bulletin board, not
# another Directive surveillance surface. PG-13 throughout (John's standing ruling).

## Client window

solreign-noticeboards-window-title = CREW NOTICEBOARD
solreign-noticeboards-window-empty = Nothing pinned right now.
solreign-noticeboards-window-offline = The board can't be read right now. Try again in a moment.
solreign-noticeboards-window-hint = Notices expire on their own. Reporting one takes it down immediately.
solreign-noticeboards-ui-submit-button = Pin Notice
solreign-noticeboards-ui-report-button = Report
solreign-noticeboards-ui-char-count = {$current}/{$max}

## PROVIDENCE-authored notices — the mandatory system-labeled marker (spec rule 5), never blended
## in as if a player wrote it.

solreign-noticeboards-providence-author = PROVIDENCE — SYSTEM NOTICE

## Submit outcomes (server-driven popups)

solreign-noticeboards-post-success-popup = Your notice is pinned to the board.

## Spec rule 2: the honest, non-lore "the daemon is not answering" outcome — channel unready, or
## the classifier round trip itself failed/timed out/was unverifiable. Distinct from a genuine
## classifier rejection below.
solreign-noticeboards-not-accepting-popup = The board is not accepting notices right now.

## Spec rule 2: a classifier rejection. Generic and private on purpose — never echoes the
## submitted text, never states the specific reason.
solreign-noticeboards-post-withheld-popup = This notice could not be posted.

solreign-noticeboards-quota-popup = You already have a notice pinned. It has to clear before you can pin another.
solreign-noticeboards-cooldown-popup = You pinned a notice too recently. Give it more time before pinning another.
solreign-noticeboards-full-popup = The board is full. Try again later.

## Rate-limit deny on submit — never left the station, distinct wording from the daemon-didn't-
## answer copy above so a busy board never reads as an outage.
solreign-noticeboards-post-busy-popup = The board is processing an earlier notice. Try again in a moment.

## Report — the one-tap containment action (spec rule 3). Quiet, no drama, no confirmation of
## outcome beyond "it's handled."
solreign-noticeboards-report-confirm-popup = Reported. The notice is down.

## PROVIDENCE seed lines (C1 council ask: boards ship seeded so they never look empty). Static,
# PG-13, never reference a specific player, report, moderation decision, or hidden note (spec rule
# 5's hard prohibition) — plain flavor text only. Still passes the same classifier round trip as
# any player note before it ever becomes visible.

solreign-noticeboards-seed-welcome = Welcome aboard. This board is for the crew — pin something, take something down, keep it useful.
solreign-noticeboards-seed-reminder = Reminder: notices come down on their own after a few days. Nothing here is permanent.

## noticeboardunhide console command (moderator-only correction tool)

cmd-noticeboardunhide-desc = Reverses a hidden noticeboard note (correcting a bad-faith report).
cmd-noticeboardunhide-help = Usage: noticeboardunhide <noteId>
cmd-noticeboardunhide-usage = Usage: noticeboardunhide <noteId>
cmd-noticeboardunhide-success = Note {$id} unhidden.
cmd-noticeboardunhide-notfound = Note {$id} was not hidden, does not exist, or has already expired.
cmd-noticeboardunhide-error = Could not unhide note: {$error}
