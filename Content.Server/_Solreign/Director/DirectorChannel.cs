using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._Solreign.Director;

/// <summary>
///     Shared auth + kill-switch + rate-limit substrate for every AGX Director-channel
///     integration (Oracle, Rivalry, LiveMap, Perks — everything that talks to the external
///     Director sidecar at <c>127.0.0.1:3000</c>). Per AGX-MERGE-PLAN.md's "Cross-cutting
///     Director-channel auth + kill-switch harness": no outbound call may go out unsigned, no
///     inbound response may be trusted unverified, and every feature must be killable at runtime
///     without a redeploy. All consuming systems MUST route through this class instead of using
///     <see cref="HttpClient"/> against the sidecar directly.
///
///     Fail-CLOSED by construction: <see cref="IsReady"/>/<see cref="TryGetReadyToken"/> require
///     both the feature CVar AND a non-whitespace, minimum-length
///     <see cref="CCVars.SolreignDirectorToken"/>; <see cref="VerifyResponse"/> rejects anything
///     with a missing, malformed, stale, mismatched, or request-unbound signature. There is no
///     code path here that sends or accepts anything unsigned — if the token is unset (the
///     default), the channel is inert.
///
///     v2 (2026-07-12, addresses Codex adversarial review "AGX harden NOT-MERGE-SAFE"):
///     <list type="bullet">
///     <item>HIGH #1 — the HMAC now covers a canonical envelope of
///     {method, path, canonical query, timestamp, nonce, body-hash} instead of just
///     "{timestamp}.{body}", both directions. Two bodyless GET requests to different endpoints
///     (or the same endpoint with a different query) in the same second no longer produce
///     identical MACs.</item>
///     <item>HIGH #2 — every signed request carries a fresh random nonce
///     (<see cref="NonceHeader"/>) registered as "in flight"; <see cref="VerifyResponse"/> only
///     accepts a response whose echoed nonce matches the specific request it's answering AND is
///     still registered (atomically consumed on first use via
///     <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove"/>). A captured/replayed response,
///     or one substituted for a different in-flight request, is rejected even if its signature
///     and timestamp are otherwise valid.</item>
///     </list>
///
///     v3 (2026-07-12, addresses Codex v2 re-review "NOT-MERGE-SAFE" P1): <see cref="VerifyResponse"/>
///     used to call the atomic nonce-consuming <c>TryRemove</c> before timestamp freshness and
///     HMAC validation. That let an attacker who merely observed a request's nonce burn it with
///     a single garbage/invalid response -- the in-flight record was gone by the time the real,
///     correctly-signed response arrived, so the legitimate response was rejected (a fail-closed
///     DoS on the caller). The in-flight record is now only read (non-destructive
///     <c>TryGetValue</c>) while validating timestamp freshness and signature; the atomic
///     <c>TryRemove</c> "consume" happens only as the FINAL acceptance step, after the signature
///     has already been proven valid. This still gives single-accept-under-concurrency (two
///     concurrent valid responses for the same nonce: exactly one wins the final
///     <c>TryRemove</c>), but an invalid response can no longer burn a nonce out from under a
///     legitimate one.
/// </summary>
public static class DirectorChannel
{
    /// <summary>Header carrying the hex HMAC-SHA256 signature of the canonical request/response envelope.</summary>
    public const string SignatureHeader = "X-Solreign-Signature";

    /// <summary>Header carrying the unix-seconds timestamp the signature was computed over.</summary>
    public const string TimestampHeader = "X-Solreign-Timestamp";

    /// <summary>
    ///     Header carrying the per-request random nonce. The sidecar MUST echo this exact value
    ///     back on its response and include it in the response's signed canonical envelope — a
    ///     response missing it, or carrying a different one, is rejected regardless of signature
    ///     validity (binds the response to this specific request; closes the replay/substitution
    ///     gap from Codex HIGH finding #2).
    /// </summary>
    public const string NonceHeader = "X-Solreign-Nonce";

    /// <summary>
    ///     Hard cap on how many Director-supplied events are processed out of a single
    ///     poll/response batch (e.g. <c>DirectorEvent[]</c>). Applies regardless of how many the
    ///     sidecar actually sent.
    /// </summary>
    public const int MaxEventBatchSize = 20;

    /// <summary>
    ///     Minimum spacing between outbound requests on the same named channel, enforced here
    ///     independent of any individual system's own poll/update cadence — a misbehaving or
    ///     misconfigured caller cannot spam the sidecar faster than this regardless of its own
    ///     timer logic.
    /// </summary>
    public static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(750);

