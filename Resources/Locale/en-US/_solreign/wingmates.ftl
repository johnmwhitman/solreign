wingmates-title = First-Shift Wingmates
wingmates-heading-idle = Find a wingmate
wingmates-heading-seeking = Request open
wingmates-heading-offer-available = Someone could use a guide
wingmates-heading-offer-received = A guide has offered
wingmates-heading-paired = Wingmates connected
wingmates-heading-paused = Wingmates paused
wingmates-heading-disabled = Wingmates unavailable
wingmates-request-intro = Choose where you would like a friendly first-shift connection.
wingmates-department-label = Department
wingmates-teaching-label = How would you like to learn?
wingmates-request-button = Find a crewmate
wingmates-charter-heading = Before volunteering, read the complete Wingmate charter:
wingmates-charter-teach-without-taking-over = • Teach without taking over the newcomer's choices or tasks.
wingmates-charter-ask-before-spoilers = • Ask before sharing spoilers, secrets, or advanced solutions.
wingmates-charter-keep-in-game = • Keep the interaction in-game.
wingmates-charter-no-private-contact = • Do not solicit private or off-platform contact.
wingmates-charter-no-hazing-pressure = • No hazing, flirting, pressure, or testing the newcomer.
wingmates-charter-no-staff-authority = • Never claim or imply staff authority.
wingmates-charter-respect-boundaries = • Immediately respect silence, decline, blocking, or separation.
wingmates-charter-consent = I have read and accept this charter.
wingmates-volunteer-opt-in = Volunteer this round
wingmates-volunteer-opt-out = Stop volunteering this round
wingmates-guide-ineligible-manual = Guide eligibility pending HR approval. Ask a moderator to approve you, or check back once auto-eligibility is enabled.
wingmates-guide-ineligible-observer = You must be an active crew member — not an observer or ghost — to volunteer as a guide.
wingmates-guide-ineligible-not-alive = You must be alive and able-bodied to volunteer as a guide.
wingmates-guide-ineligible-loading = Checking your eligibility with the Ledger — try again in a moment.
wingmates-guide-ineligible-tenure = You need more time in service before you can guide a newcomer. Ask a moderator for an early approval if you believe you qualify.
wingmates-guide-ineligible-generic = You are not currently eligible to volunteer as a guide.
wingmates-waiting-consent = Your request is open. There is no countdown and you may cancel at any time.
wingmates-no-guide-fallback = No crew guide is free right now. Try the self-guided assignment below, or check again later.
wingmates-waiting-prompt = While you wait: locate your department and introduce yourself to one crewmember.
# ALIVENESS P0 #3 — shown INSTEAD of the three waiting lines above when no other eligible crew
# member is connected: an honest solo-shift state, never an open-ended promise of a match.
wingmates-waiting-no-peers = No other crew are aboard to pair with this shift. Your request stays open in case someone arrives, and you may cancel at any time.
wingmates-waiting-no-peers-suggestion = In the meantime: try the self-guided First Assignment tab, take a work order from the Contracts Board, or address the Directive Terminal.
wingmates-cancel-request = Cancel request
wingmates-request-context-placeholder = A crewmember is looking for a guide.
wingmates-offer-button = Offer to be their wingmate
wingmates-proposal-placeholder = A guide has offered to join you.
wingmates-proposal-consent = You can accept, decline, or simply leave this open. There is no countdown.
wingmates-accept = Accept
wingmates-decline = Decline
wingmates-decline-block-guide = Also prevent this guide from offering to me in future rounds
wingmates-partner-placeholder = You are paired.
wingmates-end-explanation = Either person can end the pairing at any time, without giving a reason.
wingmates-pause = Pause shared guidance
wingmates-resume = Resume shared guidance
wingmates-end-pairing = End pairing
wingmates-block-current-partner = End and do not pair us again — this shift and all future shifts
wingmates-block-persistence-failed-count = { $count ->
    [one] Your block could not be saved, so no block was applied. Please try again.
   *[other] { $count } block confirmations failed this shift — re-check your blocks.
}
wingmates-block-persistence-failed-previous-shift = { $count ->
    [one] A block confirmation from a previous shift failed — re-check your blocks.
   *[other] { $count } block confirmations from previous shifts failed — re-check your blocks.
}

