using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Mobs.Components;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Shuttles;

/// <summary>
///     Marks an established player who re-boards the arrivals shuttle. This is deliberately not
///     <see cref="PendingClockInComponent"/>: ArrivalsSystem's genuine-latejoin coupon path also
///     calls <c>TryMakeLateJoinAntag</c>, which made every re-board trip a fresh antag roll.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignArrivalsReboardRescueComponent : Component
{
    public EntityUid Shuttle;
    public EntityUid Station;
    public bool WasAboardAtDeparture;
}

/// <summary>
///     Records the causal precondition before vanilla can dump anyone. Keeping this in a separate
///     system permits one broadcast handler before ArrivalsSystem and one after it without a
///     duplicate event subscription.
/// </summary>
public sealed partial class SolreignArrivalsReboardPreflightSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted, before: new[] { typeof(ArrivalsSystem) });
    }

    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        var query = EntityQueryEnumerator<SolreignArrivalsReboardRescueComponent, TransformComponent>();
        while (query.MoveNext(out _, out var rescue, out var xform))
        {
            if (rescue.Shuttle == args.Entity)
                rescue.WasAboardAtDeparture = xform.GridUid == args.Entity;
        }
    }
}

/// <summary>
///     Stops the arrivals shuttle from leaving established re-boarders in open space without
///     routing them through vanilla's genuine-latejoin antagonist-selection path.
///
///     Vanilla intentionally dumps uncouponed mobs to the shuttle's old map pose when the shuttle
///     leaves the station. Re-boarders are marked when they enter the arrivals grid, then the
///     broadcast FTL handler runs after <see cref="ArrivalsSystem"/> and moves only mobs that the
///     vanilla dump parented to the bare departure map onto a real late-join spawn for that station.
///     A player who stepped off normally is not on the bare map and is merely unmarked.
/// </summary>
public sealed partial class SolreignArrivalsReboardRescueSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfgManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ActorSystem _actor = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _station = default!;

    [Dependency] private EntityQuery<PendingClockInComponent> _pendingQuery = default!;
    [Dependency] private EntityQuery<ArrivalsShuttleComponent> _arrivalsShuttleQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateComponent, EntParentChangedMessage>(OnMobParentChanged);
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted, after: new[] { typeof(ArrivalsSystem) });
    }

    private void OnMobParentChanged(EntityUid uid, MobStateComponent component, ref EntParentChangedMessage args)
    {
        // A genuine late joiner already owns vanilla's coupon and must stay on vanilla's path.
        if (_pendingQuery.HasComponent(uid))
            return;

        // Returns enabled means vanilla never dumps, so there is nothing to rescue.
        if (_cfgManager.GetCVar(CCVars.ArrivalsReturns))
        {
            RemCompDeferred<SolreignArrivalsReboardRescueComponent>(uid);
            return;
        }

        var shuttle = Transform(uid).GridUid;
        if (shuttle == null || !_arrivalsShuttleQuery.TryComp(shuttle.Value, out var arrivals))
        {
            // During vanilla's dump the preflight flag is already true, so preserve the marker for
            // the post-handler. Any ordinary exit clears it immediately and cannot be misclassified.
            if (TryComp<SolreignArrivalsReboardRescueComponent>(uid, out var existing) &&
                !existing.WasAboardAtDeparture)
            {
                RemCompDeferred<SolreignArrivalsReboardRescueComponent>(uid);
            }
            return;
        }

        var rescue = EnsureComp<SolreignArrivalsReboardRescueComponent>(uid);
        rescue.Shuttle = shuttle.Value;
        rescue.Station = arrivals.Station;
    }

    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        if (args.FromMapUid == null)
            return;

        var query = EntityQueryEnumerator<SolreignArrivalsReboardRescueComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var rescue, out var xform))
        {
            if (rescue.Shuttle != args.Entity)
                continue;

            // Only a mob proven aboard immediately before ArrivalsSystem ran is eligible. This
            // prevents an intentional off-grid exit from looking like a vanilla dump.
            if (!rescue.WasAboardAtDeparture)
            {
                RemCompDeferred<SolreignArrivalsReboardRescueComponent>(uid);
                continue;
            }

            if (xform.ParentUid != args.FromMapUid)
            {
                // Leaving the arrivals side does not dump. Keep the marker for the next station-side
                // departure, but reset the per-departure proof.
                rescue.WasAboardAtDeparture = false;
                continue;
            }

            if (TryGetStationSpawn(rescue.Station, out var spawn))
            {
                _transform.SetCoordinates(uid, xform, spawn);
                if (_actor.TryGetSession(uid, out var session))
                    _chat.DispatchServerMessage(session!, Loc.GetString("latejoin-arrivals-teleport-to-spawn"));
                RemCompDeferred<SolreignArrivalsReboardRescueComponent>(uid);
                continue;
            }

            // A malformed station with no LateJoin spawn must fail safe. The shuttle has already
            // moved to FTL space before this event is raised, so putting the mob back aboard keeps
            // them alive until they can exit normally or a later station-side trip can retry.
            _transform.SetCoordinates(uid, xform, new EntityCoordinates(rescue.Shuttle, Vector2.Zero));
            rescue.WasAboardAtDeparture = false;
        }
    }

    private bool TryGetStationSpawn(EntityUid station, out EntityCoordinates spawn)
    {
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possible = new ValueList<EntityCoordinates>(32);

        while (points.MoveNext(out var uid, out var point, out var xform))
        {
            if (point.SpawnType == SpawnPointType.LateJoin &&
                _station.GetOwningStation(uid, xform) == station)
            {
                possible.Add(xform.Coordinates);
            }
        }

        if (possible.Count == 0)
        {
            spawn = default;
            return false;
        }

        spawn = _random.Pick(possible);
        return true;
    }
}
