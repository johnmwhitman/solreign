# "Directives Fax" copy pack (v14 wave-1 item #1, council C1 einstein-001 "ship first"). Same
# corporate-sinister-but-warm, PG-13 house voice as station_directive.ftl / award-popup.ftl /
# social-cheap-adds.ftl. CLOSED VOCABULARY: the only variables ever interpolated here are a short
# directive display name (Content.Server._Solreign.DirectivesFax.DirectivesFaxClauseCatalog — a fixed,
# code-owned string, never player text), a clause description (also fixed, code-owned), and small
# integer thresholds/streak counts — nothing in this file ever renders raw player, attacker, or daemon
# text.

## The printed fax paper (Content.Server._Solreign.DirectivesFax.DirectivesFaxRuleSystem.BuildFaxBody).

solreign-directives-fax-paper-name = PROVIDENCE Directive Fax
solreign-directives-fax-sender = PROVIDENCE
solreign-directives-fax-header = SOLREIGN HR — SHIFT DIRECTIVE: { $directive }
solreign-directives-fax-clauses-header = To be considered compliant this shift, the following must hold at shift end:
solreign-directives-fax-clause-line = - { $clause }

## Clause descriptions — one loc key per DirectivesFaxClauseKind (closed vocabulary, three total),
## reused on both the printed fax and the round-end compliance report.

solreign-directives-fax-clause-zero-casualties = Zero crew deaths logged this shift.
solreign-directives-fax-clause-cargo-revenue-min = Cargo account revenue up at least { $amount } cr this shift.
solreign-directives-fax-clause-supply-orders-min = At least { $count } supply orders placed this shift.

## Round-end compliance report (DirectivesFaxRuleSystem.AppendRoundEndText).

solreign-directives-fax-round-end-header = PROVIDENCE DIRECTIVE COMPLIANCE REPORT
solreign-directives-fax-round-end-met = Directive '{ $directive }' — CLAUSES MET. Compliance noted. Please continue to be noted.
solreign-directives-fax-round-end-unmet = Directive '{ $directive }' — CLAUSES UNMET. Head Office is not angry. Head Office is disappointed, which is worse.
solreign-directives-fax-round-end-clause-met = [MET] { $clause }
solreign-directives-fax-round-end-clause-unmet = [UNMET] { $clause }

## Streak milestone toasts (Content.Server._Solreign.Notifications.SolreignAwardPopup.ShowMilestone) —
## escalating deadpan per the council instruction. Same no-number-in-the-frame convention as the social
## firsts milestones; the streak count lives in the reason text itself where it matters.

solreign-directives-fax-streak-milestone-3 = Compliance Streak: 3 Shifts. Noted.
solreign-directives-fax-streak-milestone-5 = Compliance Streak: 5 Shifts. Head Office has begun a file.
solreign-directives-fax-streak-milestone-10 = Compliance Streak: 10 Shifts. The file has a tab now.
solreign-directives-fax-streak-milestone-25 = Compliance Streak: 25 Shifts. You have been externally benchmarked.
solreign-directives-fax-streak-milestone-50 = Compliance Streak: 50 Shifts. Head Office would like you to know it is watching, warmly.

## Chat mirror (screenshot-surviving, sent alongside the popup — the Social Firsts dual-delivery idiom).

solreign-directives-fax-streak-milestone-chat = PROVIDENCE COMPLIANCE NOTICE: { $reason }

## Stamped end-of-shift compliance report paper (DirectivesFaxRuleSystem.Report.cs) — the
## screenshottable/shareable artifact. Reuses the round-end-header/met/unmet/clause-met/clause-unmet
## keys above for the body; these three are the paper-only additions: a station-wide tally line (never
## a per-account streak number — streaks are personal and surface via the milestone popup/chat path
## instead) and a closing stamp line, matching the Station Audit paper's "stamped with the Solreign
## seal" framing for visual/narrative parity between the two report artifacts.

solreign-directives-fax-report-paper-name = PROVIDENCE Directive Compliance Report
solreign-directives-fax-report-tally = { $met } of { $total } present crew met every clause this shift.
solreign-directives-fax-report-stamp = [ STAMPED — verified via PROVIDENCE internal fax relay. This copy is the shift's official record. ]
