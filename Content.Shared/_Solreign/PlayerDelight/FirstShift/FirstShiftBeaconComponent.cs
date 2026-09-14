using Robust.Shared.GameObjects;

namespace Content.Shared._Solreign.PlayerDelight.FirstShift;

/// <summary>
/// Distinct ECS event boundary for First Shift on the shared physical arrival beacon.
/// It carries no state; the server owns all assignment truth round-locally.
/// </summary>
[RegisterComponent]
public sealed partial class FirstShiftBeaconComponent : Component;
