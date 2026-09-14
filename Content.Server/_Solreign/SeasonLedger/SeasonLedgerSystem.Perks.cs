using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Enums;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     AGX-GREEN/AGX-HARDEN split (AGX-MERGE-PLAN.md "SeasonLedgerSystem.Perks.cs — split the two
///     perks it actually contains"): this file used to bundle a cosmetic <c>GoldenName</c> title
///     perk with a <c>StartingGear</c> branch that spawned a contraband/weapon-bearing loadout on
///     player-attach, keyed off an unauthenticated Director response, ahead of any storefront/
///     OAuth authorization gate. The <c>StartingGear</c> branch has been deleted entirely (not
///     merged, not just disabled) — the removed code is preserved at
///     docs/agx-hold/StartingGear-perk.patch for a future clause-6 PR once a storefront/OAuth
///     side exists and John signs off. Only the cosmetic <c>GoldenName</c> path remains, and it
///     now routes through <see cref="DirectorChannel"/>: off by default
///     (<see cref="CCVars.SolreignPerksEnabled"/>), fails CLOSED (denies the perk) on a missing
///     token, an unsuccessful/unverified sidecar response, or any sidecar error — per audit S7,
///     "fail closed" means no perk is granted, never a default-grant.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly HttpClient WebhookClient = new HttpClient();
    private void InitializePerks()
    {
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttachedForPerks);
    }

    private void OnPlayerAttachedForPerks(PlayerAttachedEvent ev)
    {
        var mob = ev.Entity;
        var userId = ev.Player.UserId.UserId;
        ApplyPerksAsync(mob, userId);
    }

    private void ApplyPerksAsync(EntityUid mob, Guid userId)
    {
        // Fail CLOSED: no CVar, no well-formed token -> no request goes out and no perk is ever
        // granted. Single-read snapshot instead of separate IsReady()+GetToken() (Codex MEDIUM #4).
        if (!DirectorChannel.TryGetReadyToken(_cfg, CCVars.SolreignPerksEnabled, out var token))
            return;

        if (!DirectorChannel.TryEnterRateLimit(SolreignPerkRules.RateLimitChannel(userId)))
            return;

        FetchPerksAsync(mob, userId, token);
    }

    private async void FetchPerksAsync(EntityUid mob, Guid userId, string token)
    {
        try
        {
            var body = SolreignPerkRules.BuildRequestBody(userId);
            var (request, ctx) = DirectorChannel.BuildSignedRequest(
                HttpMethod.Post,
                $"{DirectorChannel.GetBaseUrl(_cfg)}/player_perks",
                token,
                body);
            using (request)
            {
                using var response = await WebhookClient.SendAsync(request);
                // Deny on sidecar error (audit S7) — any non-success status means no perk, full stop.
                if (!response.IsSuccessStatusCode)
                    return;

                var json = await response.Content.ReadAsStringAsync();

                response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                if (!DirectorChannel.VerifyResponse(token, ctx, json, sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                {
                    Log.Warning("Perk fetch response unsigned/unverified/unbound to request; denying perk.");
                    return;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Strict schema validation (AGX-MERGE-PLAN.md): only a typed boolean GoldenName
                // field is recognized (GetBoolean() throws -> caught below -> denies the perk --
                // for anything that isn't actually a JSON bool). Any other field (including a
                // resurrected StartingGear key) is ignored — there is no code path left in this
                // file that can act on it.
                if (root.TryGetProperty("GoldenName", out var goldenNameProp)
                    && goldenNameProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && goldenNameProp.GetBoolean())
                {
                    _pendingPerks.Enqueue(new PendingPerk(mob, userId, "GoldenName"));
                }
            }
        }
        catch (Exception e)
        {
            // Deny on sidecar error (audit S7): exceptions never fall through to granting a perk.
            Log.Error($"Perk fetch failed closed ({e.GetType().Name}).");
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<PendingPerk> _pendingPerks = new();

    private readonly record struct PendingPerk(EntityUid Mob, Guid ExpectedUserId, string PerkType);

    private void UpdatePerks()
    {
        // Codex LOW #8 (the Perks-specific instance of MEDIUM #3): a perk fetched while
        // solreign.perks.enabled was on must not still be granted after the kill switch flips
        // off between enqueue and this drain. Recheck readiness immediately before applying
        // anything, and discard the whole queue rather than trickle-apply once disabled — a
        // runtime kill switch must stop queued effects, not just new ones.
        if (!DirectorChannel.IsReady(_cfg, CCVars.SolreignPerksEnabled))
        {
            _pendingPerks.Clear();
            return;
        }

        while (_pendingPerks.TryDequeue(out var perk))
        {
            // Codex v2 re-review (check-then-drain race): the CVar could flip off mid-drain of a
            // large queue — the check above only proves readiness at the START of this Update().
            // Recheck per item (a cheap CVar read) so disabling the switch stops further grants
            // immediately instead of only before the next Update() tick.
            if (!DirectorChannel.IsReady(_cfg, CCVars.SolreignPerksEnabled))
            {
                _pendingPerks.Clear();
                return;
            }

            // The Director reply is asynchronous. The original player may have disconnected,
            // transferred bodies, or the entity may now be controlled by another account.
            // Bind the grant to the still-attached account that caused the signed request.
            if (Deleted(perk.Mob) ||
                !TryComp<ActorComponent>(perk.Mob, out var actor) ||
                !SolreignPerkRules.IsStillOwnedByExpectedUser(
                    perk.ExpectedUserId,
                    actor.PlayerSession.UserId.UserId))
                continue;

            if (perk.PerkType == "GoldenName")
            {
                var comp = EnsureComp<SeasonTitleComponent>(perk.Mob);
                var previousTitle = comp.Title;
                comp.HasGoldenNamePerk = true;
                comp.Title = SolreignPerkRules.ApplyGoldenName(comp.Title, enabled: true);
                StampIdCardStanding(perk.Mob, comp, previousTitle);
            }

            // StartingGear branch removed entirely — see docs/agx-hold/StartingGear-perk.patch
            // and AGX-MERGE-PLAN.md. Re-enters scope only with a storefront/OAuth authorization
            // side and John's sign-off (clause 6b).
        }
    }
}
