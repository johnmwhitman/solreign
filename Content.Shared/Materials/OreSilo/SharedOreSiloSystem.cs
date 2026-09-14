using Content.Shared.Power.EntitySystems;
using Content.Shared.Station;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Content.Shared.Materials.OreSilo;

public abstract partial class SharedOreSiloSystem : EntitySystem
{
    [Dependency] private SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiver = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [Dependency] private EntityQuery<OreSiloClientComponent> _clientQuery = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<OreSiloComponent, ToggleOreSiloClientMessage>(OnToggleOreSiloClient);
        SubscribeLocalEvent<OreSiloComponent, ComponentShutdown>(OnSiloShutdown);
        // Eagerly enforce the "local storage empty once it has an owning station" invariant the
        // moment a silo is initialized on an already-stationed grid (e.g. constructed in place),
        // so a silo that somehow starts with local stock folds it into the pool up front rather
        // than only on first use.
        SubscribeLocalEvent<OreSiloComponent, MapInitEvent>(OnSiloMapInit);
        Subs.BuiEvents<OreSiloComponent>(OreSiloUiKey.Key,
            subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBoundUIOpened);
        });

        // Every silo on the same station reads from and writes to one shared, station-wide
        // material bank instead of its own local storage. This mirrors (and chains with) the
        // client -> silo redirection below: client -> silo -> station pool.
        SubscribeLocalEvent<OreSiloComponent, GetStoredMaterialsEvent>(OnSiloGetStoredMaterials);
        SubscribeLocalEvent<OreSiloComponent, ConsumeStoredMaterialsEvent>(OnSiloConsumeStoredMaterials);

        SubscribeLocalEvent<OreSiloClientComponent, GetStoredMaterialsEvent>(OnGetStoredMaterials);
        SubscribeLocalEvent<OreSiloClientComponent, ConsumeStoredMaterialsEvent>(OnConsumeStoredMaterials);
        SubscribeLocalEvent<OreSiloClientComponent, ComponentShutdown>(OnClientShutdown);
    }

    private void OnToggleOreSiloClient(Entity<OreSiloComponent> ent, ref ToggleOreSiloClientMessage args)
    {
        var client = GetEntity(args.Client);

        if (!_clientQuery.TryComp(client, out var clientComp))
            return;

        if (ent.Comp.Clients.Contains(client)) // remove client
        {
            clientComp.Silo = null;
            Dirty(client, clientComp);
            ent.Comp.Clients.Remove(client);
            Dirty(ent);

            UpdateOreSiloUi(ent);
        }
        else // add client
        {
            if (!CanTransmitMaterials((ent, ent), client))
                return;

            var clientMats = _materialStorage.GetStoredMaterials(client, true);
            var inverseMats = new Dictionary<string, int>();
            foreach (var (mat, amount) in clientMats)
            {
                inverseMats.Add(mat, -amount);
            }
            _materialStorage.TryChangeMaterialAmount(client, inverseMats, localOnly: true);
            _materialStorage.TryChangeMaterialAmount(ent.Owner, clientMats);

            ent.Comp.Clients.Add(client);
            Dirty(ent);
            clientComp.Silo = ent;
            Dirty(client, clientComp);

            UpdateOreSiloUi(ent);
        }
    }

    private void OnBoundUIOpened(Entity<OreSiloComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateOreSiloUi(ent);
    }

    private void OnSiloMapInit(Entity<OreSiloComponent> ent, ref MapInitEvent args)
    {
        GetStationMaterialPool(ent.Owner, migrate: true);
    }

    /// <summary>
    /// Public entry point for eagerly folding a silo's local storage into its station's shared
    /// pool at the moment it gains a station owner (e.g. its grid being registered as a station
    /// member). Server-side ownership-transition hooks call this; it's a no-op if the silo has no
    /// owning station or its local storage is already empty.
    /// </summary>
    public void MigrateSiloIntoStationPool(EntityUid silo)
    {
        GetStationMaterialPool(silo, migrate: true);
    }

    private void OnSiloShutdown(Entity<OreSiloComponent> ent, ref ComponentShutdown args)
    {
        foreach (var client in ent.Comp.Clients)
        {
            if (!_clientQuery.TryComp(client, out var comp))
                continue;

            comp.Silo = null;
            Dirty(client, comp);
        }
    }

    protected virtual void UpdateOreSiloUi(Entity<OreSiloComponent> ent)
    {

    }

    /// <summary>
    /// Redirects reads of a silo's stored materials to its station's shared pool, so that a
    /// silo reports materials deposited through any other silo on the same station.
    /// </summary>
    private void OnSiloGetStoredMaterials(Entity<OreSiloComponent> ent, ref GetStoredMaterialsEvent args)
    {
        if (args.LocalOnly)
            return;

        // Reads deliberately do NOT migrate: SharedMaterialStorageSystem.GetStoredMaterials has
        // already snapshotted this silo's local storage into args.Materials before this handler
        // runs, so folding local -> pool here would double-count it (the stale local snapshot PLUS
        // the freshly-migrated pool total). Local and pool stay disjoint on reads and are summed
        // for a correct combined view; migration happens only on the write/consume path below,
        // which is what actually closes the duplication exploit.
        if (GetStationMaterialPool(ent.Owner, migrate: false) is not { } pool)
            return;

        foreach (var (mat, amount) in _materialStorage.GetStoredMaterials(pool, true))
        {
            var existing = args.Materials.GetOrNew(mat);
            args.Materials[mat] = existing + amount;
        }
    }

    /// <summary>
    /// Redirects material insertion/consumption on a silo to its station's shared pool, so that
    /// depositing into (or drawing from) any silo affects every silo on the same station.
    /// A silo's own local <see cref="MaterialStorageComponent.Storage"/> is intentionally left
    /// unused (always empty) once it has an owning station - this also means deconstructing one
    /// of several silos on a station does not dump the whole station's bank on the floor.
    /// </summary>
    private void OnSiloConsumeStoredMaterials(Entity<OreSiloComponent> ent, ref ConsumeStoredMaterialsEvent args)
    {
        if (args.LocalOnly)
            return;

        // migrate: true is the load-bearing half of the duplication fix. Before debiting the pool,
        // any material still sitting in THIS silo's local storage is atomically folded into the
        // pool and the local storage emptied (MigrateLocalStorageIntoPool). That means the pool
        // holds the full balance the caller was told about (which included this silo's local
        // stock), so the debit draws the whole amount from a single owner instead of clamping the
        // pool-side debit against a stale local balance and leaving the difference behind.
        if (GetStationMaterialPool(ent.Owner, migrate: true) is not { } pool)
            return;

        foreach (var (mat, amount) in args.Materials)
        {
            if (!_materialStorage.TryChangeMaterialAmount(pool, mat, amount))
                continue;

            args.Materials[mat] = 0;
        }
    }

    /// <summary>
    /// Gets the entity acting as the shared, station-wide material bank for the given silo,
    /// lazily creating (and locking down) its storage the first time it's needed.
    /// </summary>
    /// <param name="silo">The silo whose station pool is wanted.</param>
    /// <param name="migrate">
    /// When true, additionally folds any material still held in the silo's own local storage into
    /// the pool and empties the local storage (see <see cref="MigrateLocalStorageIntoPool"/>).
    /// This MUST only be done on write/consume paths, never on reads - see
    /// <see cref="OnSiloGetStoredMaterials"/> for why a read-time migration double-counts.
    /// </param>
    /// <remarks>
    /// Silos with no owning station (e.g. an unanchored/test grid) fall back to using their own
    /// local storage, so this never bricks a silo that isn't part of a proper station.
    /// </remarks>
    private EntityUid? GetStationMaterialPool(EntityUid silo, bool migrate)
    {
        var station = _station.GetOwningStation(silo);

        if (station is { } pool)
        {
            var poolStorage = EnsureComp<MaterialStorageComponent>(pool);

            LockDownPool(pool, poolStorage);

            if (migrate)
                MigrateLocalStorageIntoPool(silo, pool, poolStorage);
        }

        return station;
    }

    /// <summary>
    /// Locks a station's shared material pool down so it can only ever be mutated through a
    /// silo's own authorized, in-range BUI flow.
    /// </summary>
    /// <remarks>
    /// The station entity is globally PVS-overridden (every client always has it networked,
    /// balance and all), and <see cref="EjectMaterialMessage"/> can name an arbitrary entity UID.
    /// A freshly-created <see cref="MaterialStorageComponent"/> defaults
    /// <see cref="MaterialStorageComponent.CanEjectStoredMaterials"/> to true, which would let a
    /// modified client address the pool directly and eject the station's entire shared bank from
    /// anywhere on (or off) the station with no range, LOS, open-UI, or silo-authorization check.
    /// This is checked/re-applied every time the pool is touched so it self-heals even if
    /// something else resets the flags.
    /// </remarks>
    private void LockDownPool(EntityUid pool, MaterialStorageComponent poolStorage)
    {
        if (!poolStorage.CanEjectStoredMaterials && !poolStorage.InsertOnInteract && !poolStorage.DropOnDeconstruct)
            return;

        poolStorage.CanEjectStoredMaterials = false;
        poolStorage.InsertOnInteract = false;
        poolStorage.DropOnDeconstruct = false;
        Dirty(pool, poolStorage);
    }

    /// <summary>
    /// Enforces the invariant the redirect handlers below rely on: a silo's own local
    /// <see cref="MaterialStorageComponent.Storage"/> must always be empty once it has an owning
    /// station. If the silo still holds local material - freshly gaining a station owner with
    /// pre-existing local stock, a hot-deploy over an existing local balance, a grid removed and
    /// re-added to a station, etc. - this atomically folds that local stock into the shared
    /// station pool and clears it, so it can never be read or spent twice (once as "local", once
    /// as "pool").
    /// </summary>
    private void MigrateLocalStorageIntoPool(EntityUid silo, EntityUid pool, MaterialStorageComponent poolStorage)
    {
        // The pool's own MaterialStorageComponent redirecting to itself - nothing to migrate.
        if (silo == pool)
            return;

        if (!TryComp<MaterialStorageComponent>(silo, out var siloStorage) || siloStorage.Storage.Count == 0)
            return;

        foreach (var (material, amount) in siloStorage.Storage)
        {
            poolStorage.Storage[material] = poolStorage.Storage.GetOrNew(material) + amount;
        }

        siloStorage.Storage.Clear();

        Dirty(pool, poolStorage);
        Dirty(silo, siloStorage);
    }

    private void OnGetStoredMaterials(Entity<OreSiloClientComponent> ent, ref GetStoredMaterialsEvent args)
    {
        if (args.LocalOnly)
            return;

        if (ent.Comp.Silo is not { } silo)
            return;

        if (!CanTransmitMaterials(silo, ent))
            return;

        var materials = _materialStorage.GetStoredMaterials(silo);

        foreach (var (mat, amount) in materials)
        {
            // Don't supply materials that they don't usually have access to.
            if (!_materialStorage.IsMaterialWhitelisted((args.Entity, args.Entity), mat))
                continue;

            var existing = args.Materials.GetOrNew(mat);
            args.Materials[mat] = existing + amount;
        }
    }

    private void OnConsumeStoredMaterials(Entity<OreSiloClientComponent> ent, ref ConsumeStoredMaterialsEvent args)
    {
        if (args.LocalOnly)
            return;

        if (ent.Comp.Silo is not { } silo || !TryComp<MaterialStorageComponent>(silo, out var materialStorage))
            return;

        if (!CanTransmitMaterials(silo, ent))
            return;

        foreach (var (mat, amount) in args.Materials)
        {
            if (!_materialStorage.TryChangeMaterialAmount(silo, mat, amount, materialStorage))
                continue;
            args.Materials[mat] = 0;
        }
    }

    private void OnClientShutdown(Entity<OreSiloClientComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<OreSiloComponent>(ent.Comp.Silo, out var silo))
            return;

        silo.Clients.Remove(ent);
        Dirty(ent.Comp.Silo.Value, silo);
        UpdateOreSiloUi((ent.Comp.Silo.Value, silo));
    }

    /// <summary>
    /// Checks if a given client fulfills the criteria to link/receive materials from an ore silo.
    /// </summary>
    [PublicAPI]
    public bool CanTransmitMaterials(Entity<OreSiloComponent?, TransformComponent?> silo, EntityUid client)
    {
        if (!Resolve(silo, ref silo.Comp1, ref silo.Comp2))
            return false;

        if (!_powerReceiver.IsPowered(silo.Owner))
            return false;

        if (_transform.GetGrid(client) != _transform.GetGrid(silo.Owner))
            return false;

        if (!_transform.InRange((silo.Owner, silo.Comp2), client, silo.Comp1.Range))
            return false;

        return true;
    }
}
