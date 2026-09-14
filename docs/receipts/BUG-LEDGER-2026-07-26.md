# SOLREIGN bug ledger — opened 2026-07-26

Nothing here is a release blocker. John's call after playtesting `Solreign_update_20260726-165539Z`:
*"Assume nothing is broken right now. Push forward and record them as bugs."* These are the open
threads from the 07-26 incident day, written down so they stop living in chat.

**Live:** `Solreign_update_20260726-165539Z` · GAME master `70eb235834` · OPS `30c81536` · round 120.
**Playtest result:** Ian renders correctly — visually confirmed by John in a real client.

---

## 🔴 SR-BUG-001 — Spawn-naked, cause UNCONFIRMED
Players reported dropping in with no clothes, across two releases. **Not reproduced in the latest
playtest** ("I don't think I was naked this time"), so it is intermittent or already incidentally
fixed — we do not know which, and that is the problem.

The missing-lungs fix (SR-BUG-002) explains the *asphyxiation* only. An independent QA pass
**refuted** the theory that it also explained the nakedness: clothing displacement is purely
additive (`Content.Client/Clothing/ClientClothingSystem.cs:326-340`), so absent displacement
renders clothes undisplaced, never invisible.

**Live hypothesis:** an unguarded throw mid-equip. `Content.Server/Station/Systems/
StationSpawningSystem.cs:136-155` runs `ApplyProfileTo → EquipRoleLoadout → EquipStartingGear`
with **no try/catch**; a throw anywhere leaves a bare body and skips `SetPdaAndIdCardData`.
A player can drop in naked with perfectly good lungs.

**Now actionable:** file logging is ON as of 07-26 (see SR-NOTE-A). If it recurs, the exception
will be in `/logs/log_*.txt`. Repro attempt is John's; diagnosis is ours the moment it fires.

## 🟠 SR-BUG-002 — Shadow breathing not yet eyeballed
Root cause found and fixed: `AppearanceShadow.InitialBody.organs` declared 12 entries and **no
internal organs at all** — no Lungs. Causal chain independently confirmed end-to-end
(`RespiratorSystem.cs:90,150,154,107-120` — below the suffocation threshold it fires a literal
`GaspEmote`, which is the reported symptom). Shadow now resolves 20 organs, level with Human.
**Not verified in a live client.** John: "I'll look next time."

## ✅ SR-BUG-003 — WITHDRAWN as a FALSE POSITIVE; branch merged 2026-07-26 (fix `c12c9bdf03`)
The original claim ("prototype defined nowhere in `Resources/`") was wrong: the prototype IS
present at test runtime, declared inline via `[TestPrototypes]`. The linter redness was real but
misattributed — it is a static-field reflection artifact, not missing content.
Root cause: `PrototypeManager.ValidateFields.cs` (`ValidateStaticFields`) walks
`BindingFlags.Static` fields in BOTH linter passes; `Content.YAMLLinter.csproj` project-references
`Content.IntegrationTests`, and `Program.cs` filters YamlErrors by client/server visibility but
concatenates FieldErrors unfiltered. The client pass cannot resolve `Content.Server`'s
`HTNCompoundPrototype` kind (2 errors), and the server pass indexes only on-disk `Resources/`,
not `[TestPrototypes]` YAML (1 error). Proof pattern: `CursedMaskSystem.cs:32` uses the identical
declaration and passes only because it lives in `Content.Server`.
Interim fix (`c12c9bdf03`): removed `static` from the two fields — mutation-proved (exit 255 /
3 errors → exit 0 / "No errors found"), zero runtime behaviour change. **Now REVERTED**; the
fields are `static readonly` again because the linter itself was repaired.

### Real fix, same day — and it is NOT the follow-up this entry originally prescribed
This entry used to say the follow-up was "filter FieldErrors by client/server visibility the way
YamlErrors already are." **Testing disproved that.** With the fields re-`static`'d, the 3 errors
split by pass: the 2 "Unknown prototype **kind**" errors can only come from the CLIENT pass (a
pass that had the kind would not emit them), but "Unknown prototype: SolreignNocturneLifecycle
TestCompound" requires the kind to BE registered, so it comes from the SERVER pass — where the
server is correct that the id is absent from on-disk `Resources/`. **A visibility filter cannot
touch that error.** It would have left the linter red and is near-dead code besides: outside the
test assembly the visibility gap is unreachable, since `Content.Shared` cannot name
`Content.Server` types, and a `Content.Server` type is never loaded into the client instance.

