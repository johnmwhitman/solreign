using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the "social cheap adds" package (council memo
    ///     docs/council/2026-07-16-design-magnetism.md, item 5): the SolreignChirp greet emote's
    ///     audio/tracking, the once-per-account social-first milestone toasts (first chirp answered,
    ///     first time healed by another player, first item handed to you), and the third-visit
    ///     wingmate volunteer prompt.
    ///
    ///     Default TRUE, deliberately: every behavior behind this flag is INERT without other
    ///     players present — a chirp with nobody in range is just a text emote, a heal toast needs a
    ///     second player to do the healing, a hand-off needs a giver, and the wingmate prompt needs
    ///     wingmates enabled besides. Shipping default-on is therefore honest at population 1 (the
    ///     only population SOLREIGN will have for weeks): nothing fires until the social condition
    ///     it celebrates actually happens.
    /// </summary>
    public static readonly CVarDef<bool> SolreignSocialCheapAdds =
        CVarDef.Create("solreign.social_cheap_adds", true, CVar.SERVERONLY);
}
