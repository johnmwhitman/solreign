using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Report;

/// <summary>
///     Pure per-player, per-round submission counter for the "report a player" quick-action. Free of
///     Robust dependencies so the limiting math is unit-testable in isolation -- structurally identical
///     to <c>Content.Server._Solreign.Feedback.FeedbackRateLimiter</c> (kept as its own type, rather than
///     shared, because reports and feedback have different caps and different abuse profiles: a report
///     rate limit exists primarily to stop a griefer from spamming false reports at a target, not just to
///     bound disk writes).
/// </summary>
public sealed class ReportRateLimiter
{
    private readonly Dictionary<Guid, (int RoundId, int Count)> _perRound = new();

    /// <summary>
    ///     If <paramref name="player"/> has submitted fewer than <paramref name="maxPerRound"/> reports so
    ///     far in <paramref name="roundId"/>, records the submission and returns true. Otherwise returns
    ///     false. The count resets automatically the first time a player is seen in a new round.
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
