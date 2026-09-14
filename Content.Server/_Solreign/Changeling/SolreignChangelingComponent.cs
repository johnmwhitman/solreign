using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Marks a Talent Acquisition Specialist (spec: docs/specs/2026-07-11-changeling-spec.md) and holds
///     the identity roster + ability state. Server-only for this pass — no client UI/prediction is
///     needed yet for a skeleton + two abilities (spec §6: round integration, and by extension any
///     player-facing alias picker, is explicit follow-up).
/// </summary>
[RegisterComponent, Access(typeof(SolreignChangelingSystem))]
public sealed partial class SolreignChangelingComponent : Component
{
    // --- Absorb tuning (spec §2.1, §3) ---

    [DataField]
    public float AbsorbDoAfterSeconds = 4f;

    [DataField]
    public float AbsorbCooldownSeconds = 20f;

    [DataField]
    public int MaxKnownAliases = 6;

    /// <summary>Earliest time another absorb may complete (spec §3 rule 3).</summary>
    [ViewVariables]
    public TimeSpan NextAbsorbAllowed;

    /// <summary>Every identity absorbed so far, oldest first. Index count drives Transform (spec §2.2).</summary>
    [ViewVariables]
    public readonly List<SolreignChangelingAlias> KnownAliases = new();

    /// <summary>
    ///     Source entities already absorbed once (spec §3 rule 2 — no re-farming the same unconscious
    ///     body). Never dereferenced beyond a <c>Contains</c> check, so a stale Uid from a long-deleted
    ///     entity is harmless — worst case it merely can't be "re-absorbed" again, which was already true.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> AbsorbedSources = new();

    // --- Transform / Revert state (spec §2.2) ---

    /// <summary>The changeling's own identity, captured once at <see cref="Robust.Shared.GameObjects.ComponentStartup"/>, before any Transform.</summary>
    [ViewVariables]
    public ChangelingIdentitySnapshot? TrueForm;

    /// <summary>True while wearing an absorbed alias instead of <see cref="TrueForm"/>.</summary>
    [ViewVariables]
    public bool Transformed;

    [DataField]
    public EntProtoId TransformActionId = "ActionSolreignChangelingTransform";

    [DataField]
    public EntProtoId RevertActionId = "ActionSolreignChangelingRevert";

    [ViewVariables]
    public EntityUid? TransformActionEntity;

    [ViewVariables]
    public EntityUid? RevertActionEntity;

    // --- Arm Blade state (spec §2.3) ---

    [DataField]
    public EntProtoId ArmBladeActionId = "ActionSolreignChangelingArmBlade";

    [ViewVariables]
    public EntityUid? ArmBladeActionEntity;

    [ViewVariables]
    public bool ArmBladeExtended;

    /// <summary>Snapshot of the mob's own <c>MeleeWeaponComponent.Damage</c> before the blade overwrote it, restored on retract.</summary>
    [ViewVariables]
    public DamageSpecifier? OriginalMeleeDamage;

    /// <summary>Snapshot of the mob's own <c>MeleeWeaponComponent.Hidden</c> before the blade overwrote it.</summary>
    [ViewVariables]
    public bool OriginalMeleeHidden;

    /// <summary>Damage profile applied to the mob's own <c>MeleeWeaponComponent</c> while the blade is extended.</summary>
    [DataField]
    public DamageSpecifier ArmBladeDamage = new()
    {
        DamageDict = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> { ["Slash"] = FixedPoint2.New(12) },
    };
}
