#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PublicEnvelopeV1 = Content.Server._Solreign.PublicExport.PublicEnvelopeV1;
using PublicExportContractException = Content.Server._Solreign.PublicExport.PublicExportContractException;
using PublicExportSigner = Content.Server._Solreign.PublicExport.PublicExportSigner;
using PublicExportValidation = Content.Server._Solreign.PublicExport.PublicExportValidation;
using PublicExportValidationCode = Content.Server._Solreign.PublicExport.PublicExportValidationCode;
using ServerSnapshotV1 = Content.Server._Solreign.PublicExport.ServerSnapshotV1;

namespace Content.Tests._Solreign;

/// <summary>
/// FINDING 1 (P1): every PublicExportValidation regex previously used ^...$ anchors. In .NET,
/// $ (and $ inside a non-Multiline pattern) matches immediately before a single trailing '\n' as
/// well as at the true end of the string, so a value like "game-host-primary\n" passed identifier
/// validation and then was string.Join('\n', ...)-framed straight into the signing input, creating
/// verifier framing ambiguity. The fix replaces every anchor with \A...\z, which only ever matches
/// the true string boundaries. These tests are RED against the ^...$ implementation and GREEN once
/// PublicExportValidation.cs is switched to \A...\z.
/// </summary>
[TestFixture]
internal sealed class PublicExportValidationAnchorTests
{
    // Every private static Regex field declared on PublicExportValidation must be listed here with
    // a sample value that satisfies the pattern *without* a trailing line feed. This list itself is
    // asserted complete (see EveryDeclaredRegexFieldHasAMappedSample), so a future regex field added
    // to the validator without a corresponding entry here fails loudly instead of shipping unchecked.
    private static readonly (string FieldName, string ValidSample)[] RegexSamples =
    {
        ("MessageIdPattern", "018f3f7e-9d2a-7c11-8a22-7f4a9a2c0101"),
        ("PublisherIdPattern", "game-host-primary"),
        ("StreamIdPattern", "public-status-2026-07"),
        ("KeyIdPattern", "publisher-2026-q3"),
        ("BuildShaPattern", "0123456789abcdef0123456789abcdef01234567"),
        ("MapIdPattern", "SolreignOasis"),
        ("SeasonIdPattern", "season-1"),
        ("NextEventIdPattern", "event-storm"),
    };

    [Test]
    public void EveryDeclaredRegexFieldHasAMappedSample()
    {
        var declaredNames = GetRegexFields().Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal);
        var mappedNames = RegexSamples.Select(sample => sample.FieldName).OrderBy(name => name, StringComparer.Ordinal);

