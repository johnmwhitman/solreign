using Content.Shared.Maps;

namespace Content.Server.Maps;

/// <summary>
/// Pure, content-neutral map-selection predicates shared by the runtime manager and acceptance tests.
/// Conditions and preset-specific eligibility remain owned by <see cref="GameMapManager"/>.
/// </summary>
internal static class GameMapSelectionPolicy
{
    internal static bool IsPopulationEligible(GameMapPrototype map, int population)
    {
        return population >= 0 &&
               map.MinPlayers <= (uint) population &&
               map.MaxPlayers >= (uint) population;
    }
}
