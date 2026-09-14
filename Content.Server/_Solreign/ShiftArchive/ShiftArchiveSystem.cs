using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.ShiftArchive;
using Content.Shared.CCVar;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Solreign.ShiftArchive;

/// <summary>
///     The Shift Archive board: a wall console listing the most recent shifts as PROVIDENCE
///     recorded them, straight from <c>station_audit_log</c> plus per-round completed-contract
///     counts. Pure ledger read on <see cref="BoundUIOpenedEvent"/> — no daemon, no classifier,
///     no player-authored text; every line is a closed-vocabulary template over a real record
///     (<see cref="ShiftArchiveCopy"/>).
///
///     Async discipline is the Records-terminal shape, hardened by the Memorial post-mortem
///     (commit 3d339eec3a): the subscription handler is synchronous and delegates to a private
///     <c>async void</c> whose whole body is one try/catch — an exception escaping an
///     <c>async void</c> has no caller and takes the server down as UNHANDLED. The entity is
///     re-checked after every await (the board can be deleted mid-read), and the failure path
///     degrades to an empty board with an inner try/catch so failing to report a failure cannot
///     itself become the unhandled exception.
/// </summary>
// `partial` is required by analyzer RA0049: the [Dependency] fields below are populated by a
// source generator that emits into this type. It is only an ERROR in the Release configuration
// that Content.Packaging builds, so a Debug test run — however green — cannot catch it. This
// class shipped a green 2445-test battery and still could not be packaged.
public sealed partial class ShiftArchiveSystem : EntitySystem
{
    // SeasonLedgerStore is a plain class, not an IoC service — reach the ledger through its
    // EntitySystem facade, same as MemorialConsoleSystem does.
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private bool _enabled;
    private int _entries;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignShiftArchiveEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignShiftArchiveEntries, v => _entries = v, invokeImmediately: true);

        SubscribeLocalEvent<ShiftArchiveBoardComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    private void OnUiOpened(EntityUid uid, ShiftArchiveBoardComponent component, BoundUIOpenedEvent args)
    {
        if (args.UiKey is not ShiftArchiveUiKey)
            return;

        // CVar off: the board stays placed but presents its offline state — map placement and the
        // feature flip are independent steps (canary law), so flipping the CVar back off is a
        // complete, immediate kill switch with the boards still on the walls.
        if (!_enabled)
        {
            _uiSystem.SetUiState(uid, ShiftArchiveUiKey.Key, new ShiftArchiveState(false, new List<ShiftArchiveEntry>()));
            return;
        }

        LoadAndPush(uid);
    }

    private async void LoadAndPush(EntityUid uid)
    {
        try
        {
            // Cap clamps defensively: a mis-set CVar must never turn this into an unbounded read.
            var limit = Math.Clamp(_entries, 1, 50);
            var audits = await _ledger.GetRecentStationAuditsAsync(limit);

            // The board may have died while we were awaiting; see class remarks.
            if (Deleted(uid))
                return;

            var counts = await _ledger.GetContractCountsForRoundsAsync(audits.Select(a => a.RoundId).ToList());

            if (Deleted(uid))
                return;

            var entries = new List<ShiftArchiveEntry>(audits.Count);
            foreach (var audit in audits)
            {
                counts.TryGetValue(audit.RoundId, out var contractCount);
                var header = Render(ShiftArchiveCopy.HeaderFor(audit));
                var lines = ShiftArchiveCopy.LinesFor(audit, contractCount).Select(Render).ToList();
                entries.Add(new ShiftArchiveEntry(header, lines));
            }

            _uiSystem.SetUiState(uid, ShiftArchiveUiKey.Key, new ShiftArchiveState(true, entries));
        }
        catch (Exception e)
        {
            // Degrade to an empty archive rather than killing the round: the player sees a board
            // with no entries, which reads in-fiction as records being unavailable, and the
            // operator gets the real reason in the log.
            Log.Error($"Shift Archive board {ToPrettyString(uid)} failed to read the Season Ledger: {e}");

            if (Deleted(uid))
                return;

            try
            {
                _uiSystem.SetUiState(uid, ShiftArchiveUiKey.Key, new ShiftArchiveState(true, new List<ShiftArchiveEntry>()));
            }
            catch (Exception inner)
            {
                // Swallow deliberately so the failure to REPORT a failure cannot itself become
                // the unhandled exception this whole method exists to prevent.
                Log.Error($"Shift Archive board {ToPrettyString(uid)} could not present its fallback state: {inner}");
            }
        }
    }

    /// <summary>
    ///     The ONLY place in the feature that calls <c>Loc.GetString</c> — the wire carries fully
    ///     rendered strings (the Records-terminal precedent), so the client window is a dumb list
    ///     and no archive copy logic lives in the sandbox.
    /// </summary>
    private string Render(ShiftArchiveLine line)
    {
        return Loc.GetString(line.Key, line.Args);
    }
}
