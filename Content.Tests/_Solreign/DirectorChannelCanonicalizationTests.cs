using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure canonicalization/determinism tests for <see cref="DirectorChannel"/>'s signing
///     envelope, added alongside the v2 hardening pass (Codex adversarial review
///     "AGX harden NOT-MERGE-SAFE", 2026-07-12, HIGH findings #1/#2). These do not exercise
///     HTTP, rate-limiting, or nonce consumption — just the pure string-building the HMAC signs
///     over, via the <c>InternalsVisibleTo("Content.Tests")</c> grant on Content.Server.
///
///     Codex v2 re-review (P2): tests here originally stopped at comparing canonical-envelope
///     *strings* — they would keep passing even if production signing stopped calling
///     <see cref="DirectorChannel.BuildCanonicalEnvelope"/> at all. They still have value as
///     cheap, focused determinism checks, so they remain, but the real coverage of "does signing
///     and verification actually work, end to end, through the public API" now lives in
///     <see cref="DirectorChannelSignVerifyTests"/> below, which drives
///     <see cref="DirectorChannel.BuildSignedRequest"/> and <see cref="DirectorChannel.VerifyResponse"/>
///     directly and asserts on accept/reject outcomes, not intermediate strings.
/// </summary>
[TestFixture]
[TestOf(typeof(DirectorChannel))]
public sealed class DirectorChannelCanonicalizationTests
{
    // --- CanonicalizeQuery ---

    [Test]
    public void CanonicalizeQuery_Empty_ReturnsEmpty()
    {
        Assert.That(DirectorChannel.CanonicalizeQuery(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void CanonicalizeQuery_LeadingQuestionMarkOnly_ReturnsEmpty()
    {
        Assert.That(DirectorChannel.CanonicalizeQuery("?"), Is.EqualTo(string.Empty));
    }

    [Test]
    public void CanonicalizeQuery_StripsLeadingQuestionMark()
    {
        Assert.That(DirectorChannel.CanonicalizeQuery("?id=abc"), Is.EqualTo("id=abc"));
    }

    [Test]
    public void CanonicalizeQuery_SortsParamsByKeyRegardlessOfInputOrder()
    {
        var a = DirectorChannel.CanonicalizeQuery("?zeta=1&alpha=2");
        var b = DirectorChannel.CanonicalizeQuery("?alpha=2&zeta=1");

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.EqualTo("alpha=2&zeta=1"));
    }

    [Test]
    public void CanonicalizeQuery_DifferentValues_ProduceDifferentOutput()
    {
        var a = DirectorChannel.CanonicalizeQuery("?id=abc");
        var b = DirectorChannel.CanonicalizeQuery("?id=xyz");

        Assert.That(a, Is.Not.EqualTo(b));
    }

    // --- BuildCanonicalEnvelope: determinism ---

    [Test]
    public void BuildCanonicalEnvelope_SameInputs_ProduceIdenticalEnvelope()
    {
        var a = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/oracle", "id=1", "1000", "nonceA", "{}");
        var b = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/oracle", "id=1", "1000", "nonceA", "{}");

        Assert.That(a, Is.EqualTo(b));
    }

    // --- BuildCanonicalEnvelope: every bound field must perturb the output ---

    [Test]
    public void BuildCanonicalEnvelope_DifferentMethod_ProducesDifferentEnvelope()
    {
        var get = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/oracle", string.Empty, "1000", "n", "{}");
        var post = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/oracle", string.Empty, "1000", "n", "{}");

        Assert.That(get, Is.Not.EqualTo(post));
    }

    [Test]
    public void BuildCanonicalEnvelope_DifferentPath_ProducesDifferentEnvelope()
    {
        // This is the exact HIGH #1 regression case: a bodyless GET to two different endpoints
        // in the same second, previously signed identically ("{timestamp}.{body}" with an empty
        // body signs the same regardless of which endpoint it was for).
        var oracle = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/oracle", string.Empty, "1000", "n", string.Empty);
        var perks = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/player_perks", string.Empty, "1000", "n", string.Empty);

        Assert.That(oracle, Is.Not.EqualTo(perks));
    }

    [Test]
    public void BuildCanonicalEnvelope_DifferentQuery_ProducesDifferentEnvelope()
    {
        var idOne = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/player_perks", "id=1", "1000", "n", string.Empty);
        var idTwo = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/player_perks", "id=2", "1000", "n", string.Empty);

        Assert.That(idOne, Is.Not.EqualTo(idTwo));
    }

    [Test]
    public void BuildCanonicalEnvelope_DifferentNonce_ProducesDifferentEnvelope()
    {
        var a = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "1000", "nonce-a", "{}");
        var b = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "1000", "nonce-b", "{}");

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void BuildCanonicalEnvelope_DifferentTimestamp_ProducesDifferentEnvelope()
    {
        var a = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "1000", "n", "{}");
        var b = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "2000", "n", "{}");

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void BuildCanonicalEnvelope_DifferentBody_ProducesDifferentEnvelope()
    {
        var a = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "1000", "n", "{\"a\":1}");
        var b = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "1000", "n", "{\"a\":2}");

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void BuildCanonicalEnvelope_DifferentDirection_ProducesDifferentEnvelope()
    {
        // Request and response envelopes must not collide even with otherwise-identical fields —
        // otherwise a captured request could be replayed back as if it were a valid response.
        var req = DirectorChannel.BuildCanonicalEnvelope("req", "POST", "/rivalry", string.Empty, "1000", "n", "{}");
        var resp = DirectorChannel.BuildCanonicalEnvelope("resp", "POST", "/rivalry", string.Empty, "1000", "n", "{}");

        Assert.That(req, Is.Not.EqualTo(resp));
    }

    [Test]
    public void BuildCanonicalEnvelope_MethodIsCaseNormalized()
    {
        var lower = DirectorChannel.BuildCanonicalEnvelope("req", "get", "/oracle", string.Empty, "1000", "n", "{}");
        var upper = DirectorChannel.BuildCanonicalEnvelope("req", "GET", "/oracle", string.Empty, "1000", "n", "{}");

        Assert.That(lower, Is.EqualTo(upper));
    }
}

