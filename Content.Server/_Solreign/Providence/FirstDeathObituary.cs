using System;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     The composed Discord-obituary snapshot for ONE claimed authored first death (FD-W4,
///     docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §6/§8D) — plain data, no Robust
///     dependencies, so the payload/JSONL shaping downstream of it is unit-testable directly
///     (the <c>BugReportEntry</c> idiom).
///
///     Composed by <c>ProvidenceFirstDeathSystem</c> from EXACTLY the values persisted into the
///     once-per-account-EVER claim row (the same snapshot the FD-W3 crypt report rides), plus:
///     <list type="bullet">
///     <item><see cref="PlayerCountAtDeath"/> — connected players captured synchronously on the
///     death tick (the recap-law analogue's counting moment, spec §6.2: never advertise an empty
///     station; the LowPop counting mechanism).</item>
///     <item><see cref="RoundId"/>/<see cref="ComposedAtUtc"/> — context for the JSONL
///     manual-paste block only; neither appears in the Discord embed.</item>
///     </list>
///
///     Closed vocabulary by construction (spec §6.3): character name (handle-based identity as
///     eulogized), title, tours, and the classified cause are the ONLY player-derived facts this
///     record can carry. No account GUIDs, no attacker data, no location — the fields for them do
///     not exist, which is stronger than a convention.
/// </summary>
public sealed record FirstDeathObituary(
    DateTimeOffset ComposedAtUtc,
    int RoundId,
    string CharacterName,
    string Title,
    int Tours,
    FirstDeathCause Cause,
    int PlayerCountAtDeath);

/// <summary>
///     The obituary system's dispatch decision for one composed obituary — recorded into the
///     JSONL line BEFORE any send is attempted (write-before-dispatch), so the file is an honest
///     ledger of what the gate stack decided, not of what Discord acknowledged.
/// </summary>
public enum FirstDeathObituaryDispatch : byte
{
    /// <summary>`solreign.first_death.webhook` is empty/unconfigured (the shipping default):
    /// manual-paste mode, JSONL only, zero egress (spec §6.2 webhook gate / §9 Q1).</summary>
    WebhookEmpty,

    /// <summary>Webhook configured but connected players at death time were below
    /// `solreign.first_death.min_players`: the advertisement surface requires witnesses; JSONL
    /// only (spec §6.2 player gate — "Below the gate, the obituary goes to the JSONL file
    /// only").</summary>
    BelowPlayerGate,

    /// <summary>Both gates passed but this egress channel's own rate-limit window was busy:
    /// JSONL only, no retry (the FD-W3 review's dedicated-channel law makes this near-impossible
    /// for a once-per-account-EVER cadence, but the decision is still recorded).</summary>
    RateLimited,

    /// <summary>All gates passed; the send path was invoked (fire-and-forget).</summary>
    Dispatched,
}
