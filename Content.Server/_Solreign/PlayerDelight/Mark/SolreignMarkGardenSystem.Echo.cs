using System;
using System.Collections.Generic;
using Content.Server._Solreign.Providence;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared._Solreign.PlayerDelight.Mark;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     "Echoes of the Departed" (v14, EOD-spec): the Garden's fourth job, alongside the three
///     Mark jobs in <see cref="SolreignMarkGardenSystem"/>'s main partial. Round-start projection,
///     decoupled entirely from the death event itself: an Echo's "birth" is the EXISTING
///     <c>SeasonLedgerStore.TryClaimFirstDeathAsync</c> write (already fired by
///     <c>ProvidenceFirstDeathSystem</c>) sitting in <c>first_death</c> until the next
///     <see cref="Content.Server.Station.Events.StationPostInitEvent"/> picks it up here — the
///     exact <see cref="SolreignMarkGardenSystem.ProjectMarks"/> shape, reused near-verbatim
///     (EOD-spec §3, §4).
///
///     No owner/stranger split on examine (EOD-spec §8): a first death is already a station-wide
///     public disclosure (eulogy + crypt plaque + Discord obituary all name the player publicly
///     at claim time), so every examiner reads the identical composition — header, "IN MEMORY OF
///     {name}", the live-recomputed <see cref="FirstDeathEpitaphPicker.Pick"/> plate text, and
///     <see cref="FirstDeathCopy.CauseLabelFor"/>. The epitaph and cause label are recomputed at
///     examine time rather than cached-and-rendered, so this surface can never drift from the
///     crypt plaque's text (same pure, deterministic pick, EOD-spec §8).
///
///     Gates independently on <c>solreign.echo.enabled</c> (ships FALSE) — see the
///     <see cref="SolreignMarkGardenSystem.MaterializeAndProject"/> gate split.
/// </summary>
public sealed partial class SolreignMarkGardenSystem
{
    /// <summary>C(header) — the fixed public header line every Echo opens with.</summary>
    private const string EchoHeaderKey = "solreign-echo-header";

    /// <summary>C(in-memory-of) — the public naming line (variables: $name; the eulogy's own precedent).</summary>
    private const string EchoInMemoryOfKey = "solreign-echo-in-memory-of";

    private bool _echoEnabled;

    /// <summary>
    ///     Bumped on round reset and whenever <c>solreign.echo.enabled</c> flips off so an in-flight
    ///     <see cref="ProjectEchoes"/> that already passed the pre-await gate aborts after the ledger await.
    /// </summary>
    private int _echoProjectionGeneration;

    /// <summary>
    ///     Monotonic count of finished <see cref="ProjectEchoes"/> invocations — every exit path
    ///     (success, disabled-abort, generation-abort, entity-gone abort, exception). Integration
    ///     tests poll this instead of sleeping a fixed tick budget after kicking async-void projection.
    ///     Never reset: callers snapshot before kick and wait for a strictly greater value.
    /// </summary>
    private int _echoProjectionCompletedCount;

    /// <summary>
    ///     Monotonic count of Echo entities deleted by the post-spawn failed-anchor (no-orphans)
    ///     branch inside <see cref="TrySpawnEcho"/>. Distinguishes "never spawned" (space/missing
    ///     tile skip) from "spawned then deleted" for integration coverage.
    /// </summary>
    private int _echoFailedAnchorDeletes;

    /// <summary>
    ///     Space/missing-tile skips inside <see cref="TrySpawnEcho"/> — proves the skip branch
    ///     actually executed (vs. never reaching the tile check at all). Test evidence only.
    /// </summary>
    private int _echoSkippedTiles;

    /// <summary>
    ///     The UID spawned-then-deleted by the most recent failed-anchor Del. Lets tests assert the
    ///     concrete entity is gone rather than trusting the counter alone. Test evidence only.
    /// </summary>
    private EntityUid? _echoLastFailedAnchorUid;

    // ---------------------------------------------------------------- round-start projection (§4)

