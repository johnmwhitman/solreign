using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using PublicEnvelopeV1 = Content.Server._Solreign.PublicExport.PublicEnvelopeV1;
using PublicExportCanonicalization = Content.Server._Solreign.PublicExport.PublicExportCanonicalization;
using PublicExportContractException = Content.Server._Solreign.PublicExport.PublicExportContractException;
using PublicExportSigner = Content.Server._Solreign.PublicExport.PublicExportSigner;
using PublicExportSigningCode = Content.Server._Solreign.PublicExport.PublicExportSigningCode;
using PublicExportSigningException = Content.Server._Solreign.PublicExport.PublicExportSigningException;
using PublicExportValidationCode = Content.Server._Solreign.PublicExport.PublicExportValidationCode;
using PublicRoundPhase = Content.Server._Solreign.PublicExport.PublicRoundPhase;
using RoundElapsedBucket = Content.Server._Solreign.PublicExport.RoundElapsedBucket;
using ServerSnapshotV1 = Content.Server._Solreign.PublicExport.ServerSnapshotV1;

namespace Content.Tests._Solreign;

[TestFixture]
internal sealed class PublicExportCanonicalizationTests
{
    private const string SigningFixture = "server-snapshot-v1-signing-vector.json";

    [Test]
    public void FrozenVectorMatchesLiteralCanonicalBodyHashSigningInputAndSignature()
    {
        var fixture = LoadSigningFixture();
        var result = PublicExportSigner.Sign(fixture.Envelope, fixture.Key);

        Assert.Multiple(() =>
        {
            Assert.That(result.CanonicalBody, Is.TypeOf<ImmutableArray<byte>>());
            Assert.That(result.CanonicalBody.AsSpan().SequenceEqual(fixture.CanonicalBody), Is.True);
            Assert.That(Encoding.UTF8.GetString(result.CanonicalBody.AsSpan()), Is.EqualTo(fixture.CanonicalBodyText));
            Assert.That(result.BodySha256, Is.EqualTo(fixture.BodySha256));
            Assert.That(result.SigningInput, Is.EqualTo(fixture.SigningInput));
            Assert.That(result.Signature, Is.EqualTo(fixture.Signature));
            Assert.That(result.BodySha256, Does.Match("^[0-9a-f]{64}$"));
        });
    }

    [Test]
    public void EveryIndependentlyValidEnvelopeMutationChangesBodyHashAndSignature()
    {
        var fixture = LoadSigningFixture();
        var baseline = PublicExportSigner.Sign(fixture.Envelope, fixture.Key);

        foreach (var (name, mutate) in ValidMutations())
        {
            var mutated = mutate(fixture.Envelope);
            var result = PublicExportSigner.Sign(mutated, fixture.Key);

            Assert.Multiple(() =>
            {
                Assert.That(result.CanonicalBody, Is.Not.EqualTo(baseline.CanonicalBody), name);
                Assert.That(result.BodySha256, Is.Not.EqualTo(baseline.BodySha256), name);
                Assert.That(result.Signature, Is.Not.EqualTo(baseline.Signature), name);
            });
        }
    }

