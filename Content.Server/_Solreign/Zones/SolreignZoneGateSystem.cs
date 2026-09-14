using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.Zones;

/// <summary>
/// Loads the standalone Sublevel H ("Doom Hell Zone") grid on first use and shuttles travelers to
/// and from it. Spec: docs/specs/2026-07-11-hell-heaven-zones.md §3 (Option A, the only all-YAML,
/// paceable entry mechanic) — reframed here onto a standalone grid per this wave's brief rather
/// than the spec's original on-station-grid sketch.
///
/// Loading pattern studied from and mirrors Content.Server/Administration/Systems/AdminTestArenaSystem.cs
/// (the upstream "lazily load a grid onto a fresh map, cache it, teleport a player there" idiom) —
/// the cheapest reliable path found in the codebase for an auxiliary/pocket-dimension grid. Unlike
/// the admin arena (one map per admin), Sublevel H is a single shared instance for the whole server,
/// loaded once per round.
///
/// Entry gate: <see cref="SolreignHellZoneEntryComponent"/> + a vanilla AccessReader (access:
/// SolreignSublevelH) on a prop placed in deep maintenance — NOT placed by this wave, see that
/// component's doc comment. Return gate: <see cref="SolreignZoneReturnGateComponent"/>, placed
/// inside zone_hell.yml itself near the Boardroom exit.
///
/// Ladder gate: <see cref="SolreignHeavenZoneLadderComponent"/>, placed inside zone_hell.yml's
/// Boardroom, continues the trip Hell -> Heaven (spec §2 "THE LADDER UP") by lazily loading
/// zone_heaven.yml the same way zone_hell.yml is loaded. The Heaven side's own
/// <see cref="SolreignZoneReturnGateComponent"/> instance (placed inside zone_heaven.yml) sends
/// the traveler all the way back to their original real-world entry point.
///
/// Analyzer note: three components, three events, three subscriptions below — no (component,
/// event) pair collides with anything else in the repo.
///
/// Phase2 B1: every branch below that used to be an inline <c>if</c> condition is now a call into
/// <see cref="SolreignZoneGateRules"/>, the pure/testable half of this system (see
/// Content.Tests/_Solreign/ZoneGateRulesTests.cs). Additive refactor only — behavior is unchanged.
///
/// Phase2 A4 game-feel sweep: every transit below used to move the traveler in total silence
/// (a popup, no audio) — existing upstream teleport/holy/buzz cues, no new assets.
/// </summary>
public sealed partial class SolreignZoneGateSystem : EntitySystem
{
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public const string ZoneHellMapPath = "/Maps/_Solreign/zone_hell.yml";
    public const string ZoneHeavenMapPath = "/Maps/_Solreign/zone_heaven.yml";

    // Phase2 A4 game-feel sweep: every gate below used to move the traveler in total silence
    // (popup only, no audio) — existing upstream cues, no new assets.
    private static readonly SoundSpecifier EntrySound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");
    private static readonly SoundSpecifier ReturnSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");
    private static readonly SoundSpecifier LadderSound = new SoundPathSpecifier("/Audio/Effects/holy.ogg");
    private static readonly SoundSpecifier DeniedSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");

    private MapId? _hellZoneMap;
    private EntityUid? _hellZoneGrid;

