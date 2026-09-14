using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Content.Server._Solreign.PublicExport;

internal static class PublicExportCanonicalization
{
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    internal static ImmutableArray<byte> Canonicalize(PublicEnvelopeV1 envelope)
    {
        var validationCode = PublicExportValidation.Validate(envelope);
        if (validationCode != PublicExportValidationCode.Valid)
            throw new PublicExportContractException(validationCode);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   Indented = false,
                   SkipValidation = false,
               }))
        {
            WriteEnvelope(writer, envelope);
        }

        return ImmutableArray.CreateRange(stream.ToArray());
    }

    internal static string FormatUtc(DateTimeOffset timestamp)
    {
        return timestamp.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);
    }

    private static void WriteEnvelope(Utf8JsonWriter writer, PublicEnvelopeV1 envelope)
    {
        writer.WriteStartObject();
        writer.WriteString("idempotency_key", envelope.IdempotencyKey);
        writer.WriteString("key_id", envelope.KeyId);
        writer.WriteString("message_id", envelope.MessageId.ToString("D").ToLowerInvariant());
        writer.WriteString("message_type", envelope.MessageType);
        writer.WriteString("occurred_at", FormatUtc(envelope.OccurredAt));
        writer.WritePropertyName("payload");
        WritePayload(writer, envelope.Payload);
        writer.WriteString("published_at", FormatUtc(envelope.PublishedAt));
        writer.WriteString("publisher_id", envelope.PublisherId);
        writer.WriteString("schema_version", envelope.SchemaVersion);
        writer.WriteNumber("sequence", envelope.Sequence);
        writer.WriteString("stream_id", envelope.StreamId);
        writer.WriteEndObject();
    }

    private static void WritePayload(Utf8JsonWriter writer, ServerSnapshotV1 payload)
    {
        writer.WriteStartObject();
        writer.WriteString("build_sha", payload.BuildSha);
        writer.WriteNumber("capacity", payload.Capacity);
        writer.WriteString("fresh_until", FormatUtc(payload.FreshUntil));
        writer.WriteString("generated_at", FormatUtc(payload.GeneratedAt));
        writer.WriteString("map_id", payload.MapId);
        if (payload.NextEventAt is { } nextEventAt)
            writer.WriteString("next_event_at", FormatUtc(nextEventAt));
        if (payload.NextEventId is { } nextEventId)
            writer.WriteString("next_event_id", nextEventId);
        writer.WriteNumber("population", payload.Population);
        if (payload.RoundElapsedBucket is { } elapsedBucket)
            writer.WriteString("round_elapsed_bucket", FormatElapsedBucket(elapsedBucket));
        writer.WriteString("round_phase", FormatRoundPhase(payload.RoundPhase));
        writer.WriteString("season_id", payload.SeasonId);
        writer.WriteEndObject();
    }

    private static string FormatRoundPhase(PublicRoundPhase phase)
    {
        return phase switch
        {
            PublicRoundPhase.Lobby => "lobby",
            PublicRoundPhase.InRound => "in_round",
            PublicRoundPhase.PostRound => "post_round",
            PublicRoundPhase.Unknown => "unknown",
            _ => throw new PublicExportContractException(PublicExportValidationCode.InvalidRoundPhase),
        };
    }

    private static string FormatElapsedBucket(RoundElapsedBucket bucket)
    {
        return bucket switch
        {
            RoundElapsedBucket.ZeroToFifteen => "0-15m",
            RoundElapsedBucket.FifteenToThirty => "15-30m",
            RoundElapsedBucket.ThirtyToSixty => "30-60m",
            RoundElapsedBucket.SixtyToNinety => "60-90m",
            RoundElapsedBucket.NinetyPlus => "90m+",
            _ => throw new PublicExportContractException(PublicExportValidationCode.InvalidRoundElapsedBucket),
        };
    }
}
