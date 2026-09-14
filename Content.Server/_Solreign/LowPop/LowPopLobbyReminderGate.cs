namespace Content.Server._Solreign.LowPop;

/// <summary>
///     Pure once-per-round tracking for the low-pop lobby reminder (churn-mitigation lane). Free of
///     IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/LowPopLobbyReminderGateTests.cs) — same
///     HashSet-keyed-by-<see cref="Guid"/>-reset-on-round-boundary idiom as
///     <c>ProvidenceWelcomeGate</c>/<c>ProvidenceCommiserationGate</c>.
///
///     A player who joins the lobby, disconnects, and rejoins the SAME round only ever gets the
///     reminder once — no re-fire on reconnect, no fire-per-relog spam.
/// </summary>
public sealed class LowPopLobbyReminderGate
{
    private readonly HashSet<Guid> _remindedThisRound = new();

    /// <summary>
    ///     True if <paramref name="player"/> has not yet been reminded this round. Does not itself
    ///     record anything — pair with <see cref="MarkFired"/> once the caller has committed to sending
    ///     the reminder.
    /// </summary>
    public bool CanFire(Guid player)
    {
        return !_remindedThisRound.Contains(player);
    }

    /// <summary>Records that <paramref name="player"/> has now received their one reminder for the round.</summary>
    public void MarkFired(Guid player)
    {
        _remindedThisRound.Add(player);
    }

    /// <summary>Clears all tracked players — call on round start/restart so state never leaks across rounds.</summary>
    public void Reset()
    {
        _remindedThisRound.Clear();
    }
}
