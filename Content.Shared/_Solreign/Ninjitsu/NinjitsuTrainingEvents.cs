using Content.Shared.Actions;

namespace Content.Shared._Solreign.Ninjitsu;

/// <summary>
///     Raised when a Space Ninja uses their innate "Silent Step" action to toggle footfall
///     suppression on or off. Innate (granted directly to the ninja, not gear-gated) — handled
///     server-side only by <see cref="Content.Server._Solreign.Ninjitsu.NinjitsuSystem"/>.
/// </summary>
public sealed partial class NinjitsuSilentStepEvent : InstantActionEvent;
