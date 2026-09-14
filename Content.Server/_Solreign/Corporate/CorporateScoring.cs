using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Solreign.Corporate;

/// <summary>
///     One line of the corporate scoreboard: a performer's display name and their accrued Corporate Standing.
/// </summary>
public readonly record struct CorporateStanding(string Name, int Score);

/// <summary>
///     Pure, unit-testable ranking + formatting for the Corporate Ladder scoreboard. No ECS, no I/O, no loc —
///     the darkly-funny framing lives in the .ftl; this file only decides <em>who</em> is on top and renders
///     the deterministic "#1 Name — N pts" body lines. Kept pure so <see cref="CorporateScoringTests"/> can
///     exercise ordering, ties, and empty input without spinning up the game.
/// </summary>
public static class CorporateScoring
{
    /// <summary>
    ///     Top <paramref name="count"/> standings, most valuable first. Ties on score break deterministically
    ///     by name (ordinal) so the same inputs always render the same board. A non-positive
    ///     <paramref name="count"/> (or empty input) yields an empty list.
    /// </summary>
    public static IReadOnlyList<CorporateStanding> TopN(IEnumerable<CorporateStanding> standings, int count)
    {
        if (count <= 0)
            return Array.Empty<CorporateStanding>();

        return standings
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    /// <summary>
    ///     Renders the ranked standings into "#1 Name — N pts" body lines (1-based rank). Returns one string
    ///     per entry, in the given order; an empty input yields an empty list. Purely the numeric body — the
    ///     caller wraps these in the localized keynote/earnings framing.
    /// </summary>
    public static IReadOnlyList<string> FormatBoard(IReadOnlyList<CorporateStanding> ranked)
    {
        var lines = new List<string>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var entry = ranked[i];
            lines.Add($"#{i + 1} {entry.Name} — {entry.Score} pts");
        }

        return lines;
    }
}