    /// <summary>
    ///     How old an inbound response's signed timestamp may be before it's rejected as
    ///     stale/replayed. Also used as the in-flight-nonce retention window.
    /// </summary>
    public static readonly TimeSpan MaxResponseAge = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Minimum accepted length (after trimming) for the Director shared-secret token.
    ///     Rejects blank/whitespace/near-empty "configured" tokens as equivalent to unconfigured
    ///     (Codex MEDIUM #4) — a token this short is not meaningful key material for HMAC-SHA256.
    /// </summary>
    private const int MinTokenLength = 16;

    private const int NonceByteLength = 16;

    // Rate limiting: monotonic Environment.TickCount64 milliseconds + CAS loop (Codex MEDIUM #5 —
    // the previous GetOrAdd-then-assign pair was check-then-act, not atomic, under concurrent
    // callers on the same channel).
    private static readonly ConcurrentDictionary<string, long> LastSentTicksMs = new();

    // In-flight nonce registry: nonce -> expiry (UTC ticks). A nonce is removed the moment it is
    // successfully consumed by VerifyResponse (TryRemove), so it can never be consumed twice —
    // this is what makes a captured response non-replayable even within the freshness window.
    private static readonly ConcurrentDictionary<string, long> InFlightNonces = new();

    /// <summary>
    ///     True only if <paramref name="featureEnabled"/> is on AND a well-formed Director token
    ///     is configured. Prefer <see cref="TryGetReadyToken"/> when the token value is also
    ///     needed — it performs the same check as a single snapshot instead of two separate CVar
    ///     reads (Codex MEDIUM #4).
    /// </summary>
    public static bool IsReady(IConfigurationManager config, CVarDef<bool> featureEnabled)
    {
        return TryGetReadyToken(config, featureEnabled, out _);
    }

    /// <summary>
    ///     Single-read "is this channel allowed to run right now, and if so what token do I sign
    ///     with" snapshot. Replaces the previous idiom of calling <see cref="IsReady"/> and then
    ///     separately <see cref="GetToken"/> — those were two independent CVar reads with a
    ///     window between them where config could change (Codex MEDIUM #4, TOCTOU). Also rejects
    ///     whitespace-only or implausibly short "configured" tokens outright, so a partially-set
    ///     token can never be used to sign anything.
    ///
    ///     v11 backbone #2: additionally requires the MASTER
    ///     <see cref="CCVars.SolreignDirectorEnabled"/> switch, so the whole channel — every
    ///     consumer, regardless of its per-feature CVar — is inert unless the master is on.
    ///     Feature CVars narrow; the master (plus the token) is the floor. Callers gating on the
    ///     master itself (the poll delivery loop) just pass it as <paramref name="featureEnabled"/>.
    /// </summary>
    public static bool TryGetReadyToken(IConfigurationManager config, CVarDef<bool> featureEnabled, out string token)
    {
        token = string.Empty;

        if (!config.GetCVar(CCVars.SolreignDirectorEnabled))
            return false;

        if (!config.GetCVar(featureEnabled))
            return false;

        var candidate = config.GetCVar(CCVars.SolreignDirectorToken);
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var trimmed = candidate.Trim();
        if (trimmed.Length < MinTokenLength)
            return false;

        token = candidate;
        return true;
    }

    /// <summary>Resolves the configured Director shared-secret token (empty = channel disabled).</summary>
    public static string GetToken(IConfigurationManager config)
    {
        return config.GetCVar(CCVars.SolreignDirectorToken);
    }

    /// <summary>
    ///     Resolves the configured Director daemon base URL with any trailing slash stripped, so
    ///     callers can safely append a path like <c>"/oracle"</c>. Callers build their full URL as
    ///     <c>GetBaseUrl(config) + "/path"</c> instead of hardcoding the host.
    /// </summary>
    public static string GetBaseUrl(IConfigurationManager config)
    {
        return config.GetCVar(CCVars.SolreignDirectorUrl).TrimEnd('/');
    }

    /// <summary>
    ///     Debounce/rate-limit gate. Returns false (caller MUST skip the call) if
    ///     <paramref name="channel"/> was used more recently than <see cref="MinRequestInterval"/>
    ///     ago; otherwise atomically records "now" as the last-sent time and returns true. Uses a
    ///     compare-and-swap retry loop over monotonic <see cref="Environment.TickCount64"/> so
    ///     concurrent callers on the same channel can't both observe "it's been long enough"
    ///     (Codex MEDIUM #5).
    /// </summary>
    public static bool TryEnterRateLimit(string channel)
    {
        var nowMs = Environment.TickCount64;
        var intervalMs = (long)MinRequestInterval.TotalMilliseconds;

        while (true)
        {
            var last = LastSentTicksMs.GetOrAdd(channel, 0L);
            if (nowMs - last < intervalMs)
                return false;

            if (LastSentTicksMs.TryUpdate(channel, nowMs, last))
                return true;

            // Another thread updated the same channel between our read and our write — retry
            // against the fresh value instead of both callers proceeding.
        }
    }

