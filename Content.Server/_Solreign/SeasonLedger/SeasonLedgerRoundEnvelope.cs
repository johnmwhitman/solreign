using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server._Solreign.SeasonLedger;

public sealed class RoundEndEnvelope
{
    public int RoundId { get; }
    public string Gamemode { get; }
    public IReadOnlyList<RoundEndPlayerRecord> Players { get; }
    public IReadOnlyList<ContractLogRecord> Contracts { get; }

    public RoundEndEnvelope(
        int roundId,
        string gamemode,
        IReadOnlyList<RoundEndPlayerRecord> players,
        IReadOnlyList<ContractLogRecord> contracts)
    {
        if (roundId <= 0)
            throw new ArgumentOutOfRangeException(nameof(roundId), "Round ID must be positive.");
        if (string.IsNullOrWhiteSpace(gamemode))
            throw new ArgumentException("Gamemode must be nonempty.", nameof(gamemode));
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(contracts);

        RoundId = roundId;
        Gamemode = NormalizeGamemode(gamemode);

        var playerSnapshot = players.ToArray();
        var roster = new HashSet<Guid>();
        foreach (var player in playerSnapshot)
        {
            if (player.User == Guid.Empty)
                throw new ArgumentException("Roster users must be nonempty GUIDs.", nameof(players));
            if (!roster.Add(player.User))
                throw new ArgumentException("Roster users must be unique.", nameof(players));
            if (player.Contribution.RoundId != roundId)
                throw new ArgumentException("Every contribution must belong to the envelope round.", nameof(players));
            if (!string.Equals(NormalizeGamemode(player.Contribution.Gamemode), Gamemode, StringComparison.Ordinal))
                throw new ArgumentException("Every contribution must agree with the envelope gamemode.", nameof(players));
        }

        var contractSnapshot = contracts.ToArray();
        foreach (var contract in contractSnapshot)
        {
            if (contract.RoundId != roundId)
                throw new ArgumentException("Every contract must belong to the envelope round.", nameof(contracts));
            if (!roster.Contains(contract.User))
                throw new ArgumentException("Every contract user must belong to the envelope roster.", nameof(contracts));
            if (string.IsNullOrWhiteSpace(contract.ContractId))
                throw new ArgumentException("Contract ID must be nonempty.", nameof(contracts));
            if (string.IsNullOrWhiteSpace(contract.Scope))
                throw new ArgumentException("Contract scope must be nonempty.", nameof(contracts));
        }

        Players = Array.AsReadOnly(playerSnapshot);
        Contracts = Array.AsReadOnly(contractSnapshot);
    }

    internal CapturedRoundEndEnvelope Canonicalize()
    {
        var players = Players
            .OrderBy(player => player.User.ToString("D"), StringComparer.Ordinal)
            .Select(player => new CanonicalRoundEndPlayerRecord(
                player.User,
                player.Contribution with { Gamemode = Gamemode }))
            .ToArray();

        var orderedContracts = Contracts
            .OrderBy(contract => contract.User.ToString("D"), StringComparer.Ordinal)
            .ThenBy(contract => contract.ContractId, StringComparer.Ordinal)
            .ThenBy(contract => contract.Scope, StringComparer.Ordinal)
            .ToArray();
        var occurrences = new Dictionary<(Guid User, string ContractId, string Scope), int>();
        var contracts = new CanonicalContractLogRecord[orderedContracts.Length];
        for (var i = 0; i < orderedContracts.Length; i++)
        {
            var contract = orderedContracts[i];
            var key = (contract.User, contract.ContractId, contract.Scope);
            occurrences.TryGetValue(key, out var occurrence);
            occurrences[key] = occurrence + 1;
            contracts[i] = new CanonicalContractLogRecord(
                contract.User,
                contract.ContractId,
                contract.Scope,
                occurrence);
        }

        return CapturedRoundEndEnvelope.Create(RoundId, Gamemode, players, contracts);
    }

    private static string NormalizeGamemode(string gamemode)
    {
        return gamemode?.Trim() ?? string.Empty;
    }
}

internal readonly record struct CanonicalRoundEndPlayerRecord(Guid User, RoundContribution Contribution);

internal readonly record struct CanonicalContractLogRecord(
    Guid User,
    string ContractId,
    string Scope,
    int Occurrence);

