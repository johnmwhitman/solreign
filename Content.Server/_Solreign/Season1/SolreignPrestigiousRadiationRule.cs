using System.Collections.Generic;
using Content.Server.Storage.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Season1;

/// <summary>
///     Beat 4 of Solreign Season 1, "The Ledger Wakes" — "Prestigious Radiation". See
///     <see cref="SolreignPrestigiousRadiationRuleComponent"/> and
///     docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 4. Admin-triggered only: the
///     prototype (Resources/Prototypes/_Solreign/GameRules/season1.yml) is never referenced by any random
///     event table, so it only ever starts via the admin "Game Rules" panel or the
///     <c>addgamerule</c>/<c>startgamerule</c> console commands.
///
///     Systems touched: <c>ChatSystem</c> (PA announcements, inherited from <see cref="StationEventSystem{T}"/>),
///     <see cref="MobStateSystem"/> + <see cref="ActorComponent"/> + <see cref="SeasonTitleComponent"/>
///     (finding currently-alive, player-controlled, high-ranked crew), <see cref="PointLightSystem"/>
///     (the bioluminescence itself), and <see cref="EntityStorageSystem"/> (stashing the plaque in a
///     random station locker — the same idiom as upstream <c>RandomEntityStorageSpawnRule</c>).
///
///     Ledger trace — genuine this time, not just a comment (see the forward-reference left in
///     <c>SolreignLedgerDiscrepancyRule</c>'s doc comment naming this beat as the natural home for it):
///     who glows is read live from each crew member's <see cref="SeasonTitleComponent.RankIndex"/>,
///     stamped by <c>Content.Server._Solreign.SeasonLedger.SeasonLedgerSystem</c> at spawn. The threshold
///     check itself is pure and unit-tested — see <see cref="SolreignSeason1PrestigeRules"/>.
/// </summary>
public sealed partial class SolreignPrestigiousRadiationRule : StationEventSystem<SolreignPrestigiousRadiationRuleComponent>
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private PointLightSystem _light = default!;

    private static readonly string[] Lines =
    {
        "solreign-season1-beat4-line-1",
        "solreign-season1-beat4-line-2",
        "solreign-season1-beat4-line-3",
    };

    protected override void Started(EntityUid uid, SolreignPrestigiousRadiationRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        component.LastLineIndex = 0;
        Announce(Lines[0]);

        ApplyBioluminescence(component);
        SpawnClue(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignPrestigiousRadiationRuleComponent component, GameRuleComponent gameRule, float frameTime)
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

    protected override void Ended(EntityUid uid, SolreignPrestigiousRadiationRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        RemoveBioluminescence(component);
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
    ///     Finds every currently-alive, player-controlled crew member on a random station whose Season
    ///     Ledger rank clears <see cref="SolreignPrestigiousRadiationRuleComponent.HighRankThreshold"/>
    ///     (see <see cref="SolreignSeason1PrestigeRules"/>) and gives them a faint acid-green point light
    ///     — skipping anyone who already has one (a held flashlight, NVGs, etc.) so this never clobbers an
    ///     existing light source. Tracks who it lit in <c>component.Glowing</c> so <c>Ended</c> can strip
    ///     only those back off.
    /// </summary>
    private void ApplyBioluminescence(SolreignPrestigiousRadiationRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
            return;

        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, SeasonTitleComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out _, out var mobState, out var title, out var xform))
        {
            if (StationSystem.GetOwningStation(candidate, xform) != station)
                continue;

            if (!_mobState.IsAlive(candidate, mobState))
                continue;

            if (!SolreignSeason1PrestigeRules.QualifiesForBioluminescence(title.RankIndex, component.HighRankThreshold))
                continue;

            if (HasComp<PointLightComponent>(candidate))
                continue;

            var light = _light.EnsureLight(candidate);
            _light.SetColor(candidate, SolreignSeason1Audit.Color, light);
            _light.SetRadius(candidate, 1.5f, light);
            _light.SetEnergy(candidate, 0.6f, light);
            _light.SetEnabled(candidate, true, light);

            component.Glowing.Add(candidate);
        }
    }

    /// <summary>Strips the bioluminescence this event added, and only that — see <see cref="ApplyBioluminescence"/>.</summary>
    private void RemoveBioluminescence(SolreignPrestigiousRadiationRuleComponent component)
    {
        foreach (var candidate in component.Glowing)
        {
            if (!Deleted(candidate))
                _light.RemoveLightDeferred(candidate);
        }

        component.Glowing.Clear();
    }

    /// <summary>Spawns the engraved plaque and stashes it in a random valid locker on the target station.</summary>
    private void SpawnClue(SolreignPrestigiousRadiationRuleComponent component)
    {
        if (!TryGetRandomStation(out var station))
            return;

        var plaque = Spawn(component.CluePrototype, MapCoordinates.Nullspace);
        StashInRandomLocker(plaque, station.Value);
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
