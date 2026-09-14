namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Round objective condition: requires a Talent Acquisition Specialist to have collected N
///     identities (spec §6 round-integration follow-up; spec §1 PG rule — Absorb is a nonlethal
///     COPY, never a kill, so this objective can only ever be advanced by the harmless Absorb
///     ability, never by hurting anyone). Depends on <c>NumberObjectiveComponent</c> for its
///     min/max/target, same pairing upstream's own <c>ChangelingUniqueIdentityConditionComponent</c>
///     uses (Content.Server/Objectives/Components — a different, unrelated antag's objective, not
///     read or copied as a template here beyond the pairing idiom itself).
///
///     Holds no state of its own: <see cref="SolreignChangelingObjectiveSystem"/> reads progress
///     live off the changeling's own roster through <see cref="SolreignChangelingSystem"/>'s public
///     accessor (the component itself stays [Access]-locked to that system per ANALYZER LAW), rather
///     than tracking a separate counter that could drift from the source of truth.
/// </summary>
[RegisterComponent, Access(typeof(SolreignChangelingObjectiveSystem))]
public sealed partial class SolreignChangelingCollectIdentitiesConditionComponent : Component;
