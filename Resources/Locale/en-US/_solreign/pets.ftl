# Solreign Pets v1 — taming + follow-owner flavor. Voice: unsettlingly upbeat internal comms —
# sinister-but-premium acid-green megacorp, PG throughout. Compliance is its own reward.
# See contracts.ftl for the house style this borrows from.

## SolreignTameableSystem — feed-to-tame popups

solreign-pet-tame-success = [color=#9acd32]{CAPITALIZE(THE($target))} accepts the treat and imprints on {$owner}. HR is calling it "synergy." {POSS-ADJ($target)} tail is calling it something else.[/color]
solreign-pet-tame-retamed = [color=#9acd32]{CAPITALIZE(THE($target))} reconsiders {POSS-ADJ($target)} loyalties and imprints on {$owner} instead. The Company respects a lateral move.[/color]
solreign-pet-tame-already-bonded = {CAPITALIZE(THE($target))} happily accepts the treat. {POSS-ADJ($target)} loyalty to {$owner} was never in question. Compliance is its own reward.

## SolreignTameableSystem — verbs & orders

solreign-pet-verb-release = Release Pet
solreign-pet-verb-release-tooltip = Release ownership of this pet, allowing them to return to general station roaming.
solreign-pet-verb-follow = Command: Follow
solreign-pet-verb-stay = Command: Stay
solreign-pet-verb-toggle-tooltip = Toggle whether your companion follows your footsteps or stays put.

solreign-pet-release-success = You release {$target} back into station custody. {$target} tilts {POSS-ADJ($target)} head, free once more.
solreign-pet-toggle-follow = [color=#9acd32]{CAPITALIZE(THE($target))} perks up and prepares to follow {$user}.[/color]
solreign-pet-toggle-stay = [color=#9acd32]{CAPITALIZE(THE($target))} sits comfortably and stays put.[/color]

## SolreignTameableSystem — grooming & cleanup

solreign-pet-cleanup-success = [color=#9acd32]You give {THE($target)} a thorough grooming. {CAPITALIZE(THE($target))} sparkles with Solreign corporate polish.[/color]
solreign-pet-already-clean = {CAPITALIZE(THE($target))} is already spotless and company-compliant.

## SolreignTameableSystem — ledger stamps

solreign-pet-ledger-stamp = [color=#9acd32]Season Ledger Stamp recorded: {$stamp}. Companionship verified.[/color]

## SolreignTameableSystem — examine text

solreign-pet-examine-untamed = No Solreign asset has claimed this critter yet. Ownership paperwork is available at any time, from nobody, because it does not exist.
solreign-pet-examine-tamed = This critter is bonded to {$owner}.
solreign-pet-examine-following = It is eagerly following {$owner}'s lead.
solreign-pet-examine-staying = It is staying in place per company directive.
solreign-pet-examine-dirty = It looks a bit scuffed and in need of a quick groom.
solreign-pet-examine-stamps = This companion carries {$count} verified Season Ledger stamp(s).
solreign-pet-examine-ledger-flavor = The Season Ledger keeps no record of belly rubs. This is considered a Company oversight, not a mercy.
