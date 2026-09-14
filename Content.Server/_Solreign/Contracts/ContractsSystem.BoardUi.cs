using Content.Shared._Solreign.Contracts;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Contracts Board BUI wiring (spec Milestone 2, "the board UI"): pushes the station's contract pool
///     to every open board window and refreshes it after each mutation. Copies the upstream cargo bounty
///     console idiom exactly (<c>CargoSystem.Bounty.cs</c>: state on <c>BoundUIOpenedEvent</c> + a
///     query-all-consoles refresh after skips/claims). The claim/skip/join/launch message handlers
///     themselves live in <c>ContractsSystem.cs</c> — they were wired in Milestone 1; this partial only
///     adds the state snapshots that make the client window live. Snapshot mapping is pure
///     (<see cref="ContractBoardView"/>, unit-tested).
/// </summary>
public sealed partial class ContractsSystem
{
    [Dependency] private UserInterfaceSystem _uiSystem = default!;

    private void InitializeBoardUi()
    {
        SubscribeLocalEvent<SolreignContractsBoardComponent, BoundUIOpenedEvent>(OnBoardUiOpened);
    }

    private void OnBoardUiOpened(EntityUid uid, SolreignContractsBoardComponent component, BoundUIOpenedEvent args)
    {
        if (_station.GetOwningStation(uid) is not { } station ||
            !TryComp<StationSolreignContractsComponent>(station, out var db))
            return;

        _uiSystem.SetUiState(uid, SolreignContractsUiKey.Board, BuildBoardState(db));
    }

    /// <summary>One wire-format snapshot of a station's pool, shared by every board on that station.</summary>
    private SolreignContractsBoardState BuildBoardState(StationSolreignContractsComponent db)
    {
        return new SolreignContractsBoardState(
            ContractBoardView.BuildListings(db.Contracts),
            ContractBoardView.UntilNextSkip(db.NextSkipTime, _timing.CurTime));
    }

    /// <summary>
    ///     Refreshes every open board window on <paramref name="station"/> after a pool mutation
    ///     (claim/join/launch/skip/deposit/completion) — the bounty <c>UpdateBountyConsoles</c> idiom,
    ///     scoped per station so multi-station rounds don't cross-pollinate.
    /// </summary>
    private void UpdateBoards(EntityUid station, StationSolreignContractsComponent db)
    {
        var state = BuildBoardState(db);
        var query = EntityQueryEnumerator<SolreignContractsBoardComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (_station.GetOwningStation(uid) != station)
                continue;

            _uiSystem.SetUiState((uid, ui), SolreignContractsUiKey.Board, state);
        }
    }
}
