using Content.Server.Pinpointer;
using Content.Server.Station.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Station;
using Robust.Server.GameStates;
using Robust.Shared.Player;

namespace Content.Server.Materials;

/// <inheritdoc/>
public sealed partial class OreSiloSystem : SharedOreSiloSystem
{
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private SharedUserInterfaceSystem _userInterface = default!;

    private const float OreSiloPreloadRangeSquared = 225f; // ~1 screen

    public override void Initialize()
    {
        base.Initialize();

        // When a grid becomes a station member (a shuttle docking and being registered, a
        // grid-split re-parented onto the station, etc), every silo on that grid has just gained a
        // station owner. Fold each one's local storage into the (now-shared) station pool up
        // front, satisfying the "migrate on station-ownership acquisition, keep local empty
        // thereafter" invariant instead of waiting for the silo's first write to do it lazily.
        SubscribeLocalEvent<StationGridAddedEvent>(OnStationGridAdded);
    }

    private void OnStationGridAdded(StationGridAddedEvent ev)
    {
        var query = EntityQueryEnumerator<OreSiloComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != ev.GridId)
                continue;

            MigrateSiloIntoStationPool(uid);
        }
    }

    private readonly HashSet<Entity<OreSiloClientComponent>> _clientLookup = new();
    private readonly HashSet<(NetEntity, string, string)> _clientInformation = new();
    private readonly HashSet<EntityUid> _silosToAdd = new();
    private readonly HashSet<EntityUid> _silosToRemove = new();

    protected override void UpdateOreSiloUi(Entity<OreSiloComponent> ent)
    {
        if (!_userInterface.IsUiOpen(ent.Owner, OreSiloUiKey.Key))
            return;
        _clientLookup.Clear();
        _clientInformation.Clear();

        var xform = Transform(ent);

        // Sneakily uses override with TComponent parameter
        _entityLookup.GetEntitiesInRange(xform.Coordinates, ent.Comp.Range, _clientLookup);

        foreach (var client in _clientLookup)
        {
            // don't show already-linked clients.
            if (client.Comp.Silo is not null)
                continue;

            // Don't show clients on the screen if we can't link them.
            if (!CanTransmitMaterials((ent, ent, xform), client))
                continue;

            var netEnt = GetNetEntity(client);
            var name = Identity.Name(client, EntityManager);
            var beacon = _navMap.GetNearestBeaconString(client.Owner, onlyName: true);

            var txt = Loc.GetString("ore-silo-ui-itemlist-entry",
                ("name", name),
                ("beacon", beacon),
                ("linked", ent.Comp.Clients.Contains(client)),
                ("inRange", true));

            _clientInformation.Add((netEnt, txt, beacon));
        }

        // Get all clients of this silo, including those out of range.
        foreach (var client in ent.Comp.Clients)
        {
            var netEnt = GetNetEntity(client);
            var name = Identity.Name(client, EntityManager);
            var beacon = _navMap.GetNearestBeaconString(client, onlyName: true);
            var inRange = CanTransmitMaterials((ent, ent, xform), client);

            var txt = Loc.GetString("ore-silo-ui-itemlist-entry",
                ("name", name),
                ("beacon", beacon),
                ("linked", ent.Comp.Clients.Contains(client)),
                ("inRange", inRange));

            _clientInformation.Add((netEnt, txt, beacon));
        }

        _userInterface.SetUiState(ent.Owner, OreSiloUiKey.Key, new OreSiloBuiState(_clientInformation));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Solving an annoying problem: we need to send the silo to people who are near the silo so that
        // Things don't start wildly mispredicting. We do this as cheaply as possible via grid-based local-pos checks.
        // Sloth okay-ed this in the interim until a better solution comes around.

        var actorQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actorQuery.MoveNext(out _, out var actorComp, out var actorXform))
        {
            _silosToAdd.Clear();
            _silosToRemove.Clear();

            var clientQuery = EntityQueryEnumerator<OreSiloClientComponent, TransformComponent>();
            while (clientQuery.MoveNext(out _, out var clientComp, out var clientXform))
            {
                if (clientComp.Silo == null)
                    continue;

                // We limit it to same-grid checks only for peak perf
                if (actorXform.GridUid != clientXform.GridUid)
                    continue;

                if ((actorXform.LocalPosition - clientXform.LocalPosition).LengthSquared() <= OreSiloPreloadRangeSquared)
                {
                    _silosToAdd.Add(clientComp.Silo.Value);
                }
                else
                {
                    _silosToRemove.Add(clientComp.Silo.Value);
                }
            }

            foreach (var toRemove in _silosToRemove)
            {
                _pvsOverride.RemoveSessionOverride(toRemove, actorComp.PlayerSession);

                // Also drop the override on the station's shared material pool - other silos in
                // range will keep it alive for this session if still needed.
                if (_station.GetOwningStation(toRemove) is { } pool)
                    _pvsOverride.RemoveSessionOverride(pool, actorComp.PlayerSession);
            }
            foreach (var toAdd in _silosToAdd)
            {
                _pvsOverride.AddSessionOverride(toAdd, actorComp.PlayerSession);

                // Materials shown by this silo may have been deposited through any other silo on
                // the same station, so the shared pool entity needs to be networked too.
                if (_station.GetOwningStation(toAdd) is { } pool)
                    _pvsOverride.AddSessionOverride(pool, actorComp.PlayerSession);
            }
        }
    }
}
