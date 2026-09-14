# "Echoes of the Departed" copy pack (v14, EOD-spec §8) — the smallest copy pack in the codebase.
# No owner/stranger branch (a first death is already a station-wide public disclosure by the
# eulogy/crypt-plaque/Discord-obituary precedent, so every examiner reads the identical
# composition). $name is the only variable in the whole pack.
#
# The epitaph plate text and the cause label are deliberately NOT here — they render live via
# FirstDeathEpitaphPicker.Pick and FirstDeathCopy.CauseLabelFor (already-tested, closed-vocabulary
# C# strings), so an Echo's examine text can never drift from the crypt plaque it mirrors.

solreign-echo-header = A quiet marker. Someone's file closed here.
solreign-echo-in-memory-of = IN MEMORY OF {$name}
