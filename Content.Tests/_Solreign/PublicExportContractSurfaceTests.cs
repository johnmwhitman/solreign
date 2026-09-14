#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using PublicEnvelopeV1 = Content.Server._Solreign.PublicExport.PublicEnvelopeV1;
using PublicExportCanonicalization = Content.Server._Solreign.PublicExport.PublicExportCanonicalization;
using PublicExportValidation = Content.Server._Solreign.PublicExport.PublicExportValidation;
using PublicExportValidationCode = Content.Server._Solreign.PublicExport.PublicExportValidationCode;
using PublicRoundPhase = Content.Server._Solreign.PublicExport.PublicRoundPhase;
using RoundElapsedBucket = Content.Server._Solreign.PublicExport.RoundElapsedBucket;
using ServerSnapshotV1 = Content.Server._Solreign.PublicExport.ServerSnapshotV1;

namespace Content.Tests._Solreign;

/// <summary>
/// FINDING 3 (P1, orchestrator adversarial review 2026-07-15): CompatiblePlacements in
/// PublicExportContractTests is hand-maintained and the no-prohibited-alias-leak test
/// (ApprovedSignedOutputContainsNoProhibitedCanaryValueOrAlias) inspects only one baseline
/// envelope's emitted properties, so a new prohibited field could ship on
/// PublicEnvelopeV1/ServerSnapshotV1 while every existing test stayed green.
///
/// This freeze test reflects over the actual record types (all public members) AND the canonical
/// writer's emitted JSON property paths for a FULLY-POPULATED payload, asserting exact parity with
/// a frozen allowlist in both directions -- an added, removed, or renamed member fails immediately,
/// before any other test would notice. It further asserts every frozen member is both functionally
/// validated (mutating it to a deliberately invalid value moves Validate() away from Valid) and
/// functionally serialized (its json path is actually emitted).
/// </summary>
[TestFixture]
internal sealed class PublicExportContractSurfaceTests
{
    // (ClrTypeName, ClrMemberName, JsonPath). Frozen: any addition, removal, or rename to
    // PublicEnvelopeV1 or ServerSnapshotV1's public surface, or to the canonical writer's emitted
    // property paths, must be reflected here explicitly or this test fails.
    private static readonly (string ClrType, string ClrMember, string JsonPath)[] FrozenSurface =
    {
        ("PublicEnvelopeV1", "SchemaVersion", "schema_version"),
        ("PublicEnvelopeV1", "MessageType", "message_type"),
        ("PublicEnvelopeV1", "MessageId", "message_id"),
        ("PublicEnvelopeV1", "PublisherId", "publisher_id"),
        ("PublicEnvelopeV1", "StreamId", "stream_id"),
        ("PublicEnvelopeV1", "Sequence", "sequence"),
        ("PublicEnvelopeV1", "OccurredAt", "occurred_at"),
        ("PublicEnvelopeV1", "PublishedAt", "published_at"),
        ("PublicEnvelopeV1", "KeyId", "key_id"),
        ("PublicEnvelopeV1", "IdempotencyKey", "idempotency_key"),
        ("PublicEnvelopeV1", "Payload", "payload"),
        ("ServerSnapshotV1", "BuildSha", "payload.build_sha"),
        ("ServerSnapshotV1", "MapId", "payload.map_id"),
        ("ServerSnapshotV1", "Population", "payload.population"),
        ("ServerSnapshotV1", "Capacity", "payload.capacity"),
        ("ServerSnapshotV1", "RoundPhase", "payload.round_phase"),
        ("ServerSnapshotV1", "SeasonId", "payload.season_id"),
        ("ServerSnapshotV1", "GeneratedAt", "payload.generated_at"),
        ("ServerSnapshotV1", "FreshUntil", "payload.fresh_until"),
        ("ServerSnapshotV1", "RoundElapsedBucket", "payload.round_elapsed_bucket"),
        ("ServerSnapshotV1", "NextEventId", "payload.next_event_id"),
        ("ServerSnapshotV1", "NextEventAt", "payload.next_event_at"),
    };

