using System.Collections.Generic;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     "Item of Concern" — the audit's one deliberately FICTIONAL, deadpan-flavor slot (spec: e.g.
///     unrepaired breaches, unpaid fees). Unlike every other section, this one is not derived from
///     tracked stats; it is a closed, PG-13 vocabulary of stock corporate complaints, picked
///     deterministically by round id so the same round always reads the same concern (reproducible
///     for tests/replays) while different rounds vary. Same "deterministic-by-round-id, reads as
///     fresh from the player's seat" idiom as <c>StationDirectiveSelection.SelectDirectiveIndex</c>.
/// </summary>
public static class ItemOfConcernPicker
{
    /// <summary>Closed set of stock loc keys — NEVER free text, one PA-report-style line each.</summary>
    public static readonly IReadOnlyList<string> Ids = new[]
    {
        "unrepaired-breach",
        "unpaid-cafeteria-tabs",
        "supply-requisition-backlog",
        "vending-machine-under-investigation",
        "incident-paperwork-backlog",
        "unlogged-maintenance-tunnel",
    };

    /// <summary>Picks one id for <paramref name="roundId"/>. Total for any int (including negative
    /// or zero round ids from synthetic/test fixtures) so a caller can never index out of range.</summary>
    public static string Pick(int roundId)
    {
        var index = roundId % Ids.Count;
        if (index < 0)
            index += Ids.Count;

        return Ids[index];
    }
}
