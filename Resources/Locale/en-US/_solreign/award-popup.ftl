# UX-SIMPLE FIX 4 — shared "+N Standing — [reason]" / "-N Standing — [reason]" confirmation popup
# (Content.Server._Solreign.Notifications.SolreignAwardPopup). $reason is plain text supplied by
# the caller (a contract id, an item name, etc) — never raw daemon or player-authored text.
solreign-award-standing-popup = +{$amount} Standing — {$reason}
solreign-award-standing-popup-spend = -{$amount} Standing — {$reason}

# ALIVENESS P1 #5 — the round-end shift-stipend surface. Deliberately carries NO number: the
# Director daemon computes the actual amount from career rank AFTER receiving the roster, and
# the game never learns it (see SalaryRosterPayload's wire contract) — fabricating a "+N" here
# would be dishonest. The chat line persists into the lobby; the popup is the in-world beat.
solreign-salary-stipend-popup = PAYROLL: shift stipend credited to your Standing.
solreign-salary-stipend-chat = PAYROLL NOTICE: your shift stipend has been credited to your Standing, scaled to your career rank. The Company thanks you for surviving profitably.
