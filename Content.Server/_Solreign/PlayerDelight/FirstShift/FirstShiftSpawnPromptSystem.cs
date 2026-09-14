using System;
using System.Collections.Generic;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.PlayerDelight.FirstShift;

/// <summary>
///     The first-spawn First Shift push-prompt: once per account EVER, a brand-new player
///     (career Tours == 0) gets one private popup + chat pointer toward the Wingmate beacon and
///     the guided First Shift track it opens.
///
///     WHY THIS EXISTS: onboarding was 100% pull-based. Every other trigger in
///     <see cref="FirstShiftSystem"/> is beacon-UI-scoped — a newcomer who never finds and clicks
///     the beacon never learns the interactive tutorial exists. Two organic arrivals bounced in
///     their first fifteen minutes before this was built (the 2026-07-29 acquisition-lane INBOUND
///     and the A9 ruling record the pattern). This system makes the tutorial introduce itself.
///
///     Structurally a sibling of <see cref="Content.Server._Solreign.Social.SolreignWingmatePromptSystem"/>
///     — deliberately a separate system, NOT a partial of <see cref="FirstShiftSystem"/>: that
///     system already subscribes <see cref="RoundRestartCleanupEvent"/>, and a partial adding the
///     same (system, event) pair again is a duplicate-subscription server-boot abort.
///
///     Spawn-moment beats are a sequence, not a pile: Providence Welcome (immediate) →
///     first-shift personal beat (8s) → wingmate volunteer prompt (14s, Tours >= 3 — a DISJOINT
///     audience from this prompt's Tours == 0) → this (20s).
///
///     Once-ever is a <c>social_firsts</c> claim row
///     (<see cref="SolreignSocialFirstFlags.FirstShiftSpawnPrompt"/>, write-before-dispatch): a
///     racing double-spawn can never double-prompt, and a disconnect between claim and delivery
///     swallows the prompt forever rather than repeating it.
///
///     Gated by <see cref="CCVars.SolreignFirstShiftSpawnPrompt"/> AND
///     <see cref="CCVars.SolreignFirstShiftAssignmentsEnabled"/> — pointing a newcomer at a beacon
///     that shrugs would be worse than silence (re-checked at fire time, the mid-flight-CVar-off
///     guarantee).
/// </summary>
public sealed partial class FirstShiftSpawnPromptSystem : EntitySystem
{
    /// <summary>Fires after the Welcome (immediate), the first-shift personal beat (8s, the SAME
    /// Tours == 0 audience as this prompt), and the wingmate volunteer prompt (14s) — clear air
    /// between beats that target the same newcomer.</summary>
    private const float PromptDelaySeconds = 20f;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private FirstShiftSystem _firstShift = default!;

    /// <summary>Accounts whose prompt check already dispatched this round — marked BEFORE the
    /// async ledger read (the Welcome "mark first" idiom), reset on round boundaries.</summary>
    private readonly HashSet<Guid> _checkedThisRound = new();

