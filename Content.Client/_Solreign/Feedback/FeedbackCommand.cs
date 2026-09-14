using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.Feedback;

/// <summary>
///     Player-accessible "feedback" console command (roadmap D2.1, docs/ROADMAP-PLAYER-DELIGHT.md):
///     opens the structured feedback window. v1 entry point per the roadmap ("a console command is
///     acceptable for v1"); a HUD button is stretch goal. Idiom mirrors <c>CreditsCommand</c>/
///     <c>OpenAHelpCommand</c> (Content.Client/Commands) -- <c>[AnyCommand]</c>, delegate straight to an
///     <see cref="EntitySystem"/> rather than constructing UI inline.
/// </summary>
[UsedImplicitly, AnyCommand]
public sealed partial class FeedbackCommand : LocalizedCommands
{
    [Dependency] private IEntitySystemManager _sysMan = default!;

    public override string Command => "feedback";

    public override string Help => LocalizationManager.GetString($"cmd-{Command}-help", ("command", Command));

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _sysMan.GetEntitySystem<FeedbackClientSystem>().EnsureWindowOpen();
    }
}
