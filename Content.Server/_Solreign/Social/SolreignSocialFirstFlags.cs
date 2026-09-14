namespace Content.Server._Solreign.Social;

/// <summary>
///     The CLOSED vocabulary of <c>social_firsts</c> flag ids (see
///     <c>SeasonLedgerStore.SocialFirsts.cs</c>) — the same closed-vocabulary discipline as the
///     first-death cause strings: flag ids are compile-time constants, never free text and never
///     player-authored. Adding a milestone means adding a constant here (and its copy pair in
///     social-cheap-adds.ftl); nothing ever writes a flag id that is not on this list.
/// </summary>
public static class SolreignSocialFirstFlags
{
    /// <summary>Another player chirped back within the answer window of your chirp.</summary>
    public const string ChirpAnswered = "chirp_answered";

    /// <summary>Another player's heal restored some of your damage.</summary>
    public const string HealedByAnother = "healed_by_another";

    /// <summary>An item released by another player's hands landed in yours shortly after.</summary>
    public const string ItemReceived = "item_received";

    /// <summary>The one-time third-visit "consider volunteering as a Wingmate" prompt.</summary>
    public const string WingmatePrompt = "wingmate_prompt";

    /// <summary>The one-time first-spawn pointer toward the Wingmate beacon and the guided First
    /// Shift track (<c>FirstShiftSpawnPromptSystem</c>) — Tours == 0 audience only.</summary>
    public const string FirstShiftSpawnPrompt = "first_shift_spawn_prompt";
}