    [TestCase("schema", PublicExportValidationCode.InvalidSchemaVersion)]
    [TestCase("message_type", PublicExportValidationCode.InvalidMessageType)]
    [TestCase("idempotency_key", PublicExportValidationCode.InvalidIdempotencyKey)]
    [TestCase("generated_at", PublicExportValidationCode.GeneratedTimeMismatch)]
    [TestCase("next_event_id", PublicExportValidationCode.InvalidNextEvent)]
    [TestCase("next_event_at", PublicExportValidationCode.InvalidNextEvent)]
    public void NonIndependentTamperingIsRejectedBeforeSerialization(
        string field,
        PublicExportValidationCode expected)
    {
        var fixture = LoadSigningFixture();
        var envelope = field switch
        {
            "schema" => fixture.Envelope with { SchemaVersion = "2.0" },
            "message_type" => fixture.Envelope with { MessageType = "ServerSnapshotV2" },
            "idempotency_key" => fixture.Envelope with { IdempotencyKey = "status:tampered" },
            "generated_at" => fixture.Envelope with
            {
                Payload = fixture.Envelope.Payload with
                {
                    GeneratedAt = fixture.Envelope.Payload.GeneratedAt.AddSeconds(1),
                },
            },
            "next_event_id" => fixture.Envelope with
            {
                Payload = fixture.Envelope.Payload with { NextEventId = "event-storm" },
            },
            "next_event_at" => fixture.Envelope with
            {
                Payload = fixture.Envelope.Payload with
                {
                    NextEventAt = fixture.Envelope.Payload.GeneratedAt.AddMinutes(1),
                },
            },
            _ => throw new AssertionException("Unknown tamper case."),
        };

        var exception = Assert.Throws<PublicExportContractException>(
            () => PublicExportCanonicalization.Canonicalize(envelope));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(expected));
            Assert.That(exception.Message, Is.EqualTo(expected.ToString()));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [TestCase("publisher")]
    [TestCase("stream")]
    [TestCase("key")]
    [TestCase("season")]
    [TestCase("event")]
    public void NonAsciiPurposeIdentifiersAreRejectedBeforeSerialization(string field)
    {
        var fixture = LoadSigningFixture();
        var envelope = field switch
        {
            "publisher" => fixture.Envelope with { PublisherId = "game-host-café" },
            "stream" => fixture.Envelope with { StreamId = "public-status-café" },
            "key" => fixture.Envelope with { KeyId = "publisher-café" },
            "season" => fixture.Envelope with
            {
                Payload = fixture.Envelope.Payload with { SeasonId = "season-café" },
            },
            "event" => fixture.Envelope with
            {
                Payload = fixture.Envelope.Payload with
                {
                    NextEventId = "event-café",
                    NextEventAt = fixture.Envelope.Payload.GeneratedAt.AddMinutes(1),
                },
            },
            _ => throw new AssertionException("Unknown identifier case."),
        };

        Assert.Throws<PublicExportContractException>(() => PublicExportCanonicalization.Canonicalize(envelope));
    }

    [Test]
    public void OptionalPropertiesUseExactLexicalOrderAndEnumFormats()
    {
        var fixture = LoadSigningFixture();
        var envelope = fixture.Envelope with
        {
            Payload = fixture.Envelope.Payload with
            {
                RoundElapsedBucket = RoundElapsedBucket.NinetyPlus,
                NextEventId = "event-storm",
                NextEventAt = fixture.Envelope.Payload.GeneratedAt.AddMinutes(1),
                RoundPhase = PublicRoundPhase.PostRound,
            },
        };

        var body = Encoding.UTF8.GetString(PublicExportCanonicalization.Canonicalize(envelope).AsSpan());
        const string expectedPayload =
            "\"payload\":{\"build_sha\":\"0123456789abcdef0123456789abcdef01234567\",\"capacity\":80," +
            "\"fresh_until\":\"2026-07-15T02:31:30Z\",\"generated_at\":\"2026-07-15T02:30:00Z\"," +
            "\"map_id\":\"SolreignOasis\",\"next_event_at\":\"2026-07-15T02:31:00Z\"," +
            "\"next_event_id\":\"event-storm\",\"population\":24,\"round_elapsed_bucket\":\"90m+\"," +
            "\"round_phase\":\"post_round\",\"season_id\":\"season-1\"}";

        Assert.That(body, Does.Contain(expectedPayload));
    }

    [TestCase(PublicRoundPhase.Lobby, "lobby")]
    [TestCase(PublicRoundPhase.InRound, "in_round")]
    [TestCase(PublicRoundPhase.PostRound, "post_round")]
    [TestCase(PublicRoundPhase.Unknown, "unknown")]
    public void RoundPhaseUsesFrozenWireName(PublicRoundPhase phase, string expected)
    {
        var fixture = LoadSigningFixture();
        var envelope = fixture.Envelope with
        {
            Payload = fixture.Envelope.Payload with { RoundPhase = phase },
        };

        using var document = JsonDocument.Parse(PublicExportCanonicalization.Canonicalize(envelope).AsMemory());
        Assert.That(document.RootElement.GetProperty("payload").GetProperty("round_phase").GetString(),
            Is.EqualTo(expected));
    }