/// <summary>
///     End-to-end sign/verify tests for <see cref="DirectorChannel"/>, added for Codex v2
///     re-review finding P2 ("perturbation tests compare canonical-envelope strings, not
///     signatures; they'd pass even if production signing stopped using the envelope").
///
///     These drive the actual public API — <see cref="DirectorChannel.BuildSignedRequest"/> to
///     get a real <see cref="DirectorChannel.SignedRequestContext"/> with a real nonce registered
///     as in-flight, then <see cref="DirectorChannel.VerifyResponse"/> to accept/reject a
///     simulated sidecar response — instead of asserting on intermediate canonical strings. The
///     "expected" signature in each test is computed independently (HMAC-SHA256 over
///     <see cref="DirectorChannel.BuildCanonicalEnvelope"/>, mirroring but not calling
///     production's private Sign()), so a test only passes if <see cref="DirectorChannel.VerifyResponse"/>
///     is actually recomputing and checking a real HMAC over the real envelope — not, say, a
///     stubbed-out or bypassed check.
/// </summary>
[TestFixture]
[TestOf(typeof(DirectorChannel))]
public sealed class DirectorChannelSignVerifyTests
{
    // Meets DirectorChannel's private MinTokenLength (16); the exact value doesn't matter beyond
    // that, since these tests call BuildSignedRequest/VerifyResponse directly rather than going
    // through TryGetReadyToken.
    private const string TestToken = "test-director-token-0123456789";

