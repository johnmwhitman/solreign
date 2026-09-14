# Fast "report a player" quick-action (churn insight: self-antag needs FAST moderation tools or it
# burns the RP core). Distinct from ahelp/bwoink (no live conversation) and from the D2.1 structured
# feedback window (Content.Server._Solreign.Feedback -- that's for suggestions about the game, this is
# for a player's conduct).

cmd-report-help = Usage: report [target] — opens the "report a player" window (griefing / harassment / rules).

ui-escape-solreign-report = Report Player

report-window-title = Report Player
report-window-intro = Flag a player's conduct for staff. This goes straight into our moderation log with round context — never your chat. This is not ahelp; if you need urgent help right now, use the button below instead.
report-window-target-label = Player:
report-window-target-placeholder = Character or account name
report-window-category-label = Category:
report-category-griefing = Griefing
report-category-harassment = Harassment
report-category-rules = Rules violation
report-window-text-placeholder = Briefly describe what happened...
report-window-length = {$length} / {$max}
report-window-submit = Submit Report
report-window-sending = Sending...
report-window-success = Filed! Your reference is {$reference}.
report-window-unknown-error = Something went wrong sending your report. Please try again.

report-window-safety-heading = Need help right now?
report-window-safety-body = This report is logged for staff to review — it does not open a live conversation. If you need immediate help, use ahelp instead.
report-window-safety-button = Open ahelp

report-no-target = Please name who you're reporting.
report-empty = Please briefly describe what happened.
report-self = You can't report yourself.
report-rate-limited = You've reached the report limit for this round ({$max} reports). Thanks for flagging it — try again next round, or use ahelp if it's urgent.
