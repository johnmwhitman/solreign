# SR-W-061 Phase 1 public-export contract handoff

**Status:** Phase 1 implementation, privacy proof, review repairs, and independent reviews are complete. The branch is ready for Claude's merge evaluation; no merge, push, deploy, or activation was performed.

## Exact branch bounds

- Branch: `feat/codex-game-public-export-contract`
- GAME base: `993c3233753dd176c6ed3801975cc9eeb61544e1`
- Task 4 starting head: `6ad55abb1bc0ff61559d06fd29341904140ce7dd`
- Task 4 implementation head: `344253e6fe894aef2ab608772ef073198d4d8db8`
- Privacy-diagnostic repairs: `fc4fe156fad09b074a359ca5626489b6435a9719`, `84865c196fcb41c3b0dd9d279f74ca333a7962dc`
- Reviewed code head: `84865c196fcb41c3b0dd9d279f74ca333a7962dc`
- Current GAME `master` used for merge-tree proof: `b74fe79d3f012a3862a3fa739b73fcf554c29ca9`

The commit containing this final receipt is documentation-only and intentionally follows the exact reviewed code head above. Claude should review the complete branch range from GAME base and can separately inspect the receipt-only tip.

## What Task 4 proves

`PublicExportContractTests` loads every prohibited canary seed and derivative and attempts every compatible typed scalar placement. String fields are always exercised; GUID, `long`, `int`, and timestamp fields are exercised only when the value parses syntactically. Publisher, stream, and sequence mutations rebuild the idempotency key so the target field's shape is assessed honestly. Timestamp and optional-event pairings likewise preserve unrelated cross-field invariants where applicable.

(Pre-amendment narrative — superseded by the orchestrator-review amendment further down: the frozen set is now FIVE rows classified `DeferredToProvenancePhase`.) The fixture's original eight `known_shape_collisions` rows were frozen explicitly and classified as `RequiresAuthoritativeProvenance`: one transport UUID, one sequence, three 64-hex build hashes, and three prototype-shaped map identifiers. Every other tested placement is classified as `RejectedByShape`. Known collisions are never canonicalized or signed. The later producer must create `message_id` internally as a random transport UUID and must not accept a player-derived identifier.

For rejected placements, the bounded validation-code name and fixed canonicalization/signing exception messages are scanned against every seed and derivative with ordinal case-sensitive and lowercase-normalized comparisons. A separately approved valid envelope is signed once; its canonical body, body hash, signing input, and signature are scanned for exact canary and derivative absence. Canonical JSON property names are parsed recursively and compared exactly, including lowercase-normalized equality, against fixture aliases. Generic substring scanning of `message` is deliberately not used because `message_id` and `message_type` are contract-owned fields.

Authoritative provenance remains a hard gate for a later producer branch; it is not and cannot be represented as a Phase 1 schema property.

## Stable fixtures

- `server-snapshot-envelope-v1.schema.json`: `0a35f9a9af786b2f4bdb1cf128ee93d85950db1263ad0a165ad7013fabbb67fe` (updated by the orchestrator-review amendment below; `build_sha` pattern narrowed to 40-hex only)
- `server-snapshot-v1-signing-vector.json`: `d8e2b02f1b06420ee61a6646cf5dfbbdef4bfb982ab685d7c3e6647baa564bab` (unchanged)
- `prohibited-public-export-v1.json`: `9d58d9e09e007fc81f0c4eeda75e96f27f00d27c4df71ecf8ea92b89c1c90af2` (updated by the orchestrator-review amendment below; three `payload.build_sha` rows removed from `known_shape_collisions`, `disposition` field added to every remaining row, `phase_1_scope`/`phase_1_provenance_note` fields added)

Fresh `shasum -a 256` output on 2026-07-15 (post-amendment) matches all three fixture hashes above. The signing-vector fixture and its frozen canonical body/hash/signing-input/signature are byte-for-byte unchanged by the orchestrator-review amendment.

## Orchestrator-review amendment (2026-07-15, post-Codex handoff)