    [TestCase(RoundElapsedBucket.ZeroToFifteen, "0-15m")]
    [TestCase(RoundElapsedBucket.FifteenToThirty, "15-30m")]
    [TestCase(RoundElapsedBucket.ThirtyToSixty, "30-60m")]
    [TestCase(RoundElapsedBucket.SixtyToNinety, "60-90m")]
    [TestCase(RoundElapsedBucket.NinetyPlus, "90m+")]
    public void ElapsedBucketUsesFrozenWireName(RoundElapsedBucket bucket, string expected)
    {
        var fixture = LoadSigningFixture();
        var envelope = fixture.Envelope with
        {
            Payload = fixture.Envelope.Payload with { RoundElapsedBucket = bucket },
        };

        using var document = JsonDocument.Parse(PublicExportCanonicalization.Canonicalize(envelope).AsMemory());
        Assert.That(document.RootElement.GetProperty("payload").GetProperty("round_elapsed_bucket").GetString(),
            Is.EqualTo(expected));
    }

    [Test]
    public void ThirtyOneByteKeyFailsWithFixedCodeAndThirtyTwoByteKeySucceeds()
    {
        var fixture = LoadSigningFixture();
        var shortKey = new byte[31];
        var minimumKey = new byte[32];

        var exception = Assert.Throws<PublicExportSigningException>(
            () => PublicExportSigner.Sign(fixture.Envelope, shortKey));
        var result = PublicExportSigner.Sign(fixture.Envelope, minimumKey);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(PublicExportSigningCode.InvalidKeyMaterial));
            Assert.That(exception.Message, Is.EqualTo(nameof(PublicExportSigningCode.InvalidKeyMaterial)));
            Assert.That(exception.InnerException, Is.Null);
            Assert.That(result.Signature, Is.Not.Empty);
        });
    }

    [Test]
    public void SignatureIsUnpaddedBase64UrlAndKeyMaterialIsNotRetained()
    {
        var fixture = LoadSigningFixture();
        var result = PublicExportSigner.Sign(fixture.Envelope, fixture.Key);
        var properties = result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(result.Signature, Does.Not.Contain("="));
            Assert.That(result.Signature, Does.Not.Contain("+"));
            Assert.That(result.Signature, Does.Not.Contain("/"));
            Assert.That(properties.Select(property => property.Name),
                Is.EquivalentTo(new[] { "CanonicalBody", "BodySha256", "SigningInput", "Signature" }));
        });
    }

    [Test]
    public void ContractValidationPrecedesShortKeyRejection()
    {
        var fixture = LoadSigningFixture();
        var invalid = fixture.Envelope with { SchemaVersion = "2.0" };

        var exception = Assert.Throws<PublicExportContractException>(
            () => PublicExportSigner.Sign(invalid, new byte[31]));
        Assert.That(exception!.Code, Is.EqualTo(PublicExportValidationCode.InvalidSchemaVersion));
    }

    private static IEnumerable<(string Name, Func<PublicEnvelopeV1, PublicEnvelopeV1> Mutate)> ValidMutations()
    {
        yield return ("message_id", envelope => envelope with
        {
            MessageId = Guid.ParseExact("018f3f7e-9d2a-7c11-8a22-7f4a9a2c0102", "D"),
        });
        yield return ("publisher_id", envelope => WithIdentity(envelope, "game-host-secondary", envelope.StreamId, envelope.Sequence));
        yield return ("stream_id", envelope => WithIdentity(envelope, envelope.PublisherId, "public-status-2026-08", envelope.Sequence));
        yield return ("sequence", envelope => WithIdentity(envelope, envelope.PublisherId, envelope.StreamId, 43));
        yield return ("occurred_at/generated_at", envelope => envelope with
        {
            OccurredAt = envelope.OccurredAt.AddSeconds(-1),
            Payload = envelope.Payload with { GeneratedAt = envelope.Payload.GeneratedAt.AddSeconds(-1) },
        });
        yield return ("published_at", envelope => envelope with { PublishedAt = envelope.PublishedAt.AddSeconds(1) });
        yield return ("key_id", envelope => envelope with { KeyId = "publisher-2026-q4" });
        yield return ("build_sha", envelope => envelope with
        {
            Payload = envelope.Payload with { BuildSha = new string('a', 40) },
        });
        yield return ("capacity", envelope => envelope with
        {
            Payload = envelope.Payload with { Capacity = 81 },
        });
        yield return ("fresh_until", envelope => envelope with
        {
            Payload = envelope.Payload with { FreshUntil = envelope.Payload.FreshUntil.AddSeconds(1) },
        });
        yield return ("map_id", envelope => envelope with
        {
            Payload = envelope.Payload with { MapId = "SolreignHarbor" },
        });
        yield return ("next_event_pair", envelope => envelope with
        {
            Payload = envelope.Payload with
            {
                NextEventId = "event-storm",
                NextEventAt = envelope.Payload.GeneratedAt.AddMinutes(1),
            },
        });
        yield return ("population", envelope => envelope with
        {
            Payload = envelope.Payload with { Population = 25 },
        });
        yield return ("round_elapsed_bucket", envelope => envelope with
        {
            Payload = envelope.Payload with { RoundElapsedBucket = RoundElapsedBucket.ZeroToFifteen },
        });
        yield return ("round_phase", envelope => envelope with
        {
            Payload = envelope.Payload with { RoundPhase = PublicRoundPhase.Lobby },
        });
        yield return ("season_id", envelope => envelope with
        {
            Payload = envelope.Payload with { SeasonId = "season-2" },
        });
    }

    private static PublicEnvelopeV1 WithIdentity(
        PublicEnvelopeV1 envelope,
        string publisherId,
        string streamId,
        long sequence)
    {
        return envelope with
        {
            PublisherId = publisherId,
            StreamId = streamId,
            Sequence = sequence,
            IdempotencyKey = FormattableString.Invariant($"status:{publisherId}:{streamId}:{sequence}"),
        };
    }

    private static SigningFixtureData LoadSigningFixture()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(SigningFixture, StringComparison.Ordinal))
            .ToArray();
        Assert.That(names, Has.Length.EqualTo(1));

        using var stream = assembly.GetManifestResourceStream(names[0])
            ?? throw new InvalidOperationException("The signing fixture resource could not be opened.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var envelopeJson = root.GetProperty("envelope");
        var payloadJson = envelopeJson.GetProperty("payload");
        var envelope = new PublicEnvelopeV1(
            envelopeJson.GetProperty("schema_version").GetString()!,
            envelopeJson.GetProperty("message_type").GetString()!,
            Guid.ParseExact(envelopeJson.GetProperty("message_id").GetString()!, "D"),
            envelopeJson.GetProperty("publisher_id").GetString()!,
            envelopeJson.GetProperty("stream_id").GetString()!,
            envelopeJson.GetProperty("sequence").GetInt64(),
            ParseUtc(envelopeJson.GetProperty("occurred_at").GetString()!),
            ParseUtc(envelopeJson.GetProperty("published_at").GetString()!),
            envelopeJson.GetProperty("key_id").GetString()!,
            envelopeJson.GetProperty("idempotency_key").GetString()!,
            new ServerSnapshotV1(
                payloadJson.GetProperty("build_sha").GetString()!,
                payloadJson.GetProperty("map_id").GetString()!,
                payloadJson.GetProperty("population").GetInt32(),
                payloadJson.GetProperty("capacity").GetInt32(),
                PublicRoundPhase.InRound,
                payloadJson.GetProperty("season_id").GetString()!,
                ParseUtc(payloadJson.GetProperty("generated_at").GetString()!),
                ParseUtc(payloadJson.GetProperty("fresh_until").GetString()!)));
        var canonicalBodyText = root.GetProperty("canonical_body_utf8").GetString()!;

        return new SigningFixtureData(
            Encoding.UTF8.GetBytes(root.GetProperty("test_key_utf8").GetString()!),
            envelope,
            canonicalBodyText,
            Encoding.UTF8.GetBytes(canonicalBodyText),
            root.GetProperty("body_sha256_lower_hex").GetString()!,
            root.GetProperty("signing_input_utf8").GetString()!,
            root.GetProperty("signature_hmac_sha256_base64url_unpadded").GetString()!);
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private sealed record SigningFixtureData(
        byte[] Key,
        PublicEnvelopeV1 Envelope,
        string CanonicalBodyText,
        byte[] CanonicalBody,
        string BodySha256,
        string SigningInput,
        string Signature);
}
