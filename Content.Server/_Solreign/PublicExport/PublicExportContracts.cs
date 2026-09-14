using System;
using System.Collections.Immutable;

namespace Content.Server._Solreign.PublicExport;

internal sealed record PublicEnvelopeV1(
    string SchemaVersion,
    string MessageType,
    Guid MessageId,
    string PublisherId,
    string StreamId,
    long Sequence,
    DateTimeOffset OccurredAt,
    DateTimeOffset PublishedAt,
    string KeyId,
    string IdempotencyKey,
    ServerSnapshotV1 Payload);

internal sealed record ServerSnapshotV1(
    string BuildSha,
    string MapId,
    int Population,
    int Capacity,
    PublicRoundPhase RoundPhase,
    string SeasonId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset FreshUntil,
    RoundElapsedBucket? RoundElapsedBucket = null,
    string? NextEventId = null,
    DateTimeOffset? NextEventAt = null);

internal sealed record PublicExportSignedResult(
    ImmutableArray<byte> CanonicalBody,
    string BodySha256,
    string SigningInput,
    string Signature);

internal enum PublicRoundPhase
{
    Lobby,
    InRound,
    PostRound,
    Unknown,
}

internal enum RoundElapsedBucket
{
    ZeroToFifteen,
    FifteenToThirty,
    ThirtyToSixty,
    SixtyToNinety,
    NinetyPlus,
}

internal enum PublicExportValidationCode
{
    Valid,
    InvalidSchemaVersion,
    InvalidMessageType,
    InvalidMessageId,
    InvalidPublisherId,
    InvalidStreamId,
    InvalidSequence,
    InvalidTimestamp,
    InvalidKeyId,
    InvalidIdempotencyKey,
    InvalidBuildSha,
    InvalidMapId,
    InvalidPopulation,
    InvalidCapacity,
    PopulationExceedsCapacity,
    InvalidRoundPhase,
    InvalidRoundElapsedBucket,
    InvalidSeasonId,
    GeneratedTimeMismatch,
    PublishedBeforeOccurred,
    InvalidFreshness,
    PublishedAtOrAfterFreshUntil,
    InvalidNextEvent,
}

internal enum PublicExportSigningCode
{
    InvalidContract,
    InvalidKeyMaterial,
}

internal sealed class PublicExportContractException : Exception
{
    public PublicExportValidationCode Code { get; }

    internal PublicExportContractException(PublicExportValidationCode code)
        : base(code.ToString())
    {
        Code = code;
    }
}

internal sealed class PublicExportSigningException : Exception
{
    public PublicExportSigningCode Code { get; }

    internal PublicExportSigningException(PublicExportSigningCode code)
        : base(code.ToString())
    {
        Code = code;
    }
}
