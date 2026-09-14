namespace Content.Server._Solreign.Providence;

/// <summary>
///     Which pool of death-reactive lines applies to a given death — see
///     <see cref="ProvidenceReactiveCopy.ClassifyDeath"/>. Ordered least-to-most "aboutness", per the
///     design doc's diagnosis that concrete, memory-backed specifics are what makes a line land.
/// </summary>
public enum ProvidenceReactiveDeathLineClass
{
    /// <summary>No Ledger memory, not yet a repeat pattern this shift — an ordinary reactive line.</summary>
    Generic,

    /// <summary>This account has died <see cref="ProvidenceReactiveCopy.RepeatThreshold"/>+ times THIS
    /// shift (no Ledger memory available/applicable) — names the pattern forming in-round.</summary>
    Repeat,

    /// <summary>This account has an eligible persisted first-death record from a different round
    /// identity on file — "it never forgets" made literal, read-only against the Season Ledger.</summary>
    Memory,
}

/// <summary>
///     Pure copy-selection tables for PROVIDENCE's event-reactive lines (feat/providence-event-reactive
///     — PROVIDENCE-VOICE-DESIGN.md). Same split as <c>FirstDeathCopy</c>: no ECS, no I/O, no Loc —
///     this class only picks KEYS and CLASSES; <c>ProvidenceEventReactiveSystem</c> renders them.
///     Unit-tested in isolation (Content.Tests/_Solreign/ProvidenceReactiveCopyTests.cs).
///
///     Closed vocabulary, mirroring the first-death pack's discipline: death-generic lines take only
///     $name; the repeat line additionally takes $count; the memory line additionally takes $cause and
///     $title (both sourced from the existing, already-sanitized-by-construction first-death record —
///     $cause is <c>FirstDeathCopy.CauseLabelFor</c>'s closed display label, $title is the Ledger's own
///     TitleAtDeath string, never free text). There is no $attacker anywhere, same reasoning as the
///     first-death pack.
/// </summary>
public static class ProvidenceReactiveCopy
{
    /// <summary>PA sender label for every event-reactive line.</summary>
    public const string SenderKey = "solreign-providence-reactive-sender";

    /// <summary>
    ///     An account's death count THIS shift at or above which its next death is classified
    ///     <see cref="ProvidenceReactiveDeathLineClass.Repeat"/> instead of Generic (when Memory does
    ///     not already apply) — "a pattern is forming" needs at least this many data points to be a
    ///     pattern rather than bad luck.
    /// </summary>
    public const int RepeatThreshold = 3;

    /// <summary>Generic death-reactive lines (providence-reactive.ftl) — variables: $name.</summary>
    public static readonly string[] DeathGenericKeys =
    {
        "solreign-providence-reactive-death-generic-1",
        "solreign-providence-reactive-death-generic-2",
    };

    /// <summary>The repeat-pattern line (providence-reactive.ftl) — variables: $name, $count.</summary>
    public const string DeathRepeatKey = "solreign-providence-reactive-death-repeat";

    /// <summary>Ledger-memory death lines (providence-reactive.ftl) — variables: $name, $cause, $title.</summary>
    public static readonly string[] DeathMemoryKeys =
    {
        "solreign-providence-reactive-death-memory-1",
        "solreign-providence-reactive-death-memory-2",
    };

    /// <summary>Round-end line when nobody died this shift — variables: none.</summary>
    public const string RoundEndZeroKey = "solreign-providence-reactive-roundend-zero";

    /// <summary>Round-end line when at least one Asset died this shift — variables: $count.</summary>
    public const string RoundEndSomeKey = "solreign-providence-reactive-roundend-some";

    /// <summary>
    ///     Pure classification: which line pool applies to this death. Memory always wins over Repeat
    ///     when both would technically apply (a returning player having their 3rd death this shift AND
    ///     an eligible first-death record from a different round identity) — memory is rarer and more
    ///     meaningful, the design doc's core thesis, so it gets the line.
    /// </summary>
    public static ProvidenceReactiveDeathLineClass ClassifyDeath(bool hasLedgerMemory, int accountDeathCountThisRound)
    {
        if (hasLedgerMemory)
            return ProvidenceReactiveDeathLineClass.Memory;

        return accountDeathCountThisRound >= RepeatThreshold
            ? ProvidenceReactiveDeathLineClass.Repeat
            : ProvidenceReactiveDeathLineClass.Generic;
    }

    /// <summary>Priority tier for a classified death line — see <see cref="ProvidenceReactivePriority"/>
    /// for what the tier actually does to the anti-spam cooldown.</summary>
    public static ProvidenceReactivePriority PriorityFor(ProvidenceReactiveDeathLineClass lineClass)
    {
        return lineClass switch
        {
            ProvidenceReactiveDeathLineClass.Memory => ProvidenceReactivePriority.High,
            ProvidenceReactiveDeathLineClass.Repeat => ProvidenceReactivePriority.Normal,
            _ => ProvidenceReactivePriority.Low,
        };
    }

    /// <summary>Which round-end key applies for a given shift's death toll.</summary>
    public static string RoundEndKeyFor(int deathTollThisRound)
    {
        return deathTollThisRound <= 0 ? RoundEndZeroKey : RoundEndSomeKey;
    }
}
