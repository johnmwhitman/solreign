using Content.Shared._Solreign.Records;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Records;

/// <summary>
///     Receives the private, session-targeted Personnel Records Terminal push
///     (<see cref="RecordsTerminalSnapshotEvent"/>) and, if the matching terminal window is currently
///     open, hands it the rendered snapshot. Copies the shipped
///     <c>Content.Client._Solreign.PlayerDelight.FirstShift.FirstShiftClientSystem</c>
///     "receives only session-targeted snapshots" idiom — no local caching is needed here (unlike
///     FirstShift's generation-tracked index) because a terminal read is a one-shot render, not a
///     live multi-step coordination the window needs to resume mid-flow.
/// </summary>
public sealed partial class RecordsTerminalClientSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RecordsTerminalSnapshotEvent>(OnSnapshot);
    }

    private void OnSnapshot(RecordsTerminalSnapshotEvent message, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(message.Console, out var console))
            return;

        if (!_ui.TryGetOpenUi<RecordsTerminalBoundUserInterface>(console.Value, RecordsTerminalUiKey.Key, out var bui))
            return;

        bui.ApplySnapshot(message.Snapshot);
    }
}
