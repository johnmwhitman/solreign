using System.Linq;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Talent Acquisition Specialist — skeleton + two working abilities (Transform/Revert live in the
///     Transform partial, Arm Blade in the ArmBlade partial, Absorb in the Absorb partial). Spec:
///     docs/specs/2026-07-11-changeling-spec.md. Clean-room design from the SS13 concept only — see the
///     spec's provenance note.
/// </summary>
public sealed partial class SolreignChangelingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private HumanoidProfileSystem _humanoidProfile = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignChangelingComponent, ComponentStartup>(OnStartup);

        InitializeAbsorb();
        InitializeTransform();
        InitializeArmBlade();
    }

    /// <summary>
    ///     Captures the changeling's own identity BEFORE any Transform ever runs, so Revert (Transform
    ///     partial) always has a true form to return to — same "snapshot your own state before mutating
    ///     it" idiom the Werewolf's Waning-window revert already relies on (spec §2.2).
    /// </summary>
    private void OnStartup(Entity<SolreignChangelingComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.TrueForm = CaptureSnapshot(ent.Owner);
        GrantAbilityActions(ent);
    }

    /// <summary>
    ///     Reads a humanoid's current identity into a portable, entity-independent
    ///     <see cref="ChangelingIdentitySnapshot"/> (spec §2.1). Used both for absorbing someone else's
    ///     identity and for capturing the changeling's own true form at startup. Returns null if
    ///     <paramref name="target"/> isn't a humanoid with the expected profile component — absorbing a
    ///     non-humanoid isn't meaningful (no identity to copy).
    /// </summary>
    private ChangelingIdentitySnapshot? CaptureSnapshot(EntityUid target)
    {
        if (!TryComp<HumanoidProfileComponent>(target, out var profile))
            return null;

        // Field reads mirror IdentitySystem.GetIdentityRepresentation's own cross-system reads of
        // HumanoidProfileComponent (Content.Shared/IdentityManagement/IdentitySystem.cs) — that system
        // isn't [Access]-restricted against outside reads, only against outside writes.
        var name = Name(target);
        var characterProfile = new HumanoidCharacterProfile()
            .WithName(name)
            .WithAge(profile.Age)
            .WithSex(profile.Sex)
            .WithGender(profile.Gender)
            .WithVoice(profile.Voice)
            .WithSpecies(profile.Species);

        // Organ-level appearance (skin/eye color, markings) — the exact inverse of ApplyProfiles/
        // ApplyMarkings below (SharedVisualBodySystem.Modifiers.cs).
        Dictionary<ProtoId<OrganCategoryPrototype>, OrganProfileData> organProfiles = new();
        Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> organMarkings = new();

        if (_visualBody.TryGatherMarkingsData(target, null, out var gatheredProfiles, out _, out var gatheredMarkings))
        {
            organProfiles = gatheredProfiles;
            organMarkings = gatheredMarkings;
        }

        return new ChangelingIdentitySnapshot(characterProfile, organProfiles, organMarkings);
    }

    /// <summary>
    ///     Applies a captured snapshot onto <paramref name="target"/> — Transform and Revert (Transform
    ///     partial) both funnel through here so the "become someone" and "become yourself again" paths
    ///     can never drift apart.
    /// </summary>
    private void ApplySnapshot(EntityUid target, ChangelingIdentitySnapshot snapshot)
    {
        _humanoidProfile.ApplyProfileTo(target, snapshot.Profile);
        _visualBody.ApplyProfiles(target, snapshot.OrganProfiles);
        _visualBody.ApplyMarkings(target, snapshot.OrganMarkings);
        _metaData.SetEntityName(target, snapshot.Profile.Name);
    }

    // --- Round-integration public API (spec §6 follow-up) ---
    //
    // SolreignChangelingComponent is [Access]-locked to this system (ANALYZER LAW), so the round
    // rule (SolreignChangelingRuleSystem) and the round objective (SolreignChangelingObjectiveSystem)
    // both read the Known Aliases roster through these accessors rather than touching the component
    // directly.

    /// <summary>
    ///     Read-only view of <paramref name="uid"/>'s Known Aliases roster (display names only, oldest
    ///     first), for round-end reporting. False (with an empty list) if <paramref name="uid"/> isn't
    ///     a changeling.
    /// </summary>
    public bool TryGetKnownAliases(EntityUid uid, out IReadOnlyList<string> aliasNames)
    {
        if (!TryComp<SolreignChangelingComponent>(uid, out var changeling))
        {
            aliasNames = Array.Empty<string>();
            return false;
        }

        aliasNames = changeling.KnownAliases.Select(alias => alias.DisplayName).ToList();
        return true;
    }

    /// <summary>How many identities <paramref name="uid"/> has absorbed so far. Zero if not a changeling.</summary>
    public int GetKnownAliasCount(EntityUid uid)
    {
        return TryComp<SolreignChangelingComponent>(uid, out var changeling) ? changeling.KnownAliases.Count : 0;
    }

    /// <summary><paramref name="uid"/>'s alias-roster cap (spec §3 rule 4). Zero if not a changeling.</summary>
    public int GetMaxKnownAliases(EntityUid uid)
    {
        return TryComp<SolreignChangelingComponent>(uid, out var changeling) ? changeling.MaxKnownAliases : 0;
    }
}