# P1.4 SILENT-DROP BATCH FIX: per-action failure popups surfaced to the player when a BUI
# button's transition result is non-success. Keys are concatenated as
# "wingmates-action-{reason}" by WingmateBoundUserInterface.ApplyPrivateSnapshot. The "{reason}"
# token below describes what the rejection was, not what the player should do — the surface
# copy is intentionally neutral and short.
wingmates-action-disabled = Wingmates is unavailable right now.
wingmates-action-rate-limited = Too many actions in a short window — wait a moment and try again.
wingmates-action-invalid-department = That department is not a valid target.
wingmates-action-invalid-teaching-mode = That learning style is not supported.
wingmates-action-requested = Your request is already open with those settings.
wingmates-action-role-conflict = You already have an open request or offer — clear it before starting another.
wingmates-action-already-paired = You are already paired with someone.
wingmates-action-blocks-loading = The block list is still loading — try again in a moment.
wingmates-action-not-approved = You are not approved as a guide.
wingmates-action-not-volunteering = You are not currently volunteering as a guide.
wingmates-action-invalid-requester-token = That request is no longer reachable from this menu.
wingmates-action-blocked = You cannot pair with that guide.
wingmates-action-self-offer = You cannot offer to be your own wingmate.
wingmates-action-decline-cooldown = That guide declined your last offer — wait a few minutes before re-offering.
wingmates-action-not-seeking = That crew member is no longer looking for a guide.
wingmates-action-stale-offer = That offer has expired or been replaced.
wingmates-action-not-paired = You are not currently paired.
wingmates-action-paused = Shared guidance is already paused.
wingmates-action-not-paused = Shared guidance is not currently paused.
wingmates-action-self-block = You cannot block yourself.
wingmates-action-block-pending = Your block is being saved — wait a moment.
wingmates-action-charter-not-accepted = Read and accept the charter before volunteering.
wingmates-action-no-offer = You have no open offer to withdraw.
wingmates-action-failed = That action could not be completed.
wingmates-moderator-help = Contact a moderator
wingmates-disabled-explanation = Wingmates is unavailable while the service is under maintenance.
wingmates-close = Close
wingmates-request-context = { $name } is looking for help in { $department }. Preferred style: { $style }.
wingmates-proposal = { $name } has offered to be your wingmate.
wingmates-partner = Your wingmate is { $name }.
wingmates-fallback-crewmember = A crewmember
wingmates-fallback-guide = A guide
wingmates-fallback-crewmate = your crewmate
wingmates-department-engineering = Engineering
wingmates-department-medical = Medical
wingmates-department-service = Service
wingmates-department-science = Science
wingmates-department-security = Security
wingmates-department-cargo = Cargo
wingmates-department-command = Command
wingmates-department-unknown = an unknown department
wingmates-teaching-tour = show me around
wingmates-teaching-learn-by-doing = learn by doing
wingmates-teaching-shadow = let me shadow you
wingmates-status-idle = Ready
wingmates-status-seeking = Looking for a wingmate
wingmates-status-offerpending = Offer pending
wingmates-status-paired = Paired
wingmates-status-paused = Paused
wingmates-status-dissolved = Pairing ended
wingmates-status-expired = Offer expired
wingmates-status-disabled = Unavailable

cmd-wingmatestatus-desc = Show a player's coarse Wingmates state.
cmd-wingmatestatus-help = Usage: wingmatestatus <player>
cmd-wingmateapprove-desc = Approve a player to volunteer as a Wingmate guide this round.
cmd-wingmateapprove-help = Usage: wingmateapprove <player>
cmd-wingmaterevoke-desc = Revoke a player's Wingmate guide approval this round.
cmd-wingmaterevoke-help = Usage: wingmaterevoke <player>
cmd-wingmatedissolve-desc = End a player's current Wingmate pairing.
cmd-wingmatedissolve-help = Usage: wingmatedissolve <player>
cmd-wingmate-player-not-found = No connected player named { $player } was found.
cmd-wingmate-status = Wingmates state for { $player }: status { $status }; approved { $approved }; volunteering { $volunteering }.
cmd-wingmate-approve-result = Wingmate approval processed for { $player }. State changed: { $changed }.
cmd-wingmate-revoke-result = Wingmate revocation processed for { $player }. State changed: { $changed }.
cmd-wingmate-dissolve-result = Wingmate dissolve processed for { $player }. State changed: { $changed }.
cmd-wingmate-yes = yes
cmd-wingmate-no = no

# Private mentor-pair radio channel (Resources/Prototypes/radio_channels.yml: Wingmate).
# Lives here rather than headset-component.ftl so the vanilla file stays upstream-clean.
chat-radio-wingmate = Wingmate

# Job-select tooltips for the Wingmate pair roles (wingmate.yml). Written in-fiction:
# both roles read as Passenger to the crew, so the tooltips stay quiet about mechanics.
job-description-wingmate = A new hire still learning which doors open for them. Someone on this station has been assigned to make sure they last the shift.
job-description-wingmate-mentor = A long-tenured employee with an off-the-books assignment: keep one particular new hire alive, oriented, and off the incident reports.
