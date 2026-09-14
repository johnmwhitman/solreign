# In-game "bugreport" console command

cmd-bugreport-desc = Files an in-game defect report with Solreign QA.
cmd-bugreport-help = Usage: bugreport <text> — describe the defect you found. One report accepted every 30 seconds per account.

bugreport-empty = Usage: bugreport <text>
bugreport-confirmation = Defect filed. Solreign QA thanks you for your compliance.
bugreport-cooldown = Solreign QA is still processing your last filing. Try again in {$seconds} { $seconds ->
        [one] second
       *[other] seconds
    }.

# Discord relay embed (solreign.bugreport_webhook)
bugreport-discord-title = New defect report
bugreport-discord-field-player = Reported by
bugreport-discord-field-guid = Account GUID
bugreport-discord-field-round = Round
bugreport-discord-footer = {$server} · Solreign QA defect intake
