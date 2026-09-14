using Content.Client._Solreign.PlayerDelight.Wingmates;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.GameTicking;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.PlayerDelight.FirstShift;

/// <summary>Receives only session-targeted First Shift snapshots.</summary>
public sealed partial class FirstShiftClientSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    private readonly FirstShiftSnapshotIndex _snapshots = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<FirstShiftPrivateSnapshotEvent>(OnPrivateSnapshot);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => _snapshots.Clear());
        SubscribeLocalEvent<WingmateBeaconComponent, ComponentShutdown>(OnBeaconShutdown);
    }

    public bool TryGetSnapshot(NetEntity beacon, out FirstShiftSnapshotIndex.Entry entry) =>
        _snapshots.TryGet(beacon, out entry);

    public void Invalidate(NetEntity beacon) => _snapshots.Remove(beacon);

    private void OnBeaconShutdown(Entity<WingmateBeaconComponent> entity, ref ComponentShutdown args)
    {
        var beacon = GetNetEntity(entity.Owner);
        FirstShiftClientLifecycle.BeaconShutdown(beacon,
            _ => ClearOpenPrivateView(entity.Owner),
            Invalidate);
    }

    private void ClearOpenPrivateView(EntityUid beacon)
    {
        if (_ui.TryGetOpenUi<WingmateBoundUserInterface>(beacon, WingmateUiKey.Beacon, out var bui))
            bui.ClearFirstShiftPrivateState();
    }

    private void OnPrivateSnapshot(FirstShiftPrivateSnapshotEvent message, EntitySessionEventArgs args)
    {
        if (!_snapshots.TryUpdate(message.Beacon, message.Generation, message.State))
            return;

        if (!TryGetEntity(message.Beacon, out var beacon) ||
            !_ui.TryGetOpenUi<WingmateBoundUserInterface>(beacon.Value, WingmateUiKey.Beacon, out var bui))
            return;

        bui.ApplyFirstShiftSnapshot(message.Generation, message.State);
    }
}
