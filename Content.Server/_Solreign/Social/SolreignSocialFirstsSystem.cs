using System;
using System.Collections.Generic;
using Content.Server._Solreign.Notifications;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Social;

/// <summary>
///     "Social-first" milestone toasts (council memo docs/council/2026-07-16-design-magnetism.md,
///     item 5 — Chen's kindness-by-verb-design adds): once per account EVER, PROVIDENCE notices and
///     celebrates the first time the station is social TO you or WITH you:
///       * <c>chirp_answered</c> — you chirped (the SolreignChirp greet emote, or the species Chirp)
///         and another player chirped back within the answer window, nearby;
///       * <c>healed_by_another</c> — another player's heal restored some of your damage;
///       * <c>item_received</c> — an item released from another player's hands landed in yours.
///
///     Cosmetic flavor only, additive-only: no Standing, HR Points, or any ledger value moves (the
///     milestone popup deliberately carries no number — see <see cref="SolreignAwardPopup.ShowMilestone"/>).
///     Detection is pure observation on events every one of these systems already raises; nothing
///     upstream is edited.
///
///     Once-ever lives in the <c>social_firsts</c> table (write-before-dispatch, the first_death
///     idiom): the atomic claim is taken FIRST, the toast dispatches only on the winning claim —
///     a racing double-detect can never double-toast, and a disconnect between claim and delivery
///     swallows the toast rather than duplicating it. A process-lifetime (account, flag) cache
///     (<see cref="_known"/>) keeps the steady state cheap: after the first resolution per account
///     the DB is never asked again (bounded: accounts x 3 flags).
///
///     Identity resolution is mind-based (<see cref="SharedMindSystem.TryGetMind"/> → UserId), the
///     exact resolution ProvidenceFirstDeathSystem and the EarlyDeath tracker use, so every
///     account-keyed channel agrees on identity.
///
///     The SolreignChirp emote is deliberately SILENT (text/popup only): the lane's audio rail
///     allows only existing CC0 _Solreign sounds or none, and the _Solreign audio pack contains no
///     CC0 chirp-shaped one-shot (it is in-house CC-BY-SA music loops and announcer voice lines),
///     while the vanilla nymph_chirp.ogg is CC-BY-SA ParadiseSS13 — neither qualifies, so: none.
///
///     Everything is behind <see cref="CCVars.SolreignSocialCheapAdds"/> (default on — inert
///     without other players, which is honest at pop 1). Claims are gated at claim time to active
///     rounds (the first-death F1 rule): a post-round-brawl heal must not burn the once-ever flag
///     on a toast that could never deliver.
/// </summary>
public sealed partial class SolreignSocialFirstsSystem : EntitySystem
{
    /// <summary>The Solreign greet emote (Resources/Prototypes/_Solreign/emotes.yml).</summary>
    private const string SolreignChirpEmoteId = "SolreignChirp";

    /// <summary>The vanilla species chirp (nymph/diona) — a chirp is a chirp; kinship counts.</summary>
    private const string VanillaChirpEmoteId = "Chirp";

    /// <summary>How long a chirp stays answerable. Generous enough for a newcomer to find the
    /// emotes menu again, short enough that a reply is plausibly a reply.</summary>
    private static readonly TimeSpan ChirpAnswerWindow = TimeSpan.FromSeconds(15);

    /// <summary>Answer proximity — the standard chat voice range: you answer a chirp you could
    /// actually have "heard".</summary>
    private const float ChirpAnswerRange = 10f;

    /// <summary>How long after leaving one player's hands an item still counts as "handed" when it
    /// lands in another's — covers drop-and-pickup, a throw, and the strip-menu placement.</summary>
    private static readonly TimeSpan HandOffWindow = TimeSpan.FromSeconds(20);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>Loc key pair per flag: the milestone name (popup) and the chat mirror line.</summary>
    private static readonly Dictionary<string, (string ReasonKey, string ChatKey)> Copy = new()
    {
        [SolreignSocialFirstFlags.ChirpAnswered] =
            ("solreign-social-first-chirp-answered-reason", "solreign-social-first-chirp-answered-chat"),
        [SolreignSocialFirstFlags.HealedByAnother] =
            ("solreign-social-first-healed-reason", "solreign-social-first-healed-chat"),
        [SolreignSocialFirstFlags.ItemReceived] =
            ("solreign-social-first-item-received-reason", "solreign-social-first-item-received-chat"),
    };

