#nullable enable
using Content.Server.Administration.Systems;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     UX-SIMPLE FIX 2: pure-logic coverage for <see cref="SolreignOracleSystem.DescribeOracleFailure"/>,
///     the server-side diagnostic-log formatter for a failed Oracle webhook call. Follows the same
///     established repo convention as <c>DirectorChannelCanonicalizationTests</c>/
///     <c>BountyClaimRulesTests</c>/<c>SalaryRosterPayloadTests</c>: extract the pure decision logic
///     out of the real HTTP call site and unit-test THAT, rather than faking a real HTTP transport
///     (no fake-daemon HTTP harness exists anywhere in this repo for the Director channel — see the
///     UX-SIMPLE receipt for the research trail).
///
///     Coverage matches the mid-flight daemon-hardening note folded into this fix: the daemon's
///     current contract is a signed 503 shaped
///     <c>{"error":"oracle_unavailable","reason":"missing_api_key"|"llm_error","detail":"..."}</c>,
///     but an older daemon may return a raw, contract-less 500 with no recognizable body at all —
///     both must produce a status-code-bearing log line, and NEITHER may ever surface the parsed
///     reason/detail to the player (that discipline is enforced structurally: this formatter's
///     output only ever reaches <c>Log.Warning</c>, never <see cref="SolreignOracleSystem"/>'s
///     player-facing popup loc keys).
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignOracleSystem))]
public sealed class SolreignOracleFailureTests
{
    [Test]
    public void HardenedErrorContract_IncludesReasonAndDetailInTheLogLine()
    {
        const string body = """{"error":"oracle_unavailable","reason":"missing_api_key","detail":"ValueError"}""";

        var line = SolreignOracleSystem.DescribeOracleFailure(503, body);

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("503"));
            Assert.That(line, Does.Contain("oracle_unavailable"));
            Assert.That(line, Does.Contain("missing_api_key"));
            Assert.That(line, Does.Contain("ValueError"));
        });
    }

    [Test]
    public void HardenedErrorContract_LlmErrorReasonIsAlsoCaptured()
    {
        const string body = """{"error":"oracle_unavailable","reason":"llm_error","detail":"TimeoutError"}""";

        var line = SolreignOracleSystem.DescribeOracleFailure(503, body);

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("llm_error"));
            Assert.That(line, Does.Contain("TimeoutError"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void MissingOrBlankBody_StillProducesAStatusCodeOnlyLineWithoutThrowing(string? body)
    {
        string? line = null;
        Assert.DoesNotThrow(() => line = SolreignOracleSystem.DescribeOracleFailure(500, body));

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("500"));
            Assert.That(line, Does.Not.Contain("error="));
        });
    }

    [Test]
    public void OlderDaemonRawNonJsonBody_FallsBackToStatusCodeOnlyLineWithoutThrowing()
    {
        // An older, un-hardened daemon might return plain text or HTML on a 500 — not JSON at all.
        // Simulates the exact case the mid-flight note called out: "old daemon versions return raw
        // 500s" with no recognizable contract.
        const string body = "<html><body>Internal Server Error</body></html>";

        string? line = null;
        Assert.DoesNotThrow(() => line = SolreignOracleSystem.DescribeOracleFailure(500, body));

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("500"));
            Assert.That(line, Does.Contain("older daemon"));
        });
    }

    [Test]
    public void BodyReadFailure_StillProducesAStatusCodeOnlyLineWithoutThrowing()
    {
        string? line = null;
        Assert.DoesNotThrow(() =>
            line = SolreignOracleSystem.DescribeOracleFailure(504, null, readFailureDetail: "stream closed"));

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("504"));
            Assert.That(line, Does.Contain("stream closed"));
        });
    }

    [Test]
    public void JsonBodyMissingTheErrorField_IsTreatedAsAnUnrecognizedContractNotAThrow()
    {
        // Valid JSON, but not the hardened contract shape (no "error" field) — must fall back to
        // the generic line, never throw on a null errorDto.error.
        const string body = """{"something":"else"}""";

        string? line = null;
        Assert.DoesNotThrow(() => line = SolreignOracleSystem.DescribeOracleFailure(502, body));

        Assert.That(line, Does.Contain("older daemon"));
    }

    [Test]
    public void OversizedReasonAndDetailFieldsAreTruncatedNotLoggedInFull()
    {
        // grk code review finding (untrusted daemon fields logged unbounded): a compromised or
        // simply buggy daemon must not be able to flood the server log with an arbitrarily large
        // reason/detail string — every daemon-authored field is bounded the same way every other
        // Oracle DTO field is bounded elsewhere in this file (MaxDialogueLength, etc). Sized so the
        // overall JSON body stays well under the body-level cap (still valid, parseable JSON) while
        // each individual field exceeds the FIELD-level cap — this exercises the field truncation
        // path specifically, not the separate (and separately tested) whole-body truncation path.
        var longReason = new string('a', 1000);
        var longDetail = new string('b', 1000);
        var body = $$"""{"error":"oracle_unavailable","reason":"{{longReason}}","detail":"{{longDetail}}"}""";

        var line = SolreignOracleSystem.DescribeOracleFailure(503, body);

        Assert.Multiple(() =>
        {
            Assert.That(line.Length, Is.LessThan(1000),
                "the rendered log line must never scale with an arbitrarily large daemon-supplied field");
            Assert.That(line, Does.Not.Contain(longReason));
            Assert.That(line, Does.Not.Contain(longDetail));
            Assert.That(line, Does.Contain("oracle_unavailable"),
                "the body was still valid, parseable JSON under the body-level cap — the hardened " +
                "contract's error field must still come through, just with bounded reason/detail");
        });
    }

    [Test]
    public void OversizedBodyIsTruncatedAtTheWholeBodyLevelBeforeParsing()
    {
        // Companion to the field-level test above: a body so large it exceeds the WHOLE-BODY cap
        // gets truncated before JSON parsing even starts, which will usually land mid-structure and
        // fail to parse — falling back to the generic line rather than throwing or hanging on an
        // unbounded parse.
        var hugeReason = new string('a', 10_000);
        var body = $$"""{"error":"oracle_unavailable","reason":"{{hugeReason}}","detail":"x"}""";

        string? line = null;
        Assert.DoesNotThrow(() => line = SolreignOracleSystem.DescribeOracleFailure(503, body));

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Not.Contain(hugeReason));
            Assert.That(line!.Length, Is.LessThan(1000));
        });
    }

    [Test]
    public void OversizedReadFailureDetailIsAlsoTruncated()
    {
        var hugeDetail = new string('c', 10_000);

        var line = SolreignOracleSystem.DescribeOracleFailure(504, null, readFailureDetail: hugeDetail);

        Assert.Multiple(() =>
        {
            Assert.That(line.Length, Is.LessThan(1000));
            Assert.That(line, Does.Not.Contain(hugeDetail));
        });
    }
}