        Assert.That(declaredNames, Is.EqualTo(mappedNames),
            "Every private static Regex field on PublicExportValidation must have a terminal-LF coverage sample.");
    }

    [TestCase("MessageIdPattern")]
    [TestCase("PublisherIdPattern")]
    [TestCase("StreamIdPattern")]
    [TestCase("KeyIdPattern")]
    [TestCase("BuildShaPattern")]
    [TestCase("MapIdPattern")]
    [TestCase("SeasonIdPattern")]
    [TestCase("NextEventIdPattern")]
    public void RegexUsesTrueStringAnchorsNotLineAnchors(string fieldName)
    {
        var pattern = GetRegex(fieldName).ToString();

        Assert.Multiple(() =>
        {
            Assert.That(pattern, Does.Not.StartWith("^"), $"{fieldName} must not anchor with the line-start '^'.");
            Assert.That(pattern, Does.Not.EndWith("$"), $"{fieldName} must not anchor with the line-end '$'.");
            Assert.That(pattern, Does.StartWith(@"\A"), $"{fieldName} must anchor to the true string start with \\A.");
            Assert.That(pattern, Does.EndWith(@"\z"), $"{fieldName} must anchor to the true string end with \\z.");
        });
    }

    [TestCase("MessageIdPattern", "018f3f7e-9d2a-7c11-8a22-7f4a9a2c0101")]
    [TestCase("PublisherIdPattern", "game-host-primary")]
    [TestCase("StreamIdPattern", "public-status-2026-07")]
    [TestCase("KeyIdPattern", "publisher-2026-q3")]
    [TestCase("BuildShaPattern", "0123456789abcdef0123456789abcdef01234567")]
    [TestCase("MapIdPattern", "SolreignOasis")]
    [TestCase("SeasonIdPattern", "season-1")]
    [TestCase("NextEventIdPattern", "event-storm")]
    public void RegexRejectsSampleWithTerminalLineFeed(string fieldName, string validSample)
    {
        var regex = GetRegex(fieldName);

        Assert.That(regex.IsMatch(validSample), Is.True, $"{fieldName} baseline sample must already be valid.");
        Assert.That(regex.IsMatch(validSample + "\n"), Is.False,
            $"{fieldName} must reject its own valid sample once a terminal line feed is appended.");
    }

    [TestCase("publisher_id", PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("stream_id", PublicExportValidationCode.InvalidStreamId)]
    [TestCase("key_id", PublicExportValidationCode.InvalidKeyId)]
    [TestCase("build_sha", PublicExportValidationCode.InvalidBuildSha)]
    [TestCase("map_id", PublicExportValidationCode.InvalidMapId)]
    [TestCase("season_id", PublicExportValidationCode.InvalidSeasonId)]
    [TestCase("next_event_id", PublicExportValidationCode.InvalidNextEvent)]
    public void EnvelopeRejectsIdentifierWithTerminalLineFeed(string field, PublicExportValidationCode expected)
    {
        var valid = ValidEnvelope();
        var envelope = field switch
        {
            "publisher_id" => valid with { PublisherId = valid.PublisherId + "\n" },
            "stream_id" => valid with { StreamId = valid.StreamId + "\n" },
            "key_id" => valid with { KeyId = valid.KeyId + "\n" },
            "build_sha" => valid with { Payload = valid.Payload with { BuildSha = valid.Payload.BuildSha + "\n" } },
            "map_id" => valid with { Payload = valid.Payload with { MapId = valid.Payload.MapId + "\n" } },
            "season_id" => valid with { Payload = valid.Payload with { SeasonId = valid.Payload.SeasonId + "\n" } },
            "next_event_id" => valid with
            {
                Payload = valid.Payload with
                {
                    NextEventId = "event-next\n",
                    NextEventAt = valid.OccurredAt.AddMinutes(10),
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [Test]
    public void SigningRejectsPublisherIdentifierWithTerminalLineFeedBeforeReachingSigningInput()
    {
        var valid = ValidEnvelope();
        var taintedPublisherId = valid.PublisherId + "\n";
        var envelope = valid with
        {
            PublisherId = taintedPublisherId,
            IdempotencyKey = FormattableString.Invariant(
                $"status:{taintedPublisherId}:{valid.StreamId}:{valid.Sequence}"),
        };

        var exception = Assert.Throws<PublicExportContractException>(
            () => PublicExportSigner.Sign(envelope, new byte[32]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(PublicExportValidationCode.InvalidPublisherId));
            Assert.That(exception.Message, Does.Not.Contain("\n"));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    private static Regex GetRegex(string fieldName)
    {
        var field = typeof(PublicExportValidation).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Expected a private static field named {fieldName} on PublicExportValidation.");
        return (Regex) field.GetValue(null)!;
    }

    private static FieldInfo[] GetRegexFields()
    {
        return typeof(PublicExportValidation)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Regex))
            .ToArray();
    }

    private static PublicEnvelopeV1 ValidEnvelope()
    {
        return new PublicEnvelopeV1(
            "1.0",
            "ServerSnapshotV1",
            Guid.ParseExact("018f3f7e-9d2a-7c11-8a22-7f4a9a2c0101", "D"),
            "game-host-primary",
            "public-status-2026-07",
            42,
            ParseTimestamp("2026-07-15T02:30:00Z"),
            ParseTimestamp("2026-07-15T02:30:05Z"),
            "publisher-2026-q3",
            "status:game-host-primary:public-status-2026-07:42",
            new ServerSnapshotV1(
                "0123456789abcdef0123456789abcdef01234567",
                "SolreignOasis",
                24,
                80,
                Content.Server._Solreign.PublicExport.PublicRoundPhase.InRound,
                "season-1",
                ParseTimestamp("2026-07-15T02:30:00Z"),
                ParseTimestamp("2026-07-15T02:31:30Z")));
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
