using Content.Server._Solreign.SeasonLedger;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Console;

namespace Content.Server._Solreign.Library;

/// <summary>
///     Moderator-only "libraryunhide &lt;workId&gt;" console command — the one correction tool for a
///     bad-faith one-tap report (the <c>NoticeboardUnhideCommand</c> precedent, verbatim rationale:
///     the Role Matrix already puts "remove a message"-class containment at Junior Moderator
///     (<c>AdminFlags.Moderator</c>), and hide/report needs no independent review, so a plain
///     command is enough).
///
///     Deliberately does NOT implement a separate "permanent delete" path — the same documented
///     scope boundary Noticeboard shipped with: an early, affirmative destroy of the underlying
///     record (rather than reversibly hiding it) approaches sanction territory and this codebase
///     has no reviewer-queue primitive to hang that on yet. Hide (instant, reversible, no review)
///     is the complete ROUTINE tool this wave; permanent deletion stays a manual DBA-level action
///     outside code's authority, exactly as this lane's design brief specifies ("deletion = moderator
///     action only" — meaning outside this automated surface, not that code should build a delete button).
/// </summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class LibraryUnhideCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "libraryunhide";

    public string Description => Loc.GetString("cmd-libraryunhide-desc");

    public string Help => Loc.GetString("cmd-libraryunhide-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var workId))
        {
            shell.WriteError(Loc.GetString("cmd-libraryunhide-usage"));
            return;
        }

        var ledger = _entities.System<SeasonLedgerSystem>();
        var nowUtc = System.DateTime.UtcNow.ToString("o");

        bool unhidden;
        try
        {
            unhidden = await ledger.UnhideLibraryWorkAsync(workId, nowUtc);
        }
        catch (System.Exception e)
        {
            shell.WriteError(Loc.GetString("cmd-libraryunhide-error", ("error", e.Message)));
            return;
        }

        shell.WriteLine(Loc.GetString(unhidden ? "cmd-libraryunhide-success" : "cmd-libraryunhide-notfound", ("id", workId)));
    }
}
