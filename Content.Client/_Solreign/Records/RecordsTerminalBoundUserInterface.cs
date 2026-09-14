using Content.Client._Solreign.Records.UI;
using Content.Shared._Solreign.Records;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Records;

/// <summary>
///     Client half of the Personnel Records Terminal. Dumb renderer only — every decision (what
///     counts as a social first, whether a Mark exists, the whole title/rank/standing computation)
///     lives server-side; this window just displays whatever
///     <see cref="RecordsTerminalClientSystem"/> hands it via <see cref="ApplySnapshot"/>. Content
///     arrives out-of-band (a private networked event, not <c>UpdateState</c>) — see
///     <c>RecordsTerminalSnapshotEvent</c>'s doc comment for why a shared BUI state would leak.
/// </summary>
[UsedImplicitly]
public sealed class RecordsTerminalBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private RecordsTerminalWindow? _window;

    public RecordsTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<RecordsTerminalWindow>();
        _window.OnRefreshPressed += () => SendMessage(new RecordsTerminalRefreshMessage());
    }

    public void ApplySnapshot(RecordsTerminalSnapshot snapshot)
    {
        _window?.UpdateSnapshot(snapshot);
    }
}
