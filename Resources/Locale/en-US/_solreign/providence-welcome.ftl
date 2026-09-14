# Providence first-shift welcome beat (player-delight lane: "this station remembers you"). Private
# popup shown once per player per round, on their first eligible spawn (see ProvidenceWelcomeGate).
# Never gameplay-affecting, never blocks/replaces ahelp, admin bwoink, or the vanilla station greeting.

## Returning asset — the ledger has a career record ($tours completed shifts, $title).

solreign-providence-welcome-back-1 = Welcome back, asset. Your file shows { $tours } completed shifts and the title "{ $title }." Solreign has been expecting you.
solreign-providence-welcome-back-2 = Record retrieved. { $tours } shifts on file, current designation "{ $title }." Solreign never misplaces an asset, however long the interval.
solreign-providence-welcome-back-3 = Asset recognized. { $tours } shifts logged, standing title "{ $title }." Your permanent file has been quietly pleased to see you return.
solreign-providence-welcome-back-4 = File reopened: { $tours } shifts, "{ $title }." Solreign remembers every one of them, and always will.

## Brand-new asset — no prior record (career.Tours == 0), first shift being logged now.

solreign-providence-welcome-first-1 = First shift logged. Solreign is pleased to begin your permanent file.
solreign-providence-welcome-first-2 = New asset detected. A file has been opened in your name; today is entry number one.
solreign-providence-welcome-first-3 = Welcome aboard. Solreign did not know you yesterday. Solreign will remember you tomorrow.
solreign-providence-welcome-first-4 = Onboarding complete. Your record begins now, and — per policy — it never quite ends.

## Delayed personal-address follow-up (wow-wiring wave) — fires a few seconds after the lines above,
## privately and targeted, addressing the new asset by character name ($name). PG-13 corporate
## surveillance tone, never a threat: this is a follow-up acknowledgment, not a second monologue.

solreign-providence-first-personal-1 = Employee { $name }. Your onboarding is logged.
solreign-providence-first-personal-2 = { $name }. File opened. Attendance is noted.
solreign-providence-first-personal-3 = Welcome, { $name }. Solreign has recorded your arrival.
solreign-providence-first-personal-4 = Employee { $name }. Your permanent record begins with this shift.

## Nameless fallback (entity has no usable EntityName at fire time) — no parameters, never format-fails.

solreign-providence-first-personal-nameless = Employee. Your onboarding is logged.