    private readonly ChirpAnswerTracker _chirps = new(ChirpAnswerWindow, ChirpAnswerRange);
    private readonly HandOffTracker _handOffs = new(HandOffWindow);

    /// <summary>
    ///     Process-lifetime (account, flag) pairs known claimed OR currently in flight — marked
    ///     BEFORE the first await (the Welcome "mark first" idiom) so a racing double-detect never
    ///     double-dispatches the async claim. Only an exceptional claim failure un-marks (a lost
    ///     race or an already-claimed row is a durable "done").
    /// </summary>
    private readonly HashSet<(Guid Account, string Flag)> _known = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignSocialCheapAdds"/>.</summary>
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignSocialCheapAdds, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        // Directed pairs, all previously unclaimed (the event bus throws at Initialize on a
        // collision — server boot is the proof): humanoid-profile for the emote + damage surfaces
        // (crew bodies only — a chirping mothroach ghost role is delightful but not a milestone),
        // hands for the transfer surface.
        SubscribeLocalEvent<HumanoidProfileComponent, EmoteEvent>(OnEmote);
        SubscribeLocalEvent<HumanoidProfileComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<HandsComponent, DidEquipHandEvent>(OnDidEquipHand);
        SubscribeLocalEvent<HandsComponent, DidUnequipHandEvent>(OnDidUnequipHand);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _chirps.Clear();
        _handOffs.Clear();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _chirps.Clear();
        _handOffs.Clear();
    }

    // --- Chirp (greet verb + answered milestone) ---------------------------------------------------

    private void OnEmote(Entity<HumanoidProfileComponent> ent, ref EmoteEvent args)
    {
        if (!_enabled)
            return;

        if (args.Emote.ID != SolreignChirpEmoteId && args.Emote.ID != VanillaChirpEmoteId)
            return;

        // No audio: see the class doc — the audio rail allows only existing CC0 _Solreign sounds
        // or none, and none qualifies. The emote is its text/visual.

        // Milestone tracking is claim-gated to active rounds (see class doc).
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (!_mind.TryGetMind(ent, out _, out var mind) || mind.UserId is not { } user)
            return;

        var xform = Transform(ent);
        var answered = _chirps.RecordChirp(user.UserId, xform.MapID, _xform.GetWorldPosition(ent), _timing.CurTime);

        foreach (var chirper in answered)
        {
            TryAward(chirper, SolreignSocialFirstFlags.ChirpAnswered);
        }
    }

    // --- Healed by another -------------------------------------------------------------------------

    private void OnDamageChanged(Entity<HumanoidProfileComponent> ent, ref DamageChangedEvent args)
    {
        if (!_enabled)
            return;

        // A heal, specifically: a real negative delta (direct SetDamage carries no delta and is
        // not a social act), performed by someone.
        if (args.DamageIncreased || args.DamageDelta is not { } delta || delta.GetTotal() >= 0)
            return;

        if (args.Origin is not { } origin || origin == ent.Owner)
            return;

        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        // Both sides must be real accounts, and DIFFERENT accounts — a medibot's kindness is
        // appreciated but scripted, and healing yourself is hygiene, not society.
        if (!_mind.TryGetMind(ent, out _, out var targetMind) || targetMind.UserId is not { } target)
            return;

        if (!_mind.TryGetMind(origin, out _, out var healerMind) || healerMind.UserId is not { } healer)
            return;

        if (target == healer)
            return;

        TryAward(target.UserId, SolreignSocialFirstFlags.HealedByAnother);
    }

    // --- Item handed to you ------------------------------------------------------------------------

    private void OnDidUnequipHand(Entity<HandsComponent> ent, ref DidUnequipHandEvent args)
    {
        if (!_enabled || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (!_mind.TryGetMind(ent, out _, out var mind) || mind.UserId is not { } user)
            return;

        _handOffs.RecordRelease(args.Unequipped, user.UserId, _timing.CurTime);
    }

    private void OnDidEquipHand(Entity<HandsComponent> ent, ref DidEquipHandEvent args)
    {
        if (!_enabled || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (!_mind.TryGetMind(ent, out _, out var mind) || mind.UserId is not { } user)
            return;

        if (_handOffs.TryTakeGiver(args.Equipped, user.UserId, _timing.CurTime, out _))
            TryAward(user.UserId, SolreignSocialFirstFlags.ItemReceived);
    }

    // --- Claim + delivery --------------------------------------------------------------------------

    /// <summary>
    ///     One milestone attempt: cheap in-memory dedupe first (marked before the await — the
    ///     "mark first" idiom), then the atomic claim, then delivery only on the winning claim.
    /// </summary>
    private void TryAward(Guid account, string flag)
    {
        if (!_known.Add((account, flag)))
            return;

        ClaimAndDeliver(account, flag, _ticker.RoundId);
    }

    /// <summary>
    ///     The one async hop (house threading contract: async void + try/catch, continuations
    ///     marshal back to the game thread). A storage failure un-marks the in-memory dedupe (the
    ///     milestone stays winnable later) and costs one toast, never a crash.
    /// </summary>
    private async void ClaimAndDeliver(Guid account, string flag, int roundId)
    {
        try
        {
            var claimed = await _ledger.TryClaimSocialFirstAsync(account, flag, roundId);
            if (!claimed)
                return;

            Deliver(account, flag);
        }
        catch (Exception e)
        {
            _known.Remove((account, flag));
            Log.Error($"Error while claiming social first '{flag}' for {account}:\n{e}");
        }
    }

    /// <summary>
    ///     Fires (or silently drops) one won toast — main thread, full Welcome-style re-resolution
    ///     guard chain (never trust anything captured before the await). The claim row is already
    ///     written: a dropped delivery is swallowed forever, never duplicated (write-before-dispatch).
    /// </summary>
    private void Deliver(Guid account, string flag)
    {
        if (!_enabled || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (!Copy.TryGetValue(flag, out var copy))
            return;

        if (!_players.TryGetSessionById(new NetUserId(account), out var session))
            return;

        var chatLine = Loc.GetString(copy.ChatKey);

        if (session.AttachedEntity is { } target
            && !Deleted(target)
            && !HasComp<GhostComponent>(target)
            && (!TryComp<MobStateComponent>(target, out var mobState) || mobState.CurrentState != MobState.Dead))
        {
            SolreignAwardPopup.ShowMilestone(_popup, target, Loc.GetString(copy.ReasonKey));
        }

        // Chat mirror always sends while the session lives — survives popup-blindness and can be
        // screenshotted from history (the rehire-beat rationale).
        _chatManager.DispatchServerMessage(session, chatLine);
    }

    // --- Test seams (the ProvidenceWelcomeSystem idiom) --------------------------------------------

    /// <summary>Pending chirps / in-flight releases — integration-test visibility only.</summary>
    internal int PendingChirpCountForTests => _chirps.PendingCount;
    internal int PendingHandOffCountForTests => _handOffs.PendingCount;

    /// <summary>Drives one milestone attempt exactly as a real detection would (dedupe → claim →
    /// deliver) — integration-test only, for the positive claim+delivery path a single pooled
    /// client cannot reach socially (every real detector needs a second account).</summary>
    internal void TryAwardForTests(Guid account, string flag) => TryAward(account, flag);

    /// <summary>
    ///     Clears round-scoped trackers AND the process-lifetime dedupe cache. Pooled integration
    ///     servers persist across tests, so every test must start from the clean slate a genuinely
    ///     fresh process+round would have (the Welcome rationale, extended to the process cache —
    ///     the ledger DB underneath is per-test-temp already).
    /// </summary>
    internal void ResetStateForTests()
    {
        _chirps.Clear();
        _handOffs.Clear();
        _known.Clear();
    }
}
