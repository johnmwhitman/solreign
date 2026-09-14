using Content.Shared._Solreign.ShiftArchive;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using System;

namespace Content.Client._Solreign.ShiftArchive;

[UsedImplicitly]
public sealed class ShiftArchiveBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ShiftArchiveWindow? _window;

    public ShiftArchiveBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ShiftArchiveWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState message)
    {
        base.UpdateState(message);

        if (message is not ShiftArchiveState state)
            return;

        _window?.UpdateState(state);
    }
}
