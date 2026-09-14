# Solreign Recreation pack (mini golf + go-kart spike) — acid-green corporate voice, PG.
# See docs/specs/2026-07-11-recreation-spec.md and Entities/recreation.yml.

solreign-golf-hole-sunk = It's in the hole! Solreign Recreation Division thanks you for your participation.

# Popped to the golfer after every successful swing (SolreignGolfSystem.OnBallWhacked).
# { $count } is the running stroke total.
solreign-golf-stroke-count = Stroke { $count }.

# Popped alongside the generic solreign-golf-hole-sunk popup, with the final tally
# (SolreignGolfSystem.OnHoleTrigger). { $count } is strokes taken.
solreign-golf-hole-sunk-strokes = { $count ->
    [one] Sunk it in { $count } stroke. Solreign Recreation Division salutes your efficiency.
   *[other] Sunk it in { $count } strokes. Solreign Recreation Division salutes your efficiency.
}

# Popped to the kart on completing a lap (SolreignLapTrackerSystem.OnCheckpointTrigger).
# { $laps } of { $total } laps completed.
solreign-kart-lap-complete = Lap { $laps } of { $total } complete.

# Station-wide DispatchGlobalAnnouncement when a kart finishes its race
# (SolreignLapTrackerSystem.OnCheckpointTrigger). { $driver } is the buckled driver's (or the
# kart's own) identity name; { $laps } is the total laps just completed.
solreign-kart-race-finished = { $driver } has completed the Solreign Recreation Division go-kart circuit — { $laps } { $laps ->
    [one] lap
   *[other] laps
}, checkered flag, zero (reported) collisions.
solreign-kart-race-finished-timed = { $driver } has completed the Solreign Recreation Division go-kart circuit — { $laps } { $laps ->
    [one] lap
   *[other] laps
} in { $seconds } seconds, checkered flag, zero (reported) collisions.

# Announcement sender name for kart race-finish announcements.
solreign-recreation-division-sender = Solreign Recreation Division
