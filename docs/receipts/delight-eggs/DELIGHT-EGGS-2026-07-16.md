# Receipt — Delight-eggs batch (feat/delight-eggs + feat/website-v2)

**Date:** 2026-07-16 · **Game branch:** `feat/delight-eggs` (worktree `~/AI/solreign-trees/delight-eggs`, off `master` @ 9b662b14bf) · **Website branch:** `feat/website-v2` (worktree `~/AI/solreign-trees/website-v2`) · **Scope:** small surprise-and-delight additions across both surfaces. NOT merged, NOT pushed, NOT deployed.

SPOILER LAW: this receipt describes mechanisms and test results only in its main body. A clearly-marked SPOILER section at the bottom names the specific entities/locations touched, for internal reviewers only — do not quote that section anywhere public (commits, website copy, Discord, etc).

## Batch character (spoiler-safe)

Five small reactive beats added to the game, layered onto the house style already established by `SolreignWristOrganizerSystem` (real functionality on an existing easter egg) and `ProvidenceVoiceSystem`/`ProvidenceCommiserationSystem` (gated, anti-fatigue-capped rare station-voice events). Every addition rewards *curiosity* — repeated examining, repeated using, or a deliberate proximity check — over a one-time find. Two small beats added to the website, both layered onto existing delight infrastructure (visitor ledger, the terminal event bus, the existing hover-reveal component) rather than inventing new plumbing — the site already shipped a Konami code, a console message, an idle curfew, a click-escalation modal, and a poke-escalating interactive eye before this batch started, so the bar for "genuinely new" was high.

## Design process

Dispatched to `grk` (Grok, foreground) for ~15 mechanism-grounded surprise-and-delight concepts across both surfaces, biased toward reactive delights (curiosity-rewarding) over static finds. Full transcript is in this session's tool history; concepts and curation below.

### Concepts implemented
- **Escalating examine text** (game) — a reusable component/system, applied to two entities.
- **Used-item phrasing drift** (game) — extends the existing wrist-organizer real-functionality system.
- **Proximity payoff on existing flavor text** (game) — pays off a promise an existing easter egg's description already made but never had code behind it.
- **Delayed rare voice-line follow-up** (game) — extends the existing Providence death-commiseration system.
- **Redaction hover-reveal on an unwired page** (website) — reuses the existing `RedactedPeek` component on the one remaining static (non-interactive) redaction on the site.
- **Click-escalation into the existing terminal** (website) — reuses the existing visitor ledger + terminal event bus.

### Concepts rejected (and why)
- **Dual-examine "conspiracy" combo** (examining two unrelated objects within a window triggers a combined line) — real design merit, but needs a cross-entity pairing table and false-positive tuning; more design coordination than a single session's engineering budget without inventing an under-baked mechanism.
- **Curiosity-score meta-reward** (a station-wide rare stinger once a player has hit several other delight beats in one round) — would require every other delight system to report into a shared counter; that's a second front of coordination across systems that don't currently talk to each other, and risks feeling contrived rather than earned.
- **Cross-round "the station remembers you noticed" bookend** — the round-local variant is low-risk, but true cross-round persistence needs new storage; scoped out to avoid touching the DB layer for a flavor beat.
- **Oracle "stare, then whisper" follow-up** — the Oracle integration is Director-daemon-networked and off by default in this build (`SolreignOracleEnabled` defaults false, network-dependent); building on a disabled, external-service-dependent system was judged higher-risk than the budget warranted.
- **Wingmate beacon idle mumble** (proximity + linger timer near a beacon) — real merit, but proximity-while-lingering timers risk spamming busy areas without more tuning than this session had room for; parked rather than shipped half-tuned.
- **Website: Konami-adjacent keyboard sequence, console whisper, logo triple-click, cursor-hold afterglow** — all already shipped on the site before this batch started (Konami code + burst confetti, a console art/message, an idle-curfew tab-title nag, a click-escalation modal on the footer). Implementing "new" versions of these would have been duplication, not delight, so the website batch targeted the one remaining gap (an unwired static redaction) plus one genuinely new small interaction (click-escalation on a previously-inert decorative glyph) instead of padding to a third concept.

## Game batch — mechanisms + test results

