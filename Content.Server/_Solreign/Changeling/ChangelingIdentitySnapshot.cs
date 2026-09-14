using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     A frozen copy of one humanoid's identity, captured at absorb (or, for the changeling's own
///     true form, at <c>ComponentStartup</c>). Spec: docs/specs/2026-07-11-changeling-spec.md §2.1.
///
///     Deliberately plain data, no live <see cref="Robust.Shared.GameObjects.EntityUid"/> reference to
///     the source body — the whole point of snapshotting is that it survives the source dying,
///     respawning, or being deleted long after the round moves on.
///
///     <see cref="Profile"/> carries name/species/sex/gender/age/voice (the fields
///     <c>HumanoidProfileSystem.ApplyProfileTo</c> reads) — it is built via <c>HumanoidCharacterProfile</c>'s
///     own immutable `With*` builder methods rather than hand-rolled fields, so applying it is a single
///     call into the same upstream system every other profile application in this codebase uses.
///     <see cref="OrganProfiles"/>/<see cref="OrganMarkings"/> are the organ-level appearance data
///     (skin/eye color, markings) as returned by <c>SharedVisualBodySystem.TryGatherMarkingsData</c> —
///     applying them back is the exact inverse: <c>ApplyProfiles</c>/<c>ApplyMarkings</c>.
/// </summary>
public sealed record ChangelingIdentitySnapshot(
    HumanoidCharacterProfile Profile,
    Dictionary<ProtoId<OrganCategoryPrototype>, OrganProfileData> OrganProfiles,
    Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> OrganMarkings);

/// <summary>
///     One entry in <see cref="SolreignChangelingComponent.KnownAliases"/> — spec §4's "Known Aliases"
///     roster. <see cref="SourceEntity"/> is kept only to gate re-absorbing the SAME body (spec §3 rule
///     2); it is never dereferenced after capture beyond that existence/identity check, so a stale Uid
///     (source deleted/round-recycled) is harmless — see the doc comment on
///     <see cref="SolreignChangelingComponent.AbsorbedSources"/>.
/// </summary>
public sealed record SolreignChangelingAlias(
    string DisplayName,
    ChangelingIdentitySnapshot Snapshot,
    EntityUid SourceEntity,
    TimeSpan AbsorbedAt);
