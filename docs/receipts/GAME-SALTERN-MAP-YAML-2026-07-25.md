# GAME Saltern map YAML recovery receipt

Date: 2026-07-25
Game base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`
Verdict: **HANDOFF-READY — local correction proven; no integration authority**

## Root cause

`Resources/Prototypes/Maps/saltern.yml` correctly maps the `Saltern` prototype to
`/Maps/saltern.yml`. In the committed resource, line 70,871 ended the YAML document with `...`,
while line 70,872 began the final `ComplianceTerminal` entity group.

The introducing commit was `5d533f2f3d12973c40894bf3e10ef032672eb852`. It placed the sibling
`MemorialConsole` group before the terminator but accidentally spliced `ComplianceTerminal` after
it. Both new UIDs were otherwise unique, both prototypes existed, and parent UID `31` existed.

The malformed product map prevented Saltern from loading. `AntagGhostRoleTest` requests Saltern as
its test station, so all 28 generated cases failed in shared setup before testing antag behavior.

## Test-first evidence

Before correction:

```text
yaml.parser.ParserError: expected '<document start>', but found '<block sequence start>'
  in "Resources/Maps/saltern.yml", line 70872, column 1
```

Correction:

- move the existing `...` terminator after the `ComplianceTerminal` group;
- update `meta.entityCount` from `11452` to `11454`.

After correction, the static proof reports:

```text
documents=1
uid_count=11454
entity_count_metadata=11454
terminators=1
```

Real integration evidence, serialized with one heavy `dotnet` process at a time:

| Gate | Result |
|---|---|
| `PostMapInitTest.GameMapsLoadableTest` filtered to `Saltern` | 1 passed / 0 failed / 0 skipped, 23s |
| `GameRules.AntagGhostRoleTest` | 28 passed / 0 failed / 0 skipped, 2m55s |
| `git diff --check` | clean |

The first sandboxed VSTest attempt compiled successfully but its local test transport was denied a
loopback socket (`SocketException (13): Permission denied`). The same built artifact was then run
outside that filesystem/network sandbox with explicit approval; both results above are from those
real test executions.

## Independent review

A read-only final review returned **PASS with no P0–P3 findings**. It independently confirmed:

- the introducing commit and line-specific document-boundary defect;
- exactly one terminator, now after both final entity groups;
- 11,454 nested entities, 11,454 unique UIDs, and matching metadata;
- uniqueness and validity of UIDs `999991`/`999992`, their prototypes, and parent UID `31`;
- the direct relevance and proportionality of the 1-case map-load and 28-case antag proofs.

## Scope and compatibility

The diff changes one metadata integer and relocates one YAML terminator. It does not add, remove, or
move an entity. It does not alter RobustToolbox, serialization format, database state, network
messages, launcher discovery, hub registration, or client/server build pins.

This receipt is evidence, not merge, deploy, activation, or release authorization.
