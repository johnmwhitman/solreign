namespace Content.Shared._Solreign.PlayerDelight.Wingmates;

/// <summary>Stable localization contract for every boundary shown before guide consent.</summary>
public static class WingmateVolunteerCharter
{
    public static readonly IReadOnlyList<string> TermKeys =
    [
        "wingmates-charter-teach-without-taking-over",
        "wingmates-charter-ask-before-spoilers",
        "wingmates-charter-keep-in-game",
        "wingmates-charter-no-private-contact",
        "wingmates-charter-no-hazing-pressure",
        "wingmates-charter-no-staff-authority",
        "wingmates-charter-respect-boundaries",
    ];
}

/// <summary>
/// Pure visibility model for the Wingmates window. Keeping this exhaustive and client-agnostic makes
/// it difficult for stale controls from one consent state to leak into another.
/// </summary>
public readonly record struct WingmatePresentation(
    bool ShowRequest,
    bool ShowWaiting,
    bool ShowOffer,
    bool ShowProposal,
    bool ShowPair,
    bool ShowDisabled,
    bool ShowDissolve,
    bool ShowBlock,
    bool ShowModeratorHelp,
    bool ShowVolunteer,
    bool ShowPause,
    bool ShowResume)
{
    public static bool CanSubmitVolunteerChange(bool canVolunteer, bool isVolunteering, bool charterAccepted) =>
        isVolunteering || canVolunteer && charterAccepted;

    public static WingmatePresentation For(WingmateUiMode mode)
    {
        return mode switch
        {
            WingmateUiMode.Idle => new(true, false, false, false, false, false, false, false, false, true, false, false),
            WingmateUiMode.Seeking => new(false, true, false, false, false, false, false, false, false, false, false, false),
            WingmateUiMode.OfferAvailable => new(false, false, true, false, false, false, false, false, false, true, false, false),
            WingmateUiMode.OfferReceived => new(false, false, false, true, false, false, false, false, false, false, false, false),
            WingmateUiMode.Paired => new(false, false, false, false, true, false, true, true, true, false, true, false),
            WingmateUiMode.Paused => new(false, false, false, false, true, false, true, true, true, false, false, true),
            WingmateUiMode.Disabled => new(false, false, false, false, false, true, false, false, false, false, false, false),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
