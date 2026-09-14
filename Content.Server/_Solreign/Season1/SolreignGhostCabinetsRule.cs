using System.Collections.Generic;
using System.Linq;
using Content.Server.Storage.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Beat 3 of Solreign Season 1, "The Ledger Wakes" — "Filing Cabinets from the Void". See
///     <see cref="SolreignGhostCabinetsRuleComponent"/> and
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 3. Admin-triggered only: the
///     prototype (Resources/Prototypes/_Solreign/GameRules/season1.yml) is never referenced by any random
///     event table, so it only ever starts via the admin "Game Rules" panel or the
///     <c>addgamerule</c>/<c>startgamerule</c> console commands.
///
///     Systems touched: <c>ChatSystem</c> (PA announcements, inherited from <see cref="StationEventSystem{T}"/>),
///     <see cref="IPrototypeManager"/> (rolling one of several pre-written "archived transcript" flavors from
///     the <c>Season1GhostCabinetTranscripts</c> localized dataset), <see cref="PaperSystem"/> (stamping the
///     folder's text at spawn time), and <see cref="EntityStorageSystem"/> (stashing the clue in a random
///     station locker — the same idiom as upstream <c>RandomEntityStorageSpawnRule</c>).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this wave):
///     the transcript text is randomized from a static in-universe flavor dataset, not read from any real
///     historical round data (SS14 does not retain cross-round chat transcripts). A later wave could hook
///     genuine archived data through <c>SeasonLedgerSystem</c>/round history once that exists.
/// </summary>
public sealed partial class SolreignGhostCabinetsRule : StationEventSystem<SolreignGhostCabinetsRuleComponent>
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;

    private static readonly string[] Lines =
    {
        "solreign-season1-beat3-line-1",
        "solreign-season1-beat3-line-2",
        "solreign-season1-beat3-line-3",
    };

    protected override void Started(EntityUid uid, SolreignGhostCabinetsRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        component.LastLineIndex = 0;
        component.LastObservation = SolreignGhostCabinetsDeliveryOutcome.NotAttempted;
        Announce(Lines[0]);

        SpawnClue(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignGhostCabinetsRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        component.Elapsed += frameTime;
        var wantIndex = SolreignSeason1BeatSequencer.LineIndexForElapsed(component.Elapsed, component.LineCadenceSeconds, Lines.Length);
        for (var i = component.LastLineIndex + 1; i <= wantIndex; i++)
        {
            Announce(Lines[i]);
        }

        if (wantIndex > component.LastLineIndex)
            component.LastLineIndex = wantIndex;
    }

    private void Announce(string locKey)
    {
        ChatSystem.DispatchGlobalAnnouncement(
            Loc.GetString(locKey),
            SolreignSeason1Audit.Sender,
            playSound: false,
            colorOverride: SolreignSeason1Audit.Color);
    }

    /// <summary>
    ///     Rolls one of the pre-written transcript flavors, spawns the folder with it, and stashes it in a
    ///     random valid locker on the target station.
    /// </summary>
    private void SpawnClue(SolreignGhostCabinetsRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
        {
            component.LastObservation = SolreignGhostCabinetsDeliveryOutcome.NoStation;
            return;
        }

        if (!_prototype.TryIndex(component.TranscriptDataset, out var transcripts) ||
            transcripts.Values.Count == 0)
        {
            component.LastObservation = SolreignGhostCabinetsDeliveryOutcome.TranscriptDatasetUnavailable;
            return;
        }

        var transcriptKey = RobustRandom.Pick(transcripts.Values);
        var transcriptText = Loc.GetString(transcriptKey);

        var folder = Spawn(component.CluePrototype, MapCoordinates.Nullspace);
        _paper.SetContent(folder, transcriptText);

        StashInRandomLocker(folder, station.Value, component);
    }

    /// <summary>
    ///     Inserts <paramref name="clue"/> into a random valid locker on <paramref name="station"/> — the same
    ///     idiom as upstream <c>RandomEntityStorageSpawnRule</c>. Deletes the clue if no locker is found (e.g.
    ///     a station with no containers) so nothing is orphaned in nullspace.
    /// </summary>
    private void StashInRandomLocker(
        EntityUid clue,
        EntityUid station,
        SolreignGhostCabinetsRuleComponent component)
    {
        var validLockers = new List<(EntityUid, EntityStorageComponent)>();
        var query = EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var storage, out var xform))
        {
            if (StationSystem.GetOwningStation(ent, xform) != station)
                continue;

            if (!_entityStorage.CanInsert(clue, ent, storage))
                continue;

            validLockers.Add((ent, storage));
        }

        if (validLockers.Count == 0)
        {
            Del(clue);
            component.LastObservation = SolreignGhostCabinetsDeliveryOutcome.NoInsertableStorage;
            return;
        }

        var (locker, storageComp) = RobustRandom.Pick(validLockers);
        var insertReturned = _entityStorage.Insert(clue, locker, storageComp);
        var contained =
            TryComp<InsideEntityStorageComponent>(clue, out var inside) &&
            inside.Storage == locker &&
            storageComp.Contents.ContainedEntities.Contains(clue);
        component.LastObservation =
            SolreignGhostCabinetsDeliveryRules.ClassifyInsertion(insertReturned, contained);

        if (!insertReturned)
        {
            Del(clue);
        }
    }
}
