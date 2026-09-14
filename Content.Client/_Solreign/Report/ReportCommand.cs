using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.Report;

/// <summary>
///     Player-accessible "report" console command: the fast in-game "report a player" quick-action
///     (churn insight: self-antag needs FAST moderation tools or it burns the RP core). Opens the report
///     window, optionally pre-filled with a target name (e.g. <c>report Bob</c>) so a player who just
///     saw the griefer's name doesn't have to retype it. Idiom mirrors
///     <c>Content.Client._Solreign.Feedback.FeedbackCommand</c> / <c>CreditsCommand</c> /
///     <c>OpenAHelpCommand</c> (Content.Client/Commands) -- <c>[AnyCommand]</c>, delegate straight to an
///     <see cref="EntitySystem"/> rather than constructing UI inline.
/// </summary>
[UsedImplicitly, AnyCommand]
public sealed partial class ReportCommand : LocalizedCommands
{
    [Dependency] private IEntitySystemManager _sysMan = default!;

    public override string Command => "report";

    public override string Help => LocalizationManager.GetString($"cmd-{Command}-help", ("command", Command));

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var prefill = args.Length > 0 ? string.Join(' ', args) : null;
        _sysMan.GetEntitySystem<ReportClientSystem>().EnsureWindowOpen(prefill);
    }
}
