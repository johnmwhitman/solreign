# Talent Acquisition Specialist — the changeling antag. Spec: docs/specs/2026-07-11-changeling-spec.md.
# PG/kid-safe, acid-green corporate voice. Absorb is a nonlethal COPY, never damage — see spec §1.

# The verb offered on any humanoid to a changeling (SolreignChangelingSystem.Absorb.AddAbsorbVerb).
solreign-changeling-absorb-verb = Take Biometric Sample

# Absorb denied at do-after completion (SolreignChangelingSystem.Absorb.OnAbsorbDoAfter).
solreign-changeling-absorb-denied-not-critical = They need to be unconscious for a clean sample.
solreign-changeling-absorb-denied-already-absorbed = You've already logged everything usable from this genome.
solreign-changeling-absorb-denied-cooldown = Your sampling kit is still recalibrating.
solreign-changeling-absorb-denied-alias-limit = Your personnel file is full — no room for another alias.

# Absorb succeeds — changeling-only popup (SolreignChangelingSystem.Absorb.OnAbsorbDoAfter).
solreign-changeling-absorb-success = Biometric sample logged. New alias on file: {$name}.

# Absorb succeeds — victim-only popup. Spec §1: informative, never mechanical (no damage, no state change).
solreign-changeling-absorb-victim-popup = You stir, woozy — was that a badge scanner? Your personnel file feels... thinner.

# Transform / Revert (SolreignChangelingSystem.Transform).
solreign-changeling-transform-none = You have no biometric samples on file yet.
solreign-changeling-transform-popup = Your file photo updates itself. You are now {$name}.
solreign-changeling-revert-popup = Your original badge photo reasserts itself.

# Augmented Arm Blade (SolreignChangelingSystem.ArmBlade).
solreign-changeling-armblade-extend-popup = Your forearm reshapes into a gleaming bio-blade.
solreign-changeling-armblade-retract-popup = Your arm returns to its unremarkable, HR-compliant shape.

# --- Round integration (spec §6 follow-up): antag selection, objective, round-end summary. ---
# Resources/Prototypes/_Solreign/GameRules/changeling.yml, .../Roles/changeling_antag.yml,
# .../Objectives/changeling.yml, SolreignChangelingRuleSystem.cs.

# Antag preference menu / character setup.
roles-antag-solreign-changeling-name = Talent Acquisition Specialist
roles-antag-solreign-changeling-objective = Build a personnel file. Absorb identities from unconscious colleagues, then wear them at will — Corporate doesn't care how, only that quota gets hit.

# Admin/round-end antag-type label (AntagSelectionComponent.AgentName).
solreign-changeling-round-end-agent-name = talent acquisition specialist

# Mind role display (admin overlay only).
role-subtype-solreign-changeling = Talent Acquisition Specialist

# Briefing shown the moment the antag is assigned (corporate-dystopia voice, PG).
solreign-changeling-role-greeting = You are not on the org chart. No badge, no desk, no exit interview — Solreign Talent Acquisition doesn't hire, it absorbs. Take biometric samples from unconscious colleagues, then wear their faces at will. Collect enough personnel files and Corporate stops asking who you really are, because the paperwork already forgot you existed.

# Round objective.
objective-issuer-solreign-talent-acquisition = Solreign Talent Acquisition
objective-solreign-changeling-collect-identities-name = Build a personnel file of {$count} identities.
objective-solreign-changeling-collect-identities-description = Take {$count} biometric samples. Remember: a sample is a copy, not a rejection letter — nobody on the list should end up dead because of you.

# Round-end summary (SolreignChangelingRuleSystem.AppendRoundEndText).
solreign-changeling-round-end-header = TALENT ACQUISITION — Personnel File Audit:
solreign-changeling-round-end-was = {$name} ({$username}) was a Talent Acquisition Specialist.
solreign-changeling-round-end-aliases = Known aliases on file: {$aliases}.
solreign-changeling-round-end-aliases-none = Personnel file remained empty — no aliases collected.
