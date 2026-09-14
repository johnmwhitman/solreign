namespace Content.Server._Solreign.Noticeboards;

/// <summary>
///     Pure, unit-testable Noticeboard input shaping and the closed set of quota/capacity/cooldown
///     numbers from docs/council/2026-07-17-player-text-safety.md (the C2 posture memo — LAW for
///     this feature). No ECS, no I/O, kept pure so <c>NoticeboardRulesTests</c> can exercise every
///     branch without spinning up the game (the <c>BountyClaimRules</c> precedent).
/// </summary>
public static class NoticeboardRules
{
    /// <summary>Spec rule 2: "Hard length cap of 260 characters per note ... no multi-part notes."</summary>
    public const int MaxNoteLength = 260;

    /// <summary>
    ///     Spec rule 2: "Board capacity capped at 12 active slots total, shared between players and
    ///     PROVIDENCE."
    /// </summary>
    public const int BoardCapacity = 12;

    /// <summary>Spec rule 5: "PROVIDENCE gets its own quota, at most 2 active notices at a time, inside the shared 12-slot cap."</summary>
    public const int ProvidenceCapacity = 2;

    /// <summary>
    ///     Spec rule 2: "a 24-hour cooldown between new pins (deleting your own note early does
    ///     not reset the cooldown)." A fixed constant, not a CVar — unlike the 72h expiry, the
    ///     memo only calls out retention as the number that needs to be operator-turnable; the
    ///     cooldown is part of the closed posture, not a knob.
    /// </summary>
    public const int CooldownHours = 24;

    /// <summary>
    ///     Trims a raw submission and rejects it if that leaves nothing. Truncates rather than
    ///     rejects when over-length (the Bounty claim idiom: a slightly-over-limit honest note
    ///     should still reach the daemon), so the only hard rejection is blank/whitespace. The
    ///     client's own live char-count clamp (mirroring <c>SolreignBountyBoardWindow</c>) makes
    ///     truncation a rare, modified-client-only path in practice.
    /// </summary>
    public static bool TrySanitizePostText(string? raw, out string sanitized)
    {
        // A modified client can put a null Text on the wire; treat it as blank rather than
        // NRE-ing the game thread (the BountyClaimRules precedent).
        sanitized = (raw ?? string.Empty).Trim();
        if (sanitized.Length == 0)
            return false;

        if (sanitized.Length > MaxNoteLength)
            sanitized = sanitized[..MaxNoteLength];

        return true;
    }
}