internal sealed class CapturedRoundEndEnvelope
{
    internal const int SchemaVersion = 1;

    internal int RoundId { get; }
    internal string Gamemode { get; }
    internal IReadOnlyList<CanonicalRoundEndPlayerRecord> Players { get; }
    internal IReadOnlyList<CanonicalContractLogRecord> Contracts { get; }
    internal string CanonicalJson { get; }
    internal string CaptureHash { get; }

    private CapturedRoundEndEnvelope(
        int roundId,
        string gamemode,
        CanonicalRoundEndPlayerRecord[] players,
        CanonicalContractLogRecord[] contracts,
        string canonicalJson)
    {
        RoundId = roundId;
        Gamemode = gamemode;
        Players = Array.AsReadOnly(players);
        Contracts = Array.AsReadOnly(contracts);
        CanonicalJson = canonicalJson;
        CaptureHash = CanonicalRoundEndSerialization.Hash(canonicalJson);
    }

    internal static CapturedRoundEndEnvelope Create(
        int roundId,
        string gamemode,
        CanonicalRoundEndPlayerRecord[] players,
        CanonicalContractLogRecord[] contracts)
    {
        var json = CanonicalRoundEndSerialization.Serialize(roundId, gamemode, null, players, contracts);
        return new CapturedRoundEndEnvelope(roundId, gamemode, players, contracts, json);
    }

    internal static CapturedRoundEndEnvelope ParseCanonical(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("schema_version").GetInt32() != SchemaVersion)
            throw new InvalidDataException("Unsupported round-envelope schema.");

        var roundId = root.GetProperty("round_id").GetInt32();
        var gamemode = root.GetProperty("gamemode").GetString() ?? throw new InvalidDataException();
        var players = root.GetProperty("players").EnumerateArray().Select(player =>
        {
            var user = Guid.Parse(player.GetProperty("user").GetString() ?? string.Empty);
            var contribution = new RoundContribution(
                player.GetProperty("captain_clean").GetBoolean(),
                player.GetProperty("antag_win").GetBoolean(),
                player.GetProperty("early_death").GetBoolean(),
                player.GetProperty("round_id").GetInt32(),
                player.GetProperty("gamemode").GetString() ?? string.Empty,
                player.GetProperty("standing").GetInt32())
            {
                ContractsCompleted = player.GetProperty("contracts_completed").GetInt32(),
                ContractScore = player.GetProperty("contract_score").GetInt32(),
                HrPointsEarned = player.GetProperty("hr_points_earned").GetInt32(),
            };
            return new CanonicalRoundEndPlayerRecord(user, contribution);
        }).ToArray();
        var contracts = root.GetProperty("contracts").EnumerateArray().Select(contract =>
            new CanonicalContractLogRecord(
                Guid.Parse(contract.GetProperty("user").GetString() ?? string.Empty),
                contract.GetProperty("contract_id").GetString() ?? throw new InvalidDataException(),
                contract.GetProperty("scope").GetString() ?? throw new InvalidDataException(),
                contract.GetProperty("occurrence").GetInt32())).ToArray();

        var captured = Create(roundId, gamemode, players, contracts);
        if (!string.Equals(captured.CanonicalJson, json, StringComparison.Ordinal))
            throw new InvalidDataException("Round-envelope content is not canonical.");
        return captured;
    }
}

internal sealed class BoundCanonicalRoundEndEnvelope
{
    internal int RoundId => Captured.RoundId;
    internal string Gamemode => Captured.Gamemode;
    internal IReadOnlyList<CanonicalRoundEndPlayerRecord> Players => Captured.Players;
    internal IReadOnlyList<CanonicalContractLogRecord> Contracts => Captured.Contracts;
    internal CapturedRoundEndEnvelope Captured { get; }
    internal string SeasonId { get; }
    internal string CanonicalJson { get; }
    internal string EnvelopeHash { get; }

    private BoundCanonicalRoundEndEnvelope(
        CapturedRoundEndEnvelope captured,
        string seasonId,
        string canonicalJson)
    {
        Captured = captured;
        SeasonId = seasonId;
        CanonicalJson = canonicalJson;
        EnvelopeHash = CanonicalRoundEndSerialization.Hash(canonicalJson);
    }

