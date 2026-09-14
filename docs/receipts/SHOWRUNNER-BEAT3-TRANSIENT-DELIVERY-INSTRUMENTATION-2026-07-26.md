# Beat 3 transient delivery instrumentation receipt — 2026-07-26

## Verdict

`PASS-AS-QUALIFIED-CANONICAL-SNAPSHOT / CURRENT-HEAD DELTA UNQUALIFIED / STRICT BUILD HOLD`

This lane gives the existing Season 1 Beat 3 rule a bounded, server-local account of its latest
synchronous clue-delivery attempt. It also makes an existing-but-empty transcript dataset fail soft
instead of throwing during random selection.

This mutable server-local field is transient diagnostic state only. It is not a
`StationCapabilityObservationV1`, eligibility result, evidence packet, export contract, planner
descriptor, authorization token, audit record, or execution authority; no downstream consumer is
included.

## Identity

- Branch base (cached `origin/master` at cut):
  `6368757ee6b980db829ad06c7c21b08a9f61c6f1`.
- Pre-integration cached `origin/master`:
  `18257abd69c7b5681789d3f152a63162a38dfa2e`.
- Conflict-free synthetic implementation merge tree against that pre-integration master:
  `edb63e362460b3b368508d95e03bc92699873117`.
- Claude integration merge now on cached `origin/master`:
  `8054536cc045bd349c89e066c2aa7955b6f35a09`.
- Qualified canonical snapshot tree:
  `a00c0ac0e089adb619f96e45cbb024bf84b72c00`.
- Current cached `origin/master` after qualification:
  `70eb235834d1e43e2e7500e9b8dbbbeb64273df0`.
- Implementation commit: `b53f4e4eca6422011a4f4c2b185223866614f53c`.
- Implementation tree: `cc3244cf9ad09b5b02850e3d7ef11e41485ccf96`.
- Existing release-evidence code candidate: `aa5a51107bb312d2e7232ec70ca7327e0fe927b2`.
- Release-evidence branch documentation tip used as composition base:
  `eefe0de229b29773615af3a7ad80dcbabb170c53`.
