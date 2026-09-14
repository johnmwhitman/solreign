using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.GameTicking;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.PlayerDelight.Wingmates;

/// <summary>
/// Receives viewer-targeted state without ever treating the entity-wide BUI shell as private data.
/// </summary>
public sealed partial class WingmateClientSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    private readonly WingmateSnapshotIndex _snapshots = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WingmatePrivateSnapshotEvent>(OnPrivateSnapshot);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public bool TryGetSnapshot(NetEntity beacon, out WingmateSnapshotIndex.Entry entry)
    {
        return _snapshots.TryGet(beacon, out entry);
    }

    public bool TryConsumeTransition(NetEntity beacon, uint sequence)
    {
        return _snapshots.TryConsumeTransition(beacon, sequence);
    }

    private void OnPrivateSnapshot(WingmatePrivateSnapshotEvent message, EntitySessionEventArgs args)
    {
        if (!_snapshots.TryUpdate(message.Beacon, message.Generation, message.State))
            return;

        if (!TryGetEntity(message.Beacon, out var beacon) ||
            !_ui.TryGetOpenUi<WingmateBoundUserInterface>(beacon.Value, WingmateUiKey.Beacon, out var bui))
            return;

        bui.ApplyPrivateSnapshot(message.State);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent message)
    {
        _snapshots.Clear();
    }
}
