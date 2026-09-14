using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Player;

namespace Content.Server._Solreign.Terminator;

/// <summary>
///     Behavior for the Compliance Hunt hunter ghost-role event — see
///     <see cref="SolreignComplianceHunterRuleComponent"/> for the data bag and
///     Resources/Prototypes/_Solreign/GameRules/compliance_hunt.yml for the prototype.
///
///     Admin-invocable (<c>addgamerule SolreignComplianceHunterRule</c> then <c>startgamerule</c>)
///     and wired into the Compliance Hunt preset both as a roundstart rule and as a mid-round
///     StationEvent via <c>SolreignComplianceGhostEventsTable</c>.
///
///     Systems touched: <c>StationSystem</c>/<c>ChatSystem</c> (inherited from
///     <see cref="StationEventSystem{T}"/>: station scoping, spawn-tile selection, arrival
///     announcement) + <see cref="ComplianceFixationComponent"/> headcount for the concurrent-spawn
///     gate. The CRU's own <c>ComplianceRetrievalUnitSystem</c> handles fixation after claim; this
///     rule only places the body and ends cleanly. Modeled directly on the already-shipped
///     <c>SolreignAuditorPrimeRule</c> (same StationEventSystem&lt;T&gt; spawn-on-tile + ForceEndSelf
///     shape, swapping the population gate for a concurrent-hunter-count gate).
/// </summary>
public sealed partial class SolreignComplianceHunterRule : StationEventSystem<SolreignComplianceHunterRuleComponent>
{
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>Loc-string sweep: PA sender for Retention Division broadcasts.</summary>
    private string RetentionSender => Loc.GetString("solreign-compliance-hunter-sender");

    /// <summary>Retention Division acid-green — matches the CRU ghost-role rules color.</summary>
    private static readonly Color ArrivalColor = Color.FromHex("#9acd32");

    protected override void Started(
        EntityUid uid,
        SolreignComplianceHunterRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Spawned)
            return;

        if (!TryGetRandomStation(out var station))
        {
            Sawmill.Info("SolreignComplianceHunter: no event-eligible station found; rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        var alive = CountAliveOnStation(station.Value);
        var scaledMax = ComplianceHunterSpawnRules.CalculateMaxConcurrent(alive, component.PlayersPerHunter, component.MaxConcurrent);
        var existing = CountLiveHunters();

        if (!ComplianceHunterSpawnRules.MaySpawn(existing, scaledMax))
        {
            Sawmill.Info(
                $"SolreignComplianceHunter: concurrent gate failed ({existing} live; scaled max {scaledMax} for {alive} pop); rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        if (!TryComp<StationDataComponent>(station.Value, out var stationData) ||
            !TryFindRandomTileOnStation((station.Value, stationData), out _, out _, out var coords))
        {
            Sawmill.Warning("SolreignComplianceHunter: no valid spawn tile found on station; rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        Spawn(component.HunterPrototype, coords);
        component.Spawned = true;

        ChatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-compliance-hunter-arrival"),
            RetentionSender,
            playSound: true,
            colorOverride: ArrivalColor);

        // Fire-and-forget: the CRU mob (and its GhostRole) outlives this rule instance. Ending
        // cleanly keeps the Game Rules panel / addgamerule path from leaving a stuck active rule.
        ForceEndSelf(uid, gameRule);
    }

    /// <summary>
    ///     Live Compliance Retrieval Units currently on the map (any entity with
    ///     <see cref="ComplianceFixationComponent"/>). Used by the concurrent-spawn gate. Query-only
    ///     (entity + component both discarded, never a field touched), which stays within
    ///     ComplianceFixationComponent's own [Access(typeof(ComplianceRetrievalUnitSystem))] lock —
    ///     this rule counts matches, it never reads/writes the component.
    /// </summary>
    private int CountLiveHunters()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ComplianceFixationComponent>();
        while (query.MoveNext(out _, out _))
            count++;
        return count;
    }

    /// <summary>
    ///     Alive, player-controlled crew count on <paramref name="station"/> — same idiom as
    ///     <c>SolreignLedgerDiscrepancyRule.SpawnClue</c>'s candidate gathering.
    /// </summary>
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
