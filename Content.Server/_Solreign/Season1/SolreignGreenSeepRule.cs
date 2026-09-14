using System.Collections.Generic;
using System.Linq;
using Content.Server.Storage.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Beat 2 of Solreign Season 1, "The Ledger Wakes" — "The Green Seep". See
///     <see cref="SolreignGreenSeepRuleComponent"/> and
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 2. Admin-triggered only: the
///     prototype (Resources/Prototypes/_Solreign/GameRules/season1.yml) is never referenced by any random
///     event table, so it only ever starts via the admin "Game Rules" panel or the
///     <c>addgamerule</c>/<c>startgamerule</c> console commands.
///
///     Systems touched: <c>ChatSystem</c> (PA announcements, inherited from <see cref="StationEventSystem{T}"/>),
///     <c>GameTicker</c> (reading the round's live player count for the clipboard's "serial number", also
///     inherited), <see cref="PaperSystem"/> (stamping the clipboard's text at spawn time), and
///     <see cref="EntityStorageSystem"/> (stashing the clue in a random station locker — the same idiom as
///     upstream <c>RandomEntityStorageSpawnRule</c>).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this wave):
///     the "serial number" stamped on the clipboard is a live read of <c>GameTicker.PlayerGameStatuses</c>,
///     not a Season Ledger call. A later wave could swap it for a season-cumulative player count via
///     <c>SeasonLedgerSystem</c> once that stat exists.
/// </summary>
public sealed partial class SolreignGreenSeepRule : StationEventSystem<SolreignGreenSeepRuleComponent>
{
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;

    private static readonly string[] Lines =
    {
        "solreign-season1-beat2-line-1",
        "solreign-season1-beat2-line-2",
        "solreign-season1-beat2-line-3",
    };

    protected override void Started(EntityUid uid, SolreignGreenSeepRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        component.LastLineIndex = 0;
        Announce(Lines[0]);

        SpawnClue(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignGreenSeepRuleComponent component, GameRuleComponent gameRule, float frameTime)
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
    ///     Stamps the clipboard's "serial number" with the round's current joined-player count, spawns it,
    ///     and stashes it in a random valid locker on the target station.
    /// </summary>
    private void SpawnClue(SolreignGreenSeepRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
            return;

        var playerCount = GameTicker.PlayerGameStatuses.Count(kv => kv.Value == PlayerGameStatus.JoinedGame);

        var clipboard = Spawn(component.CluePrototype, MapCoordinates.Nullspace);
        _paper.SetContent(clipboard, Loc.GetString("solreign-season1-beat2-clipboard-content", ("serial", playerCount)));

        StashInRandomLocker(clipboard, station.Value);
    }

    /// <summary>
    ///     Inserts <paramref name="clue"/> into a random valid locker on <paramref name="station"/> — the same
    ///     idiom as upstream <c>RandomEntityStorageSpawnRule</c>. Deletes the clue if no locker is found (e.g.
    ///     a station with no containers) so nothing is orphaned in nullspace.
    /// </summary>
    private void StashInRandomLocker(EntityUid clue, EntityUid station)
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
            return;
        }

        var (locker, storageComp) = RobustRandom.Pick(validLockers);
        if (!_entityStorage.Insert(clue, locker, storageComp))
            Del(clue);
    }
}
