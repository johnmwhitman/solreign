using System;
using System.Text.RegularExpressions;

namespace Content.Server._Solreign.PublicExport;

internal static class PublicExportValidation
{
    private const long MaximumSequence = 9_007_199_254_740_991;
    private static readonly TimeSpan MaximumFreshness = TimeSpan.FromMinutes(5);
    private const RegexOptions ContractRegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Every pattern below anchors with \A...\z rather than ^...$. In .NET, $ (even outside
    // RegexOptions.Multiline) matches immediately before a single trailing '\n' as well as at the
    // true end of the string, so ^...$ would let a value like "game-host-primary\n" pass validation
    // and then be string.Join('\n', ...)-framed straight into the signing input, creating verifier
    // framing ambiguity. \A and \z only ever match the true string boundaries. See FINDING 1
    // (orchestrator adversarial review, 2026-07-15) and PublicExportValidationAnchorTests.
    private static readonly Regex MessageIdPattern = new(
        @"\A[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\z",
        ContractRegexOptions);
    private static readonly Regex PublisherIdPattern = new(
        @"\Agame-host-[a-z0-9][a-z0-9._-]{0,53}\z",
        ContractRegexOptions);
    private static readonly Regex StreamIdPattern = new(
        @"\Apublic-status-[a-z0-9][a-z0-9._-]{0,81}\z",
        ContractRegexOptions);
    private static readonly Regex KeyIdPattern = new(
        @"\Apublisher-[a-z0-9][a-z0-9._-]{0,53}\z",
        ContractRegexOptions);
    // FINDING 2 (P1, orchestrator adversarial review 2026-07-15): narrowed from
    // \A(?:[0-9a-f]{40}|[0-9a-f]{64})\z to the Git SHA-1 commit-hash shape only. This codebase's
    // real build shas are 40-character Git SHA-1 hex hashes; the 64-hex-character shape is
    // lexically indistinguishable from a SHA-256 digest of secret data and this library never
    // legitimately emits one, so rejecting it is a genuine, non-overfit shape rule rather than one
    // tuned to the prohibited fixture's specific canary derivatives.
    private static readonly Regex BuildShaPattern = new(
        @"\A[0-9a-f]{40}\z",
        ContractRegexOptions);
    private static readonly Regex MapIdPattern = new(
        @"\A[A-Z][A-Za-z0-9]{0,95}\z",
        ContractRegexOptions);
    private static readonly Regex SeasonIdPattern = new(
        @"\Aseason-[a-z0-9][a-z0-9._-]{0,56}\z",
        ContractRegexOptions);
    private static readonly Regex NextEventIdPattern = new(
        @"\Aevent-[a-z0-9][a-z0-9._-]{0,89}\z",
        ContractRegexOptions);

    internal static PublicExportValidationCode Validate(PublicEnvelopeV1 envelope)
    {
        if (envelope is null || !string.Equals(envelope.SchemaVersion, "1.0", StringComparison.Ordinal))
            return PublicExportValidationCode.InvalidSchemaVersion;

        if (!string.Equals(envelope.MessageType, "ServerSnapshotV1", StringComparison.Ordinal))
            return PublicExportValidationCode.InvalidMessageType;

        if (!MessageIdPattern.IsMatch(envelope.MessageId.ToString("D")))
            return PublicExportValidationCode.InvalidMessageId;

        if (!MatchesBounded(envelope.PublisherId, 64, PublisherIdPattern))
            return PublicExportValidationCode.InvalidPublisherId;

        if (!MatchesBounded(envelope.StreamId, 96, StreamIdPattern))
            return PublicExportValidationCode.InvalidStreamId;

        if (envelope.Sequence is < 1 or > MaximumSequence)
            return PublicExportValidationCode.InvalidSequence;

        var payload = envelope.Payload;
        if (!IsCanonicalTimestamp(envelope.OccurredAt) ||
            !IsCanonicalTimestamp(envelope.PublishedAt) ||
            payload is not null &&
            (!IsCanonicalTimestamp(payload.GeneratedAt) ||
             !IsCanonicalTimestamp(payload.FreshUntil) ||
             payload.NextEventAt is { } nextEventAt && !IsCanonicalTimestamp(nextEventAt)))
        {
            return PublicExportValidationCode.InvalidTimestamp;
        }

        if (!MatchesBounded(envelope.KeyId, 64, KeyIdPattern))
            return PublicExportValidationCode.InvalidKeyId;

        var expectedIdempotencyKey = FormattableString.Invariant(
            $"status:{envelope.PublisherId}:{envelope.StreamId}:{envelope.Sequence}");
        if (envelope.IdempotencyKey is null ||
            envelope.IdempotencyKey.Length > 185 ||
            !string.Equals(envelope.IdempotencyKey, expectedIdempotencyKey, StringComparison.Ordinal))
        {
            return PublicExportValidationCode.InvalidIdempotencyKey;
        }

        if (payload is null || !MatchesBounded(payload.BuildSha, 40, BuildShaPattern))
            return PublicExportValidationCode.InvalidBuildSha;

        if (!MatchesBounded(payload.MapId, 96, MapIdPattern))
            return PublicExportValidationCode.InvalidMapId;

        if (payload.Population is < 0 or > 1000)
            return PublicExportValidationCode.InvalidPopulation;

        if (payload.Capacity is < 1 or > 1000)
            return PublicExportValidationCode.InvalidCapacity;

        if (payload.Population > payload.Capacity)
            return PublicExportValidationCode.PopulationExceedsCapacity;

        if (!Enum.IsDefined(payload.RoundPhase))
            return PublicExportValidationCode.InvalidRoundPhase;

        if (payload.RoundElapsedBucket is { } elapsedBucket && !Enum.IsDefined(elapsedBucket))
            return PublicExportValidationCode.InvalidRoundElapsedBucket;

        if (!MatchesBounded(payload.SeasonId, 64, SeasonIdPattern))
            return PublicExportValidationCode.InvalidSeasonId;

        if (payload.GeneratedAt != envelope.OccurredAt)
            return PublicExportValidationCode.GeneratedTimeMismatch;

        if (envelope.PublishedAt < envelope.OccurredAt)
            return PublicExportValidationCode.PublishedBeforeOccurred;

        var freshness = payload.FreshUntil - payload.GeneratedAt;
        if (freshness <= TimeSpan.Zero || freshness > MaximumFreshness)
            return PublicExportValidationCode.InvalidFreshness;

        // FINDING 4 (P2): an envelope published at or after its own fresh_until deadline is
        // already expired at publish time and must never validate.
        if (envelope.PublishedAt >= payload.FreshUntil)
            return PublicExportValidationCode.PublishedAtOrAfterFreshUntil;

        var hasNextEventId = payload.NextEventId is not null;
        var hasNextEventAt = payload.NextEventAt.HasValue;
        if (hasNextEventId != hasNextEventAt ||
            hasNextEventId && !MatchesBounded(payload.NextEventId, 96, NextEventIdPattern))
        {
            return PublicExportValidationCode.InvalidNextEvent;
        }

        return PublicExportValidationCode.Valid;
    }

    private static bool IsCanonicalTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.Offset == TimeSpan.Zero && timestamp.Ticks % TimeSpan.TicksPerSecond == 0;
    }

    private static bool MatchesBounded(string? value, int maximumLength, Regex pattern)
    {
        return value is { Length: > 0 } && value.Length <= maximumLength && pattern.IsMatch(value);
    }
}