    private MapId? _heavenZoneMap;
    private EntityUid? _heavenZoneGrid;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignHellZoneEntryComponent, ActivateInWorldEvent>(OnEntryActivate);
        SubscribeLocalEvent<SolreignZoneReturnGateComponent, ActivateInWorldEvent>(OnReturnActivate);
        SubscribeLocalEvent<SolreignHeavenZoneLadderComponent, ActivateInWorldEvent>(OnLadderActivate);
    }

    private void OnEntryActivate(EntityUid uid, SolreignHellZoneEntryComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        var hasAccessReader = HasComp<AccessReaderComponent>(uid);
        // Only run the (potentially expensive) access check when there's actually a reader to
        // check against — identical short-circuit to the pre-extraction inline condition.
        var isAllowed = hasAccessReader && _accessReader.IsAllowed(args.User, uid);

        if (!SolreignZoneGateRules.IsEntryAllowed(hasAccessReader, isAllowed))
        {
            _popup.PopupEntity(Loc.GetString("solreign-hellzone-gate-denied"), args.User, args.User);
            _audio.PlayPvs(DeniedSound, uid);
            args.Handled = true;
            return;
        }

        var (_, gridUid) = EnsureHellZoneLoaded();

        var returnComp = EnsureComp<SolreignZoneReturnComponent>(args.User);
        returnComp.ReturnCoordinates = Transform(args.User).Coordinates;

        _transform.SetCoordinates(args.User, new EntityCoordinates(gridUid, comp.SpawnOffset));
        _popup.PopupEntity(Loc.GetString("solreign-hellzone-gate-enter"), args.User, args.User);
        _audio.PlayPvs(EntrySound, args.User);

        args.Handled = true;
    }

    private void OnReturnActivate(EntityUid uid, SolreignZoneReturnGateComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        var hasReturnRecord = TryComp<SolreignZoneReturnComponent>(args.User, out var ret);

        if (!SolreignZoneGateRules.CanUseReturnGate(hasReturnRecord))
        {
            _popup.PopupEntity(Loc.GetString("solreign-hellzone-gate-no-return"), args.User, args.User);
            _audio.PlayPvs(DeniedSound, uid);
            args.Handled = true;
            return;
        }

        // hasReturnRecord (checked above) guarantees TryComp populated ret; ! suppresses the
        // nullable warning the indirection through the rule predicate otherwise loses.
        _transform.SetCoordinates(args.User, ret!.ReturnCoordinates);
        RemComp<SolreignZoneReturnComponent>(args.User);
        _popup.PopupEntity(Loc.GetString("solreign-hellzone-gate-exit"), args.User, args.User);
        _audio.PlayPvs(ReturnSound, args.User);

        args.Handled = true;
    }

    private void OnLadderActivate(EntityUid uid, SolreignHeavenZoneLadderComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        // Deliberately does NOT touch SolreignZoneReturnComponent — the traveler is mid-trip
        // (Hell -> Heaven), not entering fresh, so their original real-world return point must
        // survive the hop. See SolreignHeavenZoneLadderComponent's doc comment.
        var (_, gridUid) = EnsureHeavenZoneLoaded();

        _transform.SetCoordinates(args.User, new EntityCoordinates(gridUid, comp.SpawnOffset));
        _popup.PopupEntity(Loc.GetString("solreign-heavenzone-ladder-up"), args.User, args.User);
        _audio.PlayPvs(LadderSound, args.User);

        args.Handled = true;
    }

    /// <summary>
    /// Loads Sublevel H onto its own fresh map the first time anyone enters, then reuses the same
    /// instance for the rest of the round. Mirrors AdminTestArenaSystem.AssertArenaLoaded.
    /// </summary>
    private (MapId MapId, EntityUid GridUid) EnsureHellZoneLoaded()
    {
        var cachedMapId = _hellZoneMap;
        var cachedGridUid = _hellZoneGrid;
        var hasCachedMap = cachedMapId is not null;
        var hasCachedGrid = cachedGridUid is not null;

        // Only probe Deleted/Terminating once both halves of the cache are actually populated —
        // identical short-circuit to the pre-extraction inline condition.
        var canProbeGrid = hasCachedMap && hasCachedGrid;
        var gridDeleted = canProbeGrid && Deleted(cachedGridUid!.Value);
        var gridTerminating = canProbeGrid && Terminating(cachedGridUid!.Value);

        if (SolreignZoneGateRules.IsZoneCacheValid(hasCachedMap, hasCachedGrid, gridDeleted, gridTerminating))
            return (cachedMapId!.Value, cachedGridUid!.Value);

        var mapUid = _maps.CreateMap(out var newMapId);

        if (!_loader.TryLoadGrid(newMapId, new ResPath(ZoneHellMapPath), out var grid))
        {
            QueueDel(mapUid);
            throw new InvalidOperationException("Failed to load Sublevel H (zone_hell.yml)");
        }

        _hellZoneMap = newMapId;
        _hellZoneGrid = grid.Value.Owner;

        return (newMapId, grid.Value.Owner);
    }

    /// <summary>
    /// Loads the Serenity &amp; Compliance Atrium onto its own fresh map the first time anyone
    /// reaches the ladder, then reuses the same instance for the rest of the round. Mirrors
    /// <see cref="EnsureHellZoneLoaded"/>.
    /// </summary>
    private (MapId MapId, EntityUid GridUid) EnsureHeavenZoneLoaded()
    {
        var cachedMapId = _heavenZoneMap;
        var cachedGridUid = _heavenZoneGrid;
        var hasCachedMap = cachedMapId is not null;
        var hasCachedGrid = cachedGridUid is not null;

        // Only probe Deleted/Terminating once both halves of the cache are actually populated —
        // identical short-circuit to the pre-extraction inline condition.
        var canProbeGrid = hasCachedMap && hasCachedGrid;
        var gridDeleted = canProbeGrid && Deleted(cachedGridUid!.Value);
        var gridTerminating = canProbeGrid && Terminating(cachedGridUid!.Value);

        if (SolreignZoneGateRules.IsZoneCacheValid(hasCachedMap, hasCachedGrid, gridDeleted, gridTerminating))
            return (cachedMapId!.Value, cachedGridUid!.Value);

        var mapUid = _maps.CreateMap(out var newMapId);

        if (!_loader.TryLoadGrid(newMapId, new ResPath(ZoneHeavenMapPath), out var grid))
        {
            QueueDel(mapUid);
            throw new InvalidOperationException("Failed to load the Atrium (zone_heaven.yml)");
        }

        _heavenZoneMap = newMapId;
        _heavenZoneGrid = grid.Value.Owner;

        return (newMapId, grid.Value.Owner);
    }
}
