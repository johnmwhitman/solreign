# Shift Archive board — closed-vocabulary copy pack (ShiftArchiveCopy.cs / ShiftArchiveSystem.cs).
# Every board line is one of these templates over a real station_audit_log row; the only name that
# can appear is $name from commendation_name, which the round-end commendation already announced.

# Window chrome (client-rendered)
solreign-shift-archive-window-title = SOLREIGN // SHIFT ARCHIVE
solreign-shift-archive-empty = NO SHIFTS ON RECORD. THE LEDGER AWAITS.
solreign-shift-archive-offline = ARCHIVE SYNCHRONIZATION OFFLINE.

# Entry copy (server-rendered)
solreign-shift-archive-entry-header = SHIFT { $round } // { $date }
solreign-shift-archive-line-crew = { $crew ->
    [one] One asset logged { $minutes } minutes on shift.
   *[other] { $crew } assets logged { $minutes } minutes on shift.
}
solreign-shift-archive-line-no-deaths = Zero fatalities. Compliance is its own reward.
solreign-shift-archive-line-deaths = { $count ->
    [one] One fatality processed.
   *[other] { $count } fatalities processed.
}
solreign-shift-archive-line-deaths-commemorated = { $count ->
    [one] One fatality processed. A first loss was commemorated in the Garden.
   *[other] { $count } fatalities processed. A first loss was commemorated in the Garden.
}
solreign-shift-archive-line-directive-fulfilled = Directive "{ $title }" — FULFILLED.
solreign-shift-archive-line-directive-failed = Directive "{ $title }" — UNMET. Noted in your permanent file.
solreign-shift-archive-line-directive-unreported = Directive "{ $title }" — outcome unreported.
solreign-shift-archive-line-contracts = { $count ->
    [one] One contract completed and remitted.
   *[other] { $count } contracts completed and remitted.
}
solreign-shift-archive-line-commendation = Commendation: { $name } ({ $score } merit).
