using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Solreign.PlayerDelight.FirstShift;

[AdminCommand(AdminFlags.Moderator)]
internal sealed partial class FirstShiftStatusCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "firstshiftstatus";
    public string Description => "Shows coarse First Shift round counters.";
    public string Help => "firstshiftstatus";
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0) { shell.WriteError(Help); return; }
        var system = _entities.System<FirstShiftSystem>();
        var counts = system.Counters;
        shell.WriteLine($"Enabled={system.Enabled} Active={counts.Active} Completed={counts.Completed} Rerolled={counts.Rerolled}");
    }
}

[AdminCommand(AdminFlags.Moderator)]
internal sealed partial class FirstShiftClearCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;
    public string Command => "firstshiftclear";
    public string Description => "Clears First Shift round-local assignment state.";
    public string Help => "firstshiftclear [player]";
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = _entities.System<FirstShiftSystem>();
        if (args.Length == 0) { system.ClearAllForModerator(); shell.WriteLine("First Shift assignments cleared."); return; }
        if (args.Length != 1) { shell.WriteError(Help); return; }
        if (!_players.TryGetSessionByUsername(args[0], out var target)) { shell.WriteError("Connected player not found."); return; }
        shell.WriteLine(system.ClearForModerator(target.UserId) ? "First Shift assignment cleared." : "No active First Shift assignment.");
    }
}
