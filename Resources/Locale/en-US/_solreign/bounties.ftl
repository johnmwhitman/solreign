# Solreign Liability Board (client window + verdict popup strings only; the entity's own
# name/description live directly in Resources/Prototypes/_Solreign/Entities/bounty_board.yml,
# matching house convention — see contracts_infrastructure.yml's SolreignContractsBoard and
# oracle_terminal.yml's SolreignDirectiveTerminal). Voice matches contracts.ftl/oracle.ftl:
# unsettlingly upbeat internal comms, sinister-but-premium acid-green megacorp, PG throughout.

## Liability Board — client window

solreign-bounties-window-title = LIABILITY BOARD
solreign-bounties-window-empty = No outstanding liabilities at this time. The Directive is disappointed.
solreign-bounties-window-hint = Claims are audited. Fraudulent claims amuse the Directive.
solreign-bounties-ui-submit-button = Submit Claim
solreign-bounties-ui-char-count = {$current}/{$max}

## Liability Board — server-driven verdict popup

solreign-bounties-verdict-popup = BOUNTY VERDICT: {$msg}

## Liability Board — offline state (ALIVENESS P0 #2). A failed daemon fetch must never wear the
## true-empty copy above ("No outstanding liabilities…") — same in-fiction idiom as market.ftl's
## "The dealer is not answering." Shown INSTEAD of the empty line whenever the listing fetch
## errors; the empty line only ever renders on a successful, genuinely empty response.

solreign-bounties-window-offline = The claims desk is not answering. Outstanding liabilities cannot be listed right now. The Directive is aware. The Directive is always aware.

## Liability Board — claim failure popups (ALIVENESS P0 #1). One delivered per failed claim
## submission; never the raw HTTP status or any daemon-internal detail — those are
## server-log-only, mirroring oracle.ftl's failure trio exactly. Voice: unsettlingly upbeat
## internal comms, sinister-but-premium megacorp, PG-13.

solreign-bounties-claim-failure-popup-hums = Your claim is transmitted. Nothing acknowledges it. The board hums.
solreign-bounties-claim-failure-popup-static = Your claim dissolves into static. The adjudicator declines to pick up, for now.
solreign-bounties-claim-failure-popup-quiet = The claims line goes quiet. The Company assures you your submission mattered.

## Rate-limit deny on submit — the claim never left the station, distinct wording from the
## daemon-didn't-answer trio above so a busy desk never reads as an outage.
solreign-bounties-claim-busy-popup = The claims desk is processing an earlier submission. Resubmit in a moment.

## P3.2 OTHER SILENT DROPS — bounties daemon-down on the press path (audit fix 4 of 4). The
## OnBoardUiOpened branch is read-only so the audit accepts the silent return; OnClaimMessage
## the press path needs the same "not answering" line the market/noticeboard/library open paths
## already show, so a player who mashes Submit on a half-down board sees the failure rather than
## eating the click. Mirrors the Noticeboard "NotAcceptingKey" pattern and the market buy fix.

solreign-bounties-claim-not-answering-popup = The claims desk is not answering. Try again in a moment. The Directive has been informed.
