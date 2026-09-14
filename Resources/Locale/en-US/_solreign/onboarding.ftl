# Solreign's station-wide "first five minutes" onboarding beat — one PA announcement, fired once at
# round start, that reinforces the megacorp-dystopia tone immediately and gives newcomers a one-line
# orientation. Complementary to (not a duplicate of) corporate.ftl's "keynote": that one sets the
# satirical corporate-metrics tone, this one is the practical orientation companion. Own sender key
# (rather than reusing solreign-corporate-hr-sender) so this file never needs to touch corporate.ftl.

# PA sender label stamped on this beat's broadcast.
solreign-onboarding-hr-sender = Solreign HR

# 4 rotating shift-start PA variants (SolreignOnboardingSystem, fired on RoundStartingEvent). Each
# welcomes the shift, keeps the corporate-dystopia voice, and ends with a one-line newcomer
# orientation (PDA / Conduct Charter / Feedback tool) so new arrivals aren't lost in the first
# five minutes.

solreign-onboarding-beat-1 =
    Attention, all assets: your shift has officially begun, and so has Solreign's evaluation of you.
    New hires — your PDA holds your assignment, your Conduct Charter is mandatory reading, and any
    incidents belong in the Feedback tool, not the rumor mill. Solreign is always watching. Have a
    productive shift.

solreign-onboarding-beat-2 =
    Good morning, Solreign. The clock is running, the ledger is open, and your performance is already
    being recorded. First shift? Consult your PDA, absorb the Conduct Charter, and route any incidents
    through the Feedback tool — Solreign values a well-documented workforce almost as much as a
    productive one.

solreign-onboarding-beat-3 =
    This is Solreign HR with your shift-opening notice: welcome aboard, your compliance is appreciated,
    and your alternatives are not. New assets should check their PDA for assignment, treat the Conduct
    Charter as gospel, and report incidents via the Feedback tool — anything else is a missed
    opportunity for growth.

solreign-onboarding-beat-4 =
    Solreign HR reminds all assets that the shift has begun and observation never stops. Newly
    onboarded personnel: your PDA lists your duties, your Conduct Charter lists your limits, and the
    Feedback tool lists where incidents go. Deviations will be noted. Have a rewarding shift.