    /// <summary>
    ///     Context captured when a signed request is built, needed to verify the response that
    ///     answers it. Deliberately opaque/immutable to callers beyond passing it straight to
    ///     <see cref="VerifyResponse"/> — nothing about signing should be reconstructable from
    ///     caller-supplied state alone.
    /// </summary>
    public readonly record struct SignedRequestContext(string Method, string Path, string CanonicalQuery, string Nonce);

    /// <summary>
    ///     Builds an HMAC-SHA256 signed outbound request. The signature covers a canonical
    ///     envelope of method + path + canonical query + timestamp + nonce + body hash (Codex
    ///     HIGH #1) — not just timestamp+body — so the method, path, and query are authenticated
    ///     along with the body. Registers the generated nonce as "in flight" so the eventual
    ///     response can be bound back to this exact request via <see cref="VerifyResponse"/>.
    ///     Callers must already have checked <see cref="TryGetReadyToken"/> and
    ///     <see cref="TryEnterRateLimit"/> — this does not re-check either, since it needs the
    ///     already-resolved token value.
    /// </summary>
    public static (HttpRequestMessage Request, SignedRequestContext Context) BuildSignedRequest(
        HttpMethod method, string url, string token, string body)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath;
        var canonicalQuery = CanonicalizeQuery(uri.Query);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = GenerateNonce();
        var methodUpper = method.Method.ToUpperInvariant();

        var request = new HttpRequestMessage(method, url);
        if (body.Length > 0)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var canonicalRequest = BuildCanonicalEnvelope("req", methodUpper, path, canonicalQuery, timestamp, nonce, body);
        request.Headers.Add(SignatureHeader, Sign(token, canonicalRequest));
        request.Headers.Add(TimestampHeader, timestamp);
        request.Headers.Add(NonceHeader, nonce);

        RegisterInFlight(nonce);

