using System;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure cooldown + priority anti-spam gate for <see cref="ProvidenceEventReactiveSystem"/>
///     (feat/providence-event-reactive — PROVIDENCE-VOICE-DESIGN.md). Free of IoC/engine types so it
///     is directly unit-testable (Content.Tests/_Solreign/ProvidenceReactiveDispatchGateTests.cs),
///     same split as <c>ProvidenceCommiserationGate</c>/<c>FirstDeathRoundGuard</c>.
///
///     The player-feedback P0 finding (debris clouds firing ~10 announcements in one short window,
///     PLAYER-FEEDBACK-2026-07-22.md) is exactly the failure mode this gate exists to prevent for
///     PROVIDENCE's own reactive lines: a SINGLE shared cooldown across every event-reactive line
///     (death commentary, round-end summary, etc.) — not a per-category cooldown, which a burst of
///     mixed event types would trivially route around.
///
///     Priority does not bypass the cooldown outright; it SCALES it
///     (<see cref="EffectiveCooldown"/>): a High-priority beat (Ledger memory, round-end) may follow a
///     prior line sooner than a Normal or Low one would be allowed to. This makes "priority" do real
///     anti-spam work rather than being a cosmetic tiebreaker — a burst of routine Low/Normal events
///     collapses to silence fastest, while the rare, meaningful beat still gets a chance to land.
/// </summary>
public sealed class ProvidenceReactiveDispatchGate
{
    private TimeSpan? _lastFired;

    /// <summary>
    ///     True if a line of the given <paramref name="priority"/> is allowed to fire right now — i.e.
    ///     no prior line has fired yet, or enough time has passed since the last one relative to this
    ///     priority's <see cref="EffectiveCooldown"/>. Does not itself record anything; pair with
    ///     <see cref="MarkFired"/> once the caller has actually decided to fire.
    /// </summary>
    public bool CanFire(ProvidenceReactivePriority priority, TimeSpan now, TimeSpan baseCooldown)
    {
        if (_lastFired is not { } last)
            return true;

        return now - last >= EffectiveCooldown(priority, baseCooldown);
    }

    /// <summary>Records that a line fired at <paramref name="now"/> — the clock every subsequent
    /// <see cref="CanFire"/> check (of any priority) measures against.</summary>
    public void MarkFired(TimeSpan now)
    {
        _lastFired = now;
    }

    /// <summary>Clears the fired-clock — call on round start/restart so cooldown state never leaks
    /// across rounds (a line fired near the end of round N must never suppress round N+1's first
    /// beat).</summary>
    public void Reset()
    {
        _lastFired = null;
    }

    /// <summary>
    ///     The actual cooldown a given priority must wait out, derived from
    ///     <paramref name="baseCooldown"/> (the operator-tunable CVar): High halves it, Low doubles it,
    ///     Normal is the base value unchanged. Clamped so a zero/negative base cooldown can never
    ///     invert into "always allowed regardless of priority" in a way that defeats the point — a
    ///     non-positive base cooldown collapses every tier to zero (fires every time, all tiers alike),
    ///     never negative.
    /// </summary>
    public static TimeSpan EffectiveCooldown(ProvidenceReactivePriority priority, TimeSpan baseCooldown)
    {
        if (baseCooldown <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var factor = priority switch
        {
            ProvidenceReactivePriority.High => 0.5,
            ProvidenceReactivePriority.Low => 2.0,
            _ => 1.0,
        };

        return TimeSpan.FromTicks((long) (baseCooldown.Ticks * factor));
    }
}
