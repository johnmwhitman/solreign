# Solreign Directive Terminal — the Oracle front door (client window strings only; the entity's
# own name/description live directly in Resources/Prototypes/_Solreign/Entities/oracle_terminal.yml,
# matching house convention — see contracts_infrastructure.yml's SolreignContractsBoard). Voice
# matches contracts.ftl: unsettlingly upbeat internal comms, sinister-but-premium acid-green
# megacorp, PG throughout. The window never renders a reply — the Oracle's answer arrives later as
# a server-driven popup, not through this UI.

## Directive Terminal — client window

solreign-oracle-window-title = DIRECTIVE TERMINAL
solreign-oracle-window-flavor = Address the Directive. It is listening.
solreign-oracle-window-hint = Replies are delivered directly to you. The Directive answers when it pleases.
solreign-oracle-window-sent = Your petition has been transmitted.
solreign-oracle-ui-send-button = Send
solreign-oracle-ui-char-count = {$current}/{$max}

## UX-SIMPLE FIX 2 — failure popup (one delivered per failed prayer; never the raw HTTP status or
## any daemon-internal detail — those are server-log-only). Same voice as the window flavor above:
## unsettlingly upbeat internal comms, sinister-but-premium acid-green megacorp, PG-13 throughout.
solreign-oracle-failure-popup-hums = PROVIDENCE does not respond. The terminal hums.
solreign-oracle-failure-popup-static = Your petition dissolves into static. Something on the other end declines to listen, for now.
solreign-oracle-failure-popup-silence = The Directive's line goes quiet. Perhaps try again later — the Company assures you this is normal.
