using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Antags;

/// <summary>
///     Raised when a Nocturnal Acquisitions Specialist finishes sipping a Medbay blood pack on
///     themselves (spec §4.2). No payload needed — target/user/used are already on the base
///     <see cref="DoAfterEvent"/> via the <see cref="Content.Shared.DoAfter.DoAfterArgs"/> that started
///     it. Same shape as upstream <c>HealthAnalyzerDoAfterEvent</c>.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SolreignVampireBloodPackDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>
///     Raised when a willing donor finishes the "offer a donation" consent do-after on a vampire
///     (spec §4.2, anti-grief rule 1: no attack-shaped feeding — the DONOR is <c>args.User</c> here,
///     the vampire is the do-after's target).
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SolreignVampireDonationDoAfterEvent : SimpleDoAfterEvent
{
}
