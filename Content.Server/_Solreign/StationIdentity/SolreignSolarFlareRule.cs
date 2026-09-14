using Content.Server._Solreign.Providence;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared._Solreign.FX;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Random;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     One of the two "weather-style" events the StationIdentity wave ships (the other is
///     <see cref="SolreignSporeDriftRule"/>): a few seconds of brand sting on a random station — the
///     acid-green screen-border flash (<see cref="SolreignScreenFxEvent"/>, the same general-purpose
///     "mark this moment" hook the Corporate rule's quarterly audit uses), a brief flicker of some of
///     that station's powered APCs (existing <see cref="ApcSystem.ApcToggleBreaker"/>, same primitive
///     upstream's own <c>BreakerFlipRule</c>/<c>PowerGridCheckRule</c> use — toggled off in
///     <see cref="Started"/>, guaranteed back on in <see cref="Ended"/>, same restore idiom as
///     <c>PowerGridCheckRule.Ended</c>), and Providence's event_audit voice line (same category
///     <c>SolreignAuditorPrimeRule</c> already uses for its own arrival).
///
///     Total duration is intentionally short (see the <c>SolreignSolarFlare</c> entity prototype's
///     <c>StationEvent</c> component: a few seconds), unlike upstream's own <c>SolarFlareRule</c> (which
///     runs for minutes and jams radio) — this is Solreign's own, brief moment-marker version, not a
///     reskin of the upstream mechanic.
///
///     Not yet wired into the round's random event tables (that lives in
///     Resources/Prototypes/GameRules/events.yml, outside this wave's assigned path, and no per-station
///     event-scheduling concept exists anywhere in the engine or _Solreign today for a per-map
///     preference to plug into — see <see cref="SolreignStationMoodSystem"/>'s doc comment). Admins can
///     start it directly (<c>event start SolreignSolarFlare</c>); a future event-director wave is the
///     documented, staged next step (<c>SolreignWeatherEventsTable</c>,
///     Resources/Prototypes/_Solreign/StationIdentity/game_rules_weather.yml).
/// </summary>
public sealed partial class SolreignSolarFlareRule : StationEventSystem<SolreignSolarFlareRuleComponent>
{
    /// <summary>Matches the <c>SolreignSolarFlare</c> entity prototype id — the string
    /// <see cref="SolreignStationMoodComponent.WeatherEventPrototypes"/> entries must equal for a
    /// station to be considered to prefer this event.</summary>
    public const string EventPrototypeId = "SolreignSolarFlare";

    [Dependency] private ApcSystem _apcSystem = default!;
    [Dependency] private SolreignStationMoodSystem _mood = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;

    protected override void Started(EntityUid uid, SolreignSolarFlareRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        RaiseNetworkEvent(new SolreignScreenFxEvent(component.ScreenFxDurationSeconds));
        _providence.PlayLine(ProvidenceLineCategory.EventAudit);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        var preferred = _mood.IsWeatherEventPreferred(chosenStation.Value, EventPrototypeId);
        var toFlicker = preferred ? component.PreferredApcsToFlicker : component.ApcsToFlicker;

        var candidates = new List<Entity<ApcComponent>>();
        var query = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (query.MoveNext(out var apcUid, out var apc, out var xform))
        {
            if (apc.MainBreakerEnabled && CompOrNull<StationMemberComponent>(xform.GridUid)?.Station == chosenStation)
                candidates.Add((apcUid, apc));
        }

        RobustRandom.Shuffle(candidates);

        var count = Math.Min(toFlicker, candidates.Count);
        for (var i = 0; i < count; i++)
        {
            _apcSystem.ApcToggleBreaker(candidates[i], candidates[i]);
            component.FlickeredApcs.Add(candidates[i]);
        }
    }

    protected override void Ended(EntityUid uid, SolreignSolarFlareRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        foreach (var apcUid in component.FlickeredApcs)
        {
            if (Deleted(apcUid))
                continue;

            if (TryComp<ApcComponent>(apcUid, out var apc) && !apc.MainBreakerEnabled)
                _apcSystem.ApcToggleBreaker(apcUid, apc);
        }

        component.FlickeredApcs.Clear();
    }
}
