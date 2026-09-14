using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server._Solreign.SessionTelemetry;

/// <summary>
///     Mutable per-connection accumulator, opened on InGame and flushed to one JSONL line on
///     disconnect. Holds only the account HASH — the raw user id never enters this type, so
///     nothing downstream of it can leak what it never had.
/// </summary>
public sealed class SessionTelemetryScratch
{
    public SessionTelemetryScratch(string acctHash, DateTimeOffset startedAtUtc)
    {
        AcctHash = acctHash;
        StartedAtUtc = startedAtUtc;
    }

    public string AcctHash { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public DateTimeOffset StartedAtUtc { get; }

    public int ActiveSamples;
    public int IdleSamples;
    public bool PendingActive;

    public bool EverLobby;
    public bool EverSpawned;
    public int ReadyTransitions;
    public bool? LastReady;

    public int MaxHumanPeers;
    public bool AdminAboardAny;

    private readonly SortedSet<int> _rounds = new();
    public IReadOnlyCollection<int> Rounds => _rounds;

    /// <summary>Round id 0 is the "no round" sentinel and never a real datapoint.</summary>
    public void NoteRound(int roundId)
    {
        if (roundId != 0)
            _rounds.Add(roundId);
    }

    /// <summary>Counts Ready ↔ NotReady flips across samples (first observation is not a flip).</summary>
    public void NoteReady(bool ready)
    {
        if (LastReady is { } last && last != ready)
            ReadyTransitions++;
        LastReady = ready;
    }
}

/// <summary>
///     JSON shape of one data/session_telemetry.jsonl line. Closed vocabulary (the
///     first_deaths.jsonl idiom): no username, character name, IP, HWID, raw user id, chat,
///     coordinates, or job may ever be added here — the leak test pins the property allowlist.
/// </summary>
public sealed class SessionTelemetryJsonLine
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("ts_start")] public string TsStart { get; set; } = "";
    [JsonPropertyName("ts_end")] public string TsEnd { get; set; } = "";
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("acct")] public string Acct { get; set; } = "";
    [JsonPropertyName("dur_s")] public int DurS { get; set; }
    [JsonPropertyName("active_s")] public int ActiveS { get; set; }
    [JsonPropertyName("pct_active")] public double PctActive { get; set; }
    [JsonPropertyName("samples")] public int Samples { get; set; }
    [JsonPropertyName("active_samples")] public int ActiveSamples { get; set; }
    [JsonPropertyName("interval_s")] public int IntervalS { get; set; }
    [JsonPropertyName("lobby")] public bool Lobby { get; set; }
    [JsonPropertyName("spawned")] public bool Spawned { get; set; }
    [JsonPropertyName("ready_n")] public int ReadyN { get; set; }
    [JsonPropertyName("rounds")] public List<int> Rounds { get; set; } = new();
    [JsonPropertyName("max_human_peers")] public int MaxHumanPeers { get; set; }
    [JsonPropertyName("admin_aboard")] public bool AdminAboard { get; set; }
}

/// <summary>Pure shaping + the pre-registered classification predicates.</summary>
public static class SessionTelemetryJsonl
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>
    ///     True-solo per the pre-registered criteria: never a second human aboard and never an
    ///     active admin aboard (an admin-attended session cannot count as organic solo retention).
    /// </summary>
    public static bool TrueSolo(int maxHumanPeers, bool adminAboardAny)
        => maxHumanPeers == 0 && !adminAboardAny;

    /// <summary>Serializes one closed scratch to a single JSON line (no trailing newline).</summary>
    public static string ToLine(SessionTelemetryScratch scratch, DateTimeOffset endedAtUtc, int intervalSeconds)
    {
        var samples = scratch.ActiveSamples + scratch.IdleSamples;
        var line = new SessionTelemetryJsonLine
        {
            TsStart = scratch.StartedAtUtc.ToString("O"),
            TsEnd = endedAtUtc.ToString("O"),
            SessionId = scratch.SessionId.ToString("N"),
            Acct = scratch.AcctHash,
            DurS = (int)Math.Max(0, (endedAtUtc - scratch.StartedAtUtc).TotalSeconds),
            ActiveS = scratch.ActiveSamples * intervalSeconds,
            PctActive = samples == 0 ? 0.0 : (double)scratch.ActiveSamples / samples,
            Samples = samples,
            ActiveSamples = scratch.ActiveSamples,
            IntervalS = intervalSeconds,
            Lobby = scratch.EverLobby,
            Spawned = scratch.EverSpawned,
            ReadyN = scratch.ReadyTransitions,
            Rounds = new List<int>(scratch.Rounds),
            MaxHumanPeers = scratch.MaxHumanPeers,
            AdminAboard = scratch.AdminAboardAny,
        };

        return JsonSerializer.Serialize(line, Options);
    }
}