The actual defect is **jurisdiction, not visibility**: the linter validates on-disk `Resources/`
and shipped code, but `Content.IntegrationTests` is in `FindAllTypes()` for both passes purely
because `Content.YAMLLinter.csproj` references it for `PoolManager`. Its `[TestPrototypes]` ids
are real at test runtime and invisible to the linter by construction.
Fix: `Content.YAMLLinter/Program.cs` now walks `FindAllTypes()` itself and calls the per-type
`ValidateStaticFields` overload, skipping abstract types (matching the engine's own rule) and the
test assembly. No RobustToolbox change — the engine target is still an open owner decision.
**Mutation-proved in both directions:** re-`static`'d fields → exit 0; and three deliberately bogus
prototype ids planted in shipped code (`CursedMaskSystem` server, `PaperSystem` shared,
`StampWidget` client) → all three still reported, exit 255. The gate is narrower, not blinder.
A loud anchor guard (also mutation-proved) refuses to run if the skip target ever resolves to
anything but `Content.IntegrationTests`, so a moved type cannot silently unlint a shipping assembly.
**Known and accepted:** static prototype-id fields *inside the test assembly* are no longer
linted; the test run itself is what validates them, and that code cannot ship.

## 🟠 SR-BUG-004 — the species gate is textual, not behavioral
`SolreignPlayableSpeciesViabilityTests` reads YAML. A stronger harness already exists and was
missed: `Content.IntegrationTests/Tests/Humanoid/HumanoidProfileTests.EnsureValidRandomSpecies` is
**already** `[TestCaseSource]` over every species prototype and spawns each one. Adding a soak-tick
+ zero-damage assertion there would make the invariant behavioural rather than textual, and would
catch species breakage the YAML cannot see.

## 🟡 SR-BUG-005 — Shadow markings limits are unbounded
`Resources/Prototypes/Species/shadow.yml` markingsGroup omits `Snout`, `Tail` and `Special` limits
that Human sets. `MarkingManager.EnsureValidLimits` **`continue`s** on a missing limit, so those
layers are effectively unlimited for Shadow. No crash; a content-integrity hole.

## 🟡 SR-BUG-006 — Shadow is absent from `SpeciesWeights`
`Resources/Prototypes/Species/species_weights.yml` lists 8 species; Shadow and Arachnid are not
among them, so a randomly-assigned species never rolls a Shadow. May be deliberate — needs a call.

## 🟡 SR-BUG-007 — female Shadows wear a male-cut jumpsuit
`AppearanceShadow` declares no `Inventory` component, so it inherits `InventoryBase` with no
`femaleDisplacements` (Human sets them at `human.yml:57-63`). Purely cosmetic — explicitly NOT the
cause of SR-BUG-001.

## 🟡 SR-BUG-008 — `/status` exposes no `release_id`
The boot-proof PRODUCER gap. Release identity has to be proven via the **client-pin byte size** plus
artifact size instead of one call. Closing this turns deploy verification into a single check and
would remove the main reason deploy verification is multi-step.

## 🟡 SR-BUG-009 — Oasis zoo wing is built and empty
Entrance, keeper door, APC, containment console/emitter/biofeeder/recovery pad and three "Zoo
Habitat Divider" windows, no fauna. The creatures are on Meridian. ⚠️ The dividers are the walls
BETWEEN pens; entity-occupancy cannot resolve the pen interiors (the obvious-looking region is
office/RND space containing a `SpawnPointScientist`). Needs the map editor.

## ✅ SR-BUG-011 — CLOSED: the species gate could not see the file it guarded
**This was the most important bug in this ledger and it is fixed.**
`SolreignPlayableSpeciesViabilityTests` reads the GAME repo. Shadow is authoritatively defined in
the **OPS overlay**, which `build_verify` gate 2 rsyncs OVER the game tree. So the gate written
specifically for the lungs bug PASSED while the lungless species shipped — the game repo's copy was
correct and the shipping copy was not.

Generalised, because it is not about Shadow: **a game-repo test can only prove things about a tree
the overlay is free to overwrite. Anything validating CONTENT must run post-overlay.**

Fixed two ways (OPS `af39f6f7`, `f739b42f`):
1. `cmd_species_viability` now runs inside gate 3 (BATTERY), which is already post-overlay, against
   what actually ships. Fails CLOSED if PyYAML is missing, and refuses to pass if it scanned
   implausibly little. Mutation-proved both directions on the real post-overlay tree.
2. `APPLY OVERLAY` now NAMES every file whose content it changes (`NEW`/`DIFFERS`). The clobber was
   always allowed — it being invisible was the bug.

The C# gate is kept: it gives fast feedback in the game repo. It is simply no longer the last word.

## ✅ SR-BUG-010 — CLOSED: latent inheritance bug in the species gate's resolver
`ResolveOrgans` threads one `seen` set through all parent branches and never unwinds it, so an
ancestor first visited via a dead branch returns null on a live one. Not triggered by current
content (verified by swapping `MobShadow`'s parent order), but it is a correctness bug in a function
whose entire job is modelling inheritance.

**Fixed in BOTH copies** — the same flaw had been reproduced in the Python post-overlay gate. Each
parent branch now gets its own visited set (`seen | {eid}` / a copied `HashSet`), which still
catches real cycles. Suite 2515/0/2; post-overlay gate still reports 10 species / 12 organs.

---

## SR-NOTE-A — file logging is ON (2026-07-26), and we were reading the wrong files
`[log] enabled` was **false** on the box, which is why every diagnostic written for these incidents
went nowhere. John enabled it. Verified on the box: `enabled = true`, first non-empty log since
July 18 (`log_2026-07-26-08-08-43.txt`, 6846 B, clean boot).

🔑 **The configured log is `log_*.txt`, not `Runtime-*.txt`** (`format = "log_%(date)s-%(time)s.txt"`).
Every `Runtime-*.txt` on the box is 0 bytes and always was — that is not evidence of anything.
Read `log_*.txt`.

## 🔴 SR-SEC-001 — director token exposed a third time
The `solreign.director` HMAC token sits in plaintext in `server_config.toml` and has now appeared in
three separate transcripts. John previously declined rotation. Flagging once more because three
occurrences is a pattern, not an accident. **John's call.**

---

## Changelog policy for this release
**Ian is NOT to be mentioned.** John: *"It was our mistake."* We broke his sprite in the Wave 18
batch and fixed it; a regression repair is not a feature. The same reasoning applies to the space
sheep error sprite. The honest headline material is the 15 newly-wired art families, the changeling
arm-blade drawing our own sprite, and the Oasis ahelp sign.


---

## Housekeeping 2026-07-26
Pruned five fully-merged branches created by this session (`fix/ian-corgi-sprite-and-state-gate`,
`integrate/{chain-port,codex-salvage-20260726,salvage-wave2,wave4}`) — every commit is in master, so
nothing was lost. **Deliberately left alone:** `integrate/{content-recovery,contracts-lowpop,
rootcause-198,wave6-content,world-spawn-fix}`. They are equally merged and equally safe to delete,
but they are not this session's to remove.


---

## Review round 2026-07-26 (post-close) — orchestrated adversarial + QA pass
Four fleet lanes reviewed the whole session (grk ×2, cdx, Claude QA). Outcomes:
* **Wave-4 merged content: CLEAN** across the seven shipped defect classes.
* **CLOSED (was audit gap #3): `SolreignSignContractsHowTo` is now 7/7** — wave-4's
  contracts-sign-parity branch placed Nocturne + Verdant. Verified against the map files.
* **Deploy gates hardened, 6 mutation-proved fixes** (OPS `e16de40d`, `ac737e0b`): species/body
  overlay divergence now fails the build; organ VALUES validated (Lungs must descend from
  `OrganBaseLungs`); key-presence semantics aligned with the C# twin; `rsync --checksum`;
  species check in dry-run; `roundStart` parsing normalized + species named in the log.
* **Claims audit of the handoff: 24/30 confirmed, 0 fabrications, 2 numbers refuted** and
  corrected in place (§13 diffstat attribution, §15 commit count).
* ⚠️ SR-BUG-010's **C# copy** is on master but NOT in the live artifact (built from master~2,
  test-only delta — no runtime divergence). The Python twin is active at build time. Rides the
  next deploy.