An independent orchestrator adversarial review of the reviewed code head (`84865c196f`) found four findings not caught by the Codex-side reviews described above. All four are fixed on `feat/orch-export-fixes` (based on this branch's head `89063ecfd2`); this section documents the two amendments that change fixture/doc content. Full detail lives in the orch-export-fixes commit messages.

- **P1 -- regex line-anchor ambiguity.** Every `PublicExportValidation` regex anchored with `^...$`. In .NET, `$` matches immediately before a single trailing `\n` as well as at the true end of string even without `RegexOptions.Multiline`, so a value like `"game-host-primary\n"` passed identifier validation and was then `string.Join('\n', ...)`-framed straight into `PublicExportSigner`'s signing input, corrupting the six-line verifier framing invariant. Fixed by anchoring every pattern with `\A...\z`. No fixture content changed.
- **P1 -- Phase 1 containment is lexical/schema-only; the fixture and this handoff must say so plainly, and no test may silently pass an accepted prohibited value.** `PublicExportContractTests.AssertProhibitedPlacements` previously relabeled any *accepted* (`Valid`) prohibited-canary placement as `RequiresAuthoritativeProvenance` and moved on. That framing was **not fully honest**: this library has no authoritative-provenance check anywhere, so an accepted prohibited value was a real Phase-1 containment gap dressed up as a documented exception. The fix keeps the frozen-count/frozen-row guard (an accepted placement outside the known list still fails the test), renames the disposition to `DeferredToProvenancePhase`, and requires every deferred row to carry an explicit `"disposition": "DeferredToProvenancePhase"` string in the fixture that the test cross-checks against the C# enum member name -- so the fixture and the test can never silently drift apart. Separately, three of the eight original rows (`payload.build_sha`, all three 64-hex canary derivatives) were found to have a genuine, non-overfit stricter shape rule available: this codebase's real build shas are Git SHA-1 commit hashes (40 lowercase hex characters), so `BuildShaPattern` was narrowed to `\A[0-9a-f]{40}\z` and those three derivatives are now `RejectedByShape` lexically, not accepted. `known_shape_collisions` therefore drops from eight rows to five (one `message_id` random-transport-UUID collision, one `sequence` collision, three `payload.map_id` prototype-shaped collisions) -- each of the five is lexically indistinguishable from a legitimate value of the same field and remains deferred to the later producer/exporter phase, not this pure/offline contract library. The `server-snapshot-envelope-v1.schema.json` documentary fixture's `build_sha` pattern was updated to match (`^[0-9a-f]{40}$`) so the published wire contract stays consistent with what the validator actually accepts.
- **P1 -- contract-freeze test for the record surface.** `CompatiblePlacements` (the prohibited-canary placement harness) was hand-maintained and could silently miss a newly added record member, and the no-prohibited-alias-leak test inspected only one baseline envelope's emitted properties. A new `PublicExportContractSurfaceTests` reflects over every public member of `PublicEnvelopeV1` and `ServerSnapshotV1`, reflects over the canonical writer's emitted JSON property paths for a fully-populated payload, and asserts exact parity against a frozen 22-member allowlist in both directions, plus that every member is both (a) actually enforced by `Validate()` (mutating it to a deliberately invalid value changes the result away from `Valid`) and (b) actually serialized. No fixture content changed by this finding; the existing `server-snapshot-envelope-v1.schema.json` 22-member shape already matched exactly.
- **P2 -- expired-at-publish snapshots validated.** `Validate()` never compared `PublishedAt` to `payload.FreshUntil`, so an envelope published at or after its own freshness deadline validated as `Valid`. Fixed with a new `PublicExportValidationCode.PublishedAtOrAfterFreshUntil` check (`PublishedAt >= FreshUntil` is rejected), inserted into the existing frozen precedence chain immediately after `InvalidFreshness`. No fixture content changed.

The Task 4 description below (`What Task 4 proves` / `known_shape_collisions` counts) reflects the **pre-amendment** state as Codex delivered it and is retained for the historical record; the paragraph above is authoritative for the current five-row, lexical-only Phase 1 scope.

## TDD receipts

- Task 1 fixture-only checkpoint: 3 passed, 0 failed. Contract RED then failed with six `CS0234` errors because the public-export production namespace did not exist.
- Task 2 focused GREEN: 118 passed; all SOLREIGN: 1,268 passed and two documented Windows-only skips after precedence review repairs.
- Task 3 RED: exactly two `CS0234` errors for absent canonicalizer/signer types. Focused GREEN: 26 passed; all SOLREIGN: 1,294 passed, 0 failed, two documented Windows-only skips.
- Task 4 RED: the focused build exited 1 with exactly four `CS0103` errors for the deliberately absent prohibited-fixture helper entrypoints (`LoadProhibitedFixture`, `AssertProhibitedPlacements`, and `AssertApprovedOutputContainsNoProhibitedData`).
- Task 4 focused GREEN, rerun outside the restricted vstest socket sandbox: 149 passed, 0 failed, 0 skipped.
- After both privacy-diagnostic repairs, the focused result remained 149 passed, 0 failed, 0 skipped; all SOLREIGN remained 1,296 passed, 0 failed, and two expected Windows-only skips.
- The in-sandbox focused command compiled `Content.Tests.dll`, then aborted before test execution at `System.Net.Sockets.SocketException (13): Permission denied` in `SocketServer.Start`; no passing claim is derived from that aborted run.

## Verification battery

- PublicExport filter: **PASS** — 149 passed, 0 failed, 0 skipped.
- All SOLREIGN filter: **PASS** — exit 0; 1,296 passed, 0 failed, two expected Windows-only skips, 1,298 total, 3 seconds.
- Release `Content.Server` build: **PASS** — exit 0, 0 errors, 839 existing warnings. Restricted vulnerability feeds emitted `NU1900` warnings.
- `git diff --check`: **PASS**, no output.
- `git diff --diff-filter=D --name-only master...HEAD`: **PASS**, empty deletion list.
- Bounded production-path scope scan: **PASS**. No `HttpClient`, `IStatusHost`, `File.`, `Directory.`, `SeasonLedger`, `DirectorChannel`, `IConfigurationManager`, CVar registration, lifecycle subscription, producer/factory, runtime world/account/round/session/player read, or real key material exists in `Content.Server/_Solreign/PublicExport`.
- Merge-tree validation of current GAME `master` `b74fe79d3f` with reviewed code head `84865c196f`: **PASS**, exit 0; synthetic tree `2da6c1fe2f034b1fec1de11e24abbb1b73dc8fa8`.

The Release warning count is recorded, not described as pristine. It consists of repository-wide existing analyzer/obsolete-API warnings plus restricted-feed `NU1900` warnings; the build has zero errors.

## Independent review state

- Correctness: no implementation P0-P2 findings. The sole proof-quality P3 was this receipt's stale Task 4 status/head placeholders; corrected here.
- Cryptography/vector: no cryptography P0-P3 findings. An independent Ruby/OpenSSL recomputation confirmed the 614-byte body, frozen SHA-256, exact six-line signing input, and HMAC signature. Its sole P3 was the same stale receipt state; corrected here.
- Privacy/scope: initial proof-quality P3 findings showed that failed NUnit constraints could print prohibited values or unexpected exception messages. Repairs `fc4fe156fa` and `84865c196f` replaced them with boolean-, ordinal-, and field-label-only diagnostics plus local exception capture. Final privacy re-review: **PASS**, no remaining P0-P3 findings.
- Post-repair verification: PublicExport 149/149; all SOLREIGN 1,296 passed, 0 failed, two expected Windows-only skips; test build clean; no production source changed after the zero-error Release build.

The reviewed implementation contains no unresolved P0-P3 finding. The final receipt wording was rechecked separately because it follows the reviewed code head.

## Hard boundaries

No exporter, outbox, network transport, secret, CVar, lifecycle hook, deploy, or production action exists in this branch.
SR-W-061 remains active/research only and cannot enter canary until durable local outbox, restart/network recovery, preview ingest, and live privacy evidence exist.
Claude remains the only merge operator.

No exporter, network client, outbox or filesystem write, runtime producer, Neon or Cloudflare access, CVar, secret lookup, lifecycle hook, merge, push, deploy, restart, or production access was performed while preparing this handoff.
