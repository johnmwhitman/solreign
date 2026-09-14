using System;
using System.IO;
using Content.Server.Discord;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Robust.Server;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.BugReport;

/// <summary>
///     ECS glue for the in-game "bugreport" console command (<see cref="BugReportCommand"/>):
///     appends every submission to data/bug_reports.jsonl and, if solreign.bugreport_webhook is
///     set, relays it to Discord.
///
///     Threading contract mirrors SeasonLedgerSystem / NewsSystem: the Discord send is async void
///     with try/catch so it never blocks the tick; the JSONL append is a small synchronous file
///     write on the main thread (bounded to one report per player per <see cref="Cooldown"/>, so
///     there's no meaningful perf concern from doing it inline).
/// </summary>
public sealed partial class BugReportSystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private DiscordWebhook _discord = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IBaseServer _baseServer = default!;
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>Per-player minimum interval between accepted "bugreport" submissions.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly BugReportCooldown _cooldown = new();
    private readonly object _writeLock = new();

    private string _logPath = default!;
    private WebhookIdentifier? _webhookId;

    public override void Initialize()
    {
        base.Initialize();

        var dir = _res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        _logPath = Path.Combine(dir, "bug_reports.jsonl");

        _cfg.OnValueChanged(CCVars.SolreignBugReportWebhook, OnWebhookChanged, true);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnWebhookChanged(string url)
    {
        _webhookId = null;
        if (!string.IsNullOrWhiteSpace(url))
            _discord.GetWebhook(url, data => _webhookId = data.ToIdentifier());
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _cooldown.Forget(e.Session.UserId.UserId);
    }

    /// <summary>
    ///     Entry point for <see cref="BugReportCommand"/>. Returns false (with the remaining wait) if
    ///     the player is still on cooldown; otherwise records the report and returns true.
    /// </summary>
    public bool TrySubmit(ICommonSession player, string text, out TimeSpan remaining)
    {
        var guid = player.UserId.UserId;
        if (!_cooldown.TryConsume(guid, _gameTiming.RealTime, Cooldown, out remaining))
            return false;

        var entry = new BugReportEntry(
            DateTimeOffset.UtcNow,
            player.Name,
            guid,
            _ticker.RoundId,
            text);

        WriteToLog(entry);
        SendToDiscord(entry);

        return true;
    }

    private void WriteToLog(BugReportEntry entry)
    {
        try
        {
            var line = BugReportJsonl.ToLine(entry);
            lock (_writeLock)
            {
                File.AppendAllText(_logPath, line + "\n");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error writing bug report to {_logPath}:\n{e}");
        }
    }

    private async void SendToDiscord(BugReportEntry entry)
    {
        if (_webhookId is not { } webhookId)
            return;

        try
        {
            var labels = new BugReportDiscordLabels(
                Loc.GetString("bugreport-discord-title"),
                Loc.GetString("bugreport-discord-field-player"),
                Loc.GetString("bugreport-discord-field-guid"),
                Loc.GetString("bugreport-discord-field-round"),
                Loc.GetString("bugreport-discord-footer", ("server", _baseServer.ServerName)));

            var payload = BugReportDiscordPayload.Build(entry, labels);
            await _discord.CreateMessage(webhookId, payload);
        }
        catch (Exception e)
        {
            Log.Error($"Error sending bug report to Discord webhook:\n{e}");
        }
    }
}
