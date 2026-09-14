using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

// SR-W-083 procedural humanoid movement bob.
// Own partial file per Solreign convention so parallel worktree waves don't collide.
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the client-side procedural movement bob on humanoid mobs.
    ///     Server-owned and replicated so flipping it live applies to every connected client;
    ///     the render work itself is entirely client-side. Enabled 2026-07-25 (activation pass).
    ///     Lives under the <c>solreign.movement_bob</c> table alongside <c>amplitude_px</c>/
    ///     <c>hz</c> rather than as a scalar at that node — RobustToolbox's TOML CVar writer
    ///     (SaveToTomlTable) can't have a leaf and a table share one key.
    /// </summary>
    public static readonly CVarDef<bool> SolreignMovementBobEnabled =
        CVarDef.Create("solreign.movement_bob.enabled", true, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    ///     Bob height in texture pixels. The client additionally hard-caps this
    ///     (see MovementBobMath.MaxAmplitudePixels) so a bad value can't fling sprites around.
    /// </summary>
    public static readonly CVarDef<float> SolreignMovementBobAmplitudePx =
        CVarDef.Create("solreign.movement_bob.amplitude_px", 1.5f, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    ///     Bob bounces per second while moving. Client-clamped to a sane range.
    /// </summary>
    public static readonly CVarDef<float> SolreignMovementBobHz =
        CVarDef.Create("solreign.movement_bob.hz", 2.5f, CVar.REPLICATED | CVar.SERVER);
}
