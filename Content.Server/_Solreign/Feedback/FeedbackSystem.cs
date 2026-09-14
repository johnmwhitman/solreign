using System;
using System.IO;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Roles.Jobs;
using Content.Server.Station.Systems;
using Content.Server.Mind;
using Content.Shared._Solreign.Feedback;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.Feedback;

/// <summary>
///     ECS glue for the in-game "feedback" window (roadmap D2.1, docs/ROADMAP-PLAYER-DELIGHT.md):
///     replaces the one-way "bugreport" console command with a structured surface (category + free
///     text) that auto-attaches safe context (round ID, map, job, location, server build — never chat)
///     and appends one JSONL record per submission to data/feedback/feedback-yyyy-MM-dd.jsonl, one file
///     per day so operators can tail/rotate/ship a day's worth without a database.
///
///     Threading/persistence contract mirrors <c>BugReportSystem</c>: everything here is synchronous
///     file I/O on the main thread, guarded by a lock, because submissions are cheap and rate-limited
///     (<see cref="FeedbackConstants.MaxSubmissionsPerRound"/> per player per round via <see cref="FeedbackRateLimiter"/>)
///     so there is no meaningful perf concern from doing it inline.
/// </summary>
public sealed partial class FeedbackSystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameMapManager _gameMapManager = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private MindSystem _minds = default!;
    [Dependency] private JobSystem _jobs = default!;

    private readonly FeedbackRateLimiter _rateLimiter = new();
    private readonly object _writeLock = new();

    private string _dataDir = default!;

    public override void Initialize()
    {
        base.Initialize();

        var root = _res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        _dataDir = Path.Combine(root, "feedback");

        SubscribeNetworkEvent<SubmitFeedbackEvent>(OnSubmitFeedbackEvent);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _rateLimiter.Forget(e.Session.UserId.UserId);
    }

    private void OnSubmitFeedbackEvent(SubmitFeedbackEvent msg, EntitySessionEventArgs args)
    {
        var result = Submit(args.SenderSession, msg.Category, msg.Text);
        RaiseNetworkEvent(
            new FeedbackSubmittedEvent(result.Success, result.ReferenceId, result.ErrorMessage),
            args.SenderSession);
    }

    /// <summary>
    ///     Entry point for <see cref="OnSubmitFeedbackEvent"/> (and directly callable from tests, same
    ///     shape as <c>BugReportSystem.TrySubmit</c>). Sanitizes the text, enforces the per-round rate
    ///     limit, captures safe context, and appends the JSONL record.
    /// </summary>
    public FeedbackSubmitResult Submit(ICommonSession player, FeedbackCategory category, string rawText)
    {
        var text = Sanitize(rawText);
        if (string.IsNullOrWhiteSpace(text))
            return FeedbackSubmitResult.Fail(Loc.GetString("feedback-empty"));

        var guid = player.UserId.UserId;
        var roundId = _ticker.RoundId;

        if (!_rateLimiter.TryConsume(guid, roundId, FeedbackConstants.MaxSubmissionsPerRound, out _))
        {
            return FeedbackSubmitResult.Fail(
                Loc.GetString("feedback-rate-limited", ("max", FeedbackConstants.MaxSubmissionsPerRound)));
        }

        var referenceId = MakeReferenceId();
        var entry = new FeedbackEntry(
            DateTimeOffset.UtcNow,
            referenceId,
            player.Name,
            guid,
            roundId,
            category,
            text,
            Map: _gameMapManager.GetSelectedMap()?.MapName ?? "unknown",
            Job: GetJob(player),
            Location: GetLocation(player),
            ServerBuild: _cfg.GetCVar(CVars.BuildVersion) is { Length: > 0 } v ? v : "dev");

        WriteToLog(entry);

        return FeedbackSubmitResult.Ok(referenceId);
    }

    /// <summary>
    ///     Strips markup and control characters, trims, and truncates to
    ///     <see cref="FeedbackConstants.MaxTextLength"/>. Never attaches or infers chat content — only
    ///     the text typed into the feedback window itself.
    /// </summary>
    private static string Sanitize(string rawText)
    {
        string stripped;
        try
        {
            stripped = FormattedMessage.RemoveMarkupPermissive(rawText);
        }
        catch (Exception)
        {
            // Pathological input the permissive parser still can't handle -- fall back to a blunt
            // bracket strip rather than reject the whole report.
            stripped = rawText.Replace("[", "").Replace("]", "");
        }

        // Drop control characters (keep \n so multi-line reports stay readable), then trim and cap.
        var chars = new char[stripped.Length];
        var count = 0;
        foreach (var c in stripped)
        {
            if (char.IsControl(c) && c != '\n' && c != '\r')
                continue;
            chars[count++] = c;
        }

        var clean = new string(chars, 0, count).Trim();
        return clean.Length > FeedbackConstants.MaxTextLength ? clean[..FeedbackConstants.MaxTextLength] : clean;
    }

    private static string MakeReferenceId()
    {
        // Short, human-quotable, collision-astronomically-unlikely: "SF-" (Solreign Feedback) + 8 hex
        // chars from a fresh GUID. Not meant as a lookup key into anything beyond the JSONL file itself
        // (roadmap D2.1: operator inbox is a later, separately-authenticated slice).
        return "SF-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    private string GetJob(ICommonSession player)
    {
        if (player.AttachedEntity is not { } mob)
            return "none";

        if (_minds.TryGetMind(mob, out var mindId, out _) && _jobs.MindTryGetJobName(mindId, out var jobName))
            return jobName;

        return "none";
    }

    private string GetLocation(ICommonSession player)
    {
        if (player.AttachedEntity is not { } mob)
            return "unknown";

        if (_station.GetOwningStation(mob) is { } station)
            return MetaData(station).EntityName;

        return "unknown";
    }

    private void WriteToLog(FeedbackEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            var path = Path.Combine(_dataDir, $"feedback-{entry.Timestamp:yyyy-MM-dd}.jsonl");
            var line = FeedbackJsonl.ToLine(entry);

            lock (_writeLock)
            {
                File.AppendAllText(path, line + "\n");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error writing feedback report {entry.ReferenceId} to {_dataDir}:\n{e}");
        }
    }
}

/// <summary>Outcome of <see cref="FeedbackSystem.Submit"/>.</summary>
public readonly record struct FeedbackSubmitResult(bool Success, string? ReferenceId, string? ErrorMessage)
{
    public static FeedbackSubmitResult Ok(string referenceId) => new(true, referenceId, null);
    public static FeedbackSubmitResult Fail(string errorMessage) => new(false, null, errorMessage);
}
