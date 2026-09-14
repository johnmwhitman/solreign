using System;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     FD-W3.5 — the plaque backfill. The authored first death shipped LIVE (v13.3) with W1/W2
///     only: every claim banked a <c>first_death</c> row, but the FD-W3 crypt wire
///     (<see cref="Content.Server.Administration.Systems.SolreignCryptSystem.ReportFirstDeath"/>)
///     merged after the cut — so a player whose once-EVER first death fired in that window has a
///     claim row and NO website plaque, and the claim-time wire will never fire for them again.
///     Their memorial must not be silently lost.
///
///     The fix, in three parts (all keyed on the additive <c>crypt_reported</c> column):
///       * CLAIM TIME (ProvidenceFirstDeathSystem.ClaimAndSchedule): when the FD-W3 report is
///         successfully HANDED to the crypt sender, the row is stamped — claim-time and backfill
///         can never both report one row. When the hand-off is refused (crypt gates closed,
///         rate-limited), the row stays unstamped and this backfill recovers it in a later round:
///         strictly better than the pre-W3.5 behavior, where a gates-closed claim lost its plaque
///         forever.
///       * ROUND START (<see cref="StartCryptBackfill"/>): if the crypt channel is ready, scan for
///         unstamped rows and queue them (oldest death first), composing each report from exactly
///         the persisted claim row. Gates closed → no scan at all: rows stay BANKED, never burned.
///       * UPDATE PUMP (<see cref="PumpCryptBackfill"/>): dispatch at most one queued report per
///         <see cref="CryptBackfillSpacing"/> (2× <see cref="DirectorChannel.MinRequestInterval"/>,
///         clearing the crypt_first_death channel's 1-per-750ms window with headroom for a real
///         first death landing mid-backfill), stamping each row only after a successful hand-off.
///
///     Stamping law (deliberate deviation from the house write-before-dispatch idiom; receipt
///     docs/receipts/PLAQUE-BACKFILL-2026-07-17.md): the daemon's <c>handle_first_death</c> mints a
///     plaque per POST, NOT per victim (plain INSERT, no unique constraint — verified against the
///     daemon repo), so the game-side stamp is the ONLY dedupe; and the crypt gates default OFF, so
///     stamp-before-dispatch would burn every banked memorial the first round the backfill ran with
///     the daemon off — the exact silent-loss failure this lane exists to fix. Stamping on
///     successful HAND-OFF matches the live claim-time path's own semantics (fire-and-forget;
///     delivery is never confirmed on any crypt path). Accepted residual: a crash in the
///     milliseconds between hand-off and the stamp committing re-reports one row next round — a
///     duplicate plaque on the website feed (cosmetic, admin-prunable, cousin of the FD-W3
///     receipt's accepted legendary+first-death double-mint), preferred over a silently lost
///     memorial.
/// </summary>
public sealed partial class ProvidenceFirstDeathSystem
{
    /// <summary>Spacing between backfill dispatches — 2× the Director channel's minimum request
    /// interval (750ms), so paced backfill POSTs always clear the rate limiter and a concurrent
    /// real first death still finds room in the window.</summary>
    private static readonly TimeSpan CryptBackfillSpacing = DirectorChannel.MinRequestInterval * 2;

    /// <summary>The paced backfill work queue — see <see cref="FirstDeathCryptBackfillQueue"/>.</summary>
    private readonly FirstDeathCryptBackfillQueue _cryptBackfill = new();

    /// <summary>How many backfill reports were successfully handed to the crypt sender since the
    /// last round reset — test seam, mirrors <see cref="_cryptReportsComposed"/>.</summary>
    private int _cryptBackfillHanded;

    /// <summary>
    ///     Round-start entry point (called from OnRoundStarting — the same round-boundary hook
    ///     family the beat queue resets on). Fail-closed early-outs: the universal first-death
    ///     kill switch, then the crypt channel's own readiness (master + crypt CVar + token). A
    ///     closed gate skips even the DB scan — unstamped rows stay banked for a round where the
    ///     daemon is actually reachable, never consumed on a dead channel.
    /// </summary>
    private void StartCryptBackfill()
    {
        if (!_enabled)
            return;

        if (!_crypt.FirstDeathChannelReady)
            return;

        LoadCryptBackfill();
    }

