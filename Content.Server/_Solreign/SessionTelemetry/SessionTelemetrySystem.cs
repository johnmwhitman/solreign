using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Afk;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.SessionTelemetry;

/// <summary>
///     Per-session activity ledger for the pre-registered retention criteria (ASK-3): one
///     closed-vocabulary JSONL line per connection, appended on disconnect. Activity rides the
///     existing AFK action bus (the PlayTimeTrackingSystem precedent) rather than re-subscribing
///     input events. Dark by default; with an empty pepper it writes nothing even when enabled.
/// </summary>
public sealed partial class SessionTelemetrySystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IAfkManager _afk = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;

    private readonly Dictionary<ICommonSession, SessionTelemetryScratch> _open = new();
    private readonly object _writeLock = new();

    private bool _enabled;
    private string _pepper = "";
    private int _intervalSeconds = 30;
    private int _maxBytes;
    private TimeSpan _nextSample = TimeSpan.Zero;

    private string _logPath = default!;

    /// <summary>Rotated files kept beside the live one; older rotations are pruned.</summary>
    public const int RotationsKept = 4;

    public override void Initialize()
    {
        base.Initialize();

        var dir = _res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        _logPath = Path.Combine(dir, "session_telemetry.jsonl");

        _cfg.OnValueChanged(CCVars.SolreignSessionTelemetryEnabled, v => _enabled = v, true);
        _cfg.OnValueChanged(CCVars.SolreignSessionTelemetryPepper, v => _pepper = v, true);
        _cfg.OnValueChanged(CCVars.SolreignSessionTelemetryIntervalSeconds,
            v => _intervalSeconds = Math.Clamp(v, 10, 120), true);
        _cfg.OnValueChanged(CCVars.SolreignSessionTelemetryMaxBytes, v => _maxBytes = v, true);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        _afk.PlayerDidActionEvent += OnPlayerDidAction;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _afk.PlayerDidActionEvent -= OnPlayerDidAction;

        // Graceful stop: close every open session at the moment the server goes down.
        foreach (var (_, scratch) in _open)
            Flush(scratch);
        _open.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.InGame)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_pepper) || _open.ContainsKey(e.Session))
                return;

            var hash = SessionTelemetryHash.AcctHash(_pepper, e.Session.UserId.UserId);
            _open[e.Session] = new SessionTelemetryScratch(hash, DateTimeOffset.UtcNow);
            return;
        }

        if (e.NewStatus == SessionStatus.Disconnected && _open.Remove(e.Session, out var scratch))
            Flush(scratch);
    }

    private void OnPlayerDidAction(ICommonSession session)
    {
        if (_open.TryGetValue(session, out var scratch))
            scratch.PendingActive = true;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Round ids are also sampled each interval; this just catches a restart that lands
        // entirely inside one interval so the *new* round still registers on short sessions.
        foreach (var (_, scratch) in _open)
            scratch.NoteRound(_ticker.RoundId);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_open.Count == 0)
            return;

        if (_timing.RealTime < _nextSample)
            return;

        _nextSample = _timing.RealTime + TimeSpan.FromSeconds(_intervalSeconds);
        Sample();
    }

    private void Sample()
    {
        var activeAdmins = _adminManager.ActiveAdmins
            .Where(a => a.Status == SessionStatus.InGame)
            .ToHashSet();

        var inGame = _playerManager.Sessions
            .Where(s => s.Status == SessionStatus.InGame)
            .ToList();

        var anyAdminAboard = activeAdmins.Count > 0;

        foreach (var (session, scratch) in _open)
        {
            if (scratch.PendingActive || !_afk.IsAfk(session))
                scratch.ActiveSamples++;
            else
                scratch.IdleSamples++;
            scratch.PendingActive = false;

            scratch.NoteRound(_ticker.RoundId);
            scratch.AdminAboardAny |= anyAdminAboard;

            // Human peers: other in-game sessions that are not active admins. De-admined staff
            // deliberately count as humans; an admin ghosting around does not.
            var peers = inGame.Count(s => s != session && !activeAdmins.Contains(s));
            scratch.MaxHumanPeers = Math.Max(scratch.MaxHumanPeers, peers);

            if (_ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status))
            {
                switch (status)
                {
                    case PlayerGameStatus.JoinedGame:
                        scratch.EverSpawned = true;
                        break;
                    case PlayerGameStatus.ReadyToPlay:
                        scratch.EverLobby = true;
                        scratch.NoteReady(true);
                        break;
                    case PlayerGameStatus.NotReadyToPlay:
                        scratch.EverLobby = true;
                        scratch.NoteReady(false);
                        break;
                }
            }
        }
    }

    private void Flush(SessionTelemetryScratch scratch)
    {
        // Pepper may have been cleared mid-session; a ledger with no key writes nothing.
        if (!_enabled || string.IsNullOrWhiteSpace(_pepper))
            return;

        try
        {
            var line = SessionTelemetryJsonl.ToLine(scratch, DateTimeOffset.UtcNow, _intervalSeconds);
            lock (_writeLock)
            {
                RotateIfNeeded();
                File.AppendAllText(_logPath, line + "\n");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error writing session telemetry to {_logPath}:\n{e}");
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_logPath);
        if (!info.Exists || info.Length < _maxBytes)
            return;

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        File.Move(_logPath, $"{_logPath}.{stamp}");

        var dir = Path.GetDirectoryName(_logPath)!;
        var rotated = Directory.GetFiles(dir, Path.GetFileName(_logPath) + ".*")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(RotationsKept)
            .ToList();
        foreach (var stale in rotated)
            File.Delete(stale);
    }
}
