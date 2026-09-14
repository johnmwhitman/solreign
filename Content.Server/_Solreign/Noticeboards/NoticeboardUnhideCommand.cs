using Content.Server._Solreign.SeasonLedger;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Console;

namespace Content.Server._Solreign.Noticeboards;

/// <summary>
///     Moderator-only "noticeboardunhide &lt;noteId&gt;" console command — the one correction tool
///     for a bad-faith one-tap report. Deliberately NOT a full admin UI: the Role Matrix already
///     puts "remove a message"-class containment at Junior Moderator (<c>AdminFlags.Moderator</c>),
///     and the spec's routine hide/report path needs no independent review, so a plain command is
///     enough — building a browsing UI for hidden notes was out of scope for this lane.
///
///     Deliberately does NOT implement the spec's separate "early permanent destroy" path (a
///     moderator purging the underlying record before its normal expiry, rather than hiding it) —
///     the memo requires that path to "follow the same independent-review-or-temporary-hold
///     pattern the Constitution already uses for other final decisions about a specific person's
///     material," and this codebase has no reviewer-queue primitive to hang that on yet. Hide
///     (instant, reversible, no review) plus flat auto-expiry is the complete ROUTINE tool; early
///     destroy stays unbuilt rather than shipping an unreviewed destructive command.
/// </summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class NoticeboardUnhideCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "noticeboardunhide";

    public string Description => Loc.GetString("cmd-noticeboardunhide-desc");

    public string Help => Loc.GetString("cmd-noticeboardunhide-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var noteId))
        {
            shell.WriteError(Loc.GetString("cmd-noticeboardunhide-usage"));
            return;
        }

        var ledger = _entities.System<SeasonLedgerSystem>();
        var nowUtc = System.DateTime.UtcNow.ToString("o");

        bool unhidden;
        try
        {
            unhidden = await ledger.UnhideNoticeboardNoteAsync(noteId, nowUtc);
        }
        catch (System.Exception e)
        {
            shell.WriteError(Loc.GetString("cmd-noticeboardunhide-error", ("error", e.Message)));
            return;
        }

        shell.WriteLine(Loc.GetString(unhidden ? "cmd-noticeboardunhide-success" : "cmd-noticeboardunhide-notfound", ("id", noteId)));
    }
}
