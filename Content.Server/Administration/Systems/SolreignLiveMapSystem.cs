using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Content.Server._Solreign.Director;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Administration.Systems;

/// <summary>
/// Publishes a bounded anonymous station-occupancy snapshot to the Director every ten seconds.
/// The public Director projection applies its own delayed k-anonymity boundary.
/// </summary>
public sealed partial class LiveMapSystem : EntitySystem
{
    private const string Channel = "livemap";
    private static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(10);
    private static readonly HttpClient WebhookClient = new();

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private bool _enabled;
    private TimeSpan _nextPublish;
    private int _sendInFlight;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(
            _config,
            CCVars.SolreignLivemapEnabled,
            enabled => _enabled = enabled,
            invokeImmediately: true);
        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetCadence());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetCadence());
    }

    private void ResetCadence()
    {
        _nextPublish = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled ||
            _ticker.RunLevel != GameRunLevel.InRound ||
            _ticker.RoundId <= 0 ||
            _timing.CurTime < _nextPublish)
        {
            return;
        }

        _nextPublish = _timing.CurTime + PublishInterval;

        if (Interlocked.CompareExchange(ref _sendInFlight, 1, 0) != 0)
            return;

        if (!DirectorChannel.TryGetReadyToken(
                _config,
                CCVars.SolreignLivemapEnabled,
                out var token) ||
            !DirectorChannel.TryEnterRateLimit(Channel))
        {
            Interlocked.Exchange(ref _sendInFlight, 0);
            return;
        }

        var observations = CaptureObservations();
        var snapshot = SolreignLiveMapPrivacyRules.BuildSnapshot(
            _ticker.RoundId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            observations);
        var json = JsonSerializer.Serialize(snapshot);
        var endpoint = DirectorChannel.GetBaseUrl(_config) + "/map/update";

        SendSnapshotAsync(endpoint, token, json);
    }

    private List<SolreignLiveMapPrivacyRules.Observation> CaptureObservations()
    {
        var observations =
            new List<SolreignLiveMapPrivacyRules.Observation>(
                Math.Min(_players.Sessions.Length, SolreignLiveMapPrivacyRules.MaximumOccupants));

        foreach (var session in _players.Sessions)
        {
            if (observations.Count >= SolreignLiveMapPrivacyRules.MaximumOccupants)
                break;

            if (session.Status != SessionStatus.InGame ||
                session.AttachedEntity is not { } target ||
                Deleted(target) ||
                HasComp<GhostComponent>(target) ||
                !TryComp<MobStateComponent>(target, out var mobState) ||
                mobState.CurrentState == MobState.Dead ||
                !TryComp(target, out TransformComponent? xform) ||
                xform.MapID == MapId.Nullspace ||
                xform.MapID != _ticker.DefaultMap)
            {
                continue;
            }

            var position = _transform.GetMapCoordinates(target, xform).Position;
            observations.Add(new(position.X, position.Y));
        }

        return observations;
    }

    private async void SendSnapshotAsync(string endpoint, string token, string json)
    {
        try
        {
            var (request, context) =
                DirectorChannel.BuildSignedRequest(HttpMethod.Post, endpoint, token, json);
            using (request)
            using (var response = await WebhookClient.SendAsync(request))
            {
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"LiveMap publish returned HTTP {(int) response.StatusCode}.");
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var signatures);
                response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var timestamps);
                response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonces);
                if (!DirectorChannel.VerifyResponse(
                        token,
                        context,
                        responseBody,
                        signatures?.FirstOrDefault(),
                        timestamps?.FirstOrDefault(),
                        nonces?.FirstOrDefault()))
                {
                    Log.Warning("LiveMap publish response was unsigned or unbound; discarding.");
                }
            }
        }
        catch
        {
            // Never log the payload, request URI, player state, or transport exception text.
            Log.Warning("LiveMap publish failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _sendInFlight, 0);
        }
    }
}
