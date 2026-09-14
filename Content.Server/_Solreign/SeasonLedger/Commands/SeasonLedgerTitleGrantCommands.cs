using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Console surface for admin-granted Season Ledger titles (community rewards program). Same
///     AdminFlags.Host bar as every other solreign ledger command; input policy and audit copy live in
///     <see cref="TitleGrantRules"/> so they stay pure and unit-tested. Targets resolve through
///     <see cref="IPlayerLocator"/> (name or GUID), so rewards can be granted to offline players too.
/// </summary>
[AdminCommand(AdminFlags.Host)]
internal sealed partial class SeasonLedgerGrantTitleCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    public override string Command => "solreign_grant_title";
    public override string Description =>
        "Grants a custom Season Ledger title (community rewards). Shows on examine, persists across rounds, replaces any prior grant this season.";
    public override string Help => $"{Command} <player> <title text> " +
        $"(max {TitleGrantRules.MaxLength} chars, printable ASCII, no '[' ']' '\\')";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!TitleGrantRules.TryNormalize(string.Join(' ', args[1..]), out var title, out var problem))
        {
            shell.WriteError(problem);
            return;
        }

        try
        {
            var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (located == null)
            {
                shell.WriteError($"Player not found: {args[0]}");
                return;
            }

            var grantedBy = shell.Player?.Name ?? "SERVER";
            await _ledger.GrantAdminTitleAsync(located.UserId.UserId, located.Username, title, grantedBy);
            shell.WriteLine($"Season Ledger title granted: player={located.Username} title=\"{title}\".");
        }
        catch
        {
            // Never echo exception details: they can contain paths or SQL. The store upsert is a single
            // atomic statement, so a failure here means nothing was recorded.
            shell.WriteError("Season Ledger title grant failed; nothing was recorded.");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "<playerName>")
            : CompletionResult.FromHint("<title text>");
}

[AdminCommand(AdminFlags.Host)]
internal sealed partial class SeasonLedgerRevokeTitleCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    public override string Command => "solreign_revoke_title";
    public override string Description =>
        "Revokes the current season's admin-granted Season Ledger title; the earned title shows again.";
    public override string Help => $"{Command} <player>";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (located == null)
            {
                shell.WriteError($"Player not found: {args[0]}");
                return;
            }

            var revokedBy = shell.Player?.Name ?? "SERVER";
            if (await _ledger.RevokeAdminTitleAsync(located.UserId.UserId, located.Username, revokedBy))
                shell.WriteLine($"Season Ledger title revoked: player={located.Username}.");
            else
                shell.WriteLine($"No admin-granted title on record this season: player={located.Username}.");
        }
        catch
        {
            // Never echo exception details: they can contain paths or SQL.
            shell.WriteError("Season Ledger title revoke failed; the grant (if any) is unchanged.");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "<playerName>")
            : CompletionResult.Empty;
}
