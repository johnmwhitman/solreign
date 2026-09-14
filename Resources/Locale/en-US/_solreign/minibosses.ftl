# Solreign mini-boss events. PG/kid-safe, acid-green corporate voice. Nobody actually dies to any of
# this — see Content.Server/_Solreign/MiniBoss/.

# PA sender label stamped on every mini-boss PA line (SolreignMiniBossAudit).
solreign-miniboss-sender = Solreign Ops

## The Auditor Prime (SolreignAuditorPrimeRule)

# Broadcast on spawn (SolreignAuditorPrimeRule.Started).
solreign-auditor-prime-arrival =
    Attention: the Auditor Prime has arrived for a surprise compliance walkthrough. Please have your
    paperwork, badges, and general demeanor in order.

# Popup shown above the Auditor Prime when its periodic scan pulse fires (SolreignEffectAuditPulse).
solreign-auditor-prime-scan-popup = COMPLIANCE SCAN IN PROGRESS

# Broadcast when the Auditor Prime is defeated (SolreignMiniBossSystem.OnMiniBossStateChanged).
solreign-auditor-prime-defeat =
    The Auditor Prime's walkthrough has concluded ahead of schedule. Findings are being filed
    posthumously. The Company regrets the paperwork this creates for everyone.

## Specimen Zero (SolreignSpecimenZeroRule)

# Broadcast when the containment-breach warning starts (SolreignSpecimenZeroRule.Started).
solreign-specimen-zero-warning =
    Containment advisory: Specimen Zero's habitat seal is reporting irregularities. Wildlife Compliance
    estimates a breach in { $seconds } seconds. Please remain calm and enthusiastic.

# Popup shown above Specimen Zero when its periodic roar pulse fires (SolreignEffectAuditPulse).
solreign-specimen-zero-roar-popup = SPECIMEN AGITATED

# Broadcast when Specimen Zero actually breaches containment (SolreignSpecimenZeroRule.SpawnBoss).
solreign-specimen-zero-breach =
    Containment advisory: Specimen Zero has breached containment. Wildlife Compliance recommends
    non-lethal countermeasures and reminds staff that petting is still discouraged.

# Broadcast when Specimen Zero is defeated (SolreignMiniBossSystem.OnMiniBossStateChanged).
solreign-specimen-zero-defeat =
    Specimen Zero has been safely re-contained. Wildlife Compliance thanks whoever handled that and
    would like to remind them that the tranquilizer paperwork is due by end of shift.
