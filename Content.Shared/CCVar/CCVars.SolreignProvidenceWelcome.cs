using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Providence first-shift/welcome-back CVars (player-delight lane: "this station remembers you"),
///     split into its own partial file rather than appending to <c>CCVars.Solreign.cs</c> or
///     <c>CCVars.SolreignProvidenceCommiseration.cs</c> — same D0 collision-control guidance those files
///     already follow: Providence-adjacent edits are a hotspot across concurrent worktrees, so a new,
///     narrowly-scoped CVar gets its own file.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for Providence's first-shift welcome beat
    ///     (<c>Content.Server._Solreign.Providence.ProvidenceWelcomeSystem</c>). Default on. Independent
    ///     of the base <see cref="SolreignProvidenceEnabled"/> voice switch — the welcome popup itself
    ///     is text-only and unconditional; only the first-shift induction line's bonus voice sting is
    ///     additionally gated by that switch (see <c>ProvidenceVoiceSystem.PlayLine</c>, which already
    ///     no-ops while it is off).
    /// </summary>
    public static readonly CVarDef<bool> SolreignProvidenceWelcomeEnabled =
        CVarDef.Create("solreign.providence.welcome_enabled", true, CVar.SERVERONLY);
}
