using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Player;

namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Behavior for "Specimen Zero" — see <see cref="SolreignSpecimenZeroRuleComponent"/> for the event
///     summary and Resources/Prototypes/_Solreign/GameRules/minibosses.yml for the prototype.
///     Admin-invocable today (<c>addgamerule SolreignSpecimenZero</c>); not wired into any random event
///     table.
///
///     Systems touched (binding-rule count): <see cref="MobStateSystem"/> (alive-crew headcount for the
///     population gate) + <c>StationSystem</c>/<c>ChatSystem</c> (inherited from
///     <see cref="StationEventSystem{T}"/>: station scoping, spawn-tile selection, warning + arrival
///     announcements).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this
///     wave, same idiom as <c>SolreignLedgerDiscrepancyRule</c>): the actual per-kill trace lives on
///     <see cref="SolreignMiniBossComponent"/>/<see cref="SolreignMiniBossSystem"/>, fired when the
///     spawned escapee dies, not here at spawn time.
/// </summary>
public sealed partial class SolreignSpecimenZeroRule : StationEventSystem<SolreignSpecimenZeroRuleComponent>
{
    [Dependency] private MobStateSystem _mobState = default!;

    protected override void Started(EntityUid uid, SolreignSpecimenZeroRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.WarningAnnounced)
            return;

        if (!TryGetRandomStation(out var station))
        {
            Sawmill.Info("SolreignSpecimenZero: no event-eligible station found; rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        var alive = CountAliveOnStation(station.Value);
        if (!MiniBossPopulationGate.InBounds(alive, component.MinPopulation, component.MaxPopulation))
        {
            Sawmill.Info($"SolreignSpecimenZero: population gate failed ({alive} alive; needs >= {component.MinPopulation}" +
                (component.MaxPopulation > 0 ? $" and <= {component.MaxPopulation}" : string.Empty) +
                "); rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        component.TargetStation = station.Value;
        component.Elapsed = 0f;
        component.WarningAnnounced = true;

        ChatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-specimen-zero-warning", ("seconds", (int) component.WarningSeconds)),
            SolreignMiniBossAudit.Sender,
            playSound: true,
            colorOverride: SolreignMiniBossAudit.Color);
    }

    protected override void ActiveTick(EntityUid uid, SolreignSpecimenZeroRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.Spawned || !component.WarningAnnounced)
            return;

        component.Elapsed += frameTime;
        if (!MiniBossTelegraph.WarningElapsed(component.Elapsed, component.WarningSeconds))
            return;

        SpawnBoss(uid, component, gameRule);
    }

    private void SpawnBoss(EntityUid uid, SolreignSpecimenZeroRuleComponent component, GameRuleComponent gameRule)
    {
        component.Spawned = true;

        if (component.TargetStation is not { } station ||
            !TryComp<StationDataComponent>(station, out var stationData) ||
            !TryFindRandomTileOnStation((station, stationData), out _, out _, out var coords))
        {
            Sawmill.Warning("SolreignSpecimenZero: no valid spawn tile found on station after the warning elapsed; rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        Spawn(component.BossPrototype, coords);

        ChatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-specimen-zero-breach"),
            SolreignMiniBossAudit.Sender,
            playSound: true,
            colorOverride: SolreignMiniBossAudit.Color);

        ForceEndSelf(uid, gameRule);
    }

    /// <summary>Alive, player-controlled crew count on <paramref name="station"/> — same idiom as
    /// <c>SolreignLedgerDiscrepancyRule.SpawnClue</c>'s candidate gathering.</summary>
    private int CountAliveOnStation(EntityUid station)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out _, out var mobState, out var xform))
        {
            if (StationSystem.GetOwningStation(candidate, xform) != station)
                continue;

            if (!_mobState.IsAlive(candidate, mobState))
                continue;

            count++;
        }

        return count;
    }
}
