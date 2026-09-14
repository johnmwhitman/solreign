# Solreign Perihelion — identity-pass paper props (placard + lore logs).
# See Resources/Prototypes/_Solreign/Entities/perihelion_identity.yml for where these are used.
#
# The actual solar-flare weather-event announcement text
# (station-event-solreign-solar-flare-start/end-announcement) belongs to the real
# `SolreignSolarFlare` GameRule entity (Resources/Prototypes/_Solreign/StationIdentity/
# game_rules_weather.yml, Content.Server/_Solreign/StationIdentity/SolreignSolarFlareRule.cs) — a
# concurrent StationIdentity wave already ships that text in station_identity.ftl. Perihelion
# opts into it as a PREFERRED weather event via its own `SolreignStationMoodComponent`
# (Resources/Prototypes/Maps/_Solreign/solreign_perihelion.yml, `WeatherEventPrototypes:
# [SolreignSolarFlare]`) rather than redefining the event here.

solreign-perihelion-solarflare-placard =
    SOLREIGN CORP — CORONA CONTINGENCY QUICK-REFERENCE
    Protocol ref: SolreignSolarFlareRule (station-preferred event: SolreignSolarFlare)

    1. On alert, don radiation gear before any EVA transit toward the Cupola.
    2. Comms/APCs may hiccup station-wide for a few seconds. This is expected, not a malfunction.
    3. This card is laminated for a reason. Do not remove it. Do not lose it either.

    — Filed by a Research tech who has clearly done this before.

solreign-perihelion-log-1 =
    LOGBOOK — SHIFT 14

    The Cupola's glass held again today. I keep expecting it not to. Standing out there with
    nothing under your boots but a strap and a few million kilometers of vacuum between you and
    the surface of a star does something to a person. Command wants a status update every shift.
    I keep writing "nominal." That word is doing a lot of work.

solreign-perihelion-log-2 =
    LOGBOOK — SHIFT 41

    Someone left a crown in the science wing. Not a costume prop — an actual fused lump of metal,
    heavy, warm like it just came out of a furnace. No manifest entry. No requisition slip. I asked
    around. Nobody claims it, nobody explains it, and frankly nobody wants to be the one who moves
    it. So it stays where it is. Some things you just let sit.

## Ambient mood popups (SolreignStationMoodComponent.MoodPopupLines) — short, single-line only.

solreign-perihelion-mood-popup-1 = The deck plating is warm underfoot again. Perihelion's close this orbit.
solreign-perihelion-mood-popup-2 = Somewhere aft, a radiation alarm chirps once and goes quiet.
solreign-perihelion-mood-popup-3 = The light through the Cupola's glass has gone the color of an ember.
