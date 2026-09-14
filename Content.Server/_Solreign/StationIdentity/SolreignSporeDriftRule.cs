using Content.Server.Popups;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Popups;
using Content.Shared.Station.Components;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     The other "weather-style" event the StationIdentity wave ships (see
///     <see cref="SolreignSolarFlareRule"/> for the first): scatters a harmless handful of drifting,
///     glowing spore particles (<see cref="SolreignSporeParticleComponent"/>, self-contained — each
///     spore rolls its own drift on spawn) across random tiles of a random station, with a small glow
///     popup at each one. Fully cosmetic: no damage, no gas, no gameplay state touched.
///
///     Same not-yet-in-the-random-tables scope note as <see cref="SolreignSolarFlareRule"/> — see that
///     class's doc comment.
/// </summary>
public sealed partial class SolreignSporeDriftRule : StationEventSystem<SolreignSporeDriftRuleComponent>
{
    /// <summary>Matches the <c>SolreignSporeDrift</c> entity prototype id — see
    /// <see cref="SolreignSolarFlareRule.EventPrototypeId"/> for why this constant exists.</summary>
    public const string EventPrototypeId = "SolreignSporeDrift";

    [Dependency] private SolreignStationMoodSystem _mood = default!;
    [Dependency] private PopupSystem _popup = default!;

    protected override void Started(EntityUid uid, SolreignSporeDriftRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        if (!TryComp<StationDataComponent>(chosenStation.Value, out var data))
            return;

        var preferred = _mood.IsWeatherEventPreferred(chosenStation.Value, EventPrototypeId);
        var count = preferred ? component.PreferredSporeCount : component.SporeCount;

        for (var i = 0; i < count; i++)
        {
            if (!TryFindRandomTileOnStation((chosenStation.Value, data), out _, out _, out var coords))
                continue;

            var spore = Spawn(component.SporeEffectPrototype, coords);

            if (component.GlowPopup is { } popup)
                _popup.PopupEntity(Loc.GetString(popup), spore, PopupType.Small);
        }
    }
}
