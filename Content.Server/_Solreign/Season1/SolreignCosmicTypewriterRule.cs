using System.Collections.Generic;
using Content.Server.Storage.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Beat 5 of Solreign Season 1, "The Ledger Wakes" — "The Cosmic Typewriter". See
///     <see cref="SolreignCosmicTypewriterRuleComponent"/> and
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 5. Admin-triggered only: the
///     prototype (Resources/Prototypes/_Solreign/GameRules/season1.yml) is never referenced by any random
///     event table, so it only ever starts via the admin "Game Rules" panel or the
///     <c>addgamerule</c>/<c>startgamerule</c> console commands.
///
///     Systems touched: <c>ChatSystem</c> (PA announcements, inherited from <see cref="StationEventSystem{T}"/>),
///     <c>GameTicker</c> (reading the round's live elapsed duration for the key's stamp, also inherited),
///     <see cref="PaperSystem"/> (stamping the key's text at spawn time), and
///     <see cref="EntityStorageSystem"/> (stashing the clue in a random station locker — the same idiom
///     as upstream <c>RandomEntityStorageSpawnRule</c>).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this wave):
///     the stamped time is a live read of <c>GameTicker.RoundDuration()</c> — the same "real round data,
///     no Season Ledger call" idiom Beat 2's clipboard serial number uses.
/// </summary>
public sealed partial class SolreignCosmicTypewriterRule : StationEventSystem<SolreignCosmicTypewriterRuleComponent>
{
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;

    private static readonly string[] Lines =
    {
        "solreign-season1-beat5-line-1",
        "solreign-season1-beat5-line-2",
        "solreign-season1-beat5-line-3",
    };

    protected override void Started(EntityUid uid, SolreignCosmicTypewriterRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        component.LastLineIndex = 0;
        Announce(Lines[0]);

        SpawnClue(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignCosmicTypewriterRuleComponent component, GameRuleComponent gameRule, float frameTime)
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
    ///     Stamps the key with how far into the round the rift tore open, spawns it, and stashes it in a
    ///     random valid locker on the target station.
    /// </summary>
    private void SpawnClue(SolreignCosmicTypewriterRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
            return;

        var elapsed = GameTicker.RoundDuration();
        var stamp = $"{(int) elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

        var key = Spawn(component.CluePrototype, MapCoordinates.Nullspace);
        _paper.SetContent(key, Loc.GetString("solreign-season1-beat5-key-content", ("stamp", stamp)));

        StashInRandomLocker(key, station.Value);
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