        return (request, new SignedRequestContext(methodUpper, path, canonicalQuery, nonce));
    }

    /// <summary>
    ///     Verifies an inbound Director response carries a valid, fresh, request-bound
    ///     HMAC-SHA256 signature. Fail CLOSED: a missing header, malformed/stale timestamp,
    ///     nonce that doesn't match <paramref name="ctx"/> (or isn't/is-no-longer registered as
    ///     in-flight — i.e. unknown, already-consumed, or expired), or signature mismatch all
    ///     return false, and the caller MUST discard the response body rather than act on it.
    ///
    ///     v3 ordering (Codex v2 re-review P1): the in-flight nonce record is only *read*
    ///     (non-destructive) while timestamp freshness and the HMAC are validated. The atomic
    ///     "consume" (<see cref="ConcurrentDictionary{TKey,TValue}.TryRemove"/>) happens only as
    ///     the FINAL step, after the signature has already been proven valid — so a single
    ///     invalid/garbage response bearing an observed nonce can no longer burn that nonce ahead
    ///     of the legitimate response (previously a fail-closed DoS). The final TryRemove still
    ///     guarantees single-accept under concurrency: if two valid responses race for the same
    ///     nonce, only the one that wins the TryRemove returns true.
    /// </summary>
    public static bool VerifyResponse(
        string token,
        SignedRequestContext ctx,
        string body,
        string? signatureHeader,
        string? timestampHeader,
        string? nonceHeader)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(timestampHeader) || string.IsNullOrEmpty(nonceHeader))
            return false;

        // Bind to the exact request this ctx was created for before anything else. A
        // correctly-signed response for a *different* request must never validate here.
        if (!string.Equals(nonceHeader, ctx.Nonce, StringComparison.Ordinal))
            return false;

        // Non-destructive lookup only — do NOT consume yet. Consuming here (the pre-v3 bug)
        // let an attacker who merely observed the nonce burn it with a single invalid response,
        // causing the real signed response to be rejected as "unknown nonce" a moment later.
        if (!TryPeekInFlight(nonceHeader, out var expiryTicks))
            return false;

        if (DateTime.UtcNow.Ticks > expiryTicks)
        {
            // Registered but stale — clean it up opportunistically and reject. Not the "consume
            // on success" path, just housekeeping; falls through to the same false return either way.
            InFlightNonces.TryRemove(nonceHeader, out _);
            return false;
        }

        if (!long.TryParse(timestampHeader, out var ts))
            return false;

        DateTimeOffset tsOffset;
        try
        {
            // Codex LOW #9: attacker-controlled long can be outside DateTimeOffset's supported
            // range and throw; the documented contract of this method is "return false", not
            // "throw", on any malformed input.
            tsOffset = DateTimeOffset.FromUnixTimeSeconds(ts);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - tsOffset;
        if (age < TimeSpan.Zero || age > MaxResponseAge)
            return false;

        var canonicalResponse = BuildCanonicalEnvelope("resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestampHeader, nonceHeader, body);
        var expected = Sign(token, canonicalResponse);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signatureHeader);
        var signatureValid = expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);

        if (!signatureValid)
            return false;

        // Single-use, FINAL step: only a response that has already passed freshness + HMAC
        // validation can consume the nonce. This is also the sole concurrency guard — if two
        // differently-timed valid responses somehow raced for the same nonce, only the one that
        // wins this TryRemove is accepted; the other observes the nonce already gone.
        return TryConsumeInFlight(nonceHeader);
    }

    // --- Canonicalization helpers: pure functions, unit-tested directly
    // (Content.Tests/_Solreign/DirectorChannelCanonicalizationTests.cs) via
    // [InternalsVisibleTo("Content.Tests")] on Content.Server. ---

    /// <summary>
    ///     Builds the exact byte-for-byte string that gets HMAC-signed for either a request or a
    ///     response. Same inputs always produce the same string (determinism is what makes this
    ///     testable and what makes both sides of the channel able to independently recompute the
    ///     same MAC); any single differing field (method, path, query, timestamp, nonce, or body)
    ///     changes the output.
    /// </summary>
    internal static string BuildCanonicalEnvelope(
        string direction, string method, string path, string canonicalQuery, string timestamp, string nonce, string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        return string.Join(
            '\n',
            "solreign-director-v1",
            direction,
            method.ToUpperInvariant(),
            path,
            canonicalQuery,
            timestamp,
            nonce,
            bodyHash);
    }

    /// <summary>
    ///     Canonicalizes a URL query string (as returned by <see cref="Uri.Query"/>, i.e.
    ///     including a leading '?' or empty) into a deterministic, order-independent form:
    ///     key/value pairs sorted ordinally by key then value and rejoined with '&amp;'. Two
    ///     query strings with the same pairs in a different order canonicalize identically; two
    ///     query strings with different pairs never do.
    /// </summary>
    internal static string CanonicalizeQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
            return string.Empty;

        var trimmed = query[0] == '?' ? query[1..] : query;
        if (trimmed.Length == 0)
            return string.Empty;

        var pairs = trimmed
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var idx = p.IndexOf('=');
                return idx < 0 ? (Key: p, Value: string.Empty) : (Key: p[..idx], Value: p[(idx + 1)..]);
            })
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}");

        return string.Join('&', pairs);
    }

    private static string GenerateNonce()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(NonceByteLength));
    }

    private static void RegisterInFlight(string nonce)
    {
        PruneExpired();
        InFlightNonces[nonce] = DateTime.UtcNow.Add(MaxResponseAge).Ticks;
    }

    /// <summary>
    ///     Non-destructive "is this nonce still in flight" check used while validating a
    ///     response, BEFORE the caller is willing to commit to consuming it (Codex v2 P1). Does
    ///     not remove anything — a failed/invalid response leaves the nonce registered so the
    ///     eventual legitimate response can still be accepted.
    /// </summary>
    private static bool TryPeekInFlight(string nonce, out long expiryTicks)
    {
        return InFlightNonces.TryGetValue(nonce, out expiryTicks);
    }

    private static bool TryConsumeInFlight(string nonce)
    {
        // TryRemove is the atomic "consume" — first caller to remove it wins, every subsequent
        // attempt (replay, or a second legitimate-looking response) observes it's already gone.
        // Callers MUST have already validated freshness + signature before reaching here (Codex
        // v2 P1) — this is the final acceptance gate, not a pre-validation lookup.
        if (!InFlightNonces.TryRemove(nonce, out var expiryTicks))
            return false;

        return DateTime.UtcNow.Ticks <= expiryTicks;
    }

    private static void PruneExpired()
    {
        // Volume here is bounded by TryEnterRateLimit (<=1 request/750ms per channel, a handful
        // of channels) so this is a small, cheap sweep, not an unbounded-growth vector.
        var now = DateTime.UtcNow.Ticks;
        foreach (var (nonce, expiry) in InFlightNonces)
        {
            if (expiry < now)
                InFlightNonces.TryRemove(nonce, out _);
        }
    }

    private static string Sign(string token, string canonicalEnvelope)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalEnvelope));
        return Convert.ToHexString(hash);
    }
}
