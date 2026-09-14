# "Requisitions Anonymous" — the black-market console (client window + popups). Voice matches
# contracts.ftl/oracle.ftl: unsettlingly upbeat internal comms, sinister-but-premium acid-green
# megacorp, PG throughout — except this terminal is a little less "corporate" and a little more
# "please don't ask where this came from."

## Requisitions Anonymous — client window

solreign-market-window-title = REQUISITIONS ANONYMOUS
solreign-market-window-empty = No stock tonight. Standing burns a hole in your pocket regardless.
solreign-market-window-flavor-left = Nothing here is on the manifest.
solreign-market-window-flavor-right = Requisitions Anonymous — ask no questions, we invoice none

solreign-market-status-offline = The dealer is not answering.

## P3.2 OTHER SILENT DROPS — market buy button guards (audit fix 2 of 4). Mirrors the
## bounties fix: a player mashing Buy on a half-down or rate-limited console needs the
## failure to surface as a toast, not as dead air. Distinct copy per branch so a busy desk
## never reads as an outage (same separation as bounties-claim-busy-popup vs the failure trio).

solreign-market-buy-offline-popup = The dealer is not answering. Try again in a moment.
solreign-market-buy-busy-popup = The desk is processing an earlier buyer. Resubmit in a moment.

solreign-market-ui-name-label = [bold]{$name}[/bold]
solreign-market-ui-price-label = [color=#9acd32]{$price} Standing[/color]
solreign-market-ui-description-label = {$description}
solreign-market-ui-buy-button = Buy
solreign-market-ui-sold-button = Sold

## Requisitions Anonymous — popups

solreign-market-popup-bought = Purchase confirmed: {$item}. It'll turn up. Don't ask how.
solreign-market-popup-failed = The dealer declines: {$error}

## UX-SIMPLE FIX 4 — reason text fed into the shared SolreignAwardPopup helper when this
## console has a cached price for the purchased listing (produces "-N Standing — [reason]").
solreign-market-award-reason-bought = Purchase confirmed: {$item}. It'll turn up. Don't ask how.

## Requisitions Anonymous — delivery (spawn_entity Director event, arrives out of band)

solreign-market-popup-delivery = A Requisitions Anonymous delivery materializes.
