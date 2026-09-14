# "Unscheduled Fur Event" — the werewolf antag. Spec: docs/specs/2026-07-11-werewolf-vampire-spec.md §3.
# PG/kid-safe, acid-green corporate voice. Nobody dies to any of this.

# Broadcast when a Full Moon Window opens (SolreignFullMoonWindowRuleSystem.Started).
solreign-werewolf-window-open =
    Attention: tonight's moonlight exceeds quarterly exposure limits. Any Lunar-Reactive personnel
    should expect a productivity impact. The Company thanks you for your fur.

# Broadcast when the Full Moon Window closes (SolreignFullMoonWindowRuleSystem.Ended).
solreign-werewolf-window-close =
    Moonlight levels have returned to compliance. Unscheduled Fur Events should conclude shortly.
    Please file any incident reports with Wellness by end of shift.

# Private briefing sent to the drafted employee (SolreignFullMoonWindowRuleSystem.DraftOneWerewolf).
solreign-werewolf-briefing =
    A routine wellness scan has flagged you as Lunar-Reactive. Tonight's Full Moon Window may cause
    fuzziness, then furriness, then extreme furriness. This is a normal, HR-compliant biological
    process. Try not to bowl over too many colleagues — Wellness Response is standing by with the
    Follicle Stabilizer Draught, and the chapel keeps its doors open.

# Stirring-phase warning popup (SolreignWerewolfSystem.Transform.OnEnterStirring).
solreign-werewolf-stirring-popup = Your paperwork feels strangely distant. Something is stirring.

# Shown to the wolf on transforming (SolreignWerewolfSystem.Transform.OnEnterTransformed).
solreign-werewolf-transform-popup = The Unscheduled Fur Event has fully commenced.

# Diegetic backstop shown ONLY if the wolf-form polymorph prototype ever fails to resolve again
# (Phase-2 Track A1 minimum bar — SolreignWerewolfSystem.Transform.OnEnterTransformed's TryIndex-miss
# branch). Not expected to fire in normal play now that the prototype exists.
solreign-werewolf-transform-failed-popup = The change refuses to come. Wellness has been notified of the delay.

# Shown to a mauled colleague (SolreignWerewolfSystem.Transform.ApplyMoonTouched).
solreign-werewolf-moon-touched-popup = You've been Moon-Touched! Glitter fur, occasional awoo, mild clumsiness.
