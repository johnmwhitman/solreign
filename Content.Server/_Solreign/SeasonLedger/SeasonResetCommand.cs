using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Host-only "new season" lever. Archives the current Season Ledger stats (they remain on record
///     under their old season id) and starts a fresh season, resetting everyone's tours/titles.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed partial class SeasonResetCommand : LocalizedEntityCommands
{
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    public override string Command => "solreign_season_reset";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        // Admin/host command, invoked rarely — a brief synchronous wait for the SQLite bump is acceptable
        // and gives the operator immediate confirmation of the new season id.
        var newSeason = _ledger.ResetSeasonAsync().GetAwaiter().GetResult();
        shell.WriteLine($"Season Ledger reset. New active season: {newSeason}. Prior season stats remain archived.");
    }
}
