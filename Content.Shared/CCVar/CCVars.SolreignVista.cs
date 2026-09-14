using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Kill-switch for the first-shift vista beat (council memo
    ///     docs/council/2026-07-16-design-magnetism.md, item 5): when a player with an ACTIVE First
    ///     Shift assignment first walks past their station's one composed-vista marker
    ///     (SolreignVistaMarkerComponent), PROVIDENCE privately delivers a single map-specific line,
    ///     once per player per round.
    ///
    ///     Deliberately its OWN CVar rather than joining <see cref="SolreignSocialCheapAdds"/>: that
    ///     flag's documented contract is "every behavior behind this flag is INERT without other
    ///     players present", which is what makes it honest to ship default-on at population 1. The
    ///     vista beat is the opposite — a strictly solo beat that fires with nobody else aboard — so
    ///     folding it in would silently break that flag's contract. A separate switch keeps both
    ///     honest and lets either beat be killed without touching the other.
    ///
    ///     Default TRUE: the beat is cosmetic flavor only (popup + private chat line), requires an
    ///     active First Shift assignment to exist at all, and hard-caps at one line per player per
    ///     round.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFirstShiftVistaBeat =
        CVarDef.Create("solreign.first_shift_vista_beat", true, CVar.SERVERONLY);
}