    [Test]
    public void RecordPublicMembersMatchFrozenSurfaceExactly()
    {
        var actualEnvelopeMembers = GetPublicMemberNames(typeof(PublicEnvelopeV1));
        var actualPayloadMembers = GetPublicMemberNames(typeof(ServerSnapshotV1));

        var expectedEnvelopeMembers = FrozenSurface
            .Where(entry => entry.ClrType == "PublicEnvelopeV1")
            .Select(entry => entry.ClrMember)
            .ToArray();
        var expectedPayloadMembers = FrozenSurface
            .Where(entry => entry.ClrType == "ServerSnapshotV1")
            .Select(entry => entry.ClrMember)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actualEnvelopeMembers, Is.EquivalentTo(expectedEnvelopeMembers),
                "PublicEnvelopeV1's public member surface must match the frozen allowlist exactly -- " +
                "a new field must be added to FrozenSurface (and given validation/serialization " +
                "coverage below) before it can ship.");
            Assert.That(actualPayloadMembers, Is.EquivalentTo(expectedPayloadMembers),
                "ServerSnapshotV1's public member surface must match the frozen allowlist exactly.");
        });
    }

    [Test]
    public void CanonicalWriterEmitsExactlyTheFrozenJsonPathsForAFullyPopulatedPayload()
    {
        var body = PublicExportCanonicalization.Canonicalize(FullyPopulatedEnvelope());
        using var document = JsonDocument.Parse(body.AsMemory());

        var actualPaths = EnumeratePropertyPaths(document.RootElement, prefix: null).ToArray();
        var expectedPaths = FrozenSurface.Select(entry => entry.JsonPath).ToArray();

        Assert.That(actualPaths, Is.EquivalentTo(expectedPaths),
            "The canonical writer's emitted property paths for a fully-populated payload must match " +
            "the frozen allowlist exactly in both directions -- a path emitted but not allowlisted, " +
            "or an allowlisted path never emitted, both fail.");
    }

    [Test]
    public void EveryFrozenContractMemberIsBothActuallyValidatedAndActuallySerialized()
    {
        var validationCoverage = new List<string>();
        foreach (var (clrType, clrMember, _) in FrozenSurface)
        {
            var invalidated = Invalidate(FullyPopulatedEnvelope(), clrType, clrMember);
            if (PublicExportValidation.Validate(invalidated) != PublicExportValidationCode.Valid)
                validationCoverage.Add($"{clrType}.{clrMember}");
        }

        var body = PublicExportCanonicalization.Canonicalize(FullyPopulatedEnvelope());
        using var document = JsonDocument.Parse(body.AsMemory());
        var serializedPaths = EnumeratePropertyPaths(document.RootElement, prefix: null)
            .ToHashSet(StringComparer.Ordinal);
        var serializationCoverage = FrozenSurface
            .Where(entry => serializedPaths.Contains(entry.JsonPath))
            .Select(entry => $"{entry.ClrType}.{entry.ClrMember}")
            .ToArray();

        var expectedCoverage = FrozenSurface.Select(entry => $"{entry.ClrType}.{entry.ClrMember}").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(validationCoverage, Is.EquivalentTo(expectedCoverage),
                "Every frozen contract member must be functionally validated: mutating it to a " +
                "deliberately invalid value must move Validate()'s result away from Valid.");
            Assert.That(serializationCoverage, Is.EquivalentTo(expectedCoverage),
                "Every frozen contract member must be functionally serialized by the canonical " +
                "writer for a fully-populated payload.");
        });
    }

    private static PublicEnvelopeV1 Invalidate(PublicEnvelopeV1 envelope, string clrType, string clrMember)
    {
        return (clrType, clrMember) switch
        {
            ("PublicEnvelopeV1", "SchemaVersion") => envelope with { SchemaVersion = "invalid" },
            ("PublicEnvelopeV1", "MessageType") => envelope with { MessageType = "invalid" },
            ("PublicEnvelopeV1", "MessageId") => envelope with { MessageId = Guid.Empty },
            ("PublicEnvelopeV1", "PublisherId") => envelope with { PublisherId = "invalid" },
            ("PublicEnvelopeV1", "StreamId") => envelope with { StreamId = "invalid" },
            ("PublicEnvelopeV1", "Sequence") => envelope with { Sequence = 0 },
            ("PublicEnvelopeV1", "OccurredAt") => envelope with { OccurredAt = envelope.OccurredAt.AddTicks(1) },
            ("PublicEnvelopeV1", "PublishedAt") => envelope with { PublishedAt = envelope.OccurredAt.AddSeconds(-1) },
            ("PublicEnvelopeV1", "KeyId") => envelope with { KeyId = "invalid" },
            ("PublicEnvelopeV1", "IdempotencyKey") => envelope with { IdempotencyKey = "invalid" },
            ("PublicEnvelopeV1", "Payload") => envelope with { Payload = null! },
            ("ServerSnapshotV1", "BuildSha") => envelope with { Payload = envelope.Payload with { BuildSha = "invalid" } },
            ("ServerSnapshotV1", "MapId") => envelope with { Payload = envelope.Payload with { MapId = "invalid" } },
            ("ServerSnapshotV1", "Population") => envelope with { Payload = envelope.Payload with { Population = -1 } },
            ("ServerSnapshotV1", "Capacity") => envelope with { Payload = envelope.Payload with { Capacity = 0 } },
            ("ServerSnapshotV1", "RoundPhase") => envelope with
            {
                Payload = envelope.Payload with { RoundPhase = (PublicRoundPhase) 999 },
            },
            ("ServerSnapshotV1", "SeasonId") => envelope with { Payload = envelope.Payload with { SeasonId = "invalid" } },
            ("ServerSnapshotV1", "GeneratedAt") => envelope with
            {
                Payload = envelope.Payload with { GeneratedAt = envelope.Payload.GeneratedAt.AddSeconds(1) },
            },
            ("ServerSnapshotV1", "FreshUntil") => envelope with
            {
                Payload = envelope.Payload with { FreshUntil = envelope.Payload.GeneratedAt },
            },
            ("ServerSnapshotV1", "RoundElapsedBucket") => envelope with
            {
                Payload = envelope.Payload with { RoundElapsedBucket = (RoundElapsedBucket) 999 },
            },
            ("ServerSnapshotV1", "NextEventId") => envelope with { Payload = envelope.Payload with { NextEventId = null } },
            ("ServerSnapshotV1", "NextEventAt") => envelope with { Payload = envelope.Payload with { NextEventAt = null } },
            _ => throw new ArgumentOutOfRangeException(
                nameof(clrMember),
                $"No invalid mutation mapped for {clrType}.{clrMember}. A member was added to " +
                "FrozenSurface without teaching this helper how to invalidate it."),
        };
    }

    private static string[] GetPublicMemberNames(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToArray();
    }

    private static IEnumerable<string> EnumeratePropertyPaths(JsonElement element, string? prefix)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in element.EnumerateObject())
        {
            var path = prefix is null ? property.Name : $"{prefix}.{property.Name}";
            yield return path;
            foreach (var nested in EnumeratePropertyPaths(property.Value, path))
                yield return nested;
        }
    }

    private static PublicEnvelopeV1 FullyPopulatedEnvelope()
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
                PublicRoundPhase.PostRound,
                "season-1",
                ParseTimestamp("2026-07-15T02:30:00Z"),
                ParseTimestamp("2026-07-15T02:31:30Z"),
                RoundElapsedBucket.NinetyPlus,
                "event-storm",
                ParseTimestamp("2026-07-15T02:31:00Z")));
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
