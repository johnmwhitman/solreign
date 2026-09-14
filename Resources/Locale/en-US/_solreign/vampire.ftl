# "Nocturnal Acquisitions Specialist" — the vampire antag. Spec: docs/specs/2026-07-11-werewolf-vampire-spec.md §4.
# PG/kid-safe, acid-green corporate voice. No forced biting, ever — see anti-grief rule 1.

# PA sender label stamped on every Vampire Night broadcast.
solreign-vampire-night-sender = Solreign Night Audit

# Broadcast when a vampire is drafted for the round (SolreignVampireNightRuleSystem.Started).
solreign-vampire-night-start =
    Night Audit division has completed onboarding for one (1) new Nocturnal Acquisitions Specialist.
    The Voluntary Donor Program remains open to all staff. The Executive Recharge Pod is, as always,
    available on a first-come basis.

# Private briefing sent to the drafted employee (SolreignVampireNightRuleSystem.Started).
solreign-vampire-briefing =
    Welcome to Night Audit. Your dietary requirements have been quietly updated in the HR system.
    Blood packs are stocked in Medbay; the Voluntary Donor Program covers the rest. The Executive
    Recharge Pod (a coffin, walnut veneer, barcode) reverses your thirst while you rest inside it.
    Garlic and the chapel are, unfortunately, still policy. The Company thanks you for your patience.

# Feeding blocked by garlic/chapel, at do-after completion (SolreignVampireSystem.Feeding).
solreign-vampire-feed-blocked = Something about this spot makes feeding impossible right now.

# A blood pack sip lands (SolreignVampireSystem.Feeding.OnBloodPackDoAfter).
solreign-vampire-feed-pack-success = The blood pack empties. The thirst recedes, for now.

# The consent verb offered on a vampire (SolreignVampireSystem.Feeding.AddDonationVerb).
solreign-vampire-donate-verb = Offer a Donation

# Donation completes — separate lines for vampire and donor (SolreignVampireSystem.Feeding.OnDonationDoAfter).
solreign-vampire-donate-success-vampire = The donation is... appreciated. Deeply.
solreign-vampire-donate-success-donor = You feel a little lightheaded, but otherwise fine.

# Ravenous stomach-growl popup, broadcasts position (SolreignVampireSystem.Environment.OnBandChangedEnvironment).
solreign-vampire-ravenous-growl = A loud, involuntary stomach growl echoes from nearby.
