using Content.Server._Solreign.SeasonLedger;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure eligibility rule for PROVIDENCE's Ledger-sourced death-memory line (design doc requirement
///     2: "at least one line class that cites persistent history... read-only against the Ledger").
///     Zero I/O — the Season Ledger read (<c>SeasonLedgerSystem.GetFirstDeathAsync</c>) happens in
///     <c>ProvidenceEventReactiveSystem</c>; this class only decides whether the record it got back
///     counts as genuine PRIOR history. Unit-tested in isolation
///     (Content.Tests/_Solreign/ProvidenceReactiveDeathMemoryTests.cs).
///
///     A record only counts as memory if it was claimed under a DIFFERENT round identity than the one
///     in progress right now. Round IDs may restart across server processes, so numeric ordering is
///     not a chronology proof; inequality is the available same-round race guard. This does two things:
///       1. No-history case: a null record (account has never had a recorded first death) is never
///          memory — the line degrades to silence (falls through to Generic/Repeat in
///          <c>ProvidenceReactiveCopy.ClassifyDeath</c>) rather than fabricating a memory that doesn't
///          exist. This is the literal "fails closed to silence" rail for this line class.
///       2. Same-round race guard: <c>ProvidenceFirstDeathSystem</c> subscribes the SAME
///          <c>MobStateChangedEvent</c> broadcast and, on an account's true first-ever death, claims
///          the first-death row asynchronously on that very death. Without this check, a query racing
///          that claim could — in the vanishingly rare case it resolves after the claim commits — cite
///          the death that JUST happened as if it were prior history, which is not what "it never
///          forgets" means. Requiring the record's round to differ from the current one sidesteps that
///          race entirely for normally recorded rows. The persistent record is pre-existing context
///          from another round identity, rather than the claim being written for this death.
/// </summary>
public static class ProvidenceReactiveDeathMemory
{
    /// <summary>
    ///     True if <paramref name="record"/> represents history under a different round identity
    ///     relative to <paramref name="currentRoundId"/> — false for a null record (no history at all)
    ///     and false for a record claimed in the CURRENT round (same-round race guard, see class doc
    ///     comment). This deliberately does not infer chronology from the numeric ID.
    /// </summary>
    public static bool IsEligible(FirstDeathRecord? record, int currentRoundId)
    {
        return record is not null && record.RoundId != currentRoundId;
    }
}
