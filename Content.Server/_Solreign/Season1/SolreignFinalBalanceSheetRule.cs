using System.Collections.Generic;
using System.Linq;
using Content.Server.Storage.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Beat 6 of Solreign Season 1, "The Ledger Wakes" — "The Final Balance Sheet", the season finale
///     hook. See <see cref="SolreignFinalBalanceSheetRuleComponent"/> and
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 6. Admin-triggered only: the
///     prototype (Resources/Prototypes/_Solreign/GameRules/season1.yml) is never referenced by any random
///     event table, so it only ever starts via the admin "Game Rules" panel or the
///     <c>addgamerule</c>/<c>startgamerule</c> console commands.
///
///     Systems touched: <c>ChatSystem</c> (PA announcements, inherited from <see cref="StationEventSystem{T}"/>),
///     <see cref="ActorComponent"/> (rostering every currently-attached crew member on the target station
///     for the book's final page), <see cref="PaperSystem"/> (stamping the book's text at spawn time),
///     and <see cref="EntityStorageSystem"/> (stashing the book in a random station locker — the same
///     idiom as upstream <c>RandomEntityStorageSpawnRule</c>).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this
///     wave): the roster is a live read of currently-attached <see cref="ActorComponent"/> entities on the
///     chosen station — every player <em>currently connected to that station</em>, not the bible's literal
///     "every player on the server" (this fork's station-events idiom is single-station-scoped; a later
///     multi-station wave could widen this once that matters). Names come from live entity metadata, not
///     Season Ledger stats — the same "genuine round data, not a ledger call" idiom Beat 2 and Beat 5 use.
///
///     DELIBERATELY UNRESOLVED: unlike Beats 1-3, this rule's third PA line is the season's cliffhanger,
///     not a wrap-up — no fourth line, no calming follow-up, no bespoke resolution logic. The event just
///     runs out its <c>StationEvent.duration</c> with the cliffhanger still standing. The actual
///     resolution is the live Grand Opening event, not anything in this file.
/// </summary>
public sealed partial class SolreignFinalBalanceSheetRule : StationEventSystem<SolreignFinalBalanceSheetRuleComponent>
{
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;

    private static readonly string[] Lines =
    {
        "solreign-season1-beat6-line-1",
        "solreign-season1-beat6-line-2",
        "solreign-season1-beat6-line-3", // the cliffhanger — never followed by a resolution line
    };

    protected override void Started(EntityUid uid, SolreignFinalBalanceSheetRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        component.LastLineIndex = 0;
        Announce(Lines[0]);

        SpawnClue(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignFinalBalanceSheetRuleComponent component, GameRuleComponent gameRule, float frameTime)
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
    ///     Rosters every currently-attached crew member on a random station (capped at
    ///     <see cref="SolreignFinalBalanceSheetRuleComponent.MaxRosterEntries"/>), spawns the ledger book
    ///     with their names on its final page, and stashes it in a random valid locker on that station.
    /// </summary>
    private void SpawnClue(SolreignFinalBalanceSheetRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
            return;

        var names = new List<string>();
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out _, out var xform))
        {
            if (StationSystem.GetOwningStation(candidate, xform) != station)
                continue;

            names.Add(MetaData(candidate).EntityName);

            if (names.Count >= component.MaxRosterEntries)
                break;
        }

        var roster = names.Count > 0
            ? string.Join("\n", names.Select(n => $"[X] {n}"))
            : Loc.GetString("solreign-season1-beat6-book-roster-empty");

        var book = Spawn(component.CluePrototype, MapCoordinates.Nullspace);
        _paper.SetContent(book, Loc.GetString("solreign-season1-beat6-book-content", ("roster", roster)));

        StashInRandomLocker(book, station.Value);
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
