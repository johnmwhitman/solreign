using System;
using System.IO;
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared._Solreign.Report;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.Report;

/// <summary>
///     ECS glue for the in-game "report a player" quick-action: a fast, self-contained conduct-report
///     path distinct from ahelp/bwoink (<c>Content.Server.Administration.Systems.BwoinkSystem</c>) and
///     from the D2.1 structured feedback window (<c>Content.Server._Solreign.Feedback.FeedbackSystem</c>,
///     which this mirrors structurally). Churn insight: self-antag needs FAST moderation tools or it
///     burns the RP core -- a griefed player should be able to flag the griefer in a few seconds without
///     opening a live ahelp conversation. This system does NOT replace ahelp: it appends one JSONL record
///     per report (reporter, target, round, category, text, timestamp) to
///     data/reports/reports-yyyy-MM-dd.jsonl and, if any admins are currently online, pings them via the
///     existing admin-alert channel (<see cref="IChatManager.SendAdminAlert(string)"/>) -- it never opens
///     a two-way conversation itself.
///
///     Threading/persistence contract mirrors <c>FeedbackSystem</c>: everything here is synchronous file
///     I/O on the main thread, guarded by a lock, because submissions are cheap and rate-limited
///     (<see cref="ReportConstants.MaxSubmissionsPerRound"/> per player per round via
///     <see cref="ReportRateLimiter"/>) so there is no meaningful perf concern from doing it inline.
/// </summary>
public sealed partial class ReportSystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameMapManager _gameMapManager = default!;
    [Dependency] private IChatManager _chatManager = default!;

    private readonly ReportRateLimiter _rateLimiter = new();
    private readonly object _writeLock = new();

    private string _dataDir = default!;

    public override void Initialize()
    {
        base.Initialize();

        var root = _res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        _dataDir = Path.Combine(root, "reports");

        SubscribeNetworkEvent<SubmitReportEvent>(OnSubmitReportEvent);
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

    private void OnSubmitReportEvent(SubmitReportEvent msg, EntitySessionEventArgs args)
    {
        var result = Submit(args.SenderSession, msg.Target, msg.Category, msg.Text);
        RaiseNetworkEvent(
            new ReportSubmittedEvent(result.Success, result.ReferenceId, result.ErrorMessage),
            args.SenderSession);
    }

    /// <summary>
    ///     Entry point for <see cref="OnSubmitReportEvent"/> (and directly callable from tests/integration
    ///     tests, same shape as <c>FeedbackSystem.Submit</c>). Sanitizes target + text, enforces the
    ///     per-round rate limit, refuses self-reports, resolves the target against connected sessions
    ///     (matching either OOC username or IC character name -- players usually only know the latter),
    ///     appends the JSONL record, and pings online admins.
    /// </summary>
    public ReportSubmitResult Submit(ICommonSession reporter, string rawTarget, ReportCategory category, string rawText)
    {
        var target = SanitizeShort(rawTarget, ReportConstants.MaxTargetLength);
        if (string.IsNullOrWhiteSpace(target))
            return ReportSubmitResult.Fail(Loc.GetString("report-no-target"));

        var text = SanitizeLong(rawText);
        if (string.IsNullOrWhiteSpace(text))
            return ReportSubmitResult.Fail(Loc.GetString("report-empty"));

        var reporterGuid = reporter.UserId.UserId;
        var roundId = _ticker.RoundId;

        if (!_rateLimiter.TryConsume(reporterGuid, roundId, ReportConstants.MaxSubmissionsPerRound, out _))
        {
            return ReportSubmitResult.Fail(
                Loc.GetString("report-rate-limited", ("max", ReportConstants.MaxSubmissionsPerRound)));
        }

        var (targetGuid, targetResolvedName) = ResolveTarget(target);

        if (targetGuid == reporterGuid)
            return ReportSubmitResult.Fail(Loc.GetString("report-self"));

        var referenceId = MakeReferenceId();
        var entry = new ReportEntry(
            DateTimeOffset.UtcNow,
            referenceId,
            reporter.Name,
            reporterGuid,
            target,
            targetResolvedName,
            targetGuid,
            roundId,
            category,
            text,
            Map: _gameMapManager.GetSelectedMap()?.MapName ?? "unknown");

        WriteToLog(entry);
        NotifyAdmins(entry);

        return ReportSubmitResult.Ok(referenceId);
    }

    /// <summary>
    ///     Tries to match <paramref name="target"/> against a connected session's OOC username first,
    ///     then against its attached entity's IC character name (case-insensitive). Reports are still
    ///     filed if no match is found -- an unresolved target is still useful triage context for an
    ///     admin doing a manual lookup, and rejecting outright would let a griefer dodge a report just by
    ///     having logged off or by the reporter mistyping a name slightly.
    /// </summary>
    private (Guid? Guid, string? ResolvedName) ResolveTarget(string target)
    {
        foreach (var session in _playerManager.Sessions)
        {
            if (string.Equals(session.Name, target, StringComparison.OrdinalIgnoreCase))
                return (session.UserId.UserId, session.Name);

            if (session.AttachedEntity is { } ent &&
                string.Equals(MetaData(ent).EntityName, target, StringComparison.OrdinalIgnoreCase))
            {
                return (session.UserId.UserId, session.Name);
            }
        }

        return (null, null);
    }

    /// <summary>
    ///     Strips markup and control characters, trims, and truncates to <paramref name="maxLength"/>.
    ///     Used for the target-name field (a short, single-line display name).
    /// </summary>
    private static string SanitizeShort(string rawText, int maxLength)
    {
        var clean = StripMarkupAndControlChars(rawText, allowNewlines: false);
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }

    /// <summary>
    ///     Strips markup and control characters (keeping newlines), trims, and truncates to
    ///     <see cref="ReportConstants.MaxTextLength"/>. Used for the free-text reason field. Never
    ///     attaches or infers chat content -- only the text typed into the report window itself.
    /// </summary>
    private static string SanitizeLong(string rawText)
    {
        var clean = StripMarkupAndControlChars(rawText, allowNewlines: true);
        return clean.Length > ReportConstants.MaxTextLength ? clean[..ReportConstants.MaxTextLength] : clean;
    }

    private static string StripMarkupAndControlChars(string rawText, bool allowNewlines)
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

        var chars = new char[stripped.Length];
        var count = 0;
        foreach (var c in stripped)
        {
            if (char.IsControl(c) && !(allowNewlines && (c == '\n' || c == '\r')))
                continue;
            chars[count++] = c;
        }

        return new string(chars, 0, count).Trim();
    }

    private static string MakeReferenceId()
    {
        // Short, human-quotable, collision-astronomically-unlikely: "SR-" (Solreign Report) + 8 hex
        // chars from a fresh GUID. Not meant as a lookup key into anything beyond the JSONL file itself.
        return "SR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    private void NotifyAdmins(ReportEntry entry)
    {
        var targetDisplay = entry.TargetResolvedName ?? entry.TargetAsTyped;
        var preview = entry.Text.Length > 120 ? entry.Text[..120] + "..." : entry.Text;

        _chatManager.SendAdminAlert(
            $"Player report {entry.ReferenceId}: {entry.ReporterName} reported {targetDisplay} " +
            $"for {entry.Category.ToString().ToLowerInvariant()}: {preview}");
    }

    private void WriteToLog(ReportEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            var path = Path.Combine(_dataDir, $"reports-{entry.Timestamp:yyyy-MM-dd}.jsonl");
            var line = ReportJsonl.ToLine(entry);

            lock (_writeLock)
            {
                File.AppendAllText(path, line + "\n");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error writing player report {entry.ReferenceId} to {_dataDir}:\n{e}");
        }
    }
}

/// <summary>Outcome of <see cref="ReportSystem.Submit"/>.</summary>
public readonly record struct ReportSubmitResult(bool Success, string? ReferenceId, string? ErrorMessage)
{
    public static ReportSubmitResult Ok(string referenceId) => new(true, referenceId, null);
    public static ReportSubmitResult Fail(string errorMessage) => new(false, null, errorMessage);
}
