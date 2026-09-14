using System.Linq;
using Robust.Shared.Network;

namespace Content.Server._Solreign.PlayerDelight.Wingmates;

public enum WingmateStatus : byte { Idle, Seeking, OfferPending, Paired, Paused, Dissolved, Expired }
public enum WingmateTeachingMode : byte { Tour, LearnByDoing, ShadowMe }
public readonly record struct WingmateTransitionResult
{
    public bool Changed { get; }
    public string Reason { get; }
    public Guid? OfferNonce { get; }
    public IReadOnlyList<NetUserId> AffectedUsers { get; }

    public WingmateTransitionResult(bool changed, string reason, Guid? offerNonce = null,
        IReadOnlyList<NetUserId>? affectedUsers = null)
    {
        Changed = changed;
        Reason = reason;
        OfferNonce = offerNonce;
        AffectedUsers = affectedUsers ?? Array.Empty<NetUserId>();
    }
}
public readonly record struct WingmateOffer(NetUserId Guide, NetUserId Requester, Guid Nonce, TimeSpan ExpiresAt);
public readonly record struct WingmateRequestSnapshot(NetUserId Requester, Guid RequesterToken,
    string Department, WingmateTeachingMode TeachingMode);

/// <summary>
/// Requester-owned request details, independent of any particular viewer's token for it. Returned by
/// <see cref="WingmateRoundState.GetRequestDetails"/>, which — unlike <see cref="WingmateRequestSnapshot"/>
/// handed to a specific guide — is never addressed by a viewer-bound capability token.
/// </summary>
public readonly record struct WingmateRequestDetails(NetUserId Requester,
    string Department, WingmateTeachingMode TeachingMode);

public sealed class WingmateRoundState
{
    private readonly TimeSpan _offerLifetime;
    private readonly TimeSpan _declineCooldown;
    private readonly Dictionary<NetUserId, RequestDetails> _requests = new();
    private readonly Dictionary<NetUserId, WingmateOffer> _offers = new();
    private readonly Dictionary<NetUserId, NetUserId> _outgoing = new();
    private readonly Dictionary<NetUserId, NetUserId> _partners = new();
    private readonly Dictionary<NetUserId, WingmateStatus> _statuses = new();
    private readonly HashSet<(NetUserId, NetUserId)> _blocks = new();
    private readonly Dictionary<(NetUserId Guide, NetUserId Requester), TimeSpan> _declineCooldowns = new();

    // Requester tokens are opaque, unpredictable, and viewer-bound: a token minted for one viewer to
    // address a given requester never resolves for any other viewer, so a captured/leaked token cannot
    // be replayed by a different guide account. Sequential ints were guessable/enumerable; random Guids
    // scoped per (viewer, requester) close that off.
    private readonly Dictionary<(NetUserId Viewer, NetUserId Requester), Guid> _tokensByViewerAndRequester = new();
    private readonly Dictionary<(NetUserId Viewer, Guid Token), NetUserId> _requestersByViewerAndToken = new();

