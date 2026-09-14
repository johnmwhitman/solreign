# Changeling — Talent Acquisition Specialist

Clean-room design. Written from the SS13 "changeling" concept (an identity-stealing infiltrator
antagonist) only — no fork implementation was used as a template, and no code from any other
fork/AGPL source was read, copied, or adapted. Upstream systems this pass *does* build on (all
MIT, all already present in this checkout, all explicitly studied per ground rules): `PolymorphSystem`
(referenced for contrast — deliberately NOT used, see §2.1), `IdentitySystem`/`Identity` (name
resolution idiom), `HumanoidProfileComponent`/`HumanoidProfileSystem`, and
`SharedVisualBodySystem` (organ-appearance capture/apply — `TryGatherMarkingsData` /
`ApplyProfiles` / `ApplyMarkings`).

PG/kid-safe, acid-green corporate voice, matching the existing Vampire ("Nocturnal Acquisitions
Specialist") and Werewolf ("Lunar-Reactive" HR framing) antags already in `_Solreign/Antags`.

## 1. Concept

A **Talent Acquisition Specialist** is an infiltrator who can copy the physical identity of
colleagues they find unconscious, and later "assume" one of those copied identities at will. The
corporate flavor: absorbing someone's identity is dressed up as an invasive, faintly horrifying HR
process ("Take Biometric Sample" / "Personnel File Sync") — funny-creepy, never gory, never framed
as harming the victim.

**Hard PG rule (non-negotiable, matches the ground rules for this pass):**

- Absorbing an identity is a **nonlethal COPY**, not a kill. The source target must be
  **unconscious** (`MobState.Critical` — matches the "downed, not dead" state already used
  identically by `SolreignWerewolfSystem` for its maul/`IsAlive` checks) and must **not** be dead
  (`MobState.Dead` is explicitly excluded).
- The victim is never damaged, never killed, and never rendered unable to act beyond whatever
  already put them in Critical. Absorbing is a read, not a write, against the victim.
- The victim **wakes up aware something happened**: a popup fires on them at the moment of
  absorption (`solreign-changeling-absorb-victim-popup`), and the event is queued for a **Season
  Ledger "Known Aliases" note** (§4 — not wired this pass, see the TODO in
  `SolreignChangelingSystem.Absorb.cs`, matching the same documented-but-deferred idiom
  `SolreignWerewolfSystem.OnEnterCured` already uses for its own ledger hook).

## 2. Ability list

Two abilities are implemented and working this pass. The rest of the roster is designed here for
a future build-pass (backlog), so the antag's shape is legible even though only the skeleton +ss
two abilities ship now.

### 2.1 Absorb Identity — *"Take Biometric Sample"* (IMPLEMENTED)

A verb offered on any humanoid (`GetVerbsEvent<AlternativeVerb>` on `HumanoidProfileComponent`),
visible only to an actor with `SolreignChangelingComponent`. Starts a
`SolreignChangelingAbsorbDoAfterEvent` do-after (same idiom as `SolreignVampireSystem.Feeding`'s
donation verb: gate liberally when the verb is offered, **re-validate hard at completion time**
since the target's state can change mid do-after).

On completion:
- Captures the target's identity as a `ChangelingIdentitySnapshot` — their `HumanoidCharacterProfile`
  fields (name/species/sex/gender/age/voice, via `HumanoidProfileComponent`, the same fields
  `IdentitySystem.GetIdentityRepresentation` already reads cross-system) plus their organ-level
  appearance data (`SharedVisualBodySystem.TryGatherMarkingsData` — skin/eye color and markings
  per organ category).
- Appends a `SolreignChangelingAlias` to `SolreignChangelingComponent.KnownAliases`.
- Pops up a "you've been sampled" line on the victim (no mechanical effect on them).
- Marks the source entity absorbed (`AbsorbedSources`) so the **same** unconscious body can't be
  farmed repeatedly for cooldown resets (anti-grief), starts a cooldown
  (`NextAbsorbAllowedAt`/`AbsorbCooldownSeconds`) before another absorb of any target, and caps the
  roster at `MaxKnownAliases`.

**Deliberately NOT used: `PolymorphSystem`.** Werewolf uses `PolymorphSystem.PolymorphEntity` to
swap the *entity* to a whole new wolf-form body because the human is meant to vanish for the
episode. A changeling never leaves their own body — they keep changeling-specific components,
inventory, mind, and (per spec) any other absorbed aliases — so Transform (§2.2) mutates the
*same* entity's appearance/profile data in place rather than spawning a child entity. This is the
concept-level reason the two "become something else" antags in this fork use two different
upstream mechanisms even though both "transform."

### 2.2 Transform / Revert — *"Assume Persona"* (IMPLEMENTED)

Two self-targeted `InstantAction`s (`ActionSolreignChangelingTransform`,
`ActionSolreignChangelingRevert`), granted on `ComponentStartup` — same shape as the upstream
`ActionPolymorphWizardSpider` / `ActionRevertPolymorph` pair in `Resources/Prototypes/Actions/polymorph.yml`
(studied for this pass, not reused verbatim: ours has no `Magic` component, no upgrade tiers, and
targets `HumanoidProfileComponent`/`SharedVisualBodySystem` instead of `PolymorphSystem`).

- **Transform** applies the **most recently absorbed** alias (`ChangelingIdentityRules.MostRecentAliasIndex`)
  via `HumanoidProfileSystem.ApplyProfileTo` + `SharedVisualBodySystem.ApplyProfiles`/`ApplyMarkings`
  + `MetaDataSystem.SetEntityName`. A future pass can add a picker UI for choosing among multiple
  aliases (`docs` note only — not built this pass, since the MVP need is "become *a* copied
  identity," singular, per the build brief).
- **Revert** restores the changeling's own captured `TrueForm` snapshot (captured once, at
  `ComponentStartup`, before any transform has happened — same "snapshot your own state before
  mutating it" idiom `SolreignWerewolfSystem`'s Waning-window revert already relies on, just
  applied to profile/appearance data instead of a polymorph banish/restore).
- Both actions popup on the changeling only (no popup on bystanders — a changeling's own
  appearance change is not broadcast information per spec; anyone who visually notices is playing
  it out in character, same as vanilla disguises).

### 2.3 Augmented Arm Blade — *"Deploy Retention Tool"* (IMPLEMENTED)

A toggle `InstantAction` (`ActionSolreignChangelingArmBlade`) that overrides the changeling's own
`MeleeWeaponComponent` — the same "the mob's own body is the weapon" idiom
`SolreignWerewolfSystem.Transform.cs`'s maul handler already documents ("`MeleeHitEvent` is raised
on the melee weapon entity, which for a natural attack like this is the mob itself — same idiom as
`ZombieSystem.OnMeleeHit`"). Every humanoid already carries a `MeleeWeaponComponent` for unarmed
punches (`Resources/Prototypes/Entities/Mobs/base.yml`, `Blunt: 5`); extending the blade snapshots
that original `Damage`/`Hidden` state, overwrites it with a slash-damage profile
(`SolreignChangelingComponent.ArmBladeDamage`), and flips `SolreignChangelingVisuals.ArmBladeExtended`
via `SharedAppearanceSystem.SetData` so a client-side sprite layer can react once the arm-blade art
lands (visual data wiring is complete; the bespoke sprite itself is a follow-up Antigravity
sprite-factory item, same split werewolf's wolf-form prototype already documents — "sprite
pipeline... a separate, later build-pass item"). Retracting restores the original melee stats
exactly.

Deals **no bonus stealth/instant-kill damage** — it's a stronger-than-fists melee weapon, subject
to the same combat/armor rules as any other melee weapon. No anti-grief carve-out needed beyond
what normal melee combat already enforces.

### 2.4 Backlog (designed, not built this pass)

- **Chameleon Skin** — cosmetic clothing-mimic (would reuse `Content.Shared.Polymorph.Components.ChameleonDisguiseComponent`-style
  layering, not organ-copy; separate mechanic from identity Transform).
- **Biodegrade** — self-cleanup on death (removes changeling-specific components/evidence);
  pure logic, cheap follow-up.
- **Sting (nonlethal)** — a ranged non-damaging debuff dart (sleep/silence), gated the same
  anti-grief way as Absorb (cooldown + no repeat-target farming).
- **Personnel File Sync (hive callback)** — flavor-only cross-changeling ping if the round ever
  supports multiple changelings; no mechanical coupling required.

## 3. Anti-grief / balance rules

1. Absorb never damages or kills. Full stop.
2. Absorb cannot re-farm the same unconscious body (`AbsorbedSources` check) — closes the "keep
   one knocked-out colleague on ice and re-roll cooldowns forever" loophole.
3. Absorb has a cooldown between *any* two successful absorbs (`AbsorbCooldownSeconds`), so a
   changeling can't chain-harvest an entire crit-locker in one DoAfter-spam burst.
4. `MaxKnownAliases` caps the roster (memory + fairness — no unbounded alias hoarding).
5. Arm Blade is a melee-stat swap only; no reach/speed/stealth bonus baked in this pass.
6. Every ability re-validates its preconditions at completion time, not just at verb-offer/click
   time (mirrors the Vampire feeding doc comment verbatim: state can change mid do-after).

## 4. Unique Solreign mechanic — Known Aliases (Season Ledger)

The differentiator: every identity a changeling has ever absorbed becomes a **"Known Aliases"**
entry on that account's Season Ledger page — a permanent, cross-round trophy case of who they've
copied, in the same spirit as the existing Rank/Title ledger entries.

**Not wired this pass** — `SeasonLedgerStore`/`SeasonLedgerSystem` are out of this task's assigned
paths (`Content.Server/_Solreign/Changeling` + `Content.Shared/_Solreign/Changeling` only), and
wiring it properly means an additive migration (`EnsureColumnAsync`/new table, same pattern
`contract_log` already uses) plus a `SeasonLedgerStore.AddKnownAliasAsync(...)` call from
somewhere that owns round-end submission — exactly the same trade-off
`SolreignWerewolfSystem.OnEnterCured`'s ledger TODO already documents for its own antag. The
extension point is deliberately left obvious:

- `SolreignChangelingSystem` already has the data (`SolreignChangelingComponent.KnownAliases`,
  each with a `DisplayName` and `AbsorbedAt` timestamp) in exactly the shape a future
  `AddKnownAliasAsync(Guid user, string aliasName, DateTime absorbedUtc)` call would need.
- The natural round-end hookup point is the same place `SolreignCorporateRuleSystem` already
  submits standing (`SeasonLedgerSystem.AppendRoundEndText`/`OnRoundEnd`), or a small dedicated
  `SeasonLedgerSystem.ChangelingAliases.cs` partial mirroring `SeasonLedgerSystem.Contracts.cs`'s
  shape (per-round submission API + `contract_log`-style audit table).
- Display: a `known_aliases` table (`user_id, season_id, alias_name, source_job, absorbed_utc`)
  read-joined onto the existing ledger examine/BUI surface.

## 5. Analyzer / house-rule compliance

- `SolreignChangelingComponent` has no `readonly` `[Dependency]` fields (it has none — component,
  not system) and does not shadow `Component.Owner`.
- `SolreignChangelingSystem` and its three partials all declare `[Dependency]` fields as
  non-readonly on the `partial` system class.
- The DoAfter event (`SolreignChangelingAbsorbDoAfterEvent`) and the three action events
  (`SolreignChangelingTransformActionEvent`, `SolreignChangelingRevertActionEvent`,
  `SolreignChangelingArmBladeToggleActionEvent`) all live in `Content.Shared._Solreign.Changeling`
  per the `[NetSerializable]`/DoAfter house rule.
- Exactly one `SubscribeLocalEvent<TComp, TEvent>` pair exists per (component, event) combination
  across the repo for every new type introduced here (verified by grep before writing each
  subscription — see the "no prior subscription" checks in the reviewer checklist below).

## 6. Round integration (explicitly NOT built this pass)

`SolreignChangelingComponent` is a pure mechanics component — it does nothing until something adds
it to an entity (admin command, or a future antag role/objectives/spawn-rule prototype, the same
class of follow-up `SolreignFullMoonWindowRuleSystem` provides for Werewolf). Wiring the changeling
into round antag selection, objectives, and a station spawn rule is a separate, clearly flagged
follow-up task — not silently skipped.

## 7. Testing

`Content.Tests/_Solreign/ChangelingIdentityRulesTests.cs` covers the pure decision logic
(`ChangelingIdentityRules`): absorb-gate reasons (not-critical, already-absorbed, on-cooldown,
alias-limit), cooldown math, and transform-eligibility/index-picking. ECS wiring (verbs, do-afters,
actions, appearance/profile application) is not unit-testable without a full game harness — per the
task's "do not run dotnet build" constraint, correctness there rests on the reviewer checklist
below plus the documented 1:1 mirroring of already-shipped Solreign patterns (Vampire feeding,
Werewolf transform).

## 8. Reviewer checklist

- [ ] `SolreignChangelingComponent` fields match ground-rule analyzer constraints (no readonly
      `[Dependency]`, no `Owner` shadow — N/A, it's a component not a system).
- [ ] `SolreignChangelingSystem` + partials are all `partial`, `[Dependency]` fields non-readonly.
- [ ] Absorb DoAfter re-validates `ChangelingIdentityRules.CanAbsorb` at completion, not just at
      verb-offer time.
- [ ] Absorb never calls any damage/kill API against the target — grep confirms no `DamageSpecifier`
      applied to the victim, only read (`TryGatherMarkingsData`) and popup.
- [ ] Transform/Revert never spawn a child entity (contrast with Werewolf's `PolymorphSystem` —
      Changeling mutates the same entity in place, per §2.1).
- [ ] Arm Blade restores the *exact* original `MeleeWeaponComponent.Damage`/`Hidden` on retract
      (snapshot-then-restore, not a hardcoded "fists" fallback).
- [ ] No AGPL/fork-sourced file was read or referenced while writing this pack (see the top-of-file
      provenance note) — Content.Server/Changeling, Content.Shared/Changeling,
      Content.Client/Changeling (the pre-existing vanilla upstream Changeling merged into
      space-wizards/space-station-14 itself, MIT-licensed but a *different, unrelated*
      implementation) were not used as a template; this pack lives entirely under `_Solreign`.
- [ ] Season Ledger files were not touched (out of assigned paths) — the "Known Aliases" mechanic
      is documented as a follow-up with an explicit extension point, not silently dropped.
