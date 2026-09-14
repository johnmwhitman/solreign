using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.BugReport;

/// <summary>
///     Pure per-player cooldown tracker for the "bugreport" console command. Free of Robust
///     dependencies (takes plain <see cref="TimeSpan"/> timestamps instead of reading
///     <c>IGameTiming</c> itself) so the cooldown math is unit-testable in isolation;
///     <see cref="BugReportSystem"/> feeds it <c>IGameTiming.RealTime</c> at call time.
/// </summary>
/// <remarks>
///     A single global <c>PlayerRateLimitManager</c> key (period=30s/count=1) would also work here,
///     but that manager is built for chat-style "N messages per period" spam with admin-escalation
///     semantics. "bugreport" is a single-shot report with its own confirmation/cooldown message,
///     so a small dedicated tracker (mirroring how <c>PlayerRateLimitManager</c> itself clears state
///     on disconnect) is simpler and keeps the cooldown logic directly unit-testable.
/// </remarks>
public sealed class BugReportCooldown
{
    private readonly Dictionary<Guid, TimeSpan> _lastSubmission = new();

    /// <summary>
    ///     If <paramref name="player"/> is off cooldown at <paramref name="now"/>, records the
    ///     submission time and returns true. Otherwise returns false and outputs the remaining wait.
    /// </summary>
    public bool TryConsume(Guid player, TimeSpan now, TimeSpan cooldown, out TimeSpan remaining)
    {
        if (_lastSubmission.TryGetValue(player, out var last))
        {
            var elapsed = now - last;
            if (elapsed < cooldown)
            {
                remaining = cooldown - elapsed;
                return false;
            }
        }

        _lastSubmission[player] = now;
        remaining = TimeSpan.Zero;
        return true;
    }

    /// <summary>Drops tracked state for a player (e.g. on disconnect) to bound memory use.</summary>
    public void Forget(Guid player)
    {
        _lastSubmission.Remove(player);
    }
}