    private static string ComputeExpectedSignature(
        string token, string direction, string method, string path, string canonicalQuery, string timestamp, string nonce, string body)
    {
        var envelope = DirectorChannel.BuildCanonicalEnvelope(direction, method, path, canonicalQuery, timestamp, nonce, body);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(envelope)));
    }

    private static (DirectorChannel.SignedRequestContext Ctx, string Timestamp) NewSignedRequest(
        HttpMethod method, string url, string body = "")
    {
        var (request, ctx) = DirectorChannel.BuildSignedRequest(method, url, TestToken, body);
        request.Dispose();
        return (ctx, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
    }

    // --- Happy path: proves VerifyResponse actually accepts a correctly-signed response ---

    [Test]
    public void SignAndVerify_SameInputs_ValidResponseIsAccepted()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Post, "http://127.0.0.1:3000/oracle", "{\"a\":1}");
        const string body = "{\"dialogue\":\"hi\"}";
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.True);
    }

    // --- Perturbation: signature computed for a different field than what VerifyResponse
    // recomputes against MUST fail. Unlike the pure BuildCanonicalEnvelope tests, this exercises
    // VerifyResponse's actual recomputation-and-compare path. ---

    [Test]
    public void Verify_SignatureComputedForDifferentMethod_IsRejected()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Post, "http://127.0.0.1:3000/oracle");
        const string body = "{}";
        // Signed as if this were a GET; the real ctx (and thus VerifyResponse's recomputation) is POST.
        var sig = ComputeExpectedSignature(TestToken, "resp", "GET", ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False);
    }

    [Test]
    public void Verify_SignatureComputedForDifferentPath_IsRejected()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/oracle");
        const string body = "{}";
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, "/player_perks", ctx.CanonicalQuery, timestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False);
    }

    [Test]
    public void Verify_SignatureComputedForDifferentQuery_IsRejected()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/player_perks?id=1");
        const string body = "{}";
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, "id=2", timestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False);
    }

    [Test]
    public void Verify_SignatureComputedForDifferentTimestamp_IsRejected()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/director/poll");
        const string body = "{}";
        var wrongTimestamp = (long.Parse(timestamp) - 5).ToString();
        // Signed for wrongTimestamp, but the timestamp header actually presented is `timestamp` —
        // VerifyResponse recomputes using the *presented* header, so this must not match.
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, wrongTimestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False);
    }

    [Test]
    public void Verify_SignatureComputedForDifferentNonce_IsRejected()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/oracle");
        const string body = "{}";
        // nonceHeader presented equals ctx.Nonce (required to pass the early binding check), but
        // the signature itself was computed as if a different nonce was in play.
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, "a-different-nonce", body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False);
    }

    [Test]
    public void Verify_SignatureComputedForDifferentBody_IsRejected()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/oracle");
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, "{\"a\":1}");

        // Presented body differs from what was signed.
        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, "{\"a\":2}", sig, timestamp, ctx.Nonce), Is.False);
    }

    // --- Nonce lifecycle ---

    [Test]
    public void Verify_UnknownNonce_IsRejected()
    {
        // Constructed directly rather than via BuildSignedRequest, so this nonce was never
        // registered as in-flight.
        var ctx = new DirectorChannel.SignedRequestContext("GET", "/oracle", string.Empty, "never-registered-" + Guid.NewGuid());
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string body = "{}";
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False);
    }

    [Test]
    public void Verify_AlreadyConsumedNonce_IsRejectedOnSecondDelivery()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/oracle");
        const string body = "{}";
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, body);

        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.True,
            "first delivery of a validly-signed response must be accepted");
        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce), Is.False,
            "a captured-and-replayed copy of the same response must be rejected the second time");
    }

    /// <summary>
    ///     Codex v2 re-review P1 regression test: the pre-fix VerifyResponse called the atomic
    ///     nonce-consuming TryRemove BEFORE validating anything else, so a single garbage/invalid
    ///     response bearing an observed-but-not-yet-answered nonce would burn it — and the
    ///     legitimate, correctly-signed response for that same request would then be rejected as
    ///     "unknown nonce" a moment later (a fail-closed DoS an attacker could trigger by merely
    ///     observing outbound request nonces). This test fails against the pre-fix ordering and
    ///     must pass against the fix (validate-then-consume).
    /// </summary>
    [Test]
    public void InvalidResponse_DoesNotBurnNonce_LegitimateResponseStillAcceptedAfterward()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/director/poll");
        const string body = "{\"events\":[]}";

        // An attacker (or a glitching sidecar) sends a bogus response first, using the observed
        // nonce but a garbage signature that will never validate.
        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, "not-a-real-signature", timestamp, ctx.Nonce), Is.False,
            "garbage signature must be rejected");

        // The real, correctly-signed response for the SAME request must still be accepted —
        // proving the invalid attempt above did not consume/burn the in-flight nonce.
        var goodSig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, body);
        Assert.That(DirectorChannel.VerifyResponse(TestToken, ctx, body, goodSig, timestamp, ctx.Nonce), Is.True,
            "legitimate response must still be accepted after a prior invalid attempt for the same nonce");
    }

    /// <summary>
    ///     The final atomic TryRemove is the sole concurrency guard once validate-then-consume
    ///     ordering is in place: if several threads race to deliver the identical, validly-signed
    ///     response for the same nonce, exactly one may win.
    /// </summary>
    [Test]
    public void ConcurrentIdenticalValidResponses_ExactlyOneIsAccepted()
    {
        var (ctx, timestamp) = NewSignedRequest(HttpMethod.Get, "http://127.0.0.1:3000/oracle");
        const string body = "{}";
        var sig = ComputeExpectedSignature(TestToken, "resp", ctx.Method, ctx.Path, ctx.CanonicalQuery, timestamp, ctx.Nonce, body);

        var results = new bool[16];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = DirectorChannel.VerifyResponse(TestToken, ctx, body, sig, timestamp, ctx.Nonce);
        });

        Assert.That(results.Count(r => r), Is.EqualTo(1));
    }
}
