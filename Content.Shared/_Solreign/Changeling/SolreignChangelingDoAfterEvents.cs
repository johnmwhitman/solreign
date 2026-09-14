using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Changeling;

/// <summary>
///     Raised when a Talent Acquisition Specialist finishes the "Take Biometric Sample" do-after on an
///     unconscious target (spec: docs/specs/2026-07-11-changeling-spec.md §2.1). No payload needed —
///     the performer/target are already on the base <see cref="DoAfterEvent"/> via the
///     <see cref="Content.Shared.DoAfter.DoAfterArgs"/> that started it. Same shape as
///     <c>SolreignVampireDonationDoAfterEvent</c>.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SolreignChangelingAbsorbDoAfterEvent : SimpleDoAfterEvent
{
}
