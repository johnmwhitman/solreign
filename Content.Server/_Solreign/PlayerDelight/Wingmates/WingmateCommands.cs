using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._Solreign.PlayerDelight.Wingmates;

internal abstract partial class WingmateModeratorCommand : LocalizedCommands
{
    [Dependency] protected IEntityManager EntityManager = default!;
    [Dependency] private IPlayerManager _players = default!;

    protected bool TryResolveTarget(IConsoleShell shell, string[] args, out ICommonSession target)
    {
        target = default!;
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return false;
        }

        if (_players.TryGetSessionByUsername(args[0], out target!))
            return true;

        shell.WriteError(Loc.GetString("cmd-wingmate-player-not-found", ("player", args[0])));
        return false;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "<playerName>")
            : CompletionResult.Empty;
}

internal abstract partial class WingmateMutationCommand : WingmateModeratorCommand
{
    protected abstract string ResultKey { get; }
    protected abstract WingmateTransitionResult Apply(WingmateSystem system, ICommonSession target);

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryResolveTarget(shell, args, out var target))
            return;

        var result = Apply(EntityManager.System<WingmateSystem>(), target);
        shell.WriteLine(Loc.GetString(ResultKey,
            ("player", target.Name),
            ("changed", Loc.GetString(result.Changed ? "cmd-wingmate-yes" : "cmd-wingmate-no"))));
    }
}

[AdminCommand(AdminFlags.Moderator)]
internal sealed partial class WingmateStatusCommand : WingmateModeratorCommand
{
    public override string Command => "wingmatestatus";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryResolveTarget(shell, args, out var target))
            return;

        var snapshot = EntityManager.System<WingmateSystem>().GetModeratorSnapshot(target.UserId);
        shell.WriteLine(Loc.GetString("cmd-wingmate-status",
            ("player", target.Name),
            ("status", Loc.GetString($"wingmates-status-{snapshot.Status.ToString().ToLowerInvariant()}")),
            ("approved", Loc.GetString(snapshot.Approved ? "cmd-wingmate-yes" : "cmd-wingmate-no")),
            ("volunteering", Loc.GetString(snapshot.Volunteering ? "cmd-wingmate-yes" : "cmd-wingmate-no"))));
    }
}

[AdminCommand(AdminFlags.Moderator)]
internal sealed partial class WingmateApproveCommand : WingmateMutationCommand
{
    public override string Command => "wingmateapprove";
    protected override string ResultKey => "cmd-wingmate-approve-result";
    protected override WingmateTransitionResult Apply(WingmateSystem system, ICommonSession target) =>
        system.ApproveGuide(target.UserId);
}

[AdminCommand(AdminFlags.Moderator)]
internal sealed partial class WingmateRevokeCommand : WingmateMutationCommand
{
    public override string Command => "wingmaterevoke";
    protected override string ResultKey => "cmd-wingmate-revoke-result";
    protected override WingmateTransitionResult Apply(WingmateSystem system, ICommonSession target) =>
        system.RevokeGuide(target.UserId);
}

[AdminCommand(AdminFlags.Moderator)]
internal sealed partial class WingmateDissolveCommand : WingmateMutationCommand
{
    public override string Command => "wingmatedissolve";
    protected override string ResultKey => "cmd-wingmate-dissolve-result";
    protected override WingmateTransitionResult Apply(WingmateSystem system, ICommonSession target) =>
        system.DissolveForModerator(target.UserId);
}
