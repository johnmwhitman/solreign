using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Solreign.NightVision;

/// <summary>
///     Marks a worn night-vision item (upstream <c>NightVisionComponent</c> with
///     <c>relayOverlay: true</c>) as battery powered: while toggled on it drains the slotted
///     power cell (upstream <c>PowerCellDraw</c>), shuts itself off when the cell empties or the
///     item is unequipped, and reports its remaining runtime on examine.
///
///     The bridge logic lives in <see cref="SharedSolreignNightVisionSystem"/> and its
///     server/client halves; the drain arithmetic is <see cref="NvgPowerMath"/> so it can be
///     unit tested (Content.Tests/_Solreign/NvgPowerMathTests.cs).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SolreignPoweredNightVisionComponent : Component
{
    /// <summary>
    ///     Whether taking the lenses off also switches them off, so they never drain the cell
    ///     from a pocket or bag.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool DisableWhenRemoved = true;

    /// <summary>
    ///     Charge fraction (0–1) below which the wearer's overlay starts picking up extra
    ///     shader static, telegraphing the imminent auto-shutdown.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LowChargeFraction = 0.25f;

    /// <summary>
    ///     Shader noise amount used at a fully dead cell (lerped from the base
    ///     <c>NightVisionComponent.NoiseAmount</c> as the cell empties).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LowChargeNoiseAmount = 1f;

    /// <summary>
    ///     Shader noise multiplier used at a fully dead cell (lerped from the base
    ///     <c>NightVisionComponent.NoiseMultiplier</c> as the cell empties).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LowChargeNoiseMultiplier = 6f;

    /// <summary>
    ///     Sound played when the wearer manually toggles the lenses on — the upstream toggle
    ///     action (<c>ToggleNightVisionEvent</c>) flips the overlay in total silence otherwise, no
    ///     click, no cue (Phase2 A4 game-feel sweep).
    /// </summary>
    [DataField]
    public SoundSpecifier ToggleOnSound = new SoundPathSpecifier("/Audio/Items/flashlight_on.ogg");

    /// <summary>Sound played when the lenses switch off, whether by manual toggle, dead cell, or unequip.</summary>
    [DataField]
    public SoundSpecifier ToggleOffSound = new SoundPathSpecifier("/Audio/Items/flashlight_off.ogg");

    /// <summary>
    ///     Server-only bookkeeping: the night-vision <c>Enabled</c> value as of the last sync tick.
    ///     Upstream's toggle handler raises no follow-up event to hook directly (see the server
    ///     system's class doc), so its periodic sync loop diffs against this to detect a manual
    ///     toggle edge and play the click. Not a DataField: never saved, never networked, purely a
    ///     scratch value.
    /// </summary>
    [ViewVariables]
    public bool WasEnabled;
}
