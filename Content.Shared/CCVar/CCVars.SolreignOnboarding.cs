using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Solreign's station-wide "first five minutes" onboarding-beat CVar, split into its own partial
///     file rather than appending to <c>CCVars.Solreign.cs</c> — same D0 collision-control guidance
///     <c>CCVars.SolreignProvidenceCommiseration.cs</c> and <c>CCVars.SolreignProvidenceWelcome.cs</c>
///     already follow: a new, narrowly-scoped CVar gets its own file so a concurrent worktree editing
///     the shared CCVars files never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for the round-start "onboarding beat" PA announcement
    ///     (<c>Content.Server._Solreign.Onboarding.SolreignOnboardingSystem</c>). Default on.
    ///     Independent of both <see cref="SolreignProvidenceWelcomeEnabled"/> (the per-player welcome
    ///     popup) and the Corporate Ladder game rule's own keynote broadcast — this is a distinct,
    ///     station-wide beat and can be toggled without touching either.
    /// </summary>
    public static readonly CVarDef<bool> SolreignOnboardingBeatEnabled =
        CVarDef.Create("solreign.onboarding.beat_enabled", true, CVar.SERVERONLY);
}
