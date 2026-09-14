using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.Providence;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Log;

namespace Content.Server.Administration.Systems
{
    /// <summary>
    ///     v11 backbone #3: reports player deaths to the Director daemon's Crypt
    ///     (<c>POST /api/crypt/death</c>). The DAEMON decides whether a death is "legendary"
    ///     (high standing or a nemesis kill) and generates the obituary/relic — the game only
    ///     reports the raw fact, as player GUIDs (never round-local EntityUids, see
    ///     <see cref="DeathAttribution"/>). Unlike Rivalry, an unattributable death still
    ///     reports with an empty <c>attacker_guid</c>: a high-standing player dying to the
    ///     environment is still a legendary death.
    ///
    ///     Telemetry-only, no player-facing content. Routes through
    ///     <see cref="DirectorChannel"/>: off by default (<see cref="CCVars.SolreignCryptEnabled"/>
    ///     plus the master <see cref="CCVars.SolreignDirectorEnabled"/>), fails closed without a
    ///     configured Director token, signs every outbound POST, and is rate-limited independent
    ///     of the event cadence that triggers it (mass-casualty bursts may drop reports — an
    ///     accepted tradeoff shared with the Rivalry channel; the daemon filters legendary
    ///     anyway).
    /// </summary>
    public sealed partial class SolreignCryptSystem : EntitySystem
    {
        [Dependency] private IConfigurationManager _config = default!;

        /// <summary>Per-death telemetry rate-limit channel.</summary>
        public const string Channel = "crypt";

        /// <summary>
        ///     FD-W3 review fix (grk r1 #1): the first-death memorial POST gets its OWN rate-limit
        ///     channel. The same death always fires <see cref="ReportDeath"/> first, and the claim
        ///     continuation resumes well inside <see cref="DirectorChannel.MinRequestInterval"/> —
        ///     on a shared channel the once-per-account-EVER memorial would be systematically
        ///     dropped with no retry. Once-ever cadence means this channel cannot spam.
        /// </summary>
        public const string FirstDeathChannel = "crypt_first_death";

        private static readonly HttpClient WebhookClient = new HttpClient();

        /// <summary>
        ///     Called by <see cref="SolreignDeathTelemetrySystem"/> (the single owner of the
        ///     (ActorComponent, MobStateChangedEvent) subscription — the event bus forbids two
        ///     systems subscribing to the same pair) for every player death.
        /// </summary>
        public void ReportDeath(Entity<ActorComponent> ent, EntityUid? origin)
        {
            // Single-read snapshot instead of separate IsReady()+GetToken() calls (Codex MEDIUM #4).
            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignCryptEnabled, out var token))
                return;

            if (!DeathAttribution.TryGetPlayerGuid(EntityManager, ent.Owner, out var victimGuid))
                return;

            // Best-effort attacker attribution — a miss is fine (environmental death), a
            // self-kill is reported unattributed rather than as its own nemesis.
            DeathAttribution.TryResolveAttackerGuid(EntityManager, origin, out var attackerGuid);
            if (attackerGuid == victimGuid)
                attackerGuid = null;

            if (!DirectorChannel.TryEnterRateLimit(Channel))
                return;

            // Match the daemon's canonical API: POST /api/crypt/death { victim_guid, attacker_guid }.
            // attacker_guid is a required string field daemon-side; empty means "no attacker" (the
            // daemon only runs its nemesis check `if attacker_guid`).
            var payload = new
            {
                victim_guid = victimGuid,
                attacker_guid = attackerGuid ?? string.Empty
            };

