using System;
using System.Threading.Tasks;
using Content.Server._Solreign.Records;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin Personnel Records Terminal delegation (wave-2 item einstein-016) for
///     <c>Content.Server._Solreign.Records.SolreignRecordsTerminalSystem</c> — the exact
///     <see cref="SeasonLedgerSystem"/>.SocialFirsts.cs / .FirstDeath.cs / .Mark.cs access pattern:
///     other systems reach the ledger through this system rather than standing up a second
///     <see cref="SeasonLedgerStore"/> against the same SQLite file. Bundles every read the terminal
///     needs for one account into a single call so the consuming system doesn't have to know the
///     ledger's internal table layout at all.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    public async Task<RecordsTerminalLedgerSnapshot> GetRecordsTerminalDataAsync(Guid user)
    {
        var career = await _store.GetCareerStatsAsync(user);
        var (earnedTitle, tours) = TitleRules.Compute(career);
        var (_, _, rankIndex) = RankProgression.ComputeCareerRank(career);

        var granted = await _store.GetAdminTitleAsync(user);
        var displayTitle = TitleGrantRules.ResolveDisplayTitle(earnedTitle, granted);

        var socialFirsts = await _store.GetSocialFirstFlagsAsync(user);
        var firstDeath = await _store.GetFirstDeathAsync(user);
        var mark = await _store.GetMarkAsync(user);

        return new RecordsTerminalLedgerSnapshot(
            displayTitle,
            tours,
            rankIndex,
            socialFirsts,
            firstDeath,
            mark);
    }
}
