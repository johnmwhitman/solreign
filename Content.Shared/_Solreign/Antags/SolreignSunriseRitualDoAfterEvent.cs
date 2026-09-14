using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Antags;

/// <summary>
///     Raised when the sunrise-clause cure ritual do-after completes on a werewolf or vampire.
///     Lives in Shared: DoAfter events are networked (see e.g. HealingDoAfterEvent).
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SolreignSunriseRitualDoAfterEvent : SimpleDoAfterEvent
{
}
