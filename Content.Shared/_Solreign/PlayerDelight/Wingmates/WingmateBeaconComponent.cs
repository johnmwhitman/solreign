using Robust.Shared.GameObjects;

namespace Content.Shared._Solreign.PlayerDelight.Wingmates;

/// <summary>
/// Marks a physical beacon that hosts the private Wingmates bound user interface.
/// Relationship truth remains in the server's round-local aggregate.
/// </summary>
[RegisterComponent]
public sealed partial class WingmateBeaconComponent : Component
{
}