- Exact disposable composition commit: `4763c1fff44bd4f499ed1ba2d8894d8f077bc467`.
- Exact disposable composition tree: `0ddc65ad4b88ea2e90221498e07b270572dea5f3`.
- RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`.

`eefe0de...` differs from the previously tested `aa5a511...` code candidate only by the
release-train receipt/handoff commits on that branch. No live branch, server, package, or deployment
was mutated by the disposable composition.

After the branch was cut, master first gained unrelated Shadow species viability changes and then
Claude's `integrate/wave4` merged the implementation commit plus the parked release-train,
Directives, Showrunner planner, sprite-gate, and related handoffs. This lane performed no merge.
The full battery below was run on the recorded pre-integration composition, not on exact canonical
merge `8054536...`. A separate clean detached qualification of that exact canonical commit then
passed the focused and full behavior batteries recorded below.

After that terminal qualification, `origin/master` advanced by one commit, `70eb235...`, touching
only `SolreignPlayableSpeciesViabilityTests.cs` and `Resources/Prototypes/Species/shadow.yml`.
Those paths do not collide with this lane, but the newer head was not rerun and is not covered by
the exact-snapshot claim.

Production/test source SHA-256:

| File | SHA-256 |
| --- | --- |
| `SolreignGhostCabinetsRule.cs` | `f6d896c950727c5b21b1ebeb819be631f0de3c4565f36ec4a8ae6d960d809e16` |
| `SolreignGhostCabinetsRuleComponent.cs` | `10523e243baf2c6000a5010434d810dbb16cfd8a2017ace2a47fe81c92e395f0` |
| `SolreignGhostCabinetsDeliveryRules.cs` | `1cff9c6417adace21cf596139988e3fdff4ecb37ee403fa34d185c1cae5a0344` |
| `Season1GhostCabinetsDeliveryObservationIntegrationTest.cs` | `a03adc82426431c0834886aa40c46cc0cdb5405f00c92e872dda8216b6c2a80f` |
| `SolreignGhostCabinetsDeliveryRulesTests.cs` | `5f8072065e6b65382e99937f1c505dcd4313b43c1c3924ecd24d9b28737ee7ae` |

## Production behavior

The rule resets `LastObservation` at each `Started` delivery attempt and records these handled
outcomes:

- `NoStation`;
- `TranscriptDatasetUnavailable` for a missing or empty localized dataset;
- `NoInsertableStorage`;
- `InsertFailed`;
- `DroppedAdjacent` when the storage API returns success without containment, as it does for an
  open storage entity; and
- `Inserted` only when both `InsideEntityStorageComponent.Storage` and the selected storage's
  `Contents.ContainedEntities` confirm the same locker.

For valid production content, announcement cadence, random transcript selection, clue content,
locker selection, and clue lifetime are unchanged. For malformed empty-dataset content, the rule
now records `TranscriptDatasetUnavailable` and remains announcement-only instead of calling
`Pick()` on an empty collection.

The result contains no entity, station, map, location, player, account, arbitrary text, or error
payload. It is a plain non-serialized `[ViewVariables]` field on a server-only rule component.

## Verification

All heavy .NET work was serialized with `-m:1`, `-nodeReuse:false`, and
`-p:UseSharedCompilation=false`.

| Gate | Result |
| --- | --- |
| Pure delivery classifier | 3 passed / 0 failed |
| Focused fresh-pair integration fixture on the isolated branch | 6 passed / 0 failed |
| `git diff --check` | clean |
| Strict integration-project build on branch base | baseline-red with the same 5 unrelated existing errors |
| Focused classifier on release-train composition, normal analyzers | 3 passed / 0 failed |
| Focused fresh-pair integration fixture on composition | 6 passed / 0 failed |
| Full `FullyQualifiedName~Solreign` integration band on composition | **479 passed / 0 failed / 4 expected skips / 483 total**, 12m59s |
| Focused classifier on qualified canonical snapshot `8054536...`, normal analyzers | 3 passed / 0 failed |
| Focused fresh-pair integration fixture on qualified snapshot | 6 passed / 0 failed |
| Full `FullyQualifiedName~Solreign` integration band on qualified snapshot | **487 passed / 0 failed / 4 expected skips / 491 total**, 11m26s |
| Strict integration-project build on qualified snapshot | **HOLD: same 5 errors**, 1m23s |

The strict integration-project build's five errors are:

- `NetworkedComponentParityTest.cs`: one existing `CS8604`;
- `SolreignCorporateProjectIntegrationTest.cs`: four existing `RA0033`.

They reproduced without this lane on the branch/composition and remain exact on canonical
`8054536...`. The focused and full integration behavior runs therefore used
`-p:RunAnalyzers=false -p:TreatWarningsAsErrors=false`; this is behavioral composition evidence,
not a strict-build-green claim. The focused unit classifier passed with normal analyzers on the
composition. Offline restore also emitted existing `NU1900` vulnerability-feed warnings because
the sandbox could not reach package feeds.

Exact test/build invocations (run from the relevant worktree):

```text
dotnet test Content.Tests/Content.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SolreignGhostCabinetsDeliveryRulesTests -m:1 -nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly --logger "console;verbosity=minimal"

dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~Season1GhostCabinetsDeliveryObservationIntegrationTest -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -clp:ErrorsOnly --logger "console;verbosity=minimal"

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~Solreign -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -clp:ErrorsOnly --logger "console;verbosity=minimal"
```

The integration fixture sets `Fresh = true`, snapshots pre-existing clue UIDs, and asserts only
post-baseline artifacts. Its success topology uses a distinct station entity, member grid, and
closed `LockerSteel`. The open-storage case proves one new uncontained clue remains on the selected
station and is absent from the locker's contents. Missing, empty, no-station, and no-insertable-
storage cases prove no new surviving clue.

## Evidence limits

This receipt proves bounded outcomes for the enumerated non-exceptional delivery branches on a
synthetic test grid. It does not prove:

- any of the seven rotation maps, map-pool portability, Atlas state, or exact-map eligibility;
- player discoverability, accessibility, retention, or clue persistence;
- event cadence, completion, cleanup, restart, or any other lifecycle property;
- multi-station selection or every storage prototype/full/locked state;
- a production-seam `InsertFailed` (that outcome has pure classifier coverage only);
- invalid clue-prototype, paper-system, localization, or unexpected-exception closure;
- telemetry, persistence, immutable evidence, export, signing, freshness, or trusted provenance;
- Showrunner scheduling, preview, confirmation, authorization, runtime consumption, activation,
  canary, or production readiness.

This lane performed no push, merge, deploy, restart, activation, live-box access, launcher/hub
mutation, player-data access, or credential use. Claude's canonical integration and the green
qualified-snapshot behavior battery do not broaden this receipt into current-head, strict-build, deploy, or
activation authority.
