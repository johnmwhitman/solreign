# CHEAP-ADDS receipt — 2026-07-17

**Verdict: GREEN** (build + full unit suite + Solreign integration suite + YAML linter, all clean)

- **Tree:** `/Users/johnwhitman/AI/solreign-trees/cheap-adds`, branch `feat/acts-cheap-adds`
  (off `origin/master` `481bcc212e`)
- **Brief:** PROVIDENCE-ACTS item 5 "cheap adds", per
  `docs/council/2026-07-16-design-magnetism.md` item 5 — chirp emote, social-first award
  toasts, third-visit wingmate offer
- **Lane history:** a prior agent built most of the feature then died mid-lane (uncommitted).
  This run RECOVERED that work (13 files, all uncommitted in the worktree), fixed it forward,
  and finished the lane — recovered-vs-new breakdown below.
- **Run:** 2026-07-17, foreground/blocking, sequential dotnet invocations, no merges, no pushes.

## What shipped

### 1. Chirp emote (`SolreignChirp`)
- `Resources/Prototypes/_Solreign/emotes.yml` — new vocal emote, radial-menu discoverable
  (Available=true, Category=Vocal, whitelisted to `Vocal` bodies, silicon-blacklisted), icon
  reuses the existing vanilla `Interface/Emotes/chirp.png` (no new art). Chat triggers are
  distinct multi-word phrases (`chirps hello`, `chirps a greeting`, `chirps thanks`,
  `chirps cheerfully`) — the vanilla `Chirp` owns the single words, and trigger matching is a
  whole-message lookup, so no collision (uniqueness pinned by a new integration assertion).
- **Keybind choice: menu-only.** Emotes have no per-emote key surface in this fork and claiming
  a fresh default key risks collisions — noted per the brief's rail.
