using Content.Client._Solreign.PlayerDelight.Wingmates.UI;
using Content.Client._Solreign.PlayerDelight.FirstShift;
using Content.Client.Popups;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.PlayerDelight.Wingmates;

[UsedImplicitly]
public sealed class WingmateBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WingmateWindow? _window;
    private ulong _firstShiftTransportGeneration;
    private NetEntity? _firstShiftBeacon;

    public WingmateBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _firstShiftBeacon = EntMan.GetNetEntity(Owner);
        _window = this.CreateWindow<WingmateWindow>();

        _window.OnRequest += (department, mode) => SendMessage(new WingmateRequestMessage(department, mode));
        _window.OnCancelRequest += () => SendMessage(new WingmateCancelRequestMessage());
        _window.OnOffer += requester => SendMessage(new WingmateOfferMessage(requester));
        _window.OnAccept += nonce => SendMessage(new WingmateAcceptMessage(nonce));
        _window.OnDecline += (nonce, blockGuide) => SendMessage(new WingmateDeclineMessage(nonce, blockGuide));
        _window.OnDissolve += () => SendMessage(new WingmateDissolveMessage());
        _window.OnPause += () => SendMessage(new WingmatePauseMessage());
        _window.OnResume += () => SendMessage(new WingmateResumeMessage());
        _window.OnBlock += () => SendMessage(new WingmateBlockCurrentPartnerMessage());
        _window.OnVolunteer += (volunteering, charterAccepted) =>
            SendMessage(new WingmateVolunteerMessage(volunteering, charterAccepted));
        _window.OnFirstShiftStart += (department, generation) =>
            SendMessage(new FirstShiftStartMessage(department, generation));
        _window.OnFirstShiftAdvance += generation =>
            SendMessage(new FirstShiftAdvanceMessage(_firstShiftTransportGeneration, generation));
        _window.OnFirstShiftReroll += generation =>
            SendMessage(new FirstShiftRerollMessage(_firstShiftTransportGeneration, generation));
        _window.OnFirstShiftComplete += generation =>
            SendMessage(new FirstShiftCompleteMessage(_firstShiftTransportGeneration, generation));
        _window.OnFirstShiftEnd += generation =>
            SendMessage(new FirstShiftEndMessage(_firstShiftTransportGeneration, generation));

        var system = EntMan.System<WingmateClientSystem>();
        if (system.TryGetSnapshot(EntMan.GetNetEntity(Owner), out var snapshot))
            ApplyPrivateSnapshot(snapshot.State);

        var firstShift = EntMan.System<FirstShiftClientSystem>();
        if (firstShift.TryGetSnapshot(EntMan.GetNetEntity(Owner), out var firstShiftSnapshot))
            ApplyFirstShiftSnapshot(firstShiftSnapshot.Generation, firstShiftSnapshot.State);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        // WingmatePublicShellState is intentionally not rendered as viewer state. The targeted
        // WingmatePrivateSnapshotEvent is the sole path into ApplyPrivateSnapshot.
    }

    public void ApplyPrivateSnapshot(WingmateUiState state)
    {
        // P1.4 SILENT-DROP BATCH FIX: render a one-shot failure popup when the snapshot carries a
        // transition the client has not already surfaced for this beacon. The receipt belongs to
        // WingmateClientSystem rather than this disposable window so reopening the BUI cannot replay
        // a cached rejection. A bumped seq with a null status remains unconsumed and emits nothing.
        var system = EntMan.System<WingmateClientSystem>();
        var beacon = EntMan.GetNetEntity(Owner);
        if (!string.IsNullOrEmpty(state.LastTransitionStatus) &&
            system.TryConsumeTransition(beacon, state.LastTransitionSeq))
        {
            var popup = EntMan.System<PopupSystem>();
            var message = Loc.GetString($"wingmates-action-{state.LastTransitionStatus}");
            popup.PopupCursor(message, PopupType.MediumCaution);
        }

        _window?.UpdateState(state);
    }

    public void ApplyFirstShiftSnapshot(ulong transportGeneration, FirstShiftUiState state)
    {
        _firstShiftTransportGeneration = transportGeneration;
        _window?.UpdateFirstShiftState(transportGeneration, state);
    }

    public void ClearFirstShiftPrivateState()
    {
        _firstShiftTransportGeneration = 0;
        _window?.ClearFirstShiftPrivateState();
    }

    protected override void Dispose(bool disposing)
    {
        ClearFirstShiftPrivateState();
        if (_firstShiftBeacon is { } beacon)
            EntMan.System<FirstShiftClientSystem>().Invalidate(beacon);
        _firstShiftBeacon = null;
        _firstShiftTransportGeneration = 0;
        base.Dispose(disposing);
    }
}
