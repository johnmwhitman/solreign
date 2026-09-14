# SOLREIGN Saltern map YAML recovery plan

Date: 2026-07-25
State: **implemented and locally verified; integration remains Claude-gated**

## Objective

Restore Saltern map loading after a textual splice placed the
`ComplianceTerminal` entity group after the YAML document terminator. Keep the correction
limited to document structure and accurate serializer metadata.

## Test-first sequence

1. Parse the committed map as a YAML stream and capture the line-specific failure.
2. Trace the map prototype to its exact resource and identify the introducing commit.
3. Move the existing document terminator after the final entity group.
4. Reconcile `meta.entityCount` with the actual unique entity UID count.
5. Re-run the static parser and count proof.
6. Run the existing Saltern `GameMapsLoadableTest`.
7. Run the entire `AntagGhostRoleTest` case source that previously failed during Saltern setup.
8. Obtain an independent read-only review.

## Boundaries

- No entity prototype, UID, transform, map placement, gameplay, engine, or CVar change.
- No unrelated map cleanup or serializer rewrite.
- No merge, push, deploy, restart, publication, activation, launcher, or hub mutation.
- Claude remains integrator.

## Rollback

Revert the single map-file commit. No schema, persistence, network, client, or engine rollback is
required.