- **Audio choice: NONE (changed from the dead agent's draft).** The rail allows only existing
  CC0 `_Solreign` sounds or none. Audit result: every file in
  `Resources/Audio/_Solreign/attributions.yml` is in-house **CC-BY-SA-3.0** (music loops /
  announcer voice — nothing chirp-shaped, and none CC0), and the dead agent's pick
  (`/Audio/Animals/nymph_chirp.ogg`) is **CC-BY-SA ParadiseSS13** — neither CC0 nor
  `_Solreign`. It fails the rail under any reading, so the emote ships deliberately silent;
  the wiring was removed (`SolreignSocialFirstsSystem` no longer touches audio).

### 2. Social-first award toasts (once-per-account-EVER via the Season Ledger)
- `social_firsts` table (`user_id, flag_id` PK — the `first_death` pattern generalized to one
  row per (account, flag)); atomic `INSERT … ON CONFLICT DO NOTHING` claim =
  **write-before-dispatch**: the claim is taken first, the toast dispatches only on the winning
  claim; a racing double-detect can never double-toast.
  (`SeasonLedgerStore.SocialFirsts.cs`, `SeasonLedgerSystem.SocialFirsts.cs`, schema battery in
  `SeasonLedgerStore.cs`.)
- Three milestones, closed flag vocabulary (`SolreignSocialFirstFlags`):
  `chirp_answered` (another player chirped back within 15 s, same map, ≤10 tiles),
  `healed_by_another` (a different account's heal restored damage),
  `item_received` (an item released from another account's hands landed in yours within 20 s —
  covers drop-and-pickup, throw, strip-menu placement via the two hand events).
- Detection = pure observation on events those systems already raise
  (`SolreignSocialFirstsSystem` + pure `ChirpAnswerTracker` / `HandOffTracker`); toasts reuse
  the aliveness-wave award-popup helper via a new no-number sibling
  (`SolreignAwardPopup.ShowMilestone` — social firsts move NO ledger value; a fabricated "+N"
  would violate the popup honesty law) plus a screenshot-surviving private chat mirror.
- Claims gated to active rounds (the first-death F1 rule); career-scoped — a season bump never
  resets a claim (tested).

### 3. Third-visit wingmate offer (`SolreignWingmatePromptSystem`)
- Daemon-free by construction: career `Tours >= 3` from the Season Ledger, "wingmates enabled"
  = `CCVars.SolreignWingmatesEnabled` (re-checked at fire time), "never guided" honestly
  approximated as *not currently volunteering* (`WingmateSystem.IsVolunteeringGuide`, a new
  read-only surface) + *prompted at most once ever* (`wingmate_prompt` claim row) — no
  persistent guided/guide history exists in the substrate, and the approximation is recorded in
  the class doc. One private popup + chat beat, delayed 14 s so the spawn beats read as a
  sequence (Welcome immediate → first-shift 8 s → this).

### Rails compliance
- **Additive-only:** zero vanilla files edited; three existing `_Solreign` files extended
  (SolreignAwardPopup +ShowMilestone, WingmateSystem +IsVolunteeringGuide read-only,
  SeasonLedgerStore +schema/battery); everything else is new files.
- **One CVar:** `solreign.social_cheap_adds`, **default TRUE** — every behavior behind it is
  inert without other players present (a chirp with nobody in range is a text emote; heal /
  hand-off / answered-chirp all need a second account; the wingmate prompt needs wingmates
  enabled besides), so default-on is honest at population 1.
- **PG-13 house voice:** `social-cheap-adds.ftl`, corporate-sinister-but-warm, closed
  vocabulary (no variables render player/daemon text; the popup's `$reason` is loc-resolved
  from the same file).
- **Zero daemon dependency:** everything reads the ledger SQLite + round-local ECS state.
- **SeasonLedgerDbPath.Resolve(cfg,res) law:** every integration-test ledger read resolves
  through the production helper; **QueueDel→WaitRunTicks(1)** followed for spawned test
  entities.

## Recovered vs new

**Recovered from the dead agent (kept, was uncommitted in the worktree):** CVar
(`CCVars.SolreignSocial.cs`), emote prototype + ftl copy pack, `SeasonLedgerStore.SocialFirsts.cs`
+ system partial + schema battery, `ChirpAnswerTracker` / `HandOffTracker` /
`SolreignSocialFirstFlags` / `SolreignSocialFirstsSystem` / `SolreignWingmatePromptSystem`,
`SolreignAwardPopup.ShowMilestone`, `WingmateSystem.IsVolunteeringGuide`, and all three unit-test
files (24 tests).

**Fixed/added by this run:**
- Fixed 2 compile errors (missing `Content.Server.GameTicking.Events` usings — the dead agent
  never built).
- **Removed the audio wiring** (rail violation above) — `SoundSpecifier`, `SharedAudioSystem`
  dependency, and the PlayPvs call; emote + system docs updated to record the license audit.
- **New:** `SolreignSocialCheapAddsIntegrationTest.cs` (6 tests: CVar default + all 11 copy
  keys resolve; emote prototype well-formed + global trigger-uniqueness law + usable by the
  real player mob; real chirp through the real chat system reaches the tracker; real
  drop/self-pickup tracks but never awards; claim→deliver lands exactly once EVER in the live
  ledger file, surviving a process-cache wipe; wingmate prompt = third-visit-only, fires once,
  never repeats) + a small internal seam `TryAwardForTests` (every real detector needs a second
  account a single pooled client cannot fabricate — `MindSystem.SetUserId` rejects unknown
  users).
- This receipt.

## Verification (all Release, this tree, 2026-07-17)

```
dotnet build -c Release
```
**Clean — 50 projects, 0 errors** (warnings are pre-existing NuGet advisory/prune + obsolete-API
noise; the one new CS0618 — `DamageChangedEvent` obsolete — matches the 8 pre-existing vanilla
subscribers of the same event).

```
dotnet test Content.Tests -c Release --no-build
```
**Passed! Failed: 0, Passed: 2322, Skipped: 3, Total: 2325** (baseline 2298+ ✓; skips are the
known Windows-only ACL pair + TestAlertManager).

```
dotnet test Content.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~Solreign"
```
**Passed! Failed: 0, Passed: 313, Skipped: 1, Total: 314** (baseline 307 + 6 new ✓; skip is the
known `PersistentBlockSurvivesRoundRestartAndPreventsOffer`).

```
dotnet run --project Content.YAMLLinter -c Release --no-build
```
**No errors found** (validates the new `emotes.yml` prototype).

## Skipped / deliberate non-goals

- **Emote audio** — see the license audit above; ships silent.
- **Default keybind** — menu-only (no per-emote key surface; collision risk).
- **True "never guided" history** — no substrate for it; honest approximation documented.
- **Two-client positive milestone integration test** — the `WithSecondClient` machinery exists
  but the pairing/resolution logic is exhaustively unit-tested and the claim+deliver path is
  integration-tested via the seam; a second real client adds cost, not coverage, here.
- No merges, no pushes — branch left for review.