            PostDeathReport(token, JsonSerializer.Serialize(payload));
        }

        /// <summary>
        ///     FD-W3 (FIRST-DEATH-SPEC §5): reports a CLAIMED authored first death with the additive
        ///     payload extension — the game-templated epitaph, closed-map cause label, and career
        ///     snapshot — so the daemon's no-LLM <c>handle_first_death</c> path can mint the public
        ///     crypt memorial. Called by <c>ProvidenceFirstDeathSystem</c> exactly once per account,
        ///     ever (downstream of the atomic ledger claim), NOT per death: the per-death telemetry
        ///     report above keeps firing unchanged beside it.
        ///
        ///     Same posture as <see cref="ReportDeath"/> for the gate and the signed
        ///     fire-and-forget POST (crypt CVar + master + token; daemon absent/off → silently
        ///     skipped, the in-game scene unaffected, spec rail 4) — but on its OWN rate-limit
        ///     channel: see <see cref="FirstDeathChannel"/>.
        ///
        ///     FD-W3.5: returns <c>true</c> iff the report passed every gate and was HANDED to the
        ///     fire-and-forget sender — the strongest success signal this wire can give (delivery
        ///     itself is never confirmed on any crypt path). Callers use it to stamp the claim
        ///     row's <c>crypt_reported</c> flag, the sole dedupe between the claim-time wire and
        ///     the round-start plaque backfill (the daemon mints one plaque per POST, NOT per
        ///     victim). <c>false</c> = gates closed or rate-limited: nothing was sent, the row
        ///     stays banked for a later backfill pass.
        /// </summary>
        public bool ReportFirstDeath(FirstDeathCryptReport report)
        {
            var token = string.Empty;
            var ready = FirstDeathGateOverrideForTests is { } gate
                ? gate()
                : DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignCryptEnabled, out token);

            if (!ready)
                return false;

            if (!DirectorChannel.TryEnterRateLimit(FirstDeathChannel))
                return false;

            var json = JsonSerializer.Serialize(report);

            if (FirstDeathPostOverrideForTests is { } post)
            {
                post(json);
                return true;
            }

            // Fail closed if a test forced the gate open without supplying a sender: an empty
            // token must never sign or send anything (unreachable in production — the real gate
            // above only passes with a well-formed token).
            if (token.Length == 0)
                return false;

            PostDeathReport(token, json);
            return true;
        }

        /// <summary>
        ///     True when the first-death crypt channel would accept a report right now (gates
        ///     only — the rate limiter is per-attempt). The round-start plaque backfill's scan
        ///     early-out (FD-W3.5): a closed channel means banked rows are not even scanned, never
        ///     consumed. Honors <see cref="FirstDeathGateOverrideForTests"/> so tests drive the
        ///     scan and the report through one consistent gate.
        /// </summary>
        public bool FirstDeathChannelReady =>
            FirstDeathGateOverrideForTests?.Invoke()
            ?? DirectorChannel.IsReady(_config, CCVars.SolreignCryptEnabled);

        /// <summary>
        ///     Integration-test seam (FD-W3.5): overrides the fail-closed readiness gate for
        ///     first-death reports ONLY (per-death telemetry is untouched). The test pool's
        ///     offline law forbids flipping the real Director master CVar — that would arm the
        ///     Oracle poll loop's genuine outbound HTTP — so the open-gate direction of the chain
        ///     is driven through this seam, ALWAYS paired with
        ///     <see cref="FirstDeathPostOverrideForTests"/> (an empty-token send fails closed
        ///     above). The closed-gate direction rides the REAL chain (production-default CVars).
        /// </summary>
        internal Func<bool>? FirstDeathGateOverrideForTests;

        /// <summary>Integration-test seam — see <see cref="ReportFirstDeath"/>. When set, replaces
        /// the signed HTTP sender for first-death reports only; receives the serialized payload.</summary>
        internal Action<string>? FirstDeathPostOverrideForTests;

        /// <summary>The one signed fire-and-forget sender both report shapes share. Callers must
        /// already have passed <see cref="DirectorChannel.TryGetReadyToken"/> and
        /// <see cref="DirectorChannel.TryEnterRateLimit"/>.</summary>
        private void PostDeathReport(string token, string json)
        {
            Task.Run(async () =>
            {
                try
                {
                    var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + "/api/crypt/death", token, json);
                    using (request)
                    {
                        using var response = await WebhookClient.SendAsync(request);

                        // Fire-and-forget telemetry: no response body is consumed or acted on.
                        // Still verify the signature (request-bound via ctx/nonce) if the daemon
                        // signs its ack, to keep the log honest about whether the round-trip was
                        // authenticated.
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
                                Log.Warning("Crypt death-report ack unsigned/unverified/unbound to request; ignoring response body.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Crypt death-report failed: {ex.Message}");
                }
            });
        }
    }
}