All new pure logic lives in static-method "Rules" classes (matching `SolreignWristOrganizerRules`'s existing split) and is unit-tested in `Content.Tests`. Systems are thin ECS glue over that pure logic, matching every existing `_Solreign` system in this codebase.

1. **Reusable examine-escalation component/system** — tiers of examine-text keyed on a per-examiner examine count, plus an optional rare top-tier aside. Applied to two existing easter-egg items (see SPOILER section). Pure flavor text, no gameplay effect, no broadcast — intentionally has no CVar (nothing for an operator to need to kill).
2. **Wrist-organizer phrasing drift** — the existing self-scan readout (`SolreignWristOrganizerSystem`) now drifts its phrasing across a unit's own use-count this round: plain the first use, a wry aside the second, one settled line from the third use on. Gated by `solreign.delight.wrist_drift_enabled` (default on); off restores the original single-phrasing readout untouched.
3. **Proximity payoff on unwired flavor text** — one existing easter egg's description already promised a detection behavior that had no code behind it until now. A use-in-hand, cooldown-gated scan checks for nearby supernatural-anchor sources and *probabilistically* reports atmosphere-grade static — **see the "ORCHESTRATOR REVIEW: round-integrity redesign" section below; the first draft of this mechanism was rejected and redesigned before merge review.** Gated by `solreign.delight.static_receiver_enabled` (default TRUE); off makes the item inert on use rather than always reporting calm.
4. **Providence "second condolence"** — a rare, delayed follow-up to the existing death-commiseration line, only ever scheduled for a player who already received the first one this round (the base system's own once-per-round cap guarantees this). Two independent gates: a stricter coin flip than the base line (30% vs. 50%) decides whether a follow-up gets scheduled at all, then a random 5–15 minute delay (reusing the existing `PeriodicEffectTiming` interpolation) decides when. Gated by `solreign.providence.second_condolence_enabled` (default on) + `solreign.providence.second_condolence_chance`.

### Test results (game)
- **Unit tests** (`dotnet test Content.Tests`, filtered to the new/touched fixtures): **54/54 passed**, 6ms.
- **Build**: `dotnet build Content.Server -c DebugOpt` and `dotnet build Content.Tests -c DebugOpt` — both **0 errors** (840/287 pre-existing warnings, none from new code).
- **YAML linter**: `dotnet run --project Content.YAMLLinter -c DebugOpt` — **"No errors found in 35278 ms."**
- **Providence integration test**: `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~ProvidenceVoiceSystemIntegrationTest"` — **14/14 passed**.
- **Round-start / map-load smoke test**: `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~GameMapsLoadableTest"` — **246/246 passed**, confirming the new round-boundary subscriptions (Providence second-condolence scheduling reset) don't break round start on any map.

## Website batch — mechanisms + test results

1. **Redaction hover-reveal** — the lore page's "Subject 07" mention was the one remaining static, non-interactive redacted span on the site (every other redaction already routes through the existing `RedactedPeek` component). Wired it into `RedactedPeek` with three new fragments distinct from the homepage's own lore-fragment bank. Zero new logic — reuses the existing, already-accessible (keyboard + focus + touch), already-reduced-motion-safe component verbatim.
2. **Click-escalation cursor** — a new small client component wraps the lore page's decorative blinking-cursor glyph (previously inert, purely visual) in a real `<button>`. Three clicks within 1.5 seconds fires one escalating line into the site's existing terminal overlay via the `solreign:terminal` window event (the same event bus the Konami code and footer clause modal already use) — no new UI was built. Escalation state reuses the existing device-local visitor ledger (`bumpCounter`/`recordEgg`), same idiom as the footer's existing click-escalation modal. A miss (clicking too slowly) just silently resets.

Both are client-only leaf components (same pattern as the site's existing `ReturnVisitLine`) — no SSR/SEO content changed, no new external origins, no new motion (the cursor's blink animation already existed; the reveal typewriter effect was already reduced-motion-gated inside `RedactedPeek`).

### Test results (website)
- **Build**: `npm run build` — compiled successfully, all 15 routes statically generated.
- **Test suite**: `npm test` — **104/104 passed** (includes `rules.test.js`, `site-policy.test.js`, `public-surface.test.js` — none flagged the new components).
- **Moderation validator**: `python3 deploy/validate_moderation_policy.py` — **"moderation policy validation passed."**
- **Manual browser verification** (headless preview, `wrangler pages dev` against the real static export): confirmed the redaction reveal types out one of the new fragments on click, and confirmed 3 rapid clicks on the cursor glyph fires the `solreign:terminal` event with the expected escalating line AND that the existing Terminal overlay actually renders it (`aria-hidden="false"`, transcript contains the new line).

## ORCHESTRATOR REVIEW: round-integrity redesign (2026-07-16)

The orchestrator approved 6 of 7 delights and rejected the static receiver's first draft: a deterministic, on-demand, **binary** detector of the two antagonist marker components in range is not a delight — it is an antag wallhack that metagames hidden-identity rounds the moment the community discovers it. Redesigned same-day; round integrity outranks the egg.

**New signal model** (all math in the pure `SolreignStaticReceiverRules`, unit-tested):

1. **Ambiguous sources** — a scan counts "something nearby" if ANY of six component types is in range: the two antagonist markers, moon-touched crew, ordinary science/salvage xenoartifacts (`XenoArtifactComponent`, upstream shared), consecrated-ground markers, or recharge-pod props. A crackle therefore never uniquely implies "antag nearby" — the chapel, the science wing, and a cargo delivery all crackle too.
2. **Noisy signal** — probabilistic, not binary: near a real source the receiver crackles only ~60% of scans (`NearbyCrackleChance`, DataField); with nothing in range it still false-crackles ~12% of scans (`FalseCrackleChance`). A crackle can be nothing; silence can be something.
3. **Not save-scummable** — the roll is a deterministic stable hash (splitmix64 finalizer — deliberately not `HashCode.Combine`, whose seed randomizes per process) of (item entity id, round id, time bucket), where the bucket length is the scan cooldown. Re-using the item inside one window re-yields the *same* answer; the noise cannot be averaged away by spamming or by save-scum-style re-rolls.
4. **Cooldown raised 4s → 60s** (the first draft's 4s was far too permissive for room-sweeping); popup remains holder-only (`PopupEntity(..., args.User, args.User)`).
5. **Tiered atmosphere text** — the single "something nearby does not read as ordinary crew" line (which oversold the signal as radar) is replaced by three variants picked by an independent seeded roll: "faint static" / "a low hum" / a rare (~15%) "the static almost arranges itself into words." None of them names a direction, distance, or kind of source; the calm line was also softened ("Or perhaps it's just static.").
6. **CVar** — `solreign.delight.static_receiver_enabled` unchanged, **default TRUE** (confirmed): the ambiguity+noise redesign is what makes default-on safe. Off = item inert on use.

**Redesign test results** (full battery re-run after the change):
- Unit tests (new/touched `_Solreign` fixtures incl. 18 new `SolreignStaticReceiverRulesTests` covering the probabilistic contract with exact seeded rolls — determinism, salt independence, unit-interval range, rough uniformity, can-miss / can-false-positive, clamping, variant tiers): **72/72 passed**.
- Build: `dotnet build Content.Tests -c DebugOpt` — **0 errors**.
- YAML linter: **"No errors found in 46304 ms."**
- Providence integration test: **14/14 passed**.
- GameMapsLoadableTest: **246/246 passed** (re-run after redesign).

## Commits
- Game: `feat/delight-eggs` @ `ea1d3f2e61` — "Add delight-eggs batch: 5 reactive surprises across easter eggs + Providence"
- Game (redesign): `feat/delight-eggs` @ `4be0ac38e0` — "Redesign static receiver per orchestrator round-integrity review"
- Website: `feat/website-v2` @ `5dcd17ba` — "Add two lore-page delights: redaction reveal + click-escalation cursor"

---

## SPOILER SECTION — internal reviewers only, never quote publicly

- Examine-escalation component (`SolreignCuriosityExamineComponent`/`System`/`Rules`) applied to **SolreignEgg08** (flask of golden grace) and **SolreignEgg11** (high-altitude winged berry — one of the 12 animated eggs). Tiers unlock at examine counts 1/3/6, plus a 15% chance of one extra aside line once at the top tier.
- Proximity payoff (`SolreignStaticReceiverComponent`/`System`) wired onto **SolreignEgg19** (the static-emitting pocket receiver / Silent-Hill-radio homage), whose existing description already claimed it "crackles with static whenever bad vibes... are nearby" — this batch is the first code behind that claim. Post-redesign: scan radius 5 tiles, 60s cooldown, sources = `SolreignVampireComponent` / `SolreignWerewolfComponent` / `SolreignMoonTouchedComponent` / `XenoArtifactComponent` / `SolreignChapelGroundComponent` / `SolreignCoffinComponent`; ~60% crackle near a source, ~12% false-crackle otherwise, seeded per (item, round, 60s window).
- Wrist-organizer drift touches **SolreignEgg15** (the wrist-mounted post-apocalyptic organizer) only — no new entity, extends its existing use-in-hand readout.
- Providence second condolence has no in-world location — it is a server-side timer keyed off the existing death-commiseration beat, fires as a private popup + audio line wherever the player happens to be standing 5–15 minutes later.
- Website: the redaction reveal lives in `/lore`, in the "Season 1 — The Ledger Wakes" section's "It has questions about Subject 07" line. The cursor-click egg lives in the same section, on the blinking cursor at the end of "The station is in open beta. Season 1 begins soon▮".
