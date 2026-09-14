using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Feedback;

/// <summary>
///     Pure per-player, per-round submission counter for structured feedback (roadmap D2.1: "rate-limit
///     submissions per player per round"). Free of Robust dependencies so the limiting math is
///     unit-testable in isolation — mirrors <c>BugReportCooldown</c>, but counts submissions within the
///     current round instead of gating on wall-clock elapsed time, since feedback categories (bug/idea/
///     balance/fun/confusion) are each a single-shot per topic rather than one repeated action.
/// </summary>
public sealed class FeedbackRateLimiter
{
    private readonly Dictionary<Guid, (int RoundId, int Count)> _perRound = new();

    /// <summary>
    ///     If <paramref name="player"/> has submitted fewer than <paramref name="maxPerRound"/> reports
    ///     so far in <paramref name="roundId"/>, records the submission and returns true. Otherwise
    ///     returns false. The count resets automatically the first time a player is seen in a new round
    ///     (no explicit "round started" hook needed — the round ID is supplied by the caller on every
    ///     submission, so a mismatch is detected lazily).
    /// </summary>
    public bool TryConsume(Guid player, int roundId, int maxPerRound, out int remaining)
    {
        if (_perRound.TryGetValue(player, out var entry) && entry.RoundId == roundId)
        {
            if (entry.Count >= maxPerRound)
            {
                remaining = 0;
                return false;
            }

            _perRound[player] = (roundId, entry.Count + 1);
            remaining = maxPerRound - (entry.Count + 1);
            return true;
        }

        // First submission this round (or ever) for this player.
        _perRound[player] = (roundId, 1);
        remaining = maxPerRound - 1;
        return true;
    }

    /// <summary>Drops tracked state for a player (e.g. on disconnect) to bound memory use.</summary>
    public void Forget(Guid player)
    {
        _perRound.Remove(player);
    }
}