    /// <summary>Won-but-not-yet-fired prompts (account, due time). At most one entry per account,
    /// ever, across the server's whole life.</summary>
    private readonly List<(Guid Account, TimeSpan Due)> _pending = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignFirstShiftSpawnPrompt"/>.</summary>
    private bool _promptEnabled;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignFirstShiftAssignmentsEnabled"/>.</summary>
    private bool _assignmentsEnabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignFirstShiftSpawnPrompt, v => _promptEnabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignFirstShiftAssignmentsEnabled, v => _assignmentsEnabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _checkedThisRound.Clear();
        _pending.Clear();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _checkedThisRound.Clear();
        _pending.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].Due > now)
                continue;

            var account = _pending[i].Account;
            _pending.RemoveAt(i);
            FirePrompt(account);
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!_promptEnabled || !_assignmentsEnabled)
            return;

        // The vanilla-greeting Silent contract, same as Welcome/wingmate-prompt: a silent spawn
        // opted out.
        if (ev.Silent)
            return;

        // Claim-time round gate (the first-death F1 rule, via the wingmate prompt — NOT the older
        // ProvidenceWelcomeSystem, which lacks it): never burn the once-ever claim on a prompt
        // that could not fire.
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var guid = ev.Player.UserId.UserId;
        if (!_checkedThisRound.Add(guid))
            return;

        LoadPrompt(guid);
    }

    /// <summary>
    ///     The one async hop (house threading contract: async void + try/catch): career read →
    ///     eligibility → atomic once-ever claim → schedule the delayed private beat.
    /// </summary>
    private async void LoadPrompt(Guid account)
    {
        try
        {
            // Tours == 0 is the substrate's "brand new asset" signal (tours land at round end for
            // every connected player — see ProvidenceWelcomeSystem's doc). FirstShift COMPLETION is
            // deliberately not the key: it is round-local state with no per-account persistence,
            // so a veteran who never clicked the beacon would otherwise be prompted forever.
            // Ordering is load-bearing: an ineligible visit must never burn the once-ever claim.
            var career = await _ledger.GetCareerStatsAsync(account);
            if (career.Tours != 0)
                return;

            // They already found the beacon this round: the nudge is answered; the claim is NOT
            // burned, so a future fresh spawn (same brand-new account, next round) still gets the
            // one prompt if they arrive without finding it again.
            if (_firstShift.HasActiveAssignment(new NetUserId(account)))
                return;

            var claimed = await _ledger.TryClaimSocialFirstAsync(
                account, SolreignSocialFirstFlags.FirstShiftSpawnPrompt, _ticker.RoundId);
            if (!claimed)
                return;

            _pending.Add((account, _timing.CurTime + TimeSpan.FromSeconds(PromptDelaySeconds)));
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading first-shift spawn prompt for {account}:\n{e}");
        }
    }

    /// <summary>
    ///     Fires (or silently drops) one due prompt — the full fire-time guard chain, never
    ///     trusting anything captured before the delay. The claim row is already written: a
    ///     dropped delivery is swallowed forever, never repeated (write-before-dispatch).
    /// </summary>
    private void FirePrompt(Guid account)
    {
        // Mid-flight-CVar-off guarantee, both gates.
        if (!_promptEnabled || !_assignmentsEnabled)
            return;

        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (!_players.TryGetSessionById(new NetUserId(account), out var session))
            return;

        if (session.AttachedEntity is not { } target || Deleted(target))
            return;

        if (HasComp<GhostComponent>(target))
            return;

        if (TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState == MobState.Dead)
            return;

        // If they found the beacon during the delay, the nudge is already answered.
        if (_firstShift.HasActiveAssignment(session.UserId))
            return;

        // Popup + private chat line (screenshot-surviving), no PA, no broadcast — a private
        // orientation pointer, not an announcement.
        _popup.PopupEntity(Loc.GetString("solreign-first-shift-spawn-prompt-popup"), target, target, PopupType.Medium);
        _chatManager.DispatchServerMessage(session, Loc.GetString("solreign-first-shift-spawn-prompt-chat"));
    }

    // --- Test seams (the SolreignWingmatePromptSystem idiom) ----------------------------------

    /// <summary>Won-but-unfired prompts — integration-test visibility only.</summary>
    internal int PendingPromptCountForTests => _pending.Count;

    /// <summary>Round-boundary reset without a real round event — pooled-server clean-slate law.</summary>
    internal void ResetRoundStateForTests()
    {
        _checkedThisRound.Clear();
        _pending.Clear();
    }

    /// <summary>Drains and fires every prompt due at <paramref name="now"/> — full guard chain,
    /// no real-time waiting.</summary>
    internal void FireDuePromptsForTests(TimeSpan now)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].Due > now)
                continue;

            var account = _pending[i].Account;
            _pending.RemoveAt(i);
            FirePrompt(account);
        }
    }

    /// <summary>True if the spawn handler got past every synchronous guard for this account this
    /// round (the `_checkedThisRound` mark). This is the ONLY observable that distinguishes "the
    /// Silent/RunLevel guards returned early" from "the pipeline ran but the once-ever claim was
    /// already burned" — the pool-connected account's claim is legitimately burned at pool setup,
    /// so pending-count assertions on Silent suppression pass for the WRONG reason without this.
    /// (Watched happen: the Silent-guard deletion mutation survived the pending-count assertion.)</summary>
    internal bool WasCheckedThisRoundForTests(Guid account) => _checkedThisRound.Contains(account);

    /// <summary>Runs the eligibility/claim pipeline for an arbitrary account guid without a real
    /// spawn event. Exists because the pooled connected account's once-ever claim is legitimately
    /// burned by pool-setup's own real spawn — deconfounded eligibility assertions need synthetic
    /// accounts, and scheduling is guid-keyed (fire-time session resolution silently drops guids
    /// with no live session, which is exactly what the suppression tests assert around).</summary>
    internal void LoadPromptForTests(Guid account) => LoadPrompt(account);
}
