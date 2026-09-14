# Solreign Leviathan (megacorp HEADQUARTERS, largest chassis) — the two lore-paper secrets'
# content, plus this station's ambient mood popups (SolreignStationMoodComponent.MoodPopupLines).
# Pattern matches solreign-solarflare.ftl / nocturne.ftl (Paper component `content:` locale key).
# Map placement: Resources/Maps/_Solreign/solreign_leviathan.yml. Entity defs: Resources/Prototypes/
# _Solreign/Entities/leviathan_identity.yml.

solreign-leviathan-log-1 =
    TERMINATION PROCESSING — INTERNAL USE ONLY

    Reminder to all Personnel-wing staff: "Termination Processing" refers to employment status
    only. The recycler down the hall is for scrap metal, same as every other recycler on this
    station. It is not, and has never been, anything else. This memo exists because someone
    asked. That is the only reason this memo exists.

    — H.R., Solreign Leviathan

solreign-leviathan-log-2 =
    BOARD MINUTES (EXCERPT, UNDATED)

    Item 4: motion to replace the long table passed unanimously, no discussion. Item 5: motion
    to replace the chairs flanking it passed unanimously, no discussion. Item 6: motion regarding
    the cake was tabled, again, for the eleventh consecutive quarter. The Board thanks itself for
    its continued patience on Item 6.

## Ambient mood popups — short, single-line only. "Audit-heavy" flavor per this station's brief:
## SolreignStationMoodComponent.WeatherEventPrototypes prefers both existing weather events
## (SolreignSolarFlare, SolreignSporeDrift); these popups carry the audit atmosphere the mood
## component itself has no dedicated weather event for yet (game_rules_weather.yml ships only
## solar-flare/spore-drift — see that file's own header for why a bespoke audit event is out of
## this map-only lane's scope).

solreign-leviathan-mood-popup-1 = Somewhere above, a compliance officer clears their throat into an intercom that isn't live yet.
solreign-leviathan-mood-popup-2 = The building's lights dim exactly one lumen, right on schedule, for no stated reason.
solreign-leviathan-mood-popup-3 = A memo drifts past on the concourse walkway, unclaimed, face-down.
solreign-leviathan-mood-popup-4 = Far below, an elevator chimes for a floor that isn't in the directory.