    internal static BoundCanonicalRoundEndEnvelope Bind(CapturedRoundEndEnvelope captured, string seasonId)
    {
        if (string.IsNullOrWhiteSpace(seasonId))
            throw new ArgumentException("Season ID must be nonempty.", nameof(seasonId));

        var json = CanonicalRoundEndSerialization.Serialize(
            captured.RoundId,
            captured.Gamemode,
            seasonId,
            captured.Players,
            captured.Contracts);
        return new BoundCanonicalRoundEndEnvelope(captured, seasonId, json);
    }
}

internal static class CanonicalRoundEndSerialization
{
    internal static string Serialize(
        int roundId,
        string gamemode,
        string? seasonId,
        IReadOnlyList<CanonicalRoundEndPlayerRecord> players,
        IReadOnlyList<CanonicalContractLogRecord> contracts)
    {
        var playerDtos = players.Select(player => new CanonicalPlayerDto(
            player.User.ToString("D"),
            player.Contribution.WasCaptainClean,
            player.Contribution.AntagWin,
            player.Contribution.EarlyDeath,
            player.Contribution.RoundId,
            player.Contribution.Gamemode,
            player.Contribution.Standing,
            player.Contribution.ContractsCompleted,
            player.Contribution.ContractScore,
            player.Contribution.HrPointsEarned)).ToArray();
        var contractDtos = contracts.Select(contract => new CanonicalContractDto(
            contract.User.ToString("D"),
            contract.ContractId,
            contract.Scope,
            contract.Occurrence)).ToArray();

        return seasonId == null
            ? JsonSerializer.Serialize(new CapturedEnvelopeDto(
                CapturedRoundEndEnvelope.SchemaVersion,
                roundId,
                gamemode,
                playerDtos,
                contractDtos))
            : JsonSerializer.Serialize(new BoundEnvelopeDto(
                CapturedRoundEndEnvelope.SchemaVersion,
                seasonId,
                roundId,
                gamemode,
                playerDtos,
                contractDtos));
    }

    internal static string Hash(string json)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private sealed record CapturedEnvelopeDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("round_id")] int RoundId,
        [property: JsonPropertyName("gamemode")] string Gamemode,
        [property: JsonPropertyName("players")] CanonicalPlayerDto[] Players,
        [property: JsonPropertyName("contracts")] CanonicalContractDto[] Contracts);

    private sealed record BoundEnvelopeDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("season_id")] string SeasonId,
        [property: JsonPropertyName("round_id")] int RoundId,
        [property: JsonPropertyName("gamemode")] string Gamemode,
        [property: JsonPropertyName("players")] CanonicalPlayerDto[] Players,
        [property: JsonPropertyName("contracts")] CanonicalContractDto[] Contracts);

    private sealed record CanonicalPlayerDto(
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("captain_clean")] bool WasCaptainClean,
        [property: JsonPropertyName("antag_win")] bool AntagWin,
        [property: JsonPropertyName("early_death")] bool EarlyDeath,
        [property: JsonPropertyName("round_id")] int RoundId,
        [property: JsonPropertyName("gamemode")] string Gamemode,
        [property: JsonPropertyName("standing")] int Standing,
        [property: JsonPropertyName("contracts_completed")] int ContractsCompleted,
        [property: JsonPropertyName("contract_score")] int ContractScore,
        [property: JsonPropertyName("hr_points_earned")] int HrPointsEarned);

    private sealed record CanonicalContractDto(
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("contract_id")] string ContractId,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("occurrence")] int Occurrence);
}

public enum RoundEnvelopePersistResult
{
    Committed,
    AlreadyCommitted,
}

public sealed class RoundEndReplayConflictException : InvalidOperationException
{
    public int RoundId { get; }

    internal RoundEndReplayConflictException(int roundId)
        : base($"Season Ledger round {roundId} conflicts with existing persisted evidence.")
    {
        RoundId = roundId;
    }
}

internal enum RoundEnvelopeStage
{
    AfterPlayerMutation,
    BeforeCommit,
}

internal interface IRoundEnvelopeFaultInjector
{
    void Hit(RoundEnvelopeStage stage, int index);
}

internal sealed class NoopRoundEnvelopeFaultInjector : IRoundEnvelopeFaultInjector
{
    internal static readonly NoopRoundEnvelopeFaultInjector Instance = new();

    public void Hit(RoundEnvelopeStage stage, int index)
    {
    }
}
