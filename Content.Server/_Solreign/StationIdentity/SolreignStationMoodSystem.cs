using System.Linq;
using Content.Server._Solreign.Effects;
using Content.Server.Popups;
using Content.Server.Station.Events;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Solreign's shared ambient-identity engine — the first-priority StationIdentity wave. Every other
///     Solreign lane that wants a "this station feels alive" moment references this system's data by
///     prototype only (see <see cref="SolreignStationMoodComponent"/>'s doc comment), rather than taking
///     a hard C# dependency on it.
///
///     Three mechanics, one system:
///
///     <list type="bullet">
///     <item>
///         <b>Day/night ambient lerp.</b> Studied upstream's own <c>LightCycleComponent</c>/
///         <c>SharedLightCycleSystem</c> first (Content.Shared/Light) — it already proves a per-map
///         ambient color exists to lerp (<c>Robust.Shared.Map.Components.MapLightComponent.AmbientLightColor</c>,
///         confirmed live-used server-side by <c>Content.Server.Salvage.SpawnSalvageMissionJob</c> and
///         <c>Content.Server.Parallax.BiomeSystem</c>). So per the wave brief's own fallback clause
///         ("if per-map ambient color exists, lerp THAT; else ... RoofLight pulses") — it exists, we lerp
///         it, and no <c>RoofLight</c> component exists anywhere in this codebase to fall back to
///         anyway. Deliberately NOT reusing upstream's <c>LightCycleComponent</c>/client-side
///         <c>LightCycleSystem</c>: that pair is a purely client-predicted visual (no server system
///         reads it at all in this codebase — grep confirms zero <c>LightCycleComponent</c> usage in any
///         Resources/ map), which would mean re-deriving color from a formula on every client
///         independently. This wave's ask is a slow, server-authoritative, YAML-declared mood swing tied
///         to a station identity concept, not a physically-accurate predicted day/night cycle, so a
///         small server-side throttled tick (every <see cref="ColorUpdateIntervalSeconds"/>) writing
///         straight to <c>MapLightComponent</c> is the simpler, more directly-scoped fit — and it composes
///         for free with any other system (ours or a future one) that also touches
///         <c>AmbientLightColor</c>, since we always read-modify the live value rather than caching a
///         separate authority.
///     </item>
///     <item>
///         <b>Weather-event preference.</b> <see cref="SolreignStationMoodComponent.WeatherEventPrototypes"/>
///         is map-scoped data a future event-director/scheduler wave reads wholesale (see
///         <see cref="StationMoodMath.PickWeatherEvent"/>). This wave wires the simple half:
///         <see cref="IsWeatherEventPreferred"/>, called by <see cref="SolreignSolarFlareRule"/> and
///         <see cref="SolreignSporeDriftRule"/> to lean harder into their own effect (more APCs flicker,
///         more spores drift) when a station specifically calls them out — real, observable, wired
///         behavior this wave, without inventing a full director (out of this wave's scope; no per-map
///         event-scheduling concept exists anywhere in the engine or _Solreign today — confirmed by
///         reading <c>BasicStationEventSchedulerSystem</c>, which is round-global, not per-station).
///     </item>
///     <item>
///         <b>Mood popups.</b> Rare, localized ambient flavor: a random line from
///         <see cref="SolreignStationMoodComponent.MoodPopupLines"/> popped up (PVS-scoped, so genuinely
///         "localized") at a random tile on the station, on the same random-window scheduling idiom as
///         <c>SolreignPeriodicEffectSystem</c> (<see cref="PeriodicEffectTiming.NextFireTime"/>, reused
///         rather than re-derived).
///     </item>
///     </list>
///
///     Scope limit, documented rather than silently assumed: a station spanning multiple maps/grids
///     resolves its mood to the FIRST grid's map only (<see cref="TryResolveStationMap"/>) — Solreign's
///     one live station (SolreignOasis) is single-grid, so this doesn't bite today, but a future
///     multi-grid station would only get ambient-lerp on its first grid's map.
/// </summary>
public sealed partial class SolreignStationMoodSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>How often the day/night lerp is allowed to actually write+Dirty
    /// <c>MapLightComponent</c> — well below per-tick, since the lerp is meant to read as "slowly
    /// shifting", not because per-tick math would be expensive.</summary>
    private const float ColorUpdateIntervalSeconds = 2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignStationMoodComponent, StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<SolreignStationMoodComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStationPostInit(Entity<SolreignStationMoodComponent> ent, ref StationPostInitEvent args)
    {
        var comp = ent.Comp;

        comp.CycleStartTime = _timing.CurTime;
        comp.NextColorUpdateTime = _timing.CurTime;
        comp.NextMoodPopupTime = PeriodicEffectTiming.NextFireTime(
            _timing.CurTime,
            comp.MoodPopupMinIntervalSeconds,
            comp.MoodPopupMaxIntervalSeconds,
            _random.NextDouble());

        if (!TryResolveStationMap(args.Station, out var mapUid))
            return;

        comp.MapUid = mapUid;

        var mapLight = EnsureComp<MapLightComponent>(mapUid);
        comp.OriginalAmbientColor = mapLight.AmbientLightColor;
    }

    private void OnShutdown(Entity<SolreignStationMoodComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.MapUid is not { } mapUid || TerminatingOrDeleted(mapUid))
            return;

        if (!TryComp<MapLightComponent>(mapUid, out var mapLight))
            return;

        mapLight.AmbientLightColor = ent.Comp.OriginalAmbientColor;
        Dirty(mapUid, mapLight);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SolreignStationMoodComponent>();
        while (query.MoveNext(out var stationUid, out var comp))
        {
            TickDayNight(comp);
            TickMoodPopup(stationUid, comp);
        }
    }

    private void TickDayNight(SolreignStationMoodComponent comp)
    {
        if (!comp.DayNightCycle)
            return;

        if (comp.MapUid is not { } mapUid || TerminatingOrDeleted(mapUid))
            return;

        if (_timing.CurTime < comp.NextColorUpdateTime)
            return;

        comp.NextColorUpdateTime = _timing.CurTime + TimeSpan.FromSeconds(ColorUpdateIntervalSeconds);

        if (!TryComp<MapLightComponent>(mapUid, out var mapLight))
            return;

        var elapsed = _timing.CurTime - comp.CycleStartTime;
        var srgb = StationMoodMath.LerpDayNightColor(elapsed, comp.CyclePeriodSeconds, comp.ColorA, comp.ColorB);
        var linear = Color.FromSrgb(srgb);

        if (mapLight.AmbientLightColor == linear)
            return;

        mapLight.AmbientLightColor = linear;
        Dirty(mapUid, mapLight);
    }

    private void TickMoodPopup(EntityUid stationUid, SolreignStationMoodComponent comp)
    {
        if (comp.MoodPopupLines.Count == 0)
            return;

        if (_timing.CurTime < comp.NextMoodPopupTime)
            return;

        comp.NextMoodPopupTime = PeriodicEffectTiming.NextFireTime(
            _timing.CurTime,
            comp.MoodPopupMinIntervalSeconds,
            comp.MoodPopupMaxIntervalSeconds,
            _random.NextDouble());

        if (!TryComp<StationDataComponent>(stationUid, out var data) || data.Grids.Count == 0)
            return;

        var grids = data.Grids.ToList();
        var gridUid = grids[_random.Next(grids.Count)];

        if (!TryComp<MapGridComponent>(gridUid, out var gridComp))
            return;

        var tiles = _map.GetAllTiles(gridUid, gridComp).ToList();
        if (tiles.Count == 0)
            return;

        var tile = tiles[_random.Next(tiles.Count)];
        var coords = _map.GridTileToLocal(gridUid, gridComp, tile.GridIndices);

        var line = comp.MoodPopupLines[_random.Next(comp.MoodPopupLines.Count)];
        _popup.PopupCoordinates(Loc.GetString(line), coords, PopupType.Medium);
    }

    /// <summary>
    ///     Whether <paramref name="eventPrototypeId"/> is one of <paramref name="station"/>'s declared
    ///     <see cref="SolreignStationMoodComponent.WeatherEventPrototypes"/>. Stations without the mood
    ///     component (or without this id listed) simply aren't preferred — never throws, never
    ///     required. Called by <see cref="SolreignSolarFlareRule"/>/<see cref="SolreignSporeDriftRule"/>.
    /// </summary>
    public bool IsWeatherEventPreferred(EntityUid station, string eventPrototypeId)
    {
        if (!TryComp<SolreignStationMoodComponent>(station, out var mood))
            return false;

        foreach (var proto in mood.WeatherEventPrototypes)
        {
            if (proto.Id == eventPrototypeId)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Resolves the map entity backing a station's first grid. See this class's doc comment for the
    ///     documented single-grid scope limit.
    /// </summary>
    private bool TryResolveStationMap(Entity<StationDataComponent> station, out EntityUid mapUid)
    {
        foreach (var grid in station.Comp.Grids)
        {
            if (TryComp(grid, out TransformComponent? xform) && xform.MapUid is { } map)
            {
                mapUid = map;
                return true;
            }
        }

        mapUid = default;
        return false;
    }
}
