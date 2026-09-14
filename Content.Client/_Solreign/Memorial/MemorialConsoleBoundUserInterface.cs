using Content.Shared._Solreign.Memorial;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using System;

namespace Content.Client._Solreign.Memorial;

[UsedImplicitly]
public sealed class MemorialConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MemorialConsoleWindow? _window;

    public MemorialConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MemorialConsoleWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState message)
    {
        base.UpdateState(message);

        if (message is not MemorialConsoleState state)
            return;

        _window?.UpdateState(state);
    }
}
