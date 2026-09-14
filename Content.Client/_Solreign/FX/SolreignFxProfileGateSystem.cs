using System;
using System.Collections.Generic;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.FX;

/// <summary>
///     The real <c>Content.Client</c> CVar-subscription wrapper spec §8's W2 file boundary names
///     "<c>SolreignFxProfileGate.cs</c>" — kept as a DIFFERENT type from W1's
///     <c>Content.Shared._Solreign.FX.SolreignFxProfileGate</c> (same name, different namespace)
///     rather than colliding with it: W1's own receipt explains that pure gate math had to live in
///     Shared because W1's file boundary didn't authorize touching <c>Content.Client</c> at all, and
///     explicitly says "W2 is expected to wrap this in a real <c>Content.Client</c> system." This
///     type is that wrapper — <c>...System</c> suffix added for clarity now that both types coexist
///     in the same build, same "Math class vs System class, different names" split the codebase
///     already uses for <c>MovementBobMath</c>/<c>MovementBobSystem</c>.
///
///     Subscribes <c>solreign.fx.profile</c>, <c>solreign.fx.no_flash</c>, and the engine's own
///     <c>CCVars.ReducedMotion</c> (mirroring <c>MovementBobSystem</c>'s exact
///     <c>Subs.CVar(..., invokeImmediately: true)</c> shape per spec §4), composes them into the
///     effective profile via the Shared pure gate, and runs the "explicit transition handler so
///     mid-session profile changes reconcile already-active cues" spec §4 calls for: on any change
///     that could newly disallow a category under the new effective profile, every subscriber
///     registered via <see cref="ProfileChanged"/> is notified so it can walk its own active leases
///     and drop whatever the new profile no longer allows (spec §4.0's structural rule guarantees
///     this is always safe — the dropped cue was never the sole carrier of any gameplay-critical
///     information).
/// </summary>
public sealed partial class SolreignFxProfileGateSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private bool _reducedMotionEngine;
    private SolreignFxProfile _explicitProfile = SolreignFxProfile.Full;
    private bool _noFlash;

    /// <summary>The composed effective profile right now (spec: engine reduced-motion can only strengthen an explicit <c>full</c> selection, never weaken a stricter one).</summary>
    public SolreignFxProfile CurrentProfile { get; private set; } = SolreignFxProfile.Full;

    /// <summary>Spec §4.0's dedicated photosensitivity toggle — independent of profile.</summary>
    public bool NoFlash => _noFlash;

    /// <summary>Whether the master FX Language v1 kill switch is on.</summary>
    public bool CueSystemEnabled { get; private set; }

    /// <summary>
    ///     Fired whenever <see cref="CurrentProfile"/>, <see cref="NoFlash"/>, or
    ///     <see cref="CueSystemEnabled"/> changes, AFTER the new value is already in effect — a
    ///     subscriber (<c>SolreignFxCueSystem</c>) reconciles its own active leases against the
    ///     already-updated gate state, never a stale one.
    /// </summary>
    public event Action? ProfileChanged;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignFxCueV1Enabled, v => { CueSystemEnabled = v; ProfileChanged?.Invoke(); }, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignFxProfile, v => { _explicitProfile = SolreignFxProfileGate.ParseProfile(v); Recompute(); }, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignFxNoFlash, v => { _noFlash = v; ProfileChanged?.Invoke(); }, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, v => { _reducedMotionEngine = v; Recompute(); }, invokeImmediately: true);
    }

    private void Recompute()
    {
        var next = SolreignFxProfileGate.EffectiveProfile(_explicitProfile, _reducedMotionEngine);
        if (next == CurrentProfile)
            return;

        CurrentProfile = next;
        ProfileChanged?.Invoke();
    }

    /// <summary>Convenience accessor for a category's behavior under the current effective profile.</summary>
    public SolreignFxProfilePolicy.ProfileBehavior CurrentBehavior(SolreignFxCategory category)
        => SolreignFxProfilePolicy.GetBehavior(category, CurrentProfile);
}
