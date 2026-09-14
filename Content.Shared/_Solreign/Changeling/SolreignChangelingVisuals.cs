using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Changeling;

/// <summary>
///     <see cref="Robust.Shared.GameObjects.SharedAppearanceSystem"/> data key for the Arm Blade toggle
///     (spec: docs/specs/2026-07-11-changeling-spec.md §2.3). Client-side sprite-layer reaction to this
///     key is a follow-up (the bespoke arm-blade art is a separate sprite-factory item, same split the
///     Werewolf's wolf-form prototype already documents) — this pass wires the data flag only.
/// </summary>
[Serializable, NetSerializable]
public enum SolreignChangelingVisuals : byte
{
    ArmBladeExtended,
}
