using System.Collections.Generic;
using Content.Server.Storage.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Beat 1 of Solreign Season 1, "The Ledger Wakes" — "Preemptive Bookkeeping". See
///     <see cref="SolreignLedgerDiscrepancyRuleComponent"/> and
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 1. Admin-triggered only: the
///     prototype (Resources/Prototypes/_Solreign/GameRules/season1.yml) is never referenced by any random
///     event table, so it only ever starts via the admin "Game Rules" panel or the
///     <c>addgamerule</c>/<c>startgamerule</c> console commands.
///
///     Systems touched: <c>ChatSystem</c> (PA announcements, inherited from <see cref="StationEventSystem{T}"/>),
///     <see cref="MobStateSystem"/> + <see cref="ActorComponent"/> (picking a currently-alive,
///     player-controlled crew member to name on the ledger slip), <see cref="PaperSystem"/> (stamping the
///     slip's text at spawn time), and <see cref="EntityStorageSystem"/> (stashing the slip in a random
///     station locker — the same idiom as upstream <c>RandomEntityStorageSpawnRule</c>).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this wave):
///     the crew member named on the slip is picked live from the round's mobs, not from Season Ledger data.
///     A later wave can swap this for a <c>SeasonLedgerSystem</c> lookup once a beat wants to specifically
///     call out high-ranked accounts — Beat 4 ("Prestigious Radiation") is the natural home for that hook.
/// </summary>
public sealed partial class SolreignLedgerDiscrepancyRule : StationEventSystem<SolreignLedgerDiscrepancyRuleComponent>
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;

    private static readonly string[] Lines =
    {
        "solreign-season1-beat1-line-1",
        "solreign-season1-beat1-line-2",
        "solreign-season1-beat1-line-3",
    };

    protected override void Started(EntityUid uid, SolreignLedgerDiscrepancyRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        component.LastLineIndex = 0;
        Announce(Lines[0]);

        SpawnClue(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignLedgerDiscrepancyRuleComponent component, GameRuleComponent gameRule, float frameTime)
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
    ///     Picks a currently-alive, player-controlled crew member on the target station (falling back to a
    ///     generic placeholder name if none can be found — an empty or all-ghost round shouldn't throw),
    ///     spawns the ledger slip naming them, and stashes it in a random valid locker on the same station.
    /// </summary>
    private void SpawnClue(SolreignLedgerDiscrepancyRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
            return;

        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out _, out var mobState, out var xform))
        {
            if (StationSystem.GetOwningStation(candidate, xform) != station)
                continue;

            if (!_mobState.IsAlive(candidate, mobState))
                continue;

            candidates.Add(candidate);
        }

        var name = candidates.Count > 0
            ? MetaData(RobustRandom.Pick(candidates)).EntityName
            : Loc.GetString("solreign-season1-beat1-slip-fallback-name");

        var slip = Spawn(component.CluePrototype, MapCoordinates.Nullspace);
        _paper.SetContent(slip, Loc.GetString("solreign-season1-beat1-slip-content", ("name", name)));

        StashInRandomLocker(slip, station.Value);
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
