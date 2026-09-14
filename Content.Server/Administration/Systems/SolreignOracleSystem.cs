using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Content.Server._Solreign.Director;
using Content.Server.Chat.Systems;
using Content.Shared.Administration;
using Content.Shared.Administration.Systems;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Robust.Server.Player;
using Content.Shared.Examine;
using Robust.Shared.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Server.Administration.Systems
{


    [RegisterComponent]
    public sealed partial class SolreignOracleComponent : Component
    {
    }

    public sealed class OracleResponse
    {
        public string dialogue { get; set; } = string.Empty;
        public string action { get; set; } = string.Empty;
        public int amount { get; set; }
        public string? PrototypeId { get; set; }
        public string? soundData { get; set; }
        public VfxData? vfx { get; set; }
    }

    public sealed class VfxData
    {
        public string? color { get; set; }
        public float radius { get; set; }
    }

    /// <summary>
    ///     UX-SIMPLE FIX 2: wire DTO for the daemon's hardened Oracle-unavailable error contract —
    ///     a signed 503 shaped <c>{"error":"oracle_unavailable","reason":"missing_api_key"|
    ///     "llm_error","detail":"&lt;ExceptionClassName&gt;"}</c>. Parsed BEST-EFFORT for the server
    ///     log only (<c>SolreignOracleSystem.LogOracleFailureResponseAsync</c>) — never trusted for
    ///     anything player-facing, and a missing/unparseable body (e.g. an older daemon's raw,
    ///     contract-less 500) is handled gracefully, not thrown.
    /// </summary>
    public sealed class OracleErrorDto
    {
        public string? error { get; set; }
        public string? reason { get; set; }
        public string? detail { get; set; }
    }

    public sealed class DirectorEvent
    {
        public string type { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public string action { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("event")]
        public string EventName { get; set; } = string.Empty;

        public string targetPlayerId { get; set; } = string.Empty;
        public int targetEntityId { get; set; }
        public string command { get; set; } = string.Empty;
        public string audioData { get; set; } = string.Empty;
        public string text { get; set; } = string.Empty;

        // v11 delivery vocabulary (spawn_relic): daemon-authored relic flavor. Lowercase to match
        // the daemon's JSON keys, same as the fields above; unknown fields deserialize-ignore on
        // older daemons.
        public string customName { get; set; } = string.Empty;
        public string customDesc { get; set; } = string.Empty;
    }

    public sealed class DirectorEventReceivedEvent : EntityEventArgs
    {
        public DirectorEvent Event { get; }
        public DirectorEventReceivedEvent(DirectorEvent evt) { Event = evt; }
    }

    public sealed partial class SolreignOracleSystem : EntitySystem
    {
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private SharedPopupSystem _popupSystem = default!;
        [Dependency] private EntityLookupSystem _lookup = default!;
        [Dependency] private SharedPointLightSystem _pointLight = default!;
        [Dependency] private IPrototypeManager _prototypeManager = default!;
        [Dependency] private IConfigurationManager _config = default!;
        [Dependency] private ChatSystem _chat = default!;
        [Dependency] private MetaDataSystem _metaData = default!;
        [Dependency] private IRobustRandom _random = default!;

        private static readonly HttpClient WebhookClient = new HttpClient();
        private readonly ConcurrentQueue<(EntityUid, OracleResponse)> _pendingOracleResponses = new();
        private readonly ConcurrentQueue<DirectorEvent[]> _pendingDirectorEvents = new();

        /// <summary>
        ///     UX-SIMPLE FIX 2: one queued in-fiction failure popup per failed prayer. FireWebhook
        ///     never retries on its own (one HTTP attempt per petition, already the case before this
        ///     fix), so this queue is bounded by "one entry per failed prayer this Update() hasn't
        ///     drained yet" — never a retry storm. Drained (and the entity-existence check applied)
        ///     in <see cref="Update"/>, same threading discipline as <see cref="_pendingOracleResponses"/>:
        ///     the async Task.Run continuation in <see cref="FireWebhook"/> never touches
        ///     entities/UI/popups directly.
        /// </summary>
        private readonly ConcurrentQueue<EntityUid> _pendingOracleFailures = new();

        /// <summary>
        ///     PG-13, unsettling-corporate flavor for a failed/unanswered prayer — never the raw
        ///     HTTP status, never any daemon-internal detail (missing API key, exception class,
        ///     etc — those are logged server-side only, see <see cref="LogOracleFailure"/>). One
        ///     variant is picked at random per delivered popup so repeated failures don't feel like
        ///     a canned error dialog.
        /// </summary>
        private static readonly string[] OracleFailurePopupLocKeys =
        {
            "solreign-oracle-failure-popup-hums",
            "solreign-oracle-failure-popup-static",
            "solreign-oracle-failure-popup-silence",
        };
        private float _pollTimer = 0f;
        private const float PollInterval = 10f;
        private const string Channel = "oracle";

        /// <summary>
        ///     Oracle front-door hardening (Directive Terminal, v11): the client (SolreignOracleWindow)
        ///     already enforces this same limit and disables Send while empty, but per the "client code
        ///     must never trust server-free input" rule that's UX only — a modified/replaced client, or
        ///     any other future BUI sender of <see cref="SolreignOracleMessage"/>, must not be able to
        ///     push an oversized or blank petition into <see cref="FireWebhook"/>. Truncate rather than
        ///     reject outright so a slightly-over-limit honest petition still reaches the Oracle.
        /// </summary>
        private const int MaxPlayerMessageLength = 500;

        // grk v11 review finding 7: the poll must not share a rate-limit bucket with the
        // chat-driven Oracle webhook — sustained Oracle traffic could starve the one inbound
        // delivery pipe (and with it any "stop" signals that arrive via poll).
        private const string PollChannel = "director-poll";

        /// <summary>
        ///     Curated allowlist of prototype IDs the (still-HOLD) <c>generate_item</c> action may
        ///     spawn — cosmetic-only, no weapons/contraband/mechanically-effective items, per
        ///     AGX-MERGE-PLAN.md's "restrict to a curated allowlist of cosmetic-only prototype
        ///     IDs". Intentionally left empty pending that design pass: until populated,
        ///     <c>generate_item</c> can never successfully spawn anything, even if
        ///     <see cref="CCVars.SolreignOracleEnabled"/> and the Director token are both set. The
        ///     live <c>ReloadPrototypes</c> + hot-spawn architecture itself is unchanged this
        ///     session — AGX-MERGE-PLAN.md defers the offline-validate-then-load-at-restart rework
        ///     to a dedicated follow-up.
        /// </summary>
        private static readonly HashSet<string> CosmeticPrototypeAllowlist = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<SolreignOracleComponent, SolreignOracleMessage>(OnOracleMessage);
        }

        private void OnOracleMessage(Entity<SolreignOracleComponent> ent, ref SolreignOracleMessage args)
        {
            // HOLD (AGX-INTEGRATION-CONTRACT.md clause 6): in-game LLM chat to minors, no
            // content-filter/fallback/cap. Fail CLOSED regardless of who reaches this handler —
            // off by default, and even when the CVar is flipped on, harness-readiness alone does
            // NOT constitute the required content-safety design pass + John sign-off.
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignOracleEnabled))
                return;

            var actor = args.Actor;

            if (!_playerManager.TryGetSessionByEntity(actor, out var session))
                return;

            if (string.IsNullOrWhiteSpace(args.Message))
                return;

            var playerId = session.UserId.UserId.ToString();
            var message = args.Message.Length > MaxPlayerMessageLength
                ? args.Message[..MaxPlayerMessageLength]
                : args.Message;

            FireWebhook(actor, new
            {
                PlayerId = playerId,
                Message = message
            });
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // UX-SIMPLE FIX 2: drained unconditionally, ahead of every CVar-readiness gate below —
            // a failure popup only reports that OUR OWN already-sent webhook call didn't come back
            // clean; it is not itself a daemon-driven side effect, so it must not be silently wiped
            // by the same kill-switch discipline that clears queued daemon CONTENT.
            while (_pendingOracleFailures.TryDequeue(out var failedActor))
            {
                if (!Exists(failedActor))
                    continue;

                var text = Loc.GetString(_random.Pick(OracleFailurePopupLocKeys));
                _popupSystem.PopupEntity(text, failedActor, failedActor, PopupType.MediumCaution);
            }

            _pollTimer += frameTime;
            if (_pollTimer >= PollInterval)
            {
                _pollTimer -= PollInterval;
                // v11 backbone #2: the poll is the ONE delivery channel for ALL daemon->game
                // events (broadcasts, crypt, bounty results, market spawns), so it gates on the
                // MASTER solreign.director.enabled — not the Oracle feature flag. Fail CLOSED:
                // off by default, and requires a well-formed configured Director token even when
                // the master is flipped on. Single-read snapshot instead of separate
                // IsReady()+GetToken() calls (Codex MEDIUM #4).
                if (DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignDirectorEnabled, out var pollToken)
                    && DirectorChannel.TryEnterRateLimit(PollChannel))
                {
                    PollDirector(pollToken);
                }
            }

            // Codex MEDIUM #3: a runtime kill switch must stop queued effects, not just new
            // poll/webhook calls. Recheck readiness immediately before consuming anything queued,
            // and discard rather than let stale-but-already-fetched responses trickle through
            // after disablement. v11 backbone #2 split: the MASTER switch clears everything; the
            // Oracle feature switch clears only the Oracle chat/action-pipe queue (dialogue,
            // flash_lights, generate_item — all HOLD-adjacent), leaving Director event delivery
            // governed by the master alone.
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignDirectorEnabled))
            {
                _pendingOracleResponses.Clear();
                _pendingDirectorEvents.Clear();
                return;
            }

            if (!DirectorChannel.IsReady(_config, CCVars.SolreignOracleEnabled))
                _pendingOracleResponses.Clear();

            while (_pendingOracleResponses.TryDequeue(out var item))
            {
                // Codex v2 re-review (check-then-drain race): the outer IsReady() check above
                // only proves readiness at the top of this Update() tick — a large queue can
                // still be draining several frames later after the CVar flips off. Recheck per
                // item (cheap CVar read) so disabling the kill switch mid-drain stops further
                // dialogue/action effects immediately. Oracle-queue only: if the MASTER died
                // mid-drain the Director-event loop below rechecks it per item and clears both.
                if (!DirectorChannel.IsReady(_config, CCVars.SolreignOracleEnabled))
                {
                    _pendingOracleResponses.Clear();
                    break;
                }

                var (actor, response) = item;
                if (!string.IsNullOrEmpty(response.dialogue))
                {
                    _popupSystem.PopupEntity(response.dialogue, actor, actor, PopupType.Large);
                }

                if (response.action == "flash_lights")
                {
                    var xform = Transform(actor);
                    var lights = _lookup.GetEntitiesInRange(xform.Coordinates, 10f);
                    foreach (var lightEnt in lights)
                    {
                        if (TryComp<PointLightComponent>(lightEnt, out var lightComp))
                        {
                            _pointLight.SetColor(lightEnt, Color.Green, lightComp);
                        }
                    }
                }
                else if (response.action == "generate_item")
                {
                    // GUARDRAIL 5 (v11): removed the live _prototypeManager.ReloadPrototypes(new())
                    // hot-reload — reloading prototypes on a running server is the crash/abuse
                    // pattern the contract warns about, and the daemon no longer authors prototypes
                    // (the LLM has no item-generation tool). The allowlist below is empty, so this
                    // branch is a hard no-op; kept only to reject any stray generate_item safely.
                    // Not just HasIndex (AGX-MERGE-PLAN.md): existence alone lets the Director
                    // hot-inject ANY registered prototype (weapons, contraband, anything).
                    // CosmeticPrototypeAllowlist is intentionally empty until a design pass
                    // curates it, so this is a hard no-op today regardless of what the Director
                    // returns.
                    if (!string.IsNullOrEmpty(response.PrototypeId)
                        && CosmeticPrototypeAllowlist.Contains(response.PrototypeId)
                        && _prototypeManager.HasIndex<EntityPrototype>(response.PrototypeId))
                    {
                        var uid = Spawn(response.PrototypeId, Transform(actor).Coordinates);
                        
                        if (response.vfx != null)
                        {
                            var light = EnsureComp<PointLightComponent>(uid);
                            _pointLight.SetRadius(uid, response.vfx.radius, light);
                            
                            Color color = Color.White;
                            if (response.vfx.color != null)
                            {
                                if (Color.TryFromName(response.vfx.color, out var namedColor))
                                    color = namedColor;
                                else
                                {
                                    var hexOk = Color.TryFromHex(response.vfx.color, out var hexColor);
                                    if (hexOk)
                                        color = hexColor;
                                }
                            }
                            _pointLight.SetColor(uid, color, light);
                        }

                        if (!string.IsNullOrEmpty(response.soundData))
                        {
                            Log.Info($"[Oracle] Mocking sound data: {response.soundData}");
                        }
                    }
                }
            }

            while (_pendingDirectorEvents.TryDequeue(out var events))
            {
                // Batch cap independent of whatever the sidecar actually sent (AGX-MERGE-PLAN.md
                // "cap DirectorEvent[] batch size").
                foreach (var evt in events.Take(DirectorChannel.MaxEventBatchSize))
                {
                    // Codex v2 re-review (check-then-drain race): recheck per item, not just once
                    // before this drain loop started — a batch can be up to MaxEventBatchSize (20)
                    // events, and the kill switch flipping off partway through must stop the
                    // remaining events in THIS batch too, not just the next queued batch. Gated on
                    // the MASTER switch (v11 backbone #2): Director event delivery outlives the
                    // Oracle feature flag.
                    if (!DirectorChannel.IsReady(_config, CCVars.SolreignDirectorEnabled))
                    {
                        _pendingOracleResponses.Clear();
                        _pendingDirectorEvents.Clear();
                        return;
                    }

                    // Local fan-out stays master-gated: every subscriber applies its own feature
                    // gate (SentientFactionSystem and CinematicCameraSystem both recheck
                    // solreign.oracle.enabled — verified).
                    RaiseLocalEvent(new DirectorEventReceivedEvent(evt));

                    // v11 front door #3 (Requisitions Anonymous): spawn_entity is a PLAYER-
                    // INITIATED delivery (the buyer already spent Standing via POST
                    // /api/market/buy), not an unsolicited daemon broadcast — so it is gated on
                    // solreign.market.enabled ALONE, independent of (and checked before) the
                    // broadcasts gate below, and never falls through to the broadcast-only
                    // branches. Rechecked per event, same as every other gate in this drain.
                    if (evt.action == "spawn_entity")
                    {
                        if (DirectorChannel.IsReady(_config, CCVars.SolreignMarketEnabled))
                            HandleMarketSpawnEntity(evt);

                        continue;
                    }

                    // grk v11 review P0: the PLAYER-FACING side effects below (station-wide
                    // popup/meteor text, entity spawns) must NOT ride the master switch alone —
                    // an operator enabling the master for telemetry backbone (crypt, rivalry,
                    // round orchestration) must not thereby arm daemon->player content. Feature
                    // switch, rechecked per event like the master, default OFF, HOLD (guardrail 1
                    // game-side defense-in-depth even though the daemon moderates its output).
                    if (!DirectorChannel.IsReady(_config, CCVars.SolreignDirectorBroadcastsEnabled))
                        continue;

                    if (evt.type == "popup" || evt.type == "meteor")
                    {
                        foreach (var session in _playerManager.NetworkedSessions)
                        {
                            if (session.AttachedEntity != null)
                            {
                                _popupSystem.PopupEntity(evt.message, session.AttachedEntity.Value, session.AttachedEntity.Value, PopupType.LargeCaution);
                            }
                        }
                    }
                    // GUARDRAIL 2 (v11): the pay-to-grief handler that spawned a weapon on a NAMED,
                    // non-consenting player is REMOVED. The daemon no longer sends audience_purchase/
                    // drop_weapon (redesigned to station-wide, non-combat audience events), and the
                    // game must not retain the capability to arm a targeted player even if such an
                    // event ever arrived. Do not re-add a targeted combat-item spawn here.
                    else if (evt.action == "spawn_paper")
                    {
                        bool spawned = false;
                        foreach (var session in _playerManager.NetworkedSessions)
                        {
                            if (session.AttachedEntity != null)
                            {
                                var uid = Spawn("Paper", Transform(session.AttachedEntity.Value).Coordinates);
                                Log.Info($"[Oracle] Mocking paper text to: {evt.text}");
                                spawned = true;
                                break;
                            }
                        }

                        if (!spawned)
                        {
                            var uid = Spawn("Paper", new Robust.Shared.Map.MapCoordinates(0, 0, new Robust.Shared.Map.MapId(1)));
                            Log.Info($"[Oracle] Mocking paper text to: {evt.text}");
                        }
                    }
                    else if (evt.action == "spawn_decal")
                    {
                        var nent = new NetEntity(evt.targetEntityId);
                        var uid = GetEntity(nent);
                        if (Exists(uid))
                        {
                            var coords = Transform(uid).Coordinates;
                            Log.Info($"[Oracle] Mocking DecalSystem to apply decal of type '{evt.type}' to grid at {coords}");
                        }
                    }
                    // v11 delivery vocabulary (grk draft, orchestrator-applied): radio_broadcast
                    // carries the daemon's moderated round beats (SITREPs, bounty claims, A.U.D.I.T.
                    // announcements — 14 daemon call sites) that were previously silently dropped
                    // by this validator. Text is station-wide and player-facing, hence behind the
                    // broadcasts gate above like popup/meteor.
                    else if (evt.action == "radio_broadcast")
                    {
                        // v11 ships no daemon-audio playback: log length only, never decode/play.
                        if (!string.IsNullOrEmpty(evt.audioData))
                            Log.Info($"[Oracle] radio_broadcast audioData ignored (len={evt.audioData.Length})");

                        // text already length-capped (MaxEventTextLength) by IsValidDirectorEvent.
                        _chat.DispatchGlobalAnnouncement(evt.text, sender: "The Directive", playSound: true);
                    }
                    // spawn_relic: a Persistent Relic of a legendary death re-enters the world at a
                    // Directive Terminal ("delivered by the Directive" — deterministic, map-agnostic
                    // location). Same defense-in-depth discipline as the market's spawn_entity:
                    // hardcoded base allowlist + prototype existence, daemon flavor length-capped.
                    else if (evt.action == "spawn_relic")
                    {
                        if (!RelicPrototypeAllowlist.Contains(evt.text) || !_prototypeManager.HasIndex<EntityPrototype>(evt.text))
                        {
                            Log.Warning($"[Oracle] spawn_relic rejected non-allowlisted/unknown prototype '{evt.text}'.");
                            continue;
                        }

                        EntityUid? terminal = null;
                        var terminals = EntityQueryEnumerator<SolreignOracleComponent, TransformComponent>();
                        while (terminals.MoveNext(out var termUid, out _, out var termXform))
                        {
                            if (termXform.MapUid != null)
                            {
                                terminal = termUid;
                                break;
                            }
                        }

                        if (terminal == null)
                        {
                            Log.Warning("[Oracle] spawn_relic dropped: no Directive Terminal on any map.");
                            continue;
                        }

                        var relic = Spawn(evt.text, Transform(terminal.Value).Coordinates);

                        // Validator already bounds these; slice defensively anyway (belt-and-
                        // suspenders — a validator regression must not become an unbounded rename).
                        // grk front-door review finding 1: ESCAPE the daemon-authored flavor —
                        // ExamineSystemShared feeds EntityDescription to AddMarkupOrThrow, so raw
                        // markup would style (or, if malformed, throw on) every examine of the
                        // relic. Names/descriptions must land as plain text.
                        if (!string.IsNullOrEmpty(evt.customName))
                        {
                            var relicName = evt.customName.Length > MaxCustomNameLength
                                ? evt.customName[..MaxCustomNameLength] : evt.customName;
                            _metaData.SetEntityName(relic, FormattedMessage.EscapeText(relicName));
                        }
                        if (!string.IsNullOrEmpty(evt.customDesc))
                        {
                            var relicDesc = evt.customDesc.Length > MaxCustomDescLength
                                ? evt.customDesc[..MaxCustomDescLength] : evt.customDesc;
                            _metaData.SetEntityDescription(relic, FormattedMessage.EscapeText(relicDesc));
                        }

                        Log.Info($"[Oracle] spawn_relic '{evt.text}' delivered at terminal {terminal.Value}.");
                    }
                }
            }
        }

        private void PollDirector(string token)
        {
            Task.Run(async () =>
            {
                try
                {
                    var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Get, DirectorChannel.GetBaseUrl(_config) + "/director/poll", token, string.Empty);
                    using (request)
                    {
                        using var response = await WebhookClient.SendAsync(request);
                        if (!response.IsSuccessStatusCode)
                            return;

                        var responseStr = await response.Content.ReadAsStringAsync();
                        response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                        response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                        response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                        if (!DirectorChannel.VerifyResponse(token, ctx, responseStr, sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                        {
                            Log.Warning("Director poll response unsigned/unverified/unbound to request; discarding.");
                            return;
                        }

                        var events = JsonSerializer.Deserialize<DirectorEvent[]>(responseStr);
                        if (events == null || events.Length == 0)
                            return;

                        var validated = events.Where(IsValidDirectorEvent).Take(DirectorChannel.MaxEventBatchSize).ToArray();
                        if (validated.Length > 0)
                            _pendingDirectorEvents.Enqueue(validated);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Director Poll failed: {ex.Message}");
                }
            });
        }

        private void FireWebhook(EntityUid actor, object payload)
        {
            // Single-read snapshot instead of separate IsReady()+GetToken() calls (Codex MEDIUM #4).
            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignOracleEnabled, out var token)
                || !DirectorChannel.TryEnterRateLimit(Channel))
                return;

            Task.Run(async () =>
            {
                try
                {
                    var json = JsonSerializer.Serialize(payload);
                    var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + "/oracle", token, json);
                    using (request)
                    {
                        using var response = await WebhookClient.SendAsync(request);

                        // UX-SIMPLE FIX 2: a non-2xx response used to just `return` here — silent to
                        // both the player AND the server log. Every branch below now both logs
                        // server-side (status code, and the daemon's structured reason when the
                        // hardened error contract is present) and queues exactly one in-fiction
                        // failure popup — never a retry, this webhook call is already done.
                        if (!response.IsSuccessStatusCode)
                        {
                            await LogOracleFailureResponseAsync(response);
                            _pendingOracleFailures.Enqueue(actor);
                            return;
                        }

                        var responseStr = await response.Content.ReadAsStringAsync();
                        response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                        response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                        response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                        if (!DirectorChannel.VerifyResponse(token, ctx, responseStr, sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                        {
                            Log.Warning("Oracle webhook response unsigned/unverified/unbound to request; discarding.");
                            _pendingOracleFailures.Enqueue(actor);
                            return;
                        }

                        var oracleResponse = JsonSerializer.Deserialize<OracleResponse>(responseStr);
                        if (oracleResponse != null && IsValidOracleResponse(oracleResponse))
                        {
                            _pendingOracleResponses.Enqueue((actor, oracleResponse));
                        }
                        else
                        {
                            Log.Warning("Oracle webhook returned a verified but malformed/invalid response body; discarding.");
                            _pendingOracleFailures.Enqueue(actor);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Covers timeouts and every other transport-level failure (DNS, connection
                    // refused, TLS, etc) — the daemon never even answered.
                    Log.Error($"Oracle Webhook failed: {ex.Message}");
                    _pendingOracleFailures.Enqueue(actor);
                }
            });
        }

        /// <summary>
        ///     Best-effort server-side diagnostic log for a failed Oracle webhook call. Always logs
        ///     the HTTP status code. If the body parses to the daemon's hardened error contract
        ///     (<c>{"error":"oracle_unavailable","reason":"missing_api_key"|"llm_error","detail":
        ///     "&lt;ExceptionClassName&gt;"}</c>, signed 503 per the daemon's current hardening),
        ///     also logs the structured reason/detail for operators. An older daemon's raw,
        ///     contract-less 500 (or ANY unparseable body) still gets a status-code-only log — this
        ///     must never throw or block the failure popup from being queued. The parsed
        ///     reason/detail are for THIS SERVER LOG ONLY: <see cref="OracleFailurePopupLocKeys"/>
        ///     never reflects them — a player must never see "missing API key" or an exception
        ///     class name, only in-fiction "the Directive did not answer" flavor.
        /// </summary>
        private async Task LogOracleFailureResponseAsync(HttpResponseMessage response)
        {
            var statusCode = (int) response.StatusCode;
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                // The body read itself failing must never suppress the status-code log or the
                // player-facing failure popup queued by the caller.
                Log.Warning(DescribeOracleFailure(statusCode, null, $"body read failed: {ex.Message}"));
                return;
            }

            Log.Warning(DescribeOracleFailure(statusCode, body));
        }

        /// <summary>
        ///     Pure decision seam, unit-testable without HTTP/IoC (mirrors this file's existing
        ///     <see cref="IsValidDirectorEvent"/>/<see cref="IsValidOracleResponse"/> idiom, and the
        ///     established repo convention of extracting pure logic out of a webhook call site
        ///     rather than faking a real HTTP transport — see
        ///     <c>DirectorChannelCanonicalizationTests.cs</c>, <c>BountyClaimRulesTests.cs</c>,
        ///     <c>SalaryRosterPayloadTests.cs</c> for the same pattern elsewhere in this codebase).
        ///     Always includes the HTTP status code. If <paramref name="body"/> parses to the
        ///     daemon's hardened error contract (<c>{"error":"oracle_unavailable","reason":
        ///     "missing_api_key"|"llm_error","detail":"&lt;ExceptionClassName&gt;"}</c>, a signed 503
        ///     per the daemon's current hardening), also includes the structured reason/detail for
        ///     operators. An older daemon's raw, contract-less 500 (null/blank/unparseable body, or
        ///     a body missing the <c>error</c> field) still yields a status-code-only line — never
        ///     throws. The returned text is for THE SERVER LOG ONLY: it must never reach a player —
        ///     see <see cref="OracleFailurePopupLocKeys"/>, which never reflects it.
        /// </summary>
        // grk review finding (untrusted daemon fields logged unbounded): a compromised or simply
        // buggy daemon could otherwise flood the server log with an arbitrarily large body/
        // reason/detail. This is a diagnostic log line, not player-facing content, but it is still
        // untrusted input — bound every daemon-authored field the same way every other Oracle DTO
        // field is bounded elsewhere in this file (MaxDialogueLength, MaxCustomNameLength, etc).
        private const int MaxFailureBodyLength = 4096;
        private const int MaxFailureFieldLength = 200;

        internal static string DescribeOracleFailure(int statusCode, string? body, string? readFailureDetail = null)
        {
            if (readFailureDetail != null)
                return $"[Oracle] Webhook call failed: HTTP {statusCode} (error body unparseable: {Truncate(readFailureDetail, MaxFailureFieldLength)}).";

            OracleErrorDto? errorDto = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(body))
                    errorDto = JsonSerializer.Deserialize<OracleErrorDto>(Truncate(body, MaxFailureBodyLength)!);
            }
            catch (JsonException)
            {
                // Falls through to the generic line below — an older daemon's raw 500 (plain text,
                // HTML, or simply no body) is an expected, non-exceptional shape here, not a bug.
            }

            return errorDto?.error != null
                ? $"[Oracle] Webhook call failed: HTTP {statusCode}, error={Truncate(errorDto.error, MaxFailureFieldLength)}, " +
                  $"reason={Truncate(errorDto.reason, MaxFailureFieldLength) ?? "(none)"}, " +
                  $"detail={Truncate(errorDto.detail, MaxFailureFieldLength) ?? "(none)"}."
                : $"[Oracle] Webhook call failed: HTTP {statusCode} " +
                  "(no recognized error contract in the response body — likely an older daemon).";
        }

        private static string? Truncate(string? text, int max) =>
            text == null ? null : text.Length > max ? text[..max] : text;

        // Strict schema validation (AGX-MERGE-PLAN.md: "typed, bounded, allowlisted fields — not
        // just HasIndex"). Applied to every inbound OracleResponse before it's queued for
        // processing, independent of whether the signature above already verified — defense in
        // depth against a compromised-but-correctly-signed sidecar.
        private static readonly HashSet<string> AllowedOracleActions = new() { "", "flash_lights", "generate_item" };
        private const int MaxDialogueLength = 500;
        private const int MaxSoundDataLength = 4096;
        private const float MaxVfxRadius = 25f;

        private static bool IsValidOracleResponse(OracleResponse r)
        {
            if (r.dialogue.Length > MaxDialogueLength)
                return false;

            if (!AllowedOracleActions.Contains(r.action))
                return false;

            if (r.amount is < 0 or > 1000)
                return false;

            if (r.soundData != null && r.soundData.Length > MaxSoundDataLength)
                return false;

            if (r.vfx != null)
            {
                if (r.vfx.radius < 0f || r.vfx.radius > MaxVfxRadius)
                    return false;

                if (r.vfx.color != null && r.vfx.color.Length > 32)
                    return false;
            }

            // PrototypeId is intentionally NOT enforced against the cosmetic allowlist here —
            // that check happens at spawn time in Update() so an oversized/malformed value still
            // fails this gate via string length, but the empty-by-default
            // CosmeticPrototypeAllowlist is the actual authorization boundary.
            return r.PrototypeId == null || r.PrototypeId.Length <= 128;
        }

        // type/action allowlist + bounded field lengths for Director-pushed events (popup/meteor/
        // spawn_paper/npc_command/camera_shake/spawn_decal). Delivery is gated by the MASTER
        // solreign.director.enabled at the poll level; the player-facing side effects are further
        // gated per event by solreign.director.broadcasts.enabled (see Update(), grk v11 P0);
        // this validator is defense in depth for whatever survives those gates.
        // grk v11 finding 5: audience_purchase and drop_weapon are REJECTED here outright — the
        // pay-to-grief handler was removed in the v11 SECURITY commit and the daemon no longer
        // sends them; keeping them validateable was a residual extension-point risk (any future
        // ungated DirectorEventReceivedEvent subscriber could have re-armed them).
        //
        // v11 front door #3 (Requisitions Anonymous): "spawn_entity" is added here as the delivery
        // half of a player-INITIATED purchase (POST /api/market/buy), not a daemon-initiated
        // broadcast — see Update()'s drain, where it is gated on solreign.market.enabled alone
        // (never solreign.director.broadcasts.enabled) and re-validated against
        // <see cref="SolreignMarketSpawnAllowlist"/> before anything is spawned.
        private static readonly HashSet<string> AllowedDirectorEventTypes = new() { "", "popup", "meteor" };
        private static readonly HashSet<string> AllowedDirectorEventActions = new() { "", "spawn_paper", "npc_command", "camera_shake", "spawn_decal", "spawn_entity", "radio_broadcast", "spawn_relic" };
        private static readonly HashSet<string> AllowedDirectorEventNames = new() { "" };

        /// <summary>
        ///     Curated allowlist of prototype ids a <c>spawn_entity</c> Director event (the
        ///     Requisitions Anonymous black-market delivery) may materialize on a buyer. Mirrors
        ///     the Director daemon's own vetted catalog
        ///     (<c>orchestrator/market_catalog.py</c>'s <c>spawn_entity_id</c> column) exactly —
        ///     cosmetic/novelty/utility/food only, no weapons/armor/contraband/combat-advantage
        ///     items, same category discipline as <see cref="CosmeticPrototypeAllowlist"/>. This is
        ///     the game-side backstop (defense in depth): even a compromised-but-correctly-signed
        ///     daemon response cannot spawn anything off this list, per <see cref="HandleMarketSpawnEntity"/>.
        /// </summary>
        internal static readonly HashSet<string> SolreignMarketSpawnAllowlist = new()
        {
            "ClothingHeadHatTophat",
            "ClothingHeadHatFedoraBrown",
            "ClothingHeadHatWizard",
            "ClothingNeckCloakMoth",
            "ClothingEyesGlassesSunglasses",
            "ClothingHeadHatPaper",
            "BikeHorn",
            "PlushieBee",
            "PlushieNuke",
            "BalloonCorgi",
            "ToyRubberDuck",
            "FoodCakePlain",
            "FoodDonkpocket",
            "DrinkGoldenCup",
            "FlashlightLantern",
            "ToolboxMechanicalFilled",
            "HandheldGPSBasic",
        };
        /// <summary>
        ///     Curated allowlist of base prototypes a <c>spawn_relic</c> event may materialize —
        ///     mirrors the Director daemon's own vetted <c>RELIC_BASES</c>
        ///     (<c>orchestrator/relics.py</c>) exactly: cosmetic/utility trophies only, no weapons
        ///     ("relics are trophies, not arms"). Same game-side backstop discipline as
        ///     <see cref="SolreignMarketSpawnAllowlist"/>.
        /// </summary>
        internal static readonly HashSet<string> RelicPrototypeAllowlist = new()
        {
            "Crowbar",
            "FireExtinguisher",
            "ToolboxMechanicalFilled",
            "Welder",
            "Wrench",
            "Wirecutter",
            "FlashlightLantern",
            "ClothingHeadHatTophat",
        };

        private const int MaxEventTextLength = 300;
        internal const int MaxCustomNameLength = 60;
        internal const int MaxCustomDescLength = 200;
        private const int MaxAudioDataLength = 200_000;

        /// <summary>
        ///     Requisitions Anonymous delivery: materializes a black-market purchase on its buyer.
        ///     Called only from the <c>spawn_entity</c> branch of the Director-event drain above,
        ///     already gated on <see cref="CCVars.SolreignMarketEnabled"/>. Every input is
        ///     re-validated here as defense in depth (never trust that the gate + daemon signature
        ///     alone are enough): the prototype id must be on <see cref="SolreignMarketSpawnAllowlist"/>
        ///     AND actually resolve, and <c>targetPlayerId</c> must parse as a GUID and resolve to a
        ///     currently-connected, currently-attached session. Any failure logs and drops — this
        ///     method must NEVER spawn anything off-allowlist or on an unresolvable target.
        /// </summary>
        private void HandleMarketSpawnEntity(DirectorEvent evt)
        {
            if (string.IsNullOrEmpty(evt.text) || !SolreignMarketSpawnAllowlist.Contains(evt.text))
            {
                Log.Warning($"[Market] Rejected spawn_entity for non-allowlisted prototype '{evt.text}'.");
                return;
            }

            // Not just allowlist membership: the prototype must actually be loaded, same
            // "existence alone is not authorization, but authorization without existence is a
            // crash" discipline as CosmeticPrototypeAllowlist's generate_item gate above.
            if (!_prototypeManager.HasIndex<EntityPrototype>(evt.text))
            {
                Log.Warning($"[Market] Rejected spawn_entity: allowlisted prototype '{evt.text}' has no loaded EntityPrototype.");
                return;
            }

            // IsValidDirectorEvent already required targetPlayerId to be empty-or-GUID; spawn_entity
            // additionally requires it to be non-empty — there is no "no target" delivery.
            if (string.IsNullOrEmpty(evt.targetPlayerId) || !Guid.TryParse(evt.targetPlayerId, out var targetGuid))
            {
                Log.Warning("[Market] Rejected spawn_entity with missing/invalid targetPlayerId.");
                return;
            }

            if (!_playerManager.TryGetSessionById(new NetUserId(targetGuid), out var session) || session.AttachedEntity is not { } target)
            {
                Log.Warning($"[Market] Rejected spawn_entity: target player {targetGuid} is not connected/attached.");
                return;
            }

            Spawn(evt.text, Transform(target).Coordinates);
            _popupSystem.PopupEntity(Loc.GetString("solreign-market-popup-delivery"), target, target, PopupType.Medium);
        }

        // internal (not private) for pure unit tests — Content.Server has
        // [InternalsVisibleTo("Content.Tests")], same route the DirectorChannel
        // canonicalization tests use.
        internal static bool IsValidDirectorEvent(DirectorEvent evt)
        {
            if (!AllowedDirectorEventTypes.Contains(evt.type))
                return false;

            if (!AllowedDirectorEventActions.Contains(evt.action))
                return false;

            if (!AllowedDirectorEventNames.Contains(evt.EventName))
                return false;

            if (evt.message.Length > MaxEventTextLength || evt.text.Length > MaxEventTextLength || evt.command.Length > MaxEventTextLength)
                return false;

            if (evt.audioData.Length > MaxAudioDataLength)
                return false;

            // v11 spawn_relic flavor fields — bounded like every other daemon-authored string.
            if (evt.customName.Length > MaxCustomNameLength || evt.customDesc.Length > MaxCustomDescLength)
                return false;

            if (!string.IsNullOrEmpty(evt.targetPlayerId) && !Guid.TryParse(evt.targetPlayerId, out _))
                return false;

            return true;
        }
    }
}
