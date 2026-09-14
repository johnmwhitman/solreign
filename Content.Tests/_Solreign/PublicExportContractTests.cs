#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using PublicEnvelopeV1 = Content.Server._Solreign.PublicExport.PublicEnvelopeV1;
using PublicExportContractException = Content.Server._Solreign.PublicExport.PublicExportContractException;
using PublicExportSigningCode = Content.Server._Solreign.PublicExport.PublicExportSigningCode;
using PublicExportSigningException = Content.Server._Solreign.PublicExport.PublicExportSigningException;
using PublicExportSigner = Content.Server._Solreign.PublicExport.PublicExportSigner;
using PublicExportValidation = Content.Server._Solreign.PublicExport.PublicExportValidation;
using PublicExportValidationCode = Content.Server._Solreign.PublicExport.PublicExportValidationCode;
using PublicRoundPhase = Content.Server._Solreign.PublicExport.PublicRoundPhase;
using RoundElapsedBucket = Content.Server._Solreign.PublicExport.RoundElapsedBucket;
using ServerSnapshotV1 = Content.Server._Solreign.PublicExport.ServerSnapshotV1;

namespace Content.Tests._Solreign;

[TestFixture]
internal sealed class PublicExportContractTests
{
    private static readonly PublicExportValidationCode[] ValidationPrecedenceOrder =
    {
        PublicExportValidationCode.InvalidSchemaVersion,
        PublicExportValidationCode.InvalidMessageType,
        PublicExportValidationCode.InvalidMessageId,
        PublicExportValidationCode.InvalidPublisherId,
        PublicExportValidationCode.InvalidStreamId,
        PublicExportValidationCode.InvalidSequence,
        PublicExportValidationCode.InvalidTimestamp,
        PublicExportValidationCode.InvalidKeyId,
        PublicExportValidationCode.InvalidIdempotencyKey,
        PublicExportValidationCode.InvalidBuildSha,
        PublicExportValidationCode.InvalidMapId,
        PublicExportValidationCode.InvalidPopulation,
        PublicExportValidationCode.InvalidCapacity,
        PublicExportValidationCode.PopulationExceedsCapacity,
        PublicExportValidationCode.InvalidRoundPhase,
        PublicExportValidationCode.InvalidRoundElapsedBucket,
        PublicExportValidationCode.InvalidSeasonId,
        PublicExportValidationCode.GeneratedTimeMismatch,
        PublicExportValidationCode.PublishedBeforeOccurred,
        PublicExportValidationCode.InvalidFreshness,
        PublicExportValidationCode.PublishedAtOrAfterFreshUntil,
        PublicExportValidationCode.InvalidNextEvent,
    };

    [Test]
    public void ApprovedFixtureIsAValidContract()
    {
        var envelope = ValidEnvelope();

        var result = PublicExportValidation.Validate(envelope);

        Assert.That(result, Is.EqualTo(PublicExportValidationCode.Valid));
    }

