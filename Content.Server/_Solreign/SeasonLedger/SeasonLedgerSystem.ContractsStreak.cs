using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._Solreign.Notifications;
using Content.Server.Chat.Managers;
using Content.Shared.CCVar;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Persistent per-account CONSECUTIVE-SHIFT streak for Solreign Contracts (v14 quest-board
///     extension, spec §3). Table + read/write live in
///     <c>SeasonLedgerStore.ContractsStreak.cs</c> (structurally cloned from
///     <c>SeasonLedgerStore.DirectivesFax.cs</c>'s <c>directives_fax_streak</c>); this partial owns the
///     round-end fold + milestone notification, mirroring
///     <c>DirectivesFaxRuleSystem.UpdateStreaksAndNotify</c>'s idiom.
///
///     RESET SEMANTICS (the one deliberate divergence from Directives Fax, picked at build time —
///     "Option B" in the design spec, "reset on a missed shift"): unlike Directives Fax's
///     presence-minutes gate, this reuses the round-end roster <c>OnRoundEnd</c> (SeasonLedgerSystem.cs)
///     already snapshots on the main thread BEFORE its first await — every CONNECTED account in that
///     roster (<c>RoundEndPlayerRecord.Connected</c> / <c>info.Connected</c>) gets its streak touched:
///     >=1 Personal-scope contract completed this round increments it, zero Personal completions
///     resets it to 0. Salvage participation does not count. Disconnected roster members are skipped
///     entirely (neither advance nor reset). An account absent the WHOLE shift never appears in that
///     roster and is therefore never touched — absence still never resets a streak.
///
///     Gated end-to-end by <see cref="CCVars.SolreignContractsQuestBoardEnabled"/> (dormant by
///     default): while off, <see cref="UpdateContractStreaksAndNotify"/> returns immediately before
///     touching the store at all, so <c>contracts_streak</c> rows sit inert exactly like
///     <c>directives_fax_streak</c>'s own kill-switch note describes.
///
///     Threading / durability: fold intent is journaled INSIDE the canonical envelope transaction
///     (<c>pending_contracts_streak_folds</c>). The fold itself is awaited AFTER the envelope commits
///     (not fire-and-forget). All account folds + journal consume commit in one store transaction —
///     never per-account partial folds. Failures log + retry once then rethrow; unconsumed journals
///     are replayed by recovery. Same/older-round replay returns <c>WasReplay</c> so milestone
///     notification is suppressed.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IChatManager _chatManager = default!;

    private bool _contractsQuestBoardEnabled;

    private void InitializeContractsStreak()
    {
        Subs.CVar(_cfg, CCVars.SolreignContractsQuestBoardEnabled, v => _contractsQuestBoardEnabled = v, invokeImmediately: true);
    }

    /// <summary>
    ///     Builds the fold batch from main-thread snapshots. Empty when the quest-board CVar is off
    ///     or no connected roster members exist. Must run before the first await.
    /// </summary>
    private List<ContractStreakFoldRequest>? BuildContractStreakFolds(
        IReadOnlyList<RoundEndPlayerRecord> records,
        IReadOnlyList<ContractLogRecord> contractLog)
    {
        if (!_contractsQuestBoardEnabled)
            return null;

        var folds = new List<ContractStreakFoldRequest>();
        foreach (var record in records)
        {
            if (!ContractsStreakRules.ShouldFoldAccount(record.Connected))
                continue;

            // Personal-scope completions only — salvage participation must not advance the streak.
            var personalCompletions = ContractsStreakRules.CountPersonalCompletions(contractLog, record.User);
            var completedThisRound = ContractsStreakRules.CompletedPersonalThisRound(personalCompletions);
            folds.Add(new ContractStreakFoldRequest(record.User, completedThisRound));
        }

        return folds.Count == 0 ? null : folds;
    }

    /// <summary>
    ///     Applies already-built fold intents (journaled with the envelope) and dispatches milestone
    ///     toasts. Awaited after envelope commit. Failures log + retry once then rethrow — the durable
    ///     journal keeps recovery able to finish the fold if both attempts fail.
    /// </summary>
    private async Task UpdateContractStreaksAndNotify(
        IReadOnlyList<ContractStreakFoldRequest>? folds,
        int roundId)
    {
        // ROUND-9: re-check the gate (defense in depth vs. in-flight CVar flip).
        if (!_contractsQuestBoardEnabled)
            return;
        if (folds is null || folds.Count == 0)
            return;

        IReadOnlyList<ContractStreakFoldResult> results;
        try
        {
            results = await _store.RecordContractStreakOutcomesAsync(folds, roundId);
        }
        catch (Exception e)
        {
            Log.Error($"Error recording Contracts streak outcomes (round {roundId}); retrying once:\n{e}");
            results = await _store.RecordContractStreakOutcomesAsync(folds, roundId);
        }

        foreach (var result in results)
        {
            if (!ContractsStreakRules.ShouldNotifyMilestone(
                    result.CompletedThisRound,
                    result.WasReplay,
                    result.CurrentStreak))
                continue;

            NotifyContractStreakMilestone(result.User, result.CurrentStreak);
        }
    }

    /// <summary>Fires (or silently drops) one streak-milestone toast — main thread, full
    /// re-resolution guard chain (never trust anything captured before the await), same idiom as
    /// <c>DirectivesFaxRuleSystem.NotifyMilestone</c>. The ledger row is already written: a dropped
    /// delivery is swallowed forever, never duplicated. Callers must not invoke this on replay.</summary>
    private void NotifyContractStreakMilestone(Guid account, int streak)
    {
        if (!ContractsStreakMilestones.ReasonLocKeyByStreak.TryGetValue(streak, out var reasonKey))
            return;

        if (!_playerManager.TryGetSessionById(new NetUserId(account), out var session))
            return;

        var reasonText = Loc.GetString(reasonKey);

        if (session.AttachedEntity is { } target
            && !Deleted(target)
            && !HasComp<GhostComponent>(target)
            && (!TryComp<MobStateComponent>(target, out var mobState) || mobState.CurrentState != MobState.Dead))
        {
            SolreignAwardPopup.ShowMilestone(_popup, target, reasonText);
        }

        // Chat mirror always sends while the session lives — survives popup-blindness, same
        // dual-delivery idiom as the Directives Fax streak toast.
        _chatManager.DispatchServerMessage(session, Loc.GetString("solreign-contracts-streak-milestone-chat", ("reason", reasonText)));
    }
}
