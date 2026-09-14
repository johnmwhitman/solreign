using Content.Client._Solreign.ComplianceTerminal.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.ComplianceTerminal;

[UsedImplicitly]
public sealed class ComplianceTerminalBoundUserInterface : BoundUserInterface
{
    private ComplianceTerminalWindow? _menu;

    public ComplianceTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<ComplianceTerminalWindow>();
    }
}
