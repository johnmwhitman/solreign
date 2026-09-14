using System;
using Content.Server.Popups;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Solreign.BugReport;

/// <summary>
///     Player-accessible "bugreport &lt;text&gt;" console command. Idiom mirrors LOOCCommand /
///     SuicideCommand: <c>[AnyCommand]</c>, validate <c>shell.Player</c>, delegate the actual work to
///     an <see cref="EntitySystem"/> (<see cref="BugReportSystem"/>), which also enforces the
///     30-second per-player cooldown.
/// </summary>
[AnyCommand]
internal sealed partial class BugReportCommand : IConsoleCommand
{
    // Defensive cap: Discord embed descriptions max out at 4096 chars and content at 2000; there's
    // no upstream precedent to mirror here, just keeping a griefer from bloating the JSONL/webhook.
    private const int MaxTextLength = 1000;

    [Dependency] private IEntityManager _e = default!;

    public string Command => "bugreport";

    public string Description => Loc.GetString("cmd-bugreport-desc");

    public string Help => Loc.GetString("cmd-bugreport-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length < 1)
        {
            shell.WriteError(Loc.GetString("bugreport-empty"));
            return;
        }

        var text = string.Join(" ", args).Trim();
        if (string.IsNullOrEmpty(text))
        {
            shell.WriteError(Loc.GetString("bugreport-empty"));
            return;
        }

        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];

        var system = _e.System<BugReportSystem>();

        if (!system.TrySubmit(player, text, out var remaining))
        {
            shell.WriteError(Loc.GetString("bugreport-cooldown", ("seconds", Math.Ceiling(remaining.TotalSeconds))));
            return;
        }

        var confirmation = Loc.GetString("bugreport-confirmation");
        _e.System<PopupSystem>().PopupCursor(confirmation, player);
        shell.WriteLine(confirmation);
    }
}