    [TestCase(81, 80, PublicExportValidationCode.PopulationExceedsCapacity)]
    public void RejectsPopulationAboveCapacity(int population, int capacity, PublicExportValidationCode code)
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { Population = population, Capacity = capacity },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(code));
    }

    [Test]
    public void RejectsInvalidBuildSha()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { Payload = valid.Payload with { BuildSha = "ABC123" } };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidBuildSha));
    }

    [Test]
    public void RejectsPublisherWithoutPurposePrefix()
    {
        var envelope = ValidEnvelope() with { PublisherId = "primary" };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidPublisherId));
    }

    [Test]
    public void RejectsStreamWithoutPurposePrefix()
    {
        var envelope = ValidEnvelope() with { StreamId = "2026-07" };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidStreamId));
    }

    [Test]
    public void RejectsKeyWithoutPurposePrefix()
    {
        var envelope = ValidEnvelope() with { KeyId = "2026-q3" };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidKeyId));
    }

    [Test]
    public void RejectsSeasonWithoutPurposePrefix()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { Payload = valid.Payload with { SeasonId = "1" } };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidSeasonId));
    }

    [Test]
    public void RejectsGeneratedTimeThatDiffersFromOccurredTime()
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { GeneratedAt = valid.OccurredAt.AddSeconds(1) },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.GeneratedTimeMismatch));
    }

    [Test]
    public void RejectsPublishedTimeBeforeOccurredTime()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { PublishedAt = valid.OccurredAt.AddSeconds(-1) };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.PublishedBeforeOccurred));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(301)]
    public void RejectsFreshnessOutsidePositiveFiveMinuteWindow(int freshnessSeconds)
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { FreshUntil = valid.Payload.GeneratedAt.AddSeconds(freshnessSeconds) },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidFreshness));
    }

    [Test]
    public void AcceptsFreshnessAtExactlyFiveMinutes()
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { FreshUntil = valid.Payload.GeneratedAt.AddSeconds(300) },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.Valid));
    }

    [Test]
    public void RejectsPublicationAtExactlyFreshUntil()
    {
        // FINDING 4 (P2): Validate never checked PublishedAt < FreshUntil, so an already-expired
        // snapshot (published at or after its own freshness deadline) validated as Valid.
        var valid = ValidEnvelope();
        var envelope = valid with { PublishedAt = valid.Payload.FreshUntil };

        Assert.That(
            PublicExportValidation.Validate(envelope),
            Is.EqualTo(PublicExportValidationCode.PublishedAtOrAfterFreshUntil));
    }

    [Test]
    public void RejectsPublicationAfterFreshUntil()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { PublishedAt = valid.Payload.FreshUntil.AddSeconds(1) };

        Assert.That(
            PublicExportValidation.Validate(envelope),
            Is.EqualTo(PublicExportValidationCode.PublishedAtOrAfterFreshUntil));
    }

    [Test]
    public void AcceptsPublicationOneSecondBeforeFreshUntil()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { PublishedAt = valid.Payload.FreshUntil.AddSeconds(-1) };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.Valid));
    }

    [Test]
    public void RejectsNextEventIdWithoutTimestamp()
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { NextEventId = "event-board-meeting", NextEventAt = null },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidNextEvent));
    }

    [Test]
    public void RejectsNextEventTimestampWithoutId()
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { NextEventId = null, NextEventAt = valid.OccurredAt.AddMinutes(10) },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidNextEvent));
    }

    [Test]
    public void RejectsNextEventWithoutPurposePrefix()
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with
            {
                NextEventId = "board-meeting",
                NextEventAt = valid.OccurredAt.AddMinutes(10),
            },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidNextEvent));
    }

    [Test]
    public void RejectsMalformedMapId()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { Payload = valid.Payload with { MapId = "solreign-oasis" } };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidMapId));
    }

    [Test]
    public void RejectsNonPositiveSequence()
    {
        var envelope = ValidEnvelope() with { Sequence = 0 };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidSequence));
    }

    [Test]
    public void RejectsUndefinedRoundPhase()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { Payload = valid.Payload with { RoundPhase = (PublicRoundPhase) 999 } };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidRoundPhase));
    }

    [Test]
    public void RejectsUndefinedRoundElapsedBucket()
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Payload = valid.Payload with { RoundElapsedBucket = (RoundElapsedBucket) 999 },
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidRoundElapsedBucket));
    }

    [TestCase("2026-07-15T02:30:00+00:00")]
    [TestCase("2026-07-15T02:30:00.000Z")]
    public void FixtureDtoRejectsNoncanonicalTimestamp(string value)
    {
        Assert.Throws<FormatException>(() => ParseTimestamp(value));
    }

    [Test]
    public void RejectsIdempotencyKeyThatDoesNotMatchEnvelopeIdentity()
    {
        var envelope = ValidEnvelope() with
        {
            IdempotencyKey = "status:game-host-primary:public-status-2026-07:43",
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidIdempotencyKey));
    }

    [TestCase("schema", PublicExportValidationCode.InvalidSchemaVersion)]
    [TestCase("message", PublicExportValidationCode.InvalidMessageType)]
    [TestCase("message_id", PublicExportValidationCode.InvalidMessageId)]
    [TestCase("publisher", PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("stream", PublicExportValidationCode.InvalidStreamId)]
    [TestCase("key", PublicExportValidationCode.InvalidKeyId)]
    [TestCase("idempotency", PublicExportValidationCode.InvalidIdempotencyKey)]
    [TestCase("build", PublicExportValidationCode.InvalidBuildSha)]
    [TestCase("map", PublicExportValidationCode.InvalidMapId)]
    [TestCase("season", PublicExportValidationCode.InvalidSeasonId)]
    public void RejectsNullStringsWithoutThrowing(string field, PublicExportValidationCode expected)
    {
        var valid = ValidEnvelope();
        var envelope = field switch
        {
            "schema" => valid with { SchemaVersion = null! },
            "message" => valid with { MessageType = null! },
            "message_id" => valid with { MessageId = Guid.Empty },
            "publisher" => valid with { PublisherId = null! },
            "stream" => valid with { StreamId = null! },
            "key" => valid with { KeyId = null! },
            "idempotency" => valid with { IdempotencyKey = null! },
            "build" => valid with { Payload = valid.Payload with { BuildSha = null! } },
            "map" => valid with { Payload = valid.Payload with { MapId = null! } },
            "season" => valid with { Payload = valid.Payload with { SeasonId = null! } },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [Test]
    public void RejectsNullEnvelopeAndPayloadWithoutThrowing()
    {
        Assert.That(PublicExportValidation.Validate(null!), Is.EqualTo(PublicExportValidationCode.InvalidSchemaVersion));
        Assert.That(
            PublicExportValidation.Validate(ValidEnvelope() with { Payload = null! }),
            Is.EqualTo(PublicExportValidationCode.InvalidBuildSha));
    }

    [TestCase("0.9", PublicExportValidationCode.InvalidSchemaVersion)]
    [TestCase("1.0", PublicExportValidationCode.Valid)]
    public void EnforcesExactSchemaVersion(string value, PublicExportValidationCode expected)
    {
        Assert.That(PublicExportValidation.Validate(ValidEnvelope() with { SchemaVersion = value }), Is.EqualTo(expected));
    }

    [TestCase("ServerSnapshot", PublicExportValidationCode.InvalidMessageType)]
    [TestCase("ServerSnapshotV1", PublicExportValidationCode.Valid)]
    public void EnforcesExactMessageType(string value, PublicExportValidationCode expected)
    {
        Assert.That(PublicExportValidation.Validate(ValidEnvelope() with { MessageType = value }), Is.EqualTo(expected));
    }

    [TestCase("018f784d-9b2d-1123-8123-123456789abc", PublicExportValidationCode.Valid)]
    [TestCase("018f784d-9b2d-8123-b123-123456789abc", PublicExportValidationCode.Valid)]
    [TestCase("018f784d-9b2d-0123-8123-123456789abc", PublicExportValidationCode.InvalidMessageId)]
    [TestCase("018f784d-9b2d-4123-7123-123456789abc", PublicExportValidationCode.InvalidMessageId)]
    public void EnforcesUuidVersionAndVariant(string value, PublicExportValidationCode expected)
    {
        Assert.That(
            PublicExportValidation.Validate(ValidEnvelope() with { MessageId = Guid.ParseExact(value, "D") }),
            Is.EqualTo(expected));
    }

    [TestCase(1, PublicExportValidationCode.Valid)]
    [TestCase(9007199254740991, PublicExportValidationCode.Valid)]
    [TestCase(9007199254740992, PublicExportValidationCode.InvalidSequence)]
    public void EnforcesJavaScriptSafeSequenceBounds(long sequence, PublicExportValidationCode expected)
    {
        var valid = ValidEnvelope();
        var envelope = valid with
        {
            Sequence = sequence,
            IdempotencyKey = FormattableString.Invariant($"status:{valid.PublisherId}:{valid.StreamId}:{sequence}"),
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [TestCase(-1, 80, PublicExportValidationCode.InvalidPopulation)]
    [TestCase(0, 1, PublicExportValidationCode.Valid)]
    [TestCase(1000, 1000, PublicExportValidationCode.Valid)]
    [TestCase(1001, 1000, PublicExportValidationCode.InvalidPopulation)]
    [TestCase(0, 0, PublicExportValidationCode.InvalidCapacity)]
    [TestCase(0, 1001, PublicExportValidationCode.InvalidCapacity)]
    public void EnforcesPopulationAndCapacityBounds(int population, int capacity, PublicExportValidationCode expected)
    {
        var valid = ValidEnvelope();
        var envelope = valid with { Payload = valid.Payload with { Population = population, Capacity = capacity } };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [TestCase("publisher", 54, PublicExportValidationCode.Valid)]
    [TestCase("publisher", 55, PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("stream", 82, PublicExportValidationCode.Valid)]
    [TestCase("stream", 83, PublicExportValidationCode.InvalidStreamId)]
    [TestCase("key", 54, PublicExportValidationCode.Valid)]
    [TestCase("key", 55, PublicExportValidationCode.InvalidKeyId)]
    [TestCase("map", 96, PublicExportValidationCode.Valid)]
    [TestCase("map", 97, PublicExportValidationCode.InvalidMapId)]
    [TestCase("season", 57, PublicExportValidationCode.Valid)]
    [TestCase("season", 58, PublicExportValidationCode.InvalidSeasonId)]
    [TestCase("event", 90, PublicExportValidationCode.Valid)]
    [TestCase("event", 91, PublicExportValidationCode.InvalidNextEvent)]
    public void EnforcesIdentifierLengthCeilings(string field, int suffixLength, PublicExportValidationCode expected)
    {
        var valid = ValidEnvelope();
        var envelope = field switch
        {
            "publisher" => WithIdentity(valid, $"game-host-{new string('a', suffixLength)}", valid.StreamId, valid.Sequence),
            "stream" => WithIdentity(valid, valid.PublisherId, $"public-status-{new string('a', suffixLength)}", valid.Sequence),
            "key" => valid with { KeyId = $"publisher-{new string('a', suffixLength)}" },
            "map" => valid with { Payload = valid.Payload with { MapId = $"A{new string('a', suffixLength - 1)}" } },
            "season" => valid with { Payload = valid.Payload with { SeasonId = $"season-{new string('a', suffixLength)}" } },
            "event" => valid with
            {
                Payload = valid.Payload with
                {
                    NextEventId = $"event-{new string('a', suffixLength)}",
                    NextEventAt = valid.OccurredAt.AddMinutes(10),
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [TestCase("occurred")]
    [TestCase("published")]
    [TestCase("generated")]
    [TestCase("fresh")]
    [TestCase("event")]
    public void RejectsNoncanonicalConstructedTimestamps(string field)
    {
        var valid = ValidEnvelope();
        var fractional = valid.OccurredAt.AddTicks(1);
        var envelope = field switch
        {
            "occurred" => valid with { OccurredAt = fractional },
            "published" => valid with { PublishedAt = fractional },
            "generated" => valid with { Payload = valid.Payload with { GeneratedAt = fractional } },
            "fresh" => valid with { Payload = valid.Payload with { FreshUntil = valid.Payload.FreshUntil.AddTicks(1) } },
            "event" => valid with
            {
                Payload = valid.Payload with { NextEventId = "event-next", NextEventAt = fractional.AddMinutes(10) },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidTimestamp));
    }

    [Test]
    public void RejectsConstructedTimestampWithNonzeroOffset()
    {
        var valid = ValidEnvelope();
        var envelope = valid with { PublishedAt = valid.PublishedAt.ToOffset(TimeSpan.FromHours(1)) };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.InvalidTimestamp));
    }

    [Test]
    public void ValidationCodesStayInFrozenTaskOrder()
    {
        var expected = new[] { PublicExportValidationCode.Valid }
            .Concat(ValidationPrecedenceOrder)
            .ToArray();

        Assert.That(Enum.GetValues<PublicExportValidationCode>(), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(AdjacentValidationPrecedenceCases))]
    public void ReturnsEarlierCodeForEveryAdjacentInvalidPair(
        PublicExportValidationCode earlier,
        PublicExportValidationCode later)
    {
        var envelope = WithInvalidity(WithInvalidity(ValidEnvelope(), later), earlier);

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(earlier));
    }

    [TestCase("publisher", "uppercase", PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("publisher", "whitespace", PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("publisher", "unicode", PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("publisher", "punctuation", PublicExportValidationCode.InvalidPublisherId)]
    [TestCase("stream", "uppercase", PublicExportValidationCode.InvalidStreamId)]
    [TestCase("stream", "whitespace", PublicExportValidationCode.InvalidStreamId)]
    [TestCase("stream", "unicode", PublicExportValidationCode.InvalidStreamId)]
    [TestCase("stream", "punctuation", PublicExportValidationCode.InvalidStreamId)]
    [TestCase("key", "uppercase", PublicExportValidationCode.InvalidKeyId)]
    [TestCase("key", "whitespace", PublicExportValidationCode.InvalidKeyId)]
    [TestCase("key", "unicode", PublicExportValidationCode.InvalidKeyId)]
    [TestCase("key", "punctuation", PublicExportValidationCode.InvalidKeyId)]
    [TestCase("season", "uppercase", PublicExportValidationCode.InvalidSeasonId)]
    [TestCase("season", "whitespace", PublicExportValidationCode.InvalidSeasonId)]
    [TestCase("season", "unicode", PublicExportValidationCode.InvalidSeasonId)]
    [TestCase("season", "punctuation", PublicExportValidationCode.InvalidSeasonId)]
    [TestCase("event", "uppercase", PublicExportValidationCode.InvalidNextEvent)]
    [TestCase("event", "whitespace", PublicExportValidationCode.InvalidNextEvent)]
    [TestCase("event", "unicode", PublicExportValidationCode.InvalidNextEvent)]
    [TestCase("event", "punctuation", PublicExportValidationCode.InvalidNextEvent)]
    public void RejectsIllegalCharactersAcrossPurposePrefixedIdentifiers(
        string field,
        string invalidKind,
        PublicExportValidationCode expected)
    {
        var suffix = invalidKind switch
        {
            "uppercase" => "Upper",
            "whitespace" => "bad value",
            "unicode" => "caf\u00e9",
            "punctuation" => "bad!",
            _ => throw new ArgumentOutOfRangeException(nameof(invalidKind)),
        };
        var valid = ValidEnvelope();
        var envelope = field switch
        {
            "publisher" => valid with { PublisherId = $"game-host-{suffix}" },
            "stream" => valid with { StreamId = $"public-status-{suffix}" },
            "key" => valid with { KeyId = $"publisher-{suffix}" },
            "season" => valid with { Payload = valid.Payload with { SeasonId = $"season-{suffix}" } },
            "event" => valid with
            {
                Payload = valid.Payload with
                {
                    NextEventId = $"event-{suffix}",
                    NextEventAt = valid.OccurredAt.AddMinutes(10),
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [TestCase(39, PublicExportValidationCode.InvalidBuildSha)]
    [TestCase(40, PublicExportValidationCode.Valid)]
    [TestCase(41, PublicExportValidationCode.InvalidBuildSha)]
    [TestCase(63, PublicExportValidationCode.InvalidBuildSha)]
    [TestCase(64, PublicExportValidationCode.InvalidBuildSha)]
    [TestCase(65, PublicExportValidationCode.InvalidBuildSha)]
    public void EnforcesExactBuildShaWidths(int length, PublicExportValidationCode expected)
    {
        // FINDING 2 (P1): build_sha now accepts only the Git SHA-1 commit-hash shape (exactly 40
        // lowercase hex characters). The 64-hex-character "SHA-256 digest" shape is rejected
        // outright -- it is lexically indistinguishable from a SHA-256 hash of secret data and this
        // codebase never emits SHA-256 build shas, so narrowing to 40 is a genuine, non-overfit
        // shape rule rather than one tuned to the prohibited fixture's specific canary values.
        var valid = ValidEnvelope();
        var envelope = valid with { Payload = valid.Payload with { BuildSha = new string('a', length) } };

        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(expected));
    }

    [Test]
    public void AcceptsFortyCharacterBuildShaAndMaximumIdempotencyKey()
    {
        var valid = ValidEnvelope();
        var publisher = $"game-host-{new string('a', 54)}";
        var stream = $"public-status-{new string('a', 82)}";
        var envelope = WithIdentity(valid, publisher, stream, 9007199254740991) with
        {
            Payload = valid.Payload with { BuildSha = new string('a', 40) },
        };

        Assert.That(envelope.IdempotencyKey, Has.Length.EqualTo(185));
        Assert.That(PublicExportValidation.Validate(envelope), Is.EqualTo(PublicExportValidationCode.Valid));
    }

    [Test]
    public void ContractExceptionsExposeOnlyTheirBoundedCode()
    {
        var contractException = new PublicExportContractException(PublicExportValidationCode.InvalidMapId);
        var signingException = new PublicExportSigningException(PublicExportSigningCode.InvalidKeyMaterial);

        Assert.Multiple(() =>
        {
            Assert.That(contractException.Code, Is.EqualTo(PublicExportValidationCode.InvalidMapId));
            Assert.That(contractException.Message, Is.EqualTo(nameof(PublicExportValidationCode.InvalidMapId)));
            Assert.That(contractException.InnerException, Is.Null);
            Assert.That(signingException.Code, Is.EqualTo(PublicExportSigningCode.InvalidKeyMaterial));
            Assert.That(signingException.Message, Is.EqualTo(nameof(PublicExportSigningCode.InvalidKeyMaterial)));
            Assert.That(signingException.InnerException, Is.Null);
        });
    }

    [Test]
    public void EveryProhibitedCanaryPlacementIsRejectedOrDeferredToProvenancePhase()
    {
        var fixture = LoadProhibitedFixture();

        AssertProhibitedPlacements(fixture);
    }

    [Test]
    public void ApprovedSignedOutputContainsNoProhibitedCanaryValueOrAlias()
    {
        var fixture = LoadProhibitedFixture();

        AssertApprovedOutputContainsNoProhibitedData(fixture);
    }

    private static void AssertProhibitedPlacements(ProhibitedFixtureData fixture)
    {
        // FINDING 2 (P1, orchestrator adversarial review 2026-07-15): Phase 1 containment is
        // lexical/schema shape validation only -- PublicExportValidation has no authoritative
        // provenance check anywhere in this library. The five rows below are placements where a
        // prohibited canary derivative is lexically IDENTICAL in shape to a legitimate contract
        // value (a random transport UUID, a durable stream sequence number, or a prototype-shaped
        // map id): no non-overfit shape rule can separate them from a real value of the same field.
        // Each is asserted below with an explicit CanaryPlacementDisposition.DeferredToProvenancePhase
        // -- never a silent `continue` on an accepted prohibited value. Authoritative provenance
        // enforcement is deferred to the later producer/exporter phase; see
        // docs/handoff/CODEX-PUBLIC-EXPORT-CONTRACT-2026-07-15.md (orchestrator-review amendment)
        // and prohibited-public-export-v1.json's phase_1_provenance_note.
        //
        // The three payload.build_sha rows that used to live here were removed: a genuinely
        // stricter, non-overfit shape rule exists for build_sha (this codebase's real build shas
        // are Git SHA-1 commit hashes, 40 lowercase hex characters), so PublicExportValidation now
        // rejects the 64-hex-character shape outright and those three canary derivatives are
        // RejectedByShape like any other malformed value -- see EnforcesExactBuildShaWidths.
        const string deferred = nameof(CanaryPlacementDisposition.DeferredToProvenancePhase);
        var explicitCollisions = new[]
        {
            new KnownShapeCollision("11111111-2222-4333-8444-555555555555", "message_id", "random_transport_uuid", deferred),
            new KnownShapeCollision("11111111", "sequence", "durable_stream_sequencer", deferred),
            new KnownShapeCollision("MTExMTExMTEtMjIyMi00MzMzLTg0NDQtNTU1NTU1NTU1NTU1", "payload.map_id", "validated_map_prototype", deferred),
            new KnownShapeCollision("Y2FuYXJ5LXBsYXllckBleGFtcGxlLmludmFsaWQ", "payload.map_id", "validated_map_prototype", deferred),
            new KnownShapeCollision("MTkyLjAuMi40NA", "payload.map_id", "validated_map_prototype", deferred),
        };
        Assert.That(
            fixture.KnownShapeCollisions.Count == explicitCollisions.Length,
            Is.True,
            "Known collision row count must match the frozen fixture.");
        for (var collisionIndex = 0; collisionIndex < explicitCollisions.Length; collisionIndex++)
        {
            var actualCollision = fixture.KnownShapeCollisions[collisionIndex];
            var expectedCollision = explicitCollisions[collisionIndex];
            Assert.Multiple(() =>
            {
                Assert.That(
                    string.Equals(actualCollision.Value, expectedCollision.Value, StringComparison.Ordinal),
                    Is.True,
                    $"Known collision {collisionIndex} value must match.");
                Assert.That(
                    string.Equals(actualCollision.AcceptedField, expectedCollision.AcceptedField, StringComparison.Ordinal),
                    Is.True,
                    $"Known collision {collisionIndex} field must match.");
                Assert.That(
                    string.Equals(actualCollision.RequiredControl, expectedCollision.RequiredControl, StringComparison.Ordinal),
                    Is.True,
                    $"Known collision {collisionIndex} control must match.");
                Assert.That(
                    string.Equals(actualCollision.Disposition, expectedCollision.Disposition, StringComparison.Ordinal),
                    Is.True,
                    $"Known collision {collisionIndex} disposition must match.");
            });
        }

        var expectedCollisions = explicitCollisions
            .Select(collision => (collision.Value, collision.AcceptedField))
            .ToHashSet();
        var observedCollisions = new HashSet<(string Value, string Field)>();

        for (var valueIndex = 0; valueIndex < fixture.Values.Count; valueIndex++)
        {
            var value = fixture.Values[valueIndex];
            foreach (var placement in CompatiblePlacements(value))
            {
                var code = PublicExportValidation.Validate(placement.Envelope);
                var actual = code == PublicExportValidationCode.Valid
                    ? CanaryPlacementDisposition.DeferredToProvenancePhase
                    : CanaryPlacementDisposition.RejectedByShape;
                var expected = expectedCollisions.Contains((value, placement.Field))
                    ? CanaryPlacementDisposition.DeferredToProvenancePhase
                    : CanaryPlacementDisposition.RejectedByShape;

                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    $"Canary {valueIndex} placement into {placement.Field} has the frozen disposition.");

                if (actual == CanaryPlacementDisposition.DeferredToProvenancePhase)
                {
                    // Phase 1 is lexical/schema containment only (see the fixture's
                    // phase_1_provenance_note and this method's leading comment). This branch is
                    // never a silent pass on an accepted prohibited value: the disposition is
                    // asserted explicitly and loudly against the frozen allowlist above, and this
                    // placement is required to be one of the five documented, lexically
                    // indistinguishable rows -- an accepted placement for any OTHER canary value
                    // already failed the Is.EqualTo(expected) assertion above instead of reaching
                    // here.
                    Assert.That(
                        expectedCollisions.Contains((value, placement.Field)),
                        Is.True,
                        $"Canary {valueIndex} placement into {placement.Field} was accepted (Valid) and must be one " +
                        "of the frozen known_shape_collisions rows explicitly deferred to the provenance phase; an " +
                        "accepted placement outside that frozen allowlist is a real Phase-1 containment failure.");
                    observedCollisions.Add((value, placement.Field));
                    continue;
                }

                var canonicalException = CaptureException(
                    () => Content.Server._Solreign.PublicExport.PublicExportCanonicalization.Canonicalize(placement.Envelope));
                var signingException = CaptureException(
                    () => PublicExportSigner.Sign(placement.Envelope, new byte[32]));
                Assert.That(
                    canonicalException is PublicExportContractException,
                    Is.True,
                    $"Canary {valueIndex} canonicalization must fail with the bounded contract exception.");
                Assert.That(
                    signingException is PublicExportContractException,
                    Is.True,
                    $"Canary {valueIndex} signing must fail with the bounded contract exception.");
                AssertNoFixtureValue(code.ToString(), fixture.Values, $"validation code for {placement.Field}");
                AssertNoFixtureValue(canonicalException!.Message, fixture.Values, $"canonical exception for {placement.Field}");
                AssertNoFixtureValue(signingException!.Message, fixture.Values, $"signing exception for {placement.Field}");
            }
        }

        Assert.That(
            observedCollisions.Count == expectedCollisions.Count,
            Is.True,
            "Observed collision count must match the frozen fixture.");
        for (var collisionIndex = 0; collisionIndex < explicitCollisions.Length; collisionIndex++)
        {
            var collision = explicitCollisions[collisionIndex];
            Assert.That(
                observedCollisions.Contains((collision.Value, collision.AcceptedField)),
                Is.True,
                $"Known collision {collisionIndex} must be observed.");
        }
    }

    private static void AssertApprovedOutputContainsNoProhibitedData(ProhibitedFixtureData fixture)
    {
        var envelope = ValidEnvelope();
        var key = LoadSigningFixtureKey();
        var result = PublicExportSigner.Sign(envelope, key);
        var canonicalBody = Encoding.UTF8.GetString(result.CanonicalBody.AsSpan());

        foreach (var (label, text) in new[]
                 {
                     ("canonical body", canonicalBody),
                     ("body hash", result.BodySha256),
                     ("signing input", result.SigningInput),
                     ("signature", result.Signature),
                 })
        {
            AssertNoFixtureValue(text, fixture.Values, label);
        }

        using var document = JsonDocument.Parse(result.CanonicalBody.AsMemory());
        var propertyNames = EnumeratePropertyNames(document.RootElement).ToArray();
        for (var propertyIndex = 0; propertyIndex < propertyNames.Length; propertyIndex++)
        {
            var propertyName = propertyNames[propertyIndex];
            Assert.That(
                !fixture.Aliases.Contains(propertyName),
                Is.True,
                $"Public property {propertyIndex} must not equal a prohibited alias.");
            Assert.That(
                !fixture.LowercaseAliases.Contains(propertyName.ToLowerInvariant()),
                Is.True,
                $"Public property {propertyIndex} must not equal a normalized prohibited alias.");
        }
    }

    private static IEnumerable<CanaryPlacement> CompatiblePlacements(string value)
    {
        var valid = ValidEnvelope();
        yield return new CanaryPlacement("schema_version", valid with { SchemaVersion = value });
        yield return new CanaryPlacement("message_type", valid with { MessageType = value });
        yield return new CanaryPlacement("publisher_id", WithIdentity(valid, value, valid.StreamId, valid.Sequence));
        yield return new CanaryPlacement("stream_id", WithIdentity(valid, valid.PublisherId, value, valid.Sequence));
        yield return new CanaryPlacement("key_id", valid with { KeyId = value });
        yield return new CanaryPlacement("idempotency_key", valid with { IdempotencyKey = value });
        yield return new CanaryPlacement("payload.build_sha", valid with { Payload = valid.Payload with { BuildSha = value } });
        yield return new CanaryPlacement("payload.map_id", valid with { Payload = valid.Payload with { MapId = value } });
        yield return new CanaryPlacement("payload.season_id", valid with { Payload = valid.Payload with { SeasonId = value } });
        yield return new CanaryPlacement("payload.next_event_id", valid with
        {
            Payload = valid.Payload with
            {
                NextEventId = value,
                NextEventAt = valid.Payload.GeneratedAt.AddMinutes(1),
            },
        });

        if (Guid.TryParseExact(value, "D", out var guid))
            yield return new CanaryPlacement("message_id", valid with { MessageId = guid });

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            yield return new CanaryPlacement("sequence", WithIdentity(valid, valid.PublisherId, valid.StreamId, sequence));

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
        {
            yield return new CanaryPlacement("payload.population", valid with
            {
                Payload = valid.Payload with { Population = integer },
            });
            yield return new CanaryPlacement("payload.capacity", valid with
            {
                Payload = valid.Payload with { Population = 0, Capacity = integer },
            });
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            yield return new CanaryPlacement("occurred_at", valid with
            {
                OccurredAt = timestamp,
                Payload = valid.Payload with { GeneratedAt = timestamp },
            });
            yield return new CanaryPlacement("published_at", valid with { PublishedAt = timestamp });
            yield return new CanaryPlacement("payload.generated_at", valid with
            {
                OccurredAt = timestamp,
                Payload = valid.Payload with { GeneratedAt = timestamp },
            });
            yield return new CanaryPlacement("payload.fresh_until", valid with
            {
                Payload = valid.Payload with { FreshUntil = timestamp },
            });
            yield return new CanaryPlacement("payload.next_event_at", valid with
            {
                Payload = valid.Payload with { NextEventId = "event-next", NextEventAt = timestamp },
            });
        }
    }

    private static void AssertNoFixtureValue(string text, IReadOnlyCollection<string> values, string context)
    {
        var lowercaseText = text.ToLowerInvariant();
        var valueIndex = 0;
        foreach (var value in values)
        {
            Assert.That(
                text.IndexOf(value, StringComparison.Ordinal),
                Is.EqualTo(-1),
                $"{context}: prohibited value {valueIndex} must be absent.");
            Assert.That(
                lowercaseText.IndexOf(value.ToLowerInvariant(), StringComparison.Ordinal),
                Is.EqualTo(-1),
                $"{context}: normalized prohibited value {valueIndex} must be absent.");
            valueIndex++;
        }
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var nested in EnumeratePropertyNames(property.Value))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumeratePropertyNames(item))
                    yield return nested;
            }
        }
    }

    private static ProhibitedFixtureData LoadProhibitedFixture()
    {
        using var document = JsonDocument.Parse(LoadFixture("prohibited-public-export-v1.json"));
        var root = document.RootElement;
        var values = new List<string>();
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var canary in root.GetProperty("canaries").EnumerateArray())
        {
            values.Add(canary.GetProperty("seed").GetString()!);
            values.AddRange(canary.GetProperty("derivatives").EnumerateArray().Select(item => item.GetString()!));
            foreach (var alias in canary.GetProperty("field_aliases").EnumerateArray())
                aliases.Add(alias.GetString()!);
        }

        var knownCollisions = root.GetProperty("known_shape_collisions")
            .EnumerateArray()
            .Select(item => new KnownShapeCollision(
                item.GetProperty("value").GetString()!,
                item.GetProperty("accepted_field").GetString()!,
                item.GetProperty("required_control").GetString()!,
                item.GetProperty("disposition").GetString()!))
            .ToArray();

        foreach (var collision in knownCollisions)
        {
            Assert.That(
                collision.Disposition,
                Is.EqualTo(nameof(CanaryPlacementDisposition.DeferredToProvenancePhase)),
                "Every known_shape_collisions row in the fixture must spell out the disposition " +
                "explicitly and it must match the CanaryPlacementDisposition enum member name exactly " +
                "(Phase 1 is lexical/schema containment only; no row may silently pass as accepted).");
        }

        return new ProhibitedFixtureData(
            values,
            aliases,
            aliases.Select(alias => alias.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
            knownCollisions);
    }

    private static byte[] LoadSigningFixtureKey()
    {
        using var document = JsonDocument.Parse(LoadFixture("server-snapshot-v1-signing-vector.json"));
        return Encoding.UTF8.GetBytes(document.RootElement.GetProperty("test_key_utf8").GetString()!);
    }

    private static PublicEnvelopeV1 WithIdentity(PublicEnvelopeV1 valid, string publisher, string stream, long sequence)
    {
        return valid with
        {
            PublisherId = publisher,
            StreamId = stream,
            Sequence = sequence,
            IdempotencyKey = FormattableString.Invariant($"status:{publisher}:{stream}:{sequence}"),
        };
    }

    private static IEnumerable<TestCaseData> AdjacentValidationPrecedenceCases()
    {
        for (var index = 0; index < ValidationPrecedenceOrder.Length - 1; index++)
        {
            var earlier = ValidationPrecedenceOrder[index];
            var later = ValidationPrecedenceOrder[index + 1];
            yield return new TestCaseData(earlier, later)
                .SetName($"ValidationPrecedence_{earlier}_Before_{later}");
        }
    }

    private static PublicEnvelopeV1 WithInvalidity(
        PublicEnvelopeV1 envelope,
        PublicExportValidationCode code)
    {
        return code switch
        {
            PublicExportValidationCode.InvalidSchemaVersion => envelope with { SchemaVersion = "invalid" },
            PublicExportValidationCode.InvalidMessageType => envelope with { MessageType = "invalid" },
            PublicExportValidationCode.InvalidMessageId => envelope with { MessageId = Guid.Empty },
            PublicExportValidationCode.InvalidPublisherId => envelope with { PublisherId = "invalid" },
            PublicExportValidationCode.InvalidStreamId => envelope with { StreamId = "invalid" },
            PublicExportValidationCode.InvalidSequence => envelope with { Sequence = 0 },
            PublicExportValidationCode.InvalidTimestamp => envelope with
            {
                PublishedAt = envelope.PublishedAt.AddTicks(1),
            },
            PublicExportValidationCode.InvalidKeyId => envelope with { KeyId = "invalid" },
            PublicExportValidationCode.InvalidIdempotencyKey => envelope with { IdempotencyKey = "invalid" },
            PublicExportValidationCode.InvalidBuildSha => envelope with
            {
                Payload = envelope.Payload with { BuildSha = "invalid" },
            },
            PublicExportValidationCode.InvalidMapId => envelope with
            {
                Payload = envelope.Payload with { MapId = "invalid" },
            },
            PublicExportValidationCode.InvalidPopulation => envelope with
            {
                Payload = envelope.Payload with { Population = -1 },
            },
            PublicExportValidationCode.InvalidCapacity => envelope with
            {
                Payload = envelope.Payload with { Capacity = 0 },
            },
            PublicExportValidationCode.PopulationExceedsCapacity => envelope with
            {
                Payload = envelope.Payload with { Population = envelope.Payload.Capacity + 1 },
            },
            PublicExportValidationCode.InvalidRoundPhase => envelope with
            {
                Payload = envelope.Payload with { RoundPhase = (PublicRoundPhase) 999 },
            },
            PublicExportValidationCode.InvalidRoundElapsedBucket => envelope with
            {
                Payload = envelope.Payload with { RoundElapsedBucket = (RoundElapsedBucket) 999 },
            },
            PublicExportValidationCode.InvalidSeasonId => envelope with
            {
                Payload = envelope.Payload with { SeasonId = "invalid" },
            },
            PublicExportValidationCode.GeneratedTimeMismatch => envelope with
            {
                Payload = envelope.Payload with { GeneratedAt = envelope.OccurredAt.AddSeconds(1) },
            },
            PublicExportValidationCode.PublishedBeforeOccurred => envelope with
            {
                PublishedAt = envelope.OccurredAt.AddSeconds(-1),
            },
            PublicExportValidationCode.InvalidFreshness => envelope with
            {
                Payload = envelope.Payload with { FreshUntil = envelope.Payload.GeneratedAt },
            },
            PublicExportValidationCode.PublishedAtOrAfterFreshUntil => envelope with
            {
                PublishedAt = envelope.Payload.FreshUntil,
            },
            PublicExportValidationCode.InvalidNextEvent => envelope with
            {
                Payload = envelope.Payload with { NextEventId = "event-next", NextEventAt = null },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
    }

    private static PublicEnvelopeV1 ValidEnvelope()
    {
        var bytes = LoadFixture("server-snapshot-v1-signing-vector.json");
        var fixture = JsonSerializer.Deserialize<SigningVectorDto>(bytes)
            ?? throw new InvalidOperationException("The signing vector fixture could not be deserialized.");
        var dto = fixture.Envelope;
        var payload = dto.Payload;

        return new PublicEnvelopeV1(
            dto.SchemaVersion,
            dto.MessageType,
            Guid.ParseExact(dto.MessageId, "D"),
            dto.PublisherId,
            dto.StreamId,
            dto.Sequence,
            ParseTimestamp(dto.OccurredAt),
            ParseTimestamp(dto.PublishedAt),
            dto.KeyId,
            dto.IdempotencyKey,
            new ServerSnapshotV1(
                payload.BuildSha,
                payload.MapId,
                payload.Population,
                payload.Capacity,
                ParseRoundPhase(payload.RoundPhase),
                payload.SeasonId,
                ParseTimestamp(payload.GeneratedAt),
                ParseTimestamp(payload.FreshUntil),
                ParseRoundElapsedBucket(payload.RoundElapsedBucket),
                payload.NextEventId,
                payload.NextEventAt is null ? null : ParseTimestamp(payload.NextEventAt)));
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static PublicRoundPhase ParseRoundPhase(string value)
    {
        return value switch
        {
            "lobby" => PublicRoundPhase.Lobby,
            "in_round" => PublicRoundPhase.InRound,
            "post_round" => PublicRoundPhase.PostRound,
            "unknown" => PublicRoundPhase.Unknown,
            _ => throw new FormatException("The fixture round phase is not part of the public contract."),
        };
    }

    private static RoundElapsedBucket? ParseRoundElapsedBucket(string? value)
    {
        return value switch
        {
            null => null,
            "0-15m" => RoundElapsedBucket.ZeroToFifteen,
            "15-30m" => RoundElapsedBucket.FifteenToThirty,
            "30-60m" => RoundElapsedBucket.ThirtyToSixty,
            "60-90m" => RoundElapsedBucket.SixtyToNinety,
            "90m+" => RoundElapsedBucket.NinetyPlus,
            _ => throw new FormatException("The fixture elapsed bucket is not part of the public contract."),
        };
    }

    private static byte[] LoadFixture(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var matches = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected exactly one embedded resource ending in {suffix}.");

        using var stream = assembly.GetManifestResourceStream(matches[0])
            ?? throw new InvalidOperationException("The selected embedded resource could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed record SigningVectorDto(
        [property: JsonPropertyName("envelope")] EnvelopeDto Envelope);

    private enum CanaryPlacementDisposition
    {
        RejectedByShape,
        DeferredToProvenancePhase,
    }

    private sealed record CanaryPlacement(string Field, PublicEnvelopeV1 Envelope);

    private sealed record KnownShapeCollision(string Value, string AcceptedField, string RequiredControl, string Disposition);

    private sealed record ProhibitedFixtureData(
        IReadOnlyList<string> Values,
        IReadOnlySet<string> Aliases,
        IReadOnlySet<string> LowercaseAliases,
        IReadOnlyList<KnownShapeCollision> KnownShapeCollisions);

    private sealed record EnvelopeDto(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("message_type")] string MessageType,
        [property: JsonPropertyName("message_id")] string MessageId,
        [property: JsonPropertyName("publisher_id")] string PublisherId,
        [property: JsonPropertyName("stream_id")] string StreamId,
        [property: JsonPropertyName("sequence")] long Sequence,
        [property: JsonPropertyName("occurred_at")] string OccurredAt,
        [property: JsonPropertyName("published_at")] string PublishedAt,
        [property: JsonPropertyName("key_id")] string KeyId,
        [property: JsonPropertyName("idempotency_key")] string IdempotencyKey,
        [property: JsonPropertyName("payload")] PayloadDto Payload);

    private sealed record PayloadDto(
        [property: JsonPropertyName("build_sha")] string BuildSha,
        [property: JsonPropertyName("map_id")] string MapId,
        [property: JsonPropertyName("population")] int Population,
        [property: JsonPropertyName("capacity")] int Capacity,
        [property: JsonPropertyName("round_phase")] string RoundPhase,
        [property: JsonPropertyName("season_id")] string SeasonId,
        [property: JsonPropertyName("generated_at")] string GeneratedAt,
        [property: JsonPropertyName("fresh_until")] string FreshUntil,
        [property: JsonPropertyName("round_elapsed_bucket")] string? RoundElapsedBucket = null,
        [property: JsonPropertyName("next_event_id")] string? NextEventId = null,
        [property: JsonPropertyName("next_event_at")] string? NextEventAt = null);
}
