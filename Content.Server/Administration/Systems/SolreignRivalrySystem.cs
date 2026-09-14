using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Log;

namespace Content.Server.Administration.Systems
{
    /// <summary>
    ///     GREEN (AGX-MERGE-PLAN.md item 1): telemetry-only. Fires on death (not every damage
    ///     tick) between two actor-controlled entities and POSTs attacker/victim ids to the
    ///     external Director. No persistence, no player-facing text — raw combat telemetry only.
    ///     Routes through <see cref="DirectorChannel"/>: off by default
    ///     (<see cref="CCVars.SolreignRivalryEnabled"/>), fails closed without a configured
    ///     Director token, signs every outbound POST, and is rate-limited independent of the
    ///     event cadence that triggers it.
    /// </summary>
    public sealed partial class SolreignRivalrySystem : EntitySystem
    {
        [Dependency] private IConfigurationManager _config = default!;
        [Dependency] private SeasonLedgerSystem _ledger = default!;

        private const string Channel = "rivalry";

        private static readonly HttpClient WebhookClient = new HttpClient();

        /// <summary>
        ///     Called by <see cref="SolreignDeathTelemetrySystem"/> (the single owner of the
        ///     (ActorComponent, MobStateChangedEvent) subscription — the event bus forbids two
        ///     systems subscribing to the same pair) for every player death. Death-only gating
        ///     (AGX-MERGE-PLAN.md item 1b) happens there.
        /// </summary>
        public void ReportDeath(Entity<ActorComponent> ent, EntityUid? origin)
        {
            // Single-read snapshot instead of separate IsReady()+GetToken() calls (Codex MEDIUM #4).
            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignRivalryEnabled, out var token))
                return;

            // v11 fix: send PLAYER GUIDs, not EntityUids. The daemon keys feuds and standing by
            // player GUID (the identity the Oracle webhook and Season Ledger report); EntityUids
            // are round-local and recycled, so they silently broke cross-round feud persistence
            // and the Crypt's legendary-standing join. Resolution lives in DeathAttribution
            // (shared with SolreignCryptSystem), which keeps the Codex MEDIUM #7 projectile-
            // shooter hop: direct melee origins carry ActorComponent, projectile kills resolve
            // one hop through ProjectileComponent.Shooter, everything else fails to attribute.
            if (!DeathAttribution.TryGetPlayerGuid(EntityManager, ent.Owner, out var victimGuid))
                return;

            if (!DeathAttribution.TryResolveAttackerGuid(EntityManager, origin, out var attackerGuid))
                return;

            // A self-inflicted death is not a rivalry — don't feed the daemon a (a,a) feud pair.
            if (attackerGuid == victimGuid)
                return;

            if (!DirectorChannel.TryEnterRateLimit(Channel))
                return;

            // Also persist locally as requested
            if (Guid.TryParse(attackerGuid, out var ag) && Guid.TryParse(victimGuid, out var vg))
                _ = _ledger.RecordRivalryEventAsync(ag, vg);

            // Match the daemon's canonical API: POST /api/rivalries/event { attacker_guid, victim_guid }.
            var payload = new
            {
                attacker_guid = attackerGuid,
                victim_guid = victimGuid
                // screenshotData placeholder field removed (AGX-GREEN cleanup) — it was a
                // "mock_base64_image..." string with no capture logic behind it; don't ship a
                // fake data contract to the Director.
            };

            Task.Run(async () =>
            {
                try
                {
                    var json = JsonSerializer.Serialize(payload);
                    var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + "/api/rivalries/event", token, json);
                    using (request)
                    {
                        using var response = await WebhookClient.SendAsync(request);

                        // Fire-and-forget telemetry: no response body is consumed or acted on, so
                        // there is nothing further to schema-validate here. Still verify the
                        // signature (now request-bound via ctx/nonce, Codex HIGH #2) if the
                        // sidecar chooses to sign its ack, to keep the log honest about whether
                        // the round-trip was authenticated.
                        if (response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                            response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                            response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                            var verified = DirectorChannel.VerifyResponse(
                                token,
                                ctx,
                                body,
                                sigVals is { } sv ? System.Linq.Enumerable.FirstOrDefault(sv) : null,
                                tsVals is { } tv ? System.Linq.Enumerable.FirstOrDefault(tv) : null,
                                nonceVals is { } nv ? System.Linq.Enumerable.FirstOrDefault(nv) : null);
                            if (!verified)
                                Log.Warning("Rivalry Webhook ack unsigned/unverified/unbound to request; ignoring response body.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Rivalry Webhook failed: {ex.Message}");
                }
            });
        }

    }
}