    /// <summary>
    ///     Mirrors <see cref="ProjectMarks"/> exactly: async-read every eligible <c>first_death</c>
    ///     row (most-recent-death-first, capped at <c>solreign.echo.slots</c>), skip
    ///     corrupt/unparseable rows without throwing (the same law as <see cref="ProjectMarks"/>),
    ///     compute <see cref="MarkAgeRules.StageAt"/> per row off <c>died_at_utc</c>, and spawn the
    ///     stage-appropriate Echo prototype at a deterministic slot. The slot index is the row's
    ///     ordinal position in the capped, ordered query result (EOD-spec §5/§6) — there is no
    ///     persisted slot column on <c>first_death</c>, unlike <c>mark</c>.
    /// </summary>
    private async void ProjectEchoes(EntityUid station, EntityUid garden)
    {
        // Capture generation BEFORE the first await (LoadTitle / gate-before-await shape).
        var generation = _echoProjectionGeneration;

        try
        {
            if (!_echoEnabled)
                return;

            // Pre-await: garden must still exist so we can clamp the ledger LIMIT to its bed size.
            if (Deleted(station) || Deleted(garden) || !TryComp<SolreignMarkGardenComponent>(garden, out var preGardenComp))
                return;

            // Capacity law: never request more rows than unique Echo bed tiles (modulo wrap would
            // otherwise stack N physics entities on the same 10 offsets).
            var slots = Math.Clamp(_cfg.GetCVar(CCVars.SolreignEchoSlots), 0, preGardenComp.EchoSlotOffsets.Count);
            var records = await _ledger.GetAllFirstDeathsAsync(slots);

            // Post-await discipline (LoadWelcome shape): CVar may have flipped, round may have reset,
            // or the station/garden may have been deleted underneath us.
            if (!_echoEnabled || generation != _echoProjectionGeneration)
                return;
            if (Deleted(station) || Deleted(garden) || !TryComp<SolreignMarkGardenComponent>(garden, out var gardenComp))
                return;

            var now = DateTime.UtcNow;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];

                // Corrupt timestamps skip — never throw inside a round-start projection.
                if (!MarkAgeRules.TryParseLedgerUtc(record.DiedAtUtc, out var died))
                {
                    Log.Warning($"First-death row for {record.User} has unparseable died_at_utc; echo skipped.");
                    continue;
                }

                // The claim row's cause string round-trips back to the enum (the LoadRehire
                // idiom, ProvidenceFirstDeathSystem.cs) — an unparseable value (never expected;
                // closed vocabulary) degrades to Unknown flavor rather than skipping the row.
                if (!Enum.TryParse<FirstDeathCause>(record.Cause, ignoreCase: true, out var cause))
                    cause = FirstDeathCause.Unknown;

                var stage = MarkAgeRules.StageAt(died, now);
                TrySpawnEcho(garden, gardenComp, stage, i, record.CharacterName, cause, record.ToursAtDeath, record.TitleAtDeath);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error while projecting echoes onto station {station}:\n{e}");
        }
        finally
        {
            // Every exit path (success, early abort, exception) signals completion for tests.
            _echoProjectionCompletedCount++;
        }
    }

    /// <summary>
    ///     Spawns one Echo projection at its deterministic garden slot. Space/missing tiles skip;
    ///     an entity that fails to anchor is deleted (no-orphans) — the exact
    ///     <see cref="TrySpawnMark"/> discipline, over <see cref="SolreignMarkGardenComponent.EchoSlotOffsets"/>
    ///     instead of <see cref="SolreignMarkGardenComponent.SlotOffsets"/>.
    /// </summary>
    private EntityUid? TrySpawnEcho(
        EntityUid garden,
        SolreignMarkGardenComponent gardenComp,
        int stage,
        int slotIndex,
        string characterName,
        FirstDeathCause cause,
        int toursAtDeath,
        string titleAtDeath)
    {
        var gardenXform = Transform(garden);
        if (gardenXform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        if (gardenComp.EchoSlotOffsets.Count == 0)
            return null;

        var indices = _map.TileIndicesFor(gridUid, grid, gardenXform.Coordinates)
                      + MarkGardenLayout.SlotOffset(slotIndex, gardenComp.EchoSlotOffsets);

        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef) || _turf.IsSpace(tileRef))
        {
            _echoSkippedTiles++;
            Log.Warning($"Echo slot {slotIndex} resolves to a missing/space tile beside {ToPrettyString(garden)}; projection skipped.");
            return null;
        }

        var uid = Spawn(EchoProjectionRules.PrototypeFor(stage), _map.GridTileToLocal(gridUid, grid, indices));
        // ForceEchoAnchorFailureForTests is the only production-side test seam that can force the
        // post-spawn Del branch: a solid non-space tile always anchors under the current engine, so
        // the defensive no-orphans path is otherwise unreachable from integration fixtures.
        var anchored = Transform(uid).Anchored && !ForceEchoAnchorFailureForTests;
        if (!anchored)
        {
            _echoLastFailedAnchorUid = uid;
            Del(uid);
            _echoFailedAnchorDeletes++;
            Log.Warning($"Echo projection for slot {slotIndex} failed to anchor; deleted (no orphans).");
            return null;
        }

        var echo = EnsureComp<SolreignEchoComponent>(uid);
        echo.CharacterName = characterName;
        echo.Cause = cause;
        echo.ToursAtDeath = toursAtDeath;
        echo.TitleAtDeath = titleAtDeath;
        echo.Stage = stage;
        return uid;
    }

    /// <summary>
    ///     Kill-switch side effect: <c>solreign.echo.enabled</c> going false mid-round must not leave
    ///     visible static-physics Echo projections (examine was already gated; the entities themselves
    ///     were not). Also bumps <see cref="_echoProjectionGeneration"/> so in-flight projection aborts.
    /// </summary>
    private void OnEchoEnabledChanged(bool enabled)
    {
        _echoEnabled = enabled;
        if (enabled)
            return;

        _echoProjectionGeneration++;
        ClearProjectedEchoes();
    }

    /// <summary>Deletes every live <see cref="SolreignEchoComponent"/> projection (round-local; ledger rows stay).</summary>
    private void ClearProjectedEchoes()
    {
        var toDelete = new List<EntityUid>();
        var query = EntityQueryEnumerator<SolreignEchoComponent>();
        while (query.MoveNext(out var uid, out _))
            toDelete.Add(uid);

        foreach (var uid in toDelete)
            Del(uid);
    }

    // ---------------------------------------------------------------- examine (§8)

    /// <summary>
    ///     No owner/stranger branch (unlike <see cref="OnMarkExamined"/>) — a first death is
    ///     already a public disclosure, so every examiner reads the identical composition: a
    ///     fixed header, the public naming line, the live-recomputed epitaph plate text, and the
    ///     closed-vocabulary cause label. No variable substitution beyond <c>$name</c> — every
    ///     other string is either a fixed loc key or a value already flowing through an existing,
    ///     tested, closed-vocabulary pipeline (EOD-spec §8).
    /// </summary>
    private void OnEchoExamined(EntityUid uid, SolreignEchoComponent component, ExaminedEvent args)
    {
        if (!_echoEnabled)
            return;

        args.PushMarkup(Loc.GetString(EchoHeaderKey));
        args.PushMarkup(Loc.GetString(EchoInMemoryOfKey, ("name", component.CharacterName)));

        var epitaph = FirstDeathEpitaphPicker.Pick(component.ToursAtDeath, component.Cause, component.TitleAtDeath);
        args.PushMarkup(epitaph.Text);
        args.PushMarkup(FirstDeathCopy.CauseLabelFor(component.Cause));
    }

    // ---------------------------------------------------------------- test seams (house ForTests idiom)

    /// <summary>
    ///     Finished <see cref="ProjectEchoes"/> invocations (every exit path) — integration-test
    ///     visibility only. Monotonic; snapshot before kick and poll for a greater value.
    /// </summary>
    internal int EchoProjectionCompletedCountForTests => _echoProjectionCompletedCount;

    /// <summary>
    ///     Post-spawn failed-anchor deletions inside <see cref="TrySpawnEcho"/> — integration-test
    ///     visibility only. Distinguishes space/missing skip (counter unchanged) from spawn-then-Del.
    /// </summary>
    internal int EchoFailedAnchorDeletesForTests => _echoFailedAnchorDeletes;

    /// <summary>Space/missing-tile skip count — proves the skip branch ran. Test evidence only.</summary>
    internal int EchoSkippedTilesForTests => _echoSkippedTiles;

    /// <summary>UID of the most recent spawned-then-deleted failed-anchor Echo. Test evidence only.</summary>
    internal EntityUid? EchoLastFailedAnchorUidForTests => _echoLastFailedAnchorUid;

    /// <summary>
    ///     When true, <see cref="TrySpawnEcho"/> treats every post-spawn as unanchored so the
    ///     no-orphans Del branch runs. Integration-test only — never set on a live server.
    /// </summary>
    internal bool ForceEchoAnchorFailureForTests { get; set; }
}