    /// <summary>
    ///     The one async hop of the backfill (house threading contract: async void + try/catch,
    ///     never blocks the tick; continuations marshal back to the main thread, the
    ///     ClaimAndSchedule precedent). Queries every unstamped row and queues a composed report
    ///     for each — a storage failure costs one round's backfill pass, never a crash, and the
    ///     next round rescans.
    /// </summary>
    private async void LoadCryptBackfill()
    {
        try
        {
            var rows = await _ledger.GetCryptUnreportedFirstDeathsAsync();
            foreach (var row in rows)
            {
                _cryptBackfill.Enqueue(new FirstDeathCryptBackfillItem(row.User, BuildBackfillReport(row)));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading the first-death crypt backfill queue:\n{e}");
        }
    }

    /// <summary>
    ///     Re-composes the FD-W3 wire report from a persisted claim row — the backfill's
    ///     counterpart of the claim-time <c>FirstDeathCryptReport.Build</c> call, sourcing every
    ///     field from what <c>TryClaimFirstDeathAsync</c> recorded at the death tick. The epitaph
    ///     is re-rendered from the persisted plate id (the ledger stores plates by id, never free
    ///     text): the id resolves through <see cref="FirstDeathEpitaphPicker.TryGetPlateTemplate"/>
    ///     with <c>{title}</c> substituted from the recorded title; an unknown id (a plate retired
    ///     from the copy pack after the claim) degrades to the deterministic re-pick over the same
    ///     recorded (tours, cause, title) triple, and an unparseable cause string (never expected —
    ///     closed vocabulary) degrades to Unknown, the LoadRehire idiom.
    /// </summary>
    internal static FirstDeathCryptReport BuildBackfillReport(FirstDeathRecord record)
    {
        if (!Enum.TryParse<FirstDeathCause>(record.Cause, ignoreCase: true, out var cause))
            cause = FirstDeathCause.Unknown;

        string epitaphText;
        if (FirstDeathEpitaphPicker.TryGetPlateTemplate(record.EpitaphId, out var template))
            epitaphText = template.Replace("{title}", record.TitleAtDeath);
        else
            epitaphText = FirstDeathEpitaphPicker.Pick(record.ToursAtDeath, cause, record.TitleAtDeath).Text;

        return FirstDeathCryptReport.Build(
            record.User,
            record.CharacterName,
            epitaphText,
            cause,
            record.ToursAtDeath,
            record.TitleAtDeath);
    }

    /// <summary>
    ///     Per-tick pump (called from Update): releases at most one paced item and dispatches it.
    ///     Re-checks the universal kill switch at dispatch time (the FireBeat idiom — a mid-round
    ///     CVar flip freezes the queue; round cleanup clears it).
    /// </summary>
    private void PumpCryptBackfill()
    {
        if (!_enabled)
            return;

        if (!_cryptBackfill.TryDequeueDue(_timing.CurTime, CryptBackfillSpacing, out var item))
            return;

        DispatchCryptBackfill(item);
    }

    /// <summary>
    ///     Hands one backfill report to the crypt sender and, ONLY on a successful hand-off,
    ///     stamps the row (see the stamping law in the class doc). A refused hand-off (gate
    ///     flipped closed mid-round, rate-limit collision with a real first death) leaves the row
    ///     unstamped — banked for the next round's scan, never burned.
    /// </summary>
    private async void DispatchCryptBackfill(FirstDeathCryptBackfillItem item)
    {
        try
        {
            if (!_crypt.ReportFirstDeath(item.Report))
                return;

            _cryptBackfillHanded++;

            if (!await _ledger.TryMarkFirstDeathCryptReportedAsync(item.AccountId))
            {
                // Should be unreachable (the scan only queues unstamped rows and nothing else
                // stamps mid-round) — but if it ever races, the daemon may now hold a duplicate
                // plaque; say so honestly instead of hiding it.
                Log.Warning($"First-death crypt backfill for {item.AccountId} found the row already "
                            + "stamped after hand-off — a duplicate plaque may have been minted.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error while dispatching first-death crypt backfill for {item.AccountId}:\n{e}");
        }
    }

    // --- Test seams (the ProvidenceWelcomeSystem idiom) ----------------------------------------------

    /// <summary>Pending backfill queue depth — integration-test visibility only.</summary>
    internal int CryptBackfillQueuedForTests => _cryptBackfill.Count;

    /// <summary>Successful backfill hand-offs since the last round reset — test visibility only.</summary>
    internal int CryptBackfillHandedForTests => _cryptBackfillHanded;

    /// <summary>Runs the round-start scan gate chain on demand — integration tests trigger the
    /// backfill without restarting the pooled server's round (the REAL Update pump then paces the
    /// dispatch, exactly as a live round would).</summary>
    internal void StartCryptBackfillForTests() => StartCryptBackfill();

    /// <summary>Clears the backfill queue and hand-off counter — the round-boundary reset,
    /// shared by the real round hooks and <see cref="ResetRoundStateForTests"/>.</summary>
    private void ResetCryptBackfillState()
    {
        _cryptBackfill.Clear();
        _cryptBackfillHanded = 0;
    }
}