    public WingmateRoundState(TimeSpan offerLifetime, TimeSpan? declineCooldown = null)
    {
        _offerLifetime = offerLifetime;
        _declineCooldown = declineCooldown ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Monotonically increasing counter bumped every <see cref="Clear"/> (round restart or feature
    /// disable). Lets a caller that kicked off async work — e.g. a durable-block SQLite write — stamp
    /// the round it started in, then detect at completion time whether that round is still current. A
    /// completion whose captured epoch no longer matches this value originated in a round that has
    /// since ended and must not be applied as if it were current-round activity.
    /// </summary>
    public int RoundEpoch { get; private set; }

    public WingmateTransitionResult Request(NetUserId requester, string department, WingmateTeachingMode mode, TimeSpan now)
    {
        var details = new RequestDetails(department, mode);
        if (_requests.TryGetValue(requester, out var existing) && existing == details &&
            GetStatus(requester) is WingmateStatus.Seeking or WingmateStatus.OfferPending)
            return new(false, "requested");
        if (_outgoing.ContainsKey(requester) || _offers.ContainsKey(requester))
            return new(false, "role-conflict");
        if (_partners.ContainsKey(requester))
            return new(false, "already-paired");
        RemoveIncoming(requester, WingmateStatus.Idle);
        _requests[requester] = details;
        _statuses[requester] = WingmateStatus.Seeking;
        return new(true, "requested", affectedUsers: new[] { requester });
    }

    public WingmateTransitionResult Offer(NetUserId guide, NetUserId requester, TimeSpan now)
    {
        if (guide == requester) return new(false, "self-offer");
        if (_blocks.Contains((guide, requester))) return new(false, "blocked");
        if (_declineCooldowns.TryGetValue((guide, requester), out var cooldownUntil) && cooldownUntil > now)
            return new(false, "decline-cooldown");
        if (_requests.ContainsKey(guide) || _offers.ContainsKey(guide)) return new(false, "role-conflict");
        if (_outgoing.ContainsKey(requester)) return new(false, "role-conflict");
        if (!_requests.ContainsKey(requester) || GetStatus(requester) is not (WingmateStatus.Seeking or WingmateStatus.OfferPending))
            return new(false, "not-seeking");
        if (_partners.ContainsKey(guide)) return new(false, "already-paired");
        var affected = new HashSet<NetUserId> { guide, requester };
        if (_offers.TryGetValue(requester, out var displaced))
            affected.Add(displaced.Guide);
        if (_outgoing.TryGetValue(guide, out var previousRequester))
            affected.Add(previousRequester);
        RemoveOutgoing(guide);
        RemoveIncoming(requester, WingmateStatus.Idle);
        var offer = new WingmateOffer(guide, requester, Guid.NewGuid(), now + _offerLifetime);
        _offers[requester] = offer;
        _outgoing[guide] = requester;
        _statuses[requester] = WingmateStatus.OfferPending;
        _statuses[guide] = WingmateStatus.OfferPending;
        return new(true, "offered", offer.Nonce, affected.ToArray());
    }

    public WingmateTransitionResult Accept(NetUserId requester, Guid nonce, TimeSpan now)
    {
        Expire(now);
        if (!_offers.TryGetValue(requester, out var offer) || offer.Nonce != nonce) return new(false, "stale-offer");

        // Pairing is exclusive. Clear every pending role held by either participant before pairing.
        RemoveOutgoing(requester);
        RemoveIncoming(requester, WingmateStatus.Idle);
        RemoveOutgoing(offer.Guide);
        RemoveIncoming(offer.Guide, WingmateStatus.Idle);
        _requests.Remove(requester);
        _requests.Remove(offer.Guide);
        RemoveRequesterTokens(requester);
        RemoveRequesterTokens(offer.Guide);
        _partners[requester] = offer.Guide;
        _partners[offer.Guide] = requester;
        _statuses[requester] = WingmateStatus.Paired;
        _statuses[offer.Guide] = WingmateStatus.Paired;
        return new(true, "paired", affectedUsers: new[] { requester, offer.Guide });
    }

    public WingmateTransitionResult Decline(NetUserId requester, Guid nonce, TimeSpan now)
    {
        Expire(now);
        if (!_offers.TryGetValue(requester, out var offer) || offer.Nonce != nonce) return new(false, "stale-offer");
        _offers.Remove(requester);
        _outgoing.Remove(offer.Guide);
        _statuses[requester] = WingmateStatus.Seeking;
        _statuses[offer.Guide] = WingmateStatus.Idle;
        _declineCooldowns[(offer.Guide, requester)] = now + _declineCooldown;
        return new(true, "declined", affectedUsers: new[] { requester, offer.Guide });
    }

    public WingmateTransitionResult Dissolve(NetUserId actor)
    {
        if (!_partners.TryGetValue(actor, out var partner)) return new(false, "not-paired");
        _partners.Remove(actor);
        _partners.Remove(partner);
        _statuses[actor] = WingmateStatus.Dissolved;
        _statuses[partner] = WingmateStatus.Dissolved;
        return new(true, "dissolved", affectedUsers: new[] { actor, partner });
    }

    public WingmateTransitionResult Pause(NetUserId actor)
    {
        if (!_partners.TryGetValue(actor, out var partner)) return new(false, "not-paired");
        if (GetStatus(actor) == WingmateStatus.Paused && GetStatus(partner) == WingmateStatus.Paused)
            return new(false, "paused");
        _statuses[actor] = WingmateStatus.Paused;
        _statuses[partner] = WingmateStatus.Paused;
        return new(true, "paused", affectedUsers: new[] { actor, partner });
    }

    public WingmateTransitionResult Resume(NetUserId actor)
    {
        if (!_partners.TryGetValue(actor, out var partner)) return new(false, "not-paired");
        if (GetStatus(actor) != WingmateStatus.Paused || GetStatus(partner) != WingmateStatus.Paused)
            return new(false, "not-paused");
        _statuses[actor] = WingmateStatus.Paired;
        _statuses[partner] = WingmateStatus.Paired;
        return new(true, "resumed", affectedUsers: new[] { actor, partner });
    }

    public WingmateTransitionResult BlockForRound(NetUserId actor, NetUserId other)
    {
        if (actor == other) return new(false, "self-block");
        var changed = _blocks.Add((actor, other));
        _blocks.Add((other, actor));
        if (_partners.GetValueOrDefault(actor) == other) { Dissolve(actor); changed = true; }
        RemoveBetween(actor, other);
        return new(changed, "blocked", affectedUsers: changed ? new[] { actor, other } : Array.Empty<NetUserId>());
    }

    public IReadOnlyCollection<NetUserId> Expire(TimeSpan now)
    {
        var affected = new HashSet<NetUserId>();
        foreach (var (requester, offer) in _offers.ToArray())
        {
            if (offer.ExpiresAt > now) continue;
            _offers.Remove(requester);
            _outgoing.Remove(offer.Guide);
            _statuses[requester] = WingmateStatus.Seeking;
            _statuses[offer.Guide] = WingmateStatus.Expired;
            affected.Add(requester);
            affected.Add(offer.Guide);
        }
        return affected;
    }

    public void Clear()
    {
        _requests.Clear(); _offers.Clear(); _outgoing.Clear(); _partners.Clear(); _statuses.Clear(); _blocks.Clear();
        _declineCooldowns.Clear();
        _tokensByViewerAndRequester.Clear();
        _requestersByViewerAndToken.Clear();
        RoundEpoch++;
    }

    public WingmateStatus GetStatus(NetUserId user) => _statuses.GetValueOrDefault(user, WingmateStatus.Idle);
    public NetUserId? GetPartner(NetUserId user) => _partners.TryGetValue(user, out var partner) ? partner : null;
    public WingmateOffer? GetIncomingOffer(NetUserId requester) => _offers.TryGetValue(requester, out var offer) ? offer : null;
    public WingmateRequestDetails? GetRequestDetails(NetUserId requester) =>
        _requests.TryGetValue(requester, out var details)
            ? new WingmateRequestDetails(requester, details.Department, details.Mode)
            : null;

    /// <summary>
    /// Resolves an opaque requester token to the requester it addresses, but only when it was the
    /// token minted for this specific <paramref name="viewer"/>. A token captured by one guide can
    /// never be replayed by another — this is the enforcement point for that guarantee.
    /// </summary>
    public bool TryResolveRequesterToken(NetUserId viewer, Guid token, out NetUserId requester) =>
        _requestersByViewerAndToken.TryGetValue((viewer, token), out requester) &&
        _requests.ContainsKey(requester);

    public IReadOnlyList<WingmateRequestSnapshot> GetSeekingRequests(NetUserId viewer) => _requests
        .Where(entry => GetStatus(entry.Key) == WingmateStatus.Seeking)
        .Where(entry => !_blocks.Contains((viewer, entry.Key)))
        .Select(entry => new WingmateRequestSnapshot(
            entry.Key,
            GetOrCreateRequesterToken(viewer, entry.Key),
            entry.Value.Department, entry.Value.Mode))
        .ToArray();

    public WingmateTransitionResult WithdrawOutgoingOffer(NetUserId guide)
    {
        if (!_outgoing.TryGetValue(guide, out var requester)) return new(false, "no-offer");
        RemoveOutgoing(guide);
        _statuses[guide] = WingmateStatus.Idle;
        return new(true, "offer-withdrawn", affectedUsers: new[] { guide, requester });
    }

    public IReadOnlyCollection<NetUserId> RemoveUser(NetUserId user)
    {
        var wasPaired = _partners.ContainsKey(user);
        var affected = new HashSet<NetUserId> { user };
        foreach (var participant in Dissolve(user).AffectedUsers)
            affected.Add(participant);
        if (_outgoing.TryGetValue(user, out var outgoingRequester))
            affected.Add(outgoingRequester);
        if (_offers.TryGetValue(user, out var incomingOffer))
            affected.Add(incomingOffer.Guide);
        RemoveOutgoing(user);
        RemoveIncoming(user, WingmateStatus.Idle);
        _requests.Remove(user);
        RemoveRequesterTokens(user);
        if (!wasPaired)
            _statuses.Remove(user);
        return affected;
    }

    private void RemoveIncoming(NetUserId requester, WingmateStatus guideStatus)
    {
        if (!_offers.Remove(requester, out var offer)) return;
        _outgoing.Remove(offer.Guide);
        _statuses[offer.Guide] = guideStatus;
    }

    private void RemoveOutgoing(NetUserId guide)
    {
        if (!_outgoing.Remove(guide, out var requester)) return;
        _offers.Remove(requester);
        _statuses[requester] = WingmateStatus.Seeking;
    }

    private void RemoveBetween(NetUserId first, NetUserId second)
    {
        if (_offers.TryGetValue(first, out var a) && a.Guide == second)
        {
            RemoveIncoming(first, WingmateStatus.Idle);
            _statuses[first] = WingmateStatus.Seeking;
        }

        if (_offers.TryGetValue(second, out var b) && b.Guide == first)
        {
            RemoveIncoming(second, WingmateStatus.Idle);
            _statuses[second] = WingmateStatus.Seeking;
        }
    }

    private Guid GetOrCreateRequesterToken(NetUserId viewer, NetUserId requester)
    {
        if (_tokensByViewerAndRequester.TryGetValue((viewer, requester), out var token))
            return token;

        // Collisions are astronomically unlikely (122-bit random Guid), but guard against the
        // pathological case rather than assume it away.
        do
        {
            token = Guid.NewGuid();
        } while (_requestersByViewerAndToken.ContainsKey((viewer, token)));

        _tokensByViewerAndRequester[(viewer, requester)] = token;
        _requestersByViewerAndToken[(viewer, token)] = requester;
        return token;
    }

    private void RemoveRequesterTokens(NetUserId requester)
    {
        foreach (var entry in _tokensByViewerAndRequester
                     .Where(entry => entry.Key.Requester == requester)
                     .ToArray())
        {
            _tokensByViewerAndRequester.Remove(entry.Key);
            _requestersByViewerAndToken.Remove((entry.Key.Viewer, entry.Value));
        }
    }

    private readonly record struct RequestDetails(string Department, WingmateTeachingMode Mode);
}

/// <summary>
/// Per-round fixed-window limiter. Rejected and malformed attempts count so validation cannot be
/// used as an unmetered request path.
/// </summary>
internal sealed class WingmateFixedWindowLimiter
{
    // Opportunistic TTL sweep cadence: every SweepInterval calls to TryConsume, prune entries whose
    // window has fully expired. Without this, unique-account hopping over the course of a round grows
    // _windows monotonically — TryConsume only ever replaces the CURRENT account's own expired entry,
    // and Clear() only runs at a round boundary, so every distinct account that ever called Request or
    // Offer leaves a permanent entry behind. A counter-gated sweep (rather than sweeping every call)
    // keeps the common-case cost at O(1) while still bounding growth.
    private const int SweepInterval = 64;

    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly Dictionary<NetUserId, Window> _windows = new();
    private int _opsSincePrune;

    public WingmateFixedWindowLimiter(int limit, TimeSpan window)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        _limit = limit;
        _window = window;
    }

    public bool TryConsume(NetUserId user, TimeSpan now)
    {
        PruneIfDue(now);

        if (!_windows.TryGetValue(user, out var window) || now - window.Start >= _window)
        {
            _windows[user] = new Window(now, 1);
            return true;
        }

        if (window.Count >= _limit)
            return false;

        _windows[user] = window with { Count = window.Count + 1 };
        return true;
    }

    /// <summary>
    /// Removes every entry whose window has fully expired relative to <paramref name="now"/>. Never
    /// touches an entry still inside its active window — that's the reconnect-resistance guarantee
    /// (a reconnect mid-window must still see its already-consumed quota), and a fully expired entry is
    /// exactly the case <see cref="TryConsume"/> already treats as "start a fresh window", so pruning it
    /// changes no observable behavior, only reclaims memory.
    /// </summary>
    private void PruneIfDue(TimeSpan now)
    {
        if (++_opsSincePrune < SweepInterval)
            return;
        _opsSincePrune = 0;

        foreach (var user in _windows
                     .Where(entry => now - entry.Value.Start >= _window)
                     .Select(entry => entry.Key)
                     .ToArray())
            _windows.Remove(user);
    }

    // Intentionally no per-user Remove(): windows are keyed by the account-stable NetUserId and must
    // survive a disconnect/reconnect within the same round (a bad actor could otherwise reset their
    // rate-limit window on demand by reconnecting). Only a round boundary clears them.
    public void Clear()
    {
        _windows.Clear();
        _opsSincePrune = 0;
    }

    internal int WindowCountForTests => _windows.Count;

    private readonly record struct Window(TimeSpan Start, int Count);
}
