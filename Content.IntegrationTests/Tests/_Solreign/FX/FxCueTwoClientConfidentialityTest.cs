#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._Solreign.FX;
using Content.Client.IoC;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Changeling;
using Content.Server._Solreign.FX;
using Content.Shared._Solreign.Changeling;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign.FX;

/// <summary>
///     W5 (`fx-confidentiality-tests`) — the FULL two-client confidentiality proof of spec
///     <c>docs/specs/FX-LANGUAGE-V1-SPEC-2026-07-16.md</c> §5.3 plus §7's PVS-scoping row, the
///     final wave of FX Language v1. W4 already shipped a deliberately-minimal preview
///     (<see cref="FxArmBladeConfidentialityIntegrationTest"/>: detail-to-actor-only,
///     generic-to-bystander-only, scrubbed fields, independent seeds, raw-wire assertions); this
///     fixture adds the two §5.3/§7 properties W4's receipt explicitly left to W5:
///
///     1. <b>Gap-free broadcast CorrelationId sequence</b> (§5.3.5, grk #6): across a whole
///        scenario — plain broadcast cues interleaved with TWO secret-role emits (Extend AND
///        Retract) — a bystander's received broadcast-stream CorrelationIds are strictly
///        consecutive. The session-targeted detail cues were minted from an independent counter
///        namespace, so their emission leaves no observable hole a bystander could use to infer
///        "a private cue fired near me just now."
///
///     2. <b>PVS scoping actually applied, never assumed</b> (§7, grk #3): with the engine's real
///        PVS culling enabled (<c>net.pvs</c> — the pooled test harness disables it by default,
///        which makes <c>Filter.Pvs</c> degenerate to all-players and would silently vacuate this
///        property), a connected client far outside PVS range of an in-PVS fire receives ZERO
///        cues on the raw wire — not the generic, not the detail, nothing.
///
///     Everything runs against the REAL consumer (the W4 changeling arm-blade,
///     <c>SolreignChangelingSystem.ArmBlade.cs</c>) and the real W2 raise path
///     (<see cref="SolreignFxServerSystem"/>), behind the default-on
///     <c>solreign.fx.cue_v1</c> master kill switch with prior state restored per test.
///
///     Two-real-client wiring follows <c>PlayerDelightTwoClientWireIntegrationTest.cs</c>'s
///     template via W4's <see cref="FxArmBladeConfidentialityIntegrationTest"/> — that class's
///     helpers are private to it, so the minimal second-client lifecycle subset is reproduced
///     here (same rationale both prior files documented).
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class FxCueTwoClientConfidentialityTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // These raw-wire tests must not inherit queued default-on FX events whose entity anchors
        // were deleted while a previous real-ticker pair was recycled.
        Fresh = true,
        DummyTicker = false,
    };

    /// <summary>
    ///     Spec §5.3, the full scenario: interleaves plain broadcast cues (<c>impact_light</c> via
    ///     <see cref="SolreignFxServerSystem.RaiseCue"/> — the non-secret path) with BOTH halves of
    ///     the arm-blade cycle (Extend + Retract, each a <see cref="SolreignFxServerSystem.RaiseSecretRoleCue"/>
    ///     pair-emit), then proves, per client:
    ///
    ///     Actor: real detail cues with the real non-default parameters for BOTH beats, exactly one
    ///     accepted activation per emit (§5.3.6 — no double delivery), never the generic (raw-wire
    ///     checked), detail CorrelationIds consecutive in their own targeted namespace.
    ///
    ///     Bystander: never the real id (raw-wire checked), both generic cues scrubbed to the
    ///     generic prototype's fixed defaults — including Retract, whose real detail duration
    ///     (0.6s) DIFFERS from the generic default (1.2s), a stronger fingerprinting probe than
    ///     W4's Extend-only check where the two durations coincide — independent seeds per pair,
    ///     and (§5.3.5) a strictly consecutive broadcast CorrelationId sequence across all five
    ///     broadcast cues even though two targeted detail cues were minted in between.
    /// </summary>
    [Test]
    public async Task ArmBladeCycle_BystanderBroadcastStreamIsScrubbedAndGapFree_ActorGetsExactlyOneDetailPerEmit()
    {
        const string bystanderUsername = "SolreignFxW5Bystander";

        await WithSecondClient(bystanderUsername, async bystander =>
        {
            EntityUid changelingUid = default;
            var cvarFlipped = false;
            var fxWasEnabled = false;
            var worldFeedbackWasEnabled = false;
            var worldFeedbackCvarFlipped = false;

            try
            {
                await Server.WaitAssertion(() =>
                {
                    // This scenario asserts an exact five-cue stream raised below. Isolate it from
                    // ordinary damage feedback, which is an independent default-on FX producer.
                    worldFeedbackWasEnabled =
                        Server.CfgMan.GetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled);
                    if (worldFeedbackWasEnabled)
                    {
                        Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled, false);
                        worldFeedbackCvarFlipped = true;
                    }

                    var players = Server.ResolveDependency<IPlayerManager>();
                    Assert.That(players.TryGetSessionByUsername(bystanderUsername, out var bystanderSession), Is.True);
                    var actorSession = players.Sessions.Single(s => s.Name != bystanderUsername);

                    var mapSystem = Server.EntMan.System<SharedMapSystem>();
                    mapSystem.CreateMap(out var mapId);

                    // Same body note as W4's sibling test: MobLing is just a convenient humanoid
                    // body; the FX cue is raised identically regardless of body, and the real-antag
                    // -body coverage lives in SolreignChangelingTransformCycleIntegrationTest.
                    changelingUid = Server.EntMan.SpawnEntity("MobLing", new MapCoordinates(0f, 0f, mapId));
                    Server.EntMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);
                    Assert.That(players.SetAttachedEntity(actorSession, changelingUid), Is.True);

                    // Bystander well within PVS range (and the pool disables net.pvs anyway, so
                    // Filter.Pvs adds all players here — the out-of-range property is the OTHER
                    // test's job, with culling actually enabled).
                    var bystanderUid = Server.EntMan.SpawnEntity("MobHuman", new MapCoordinates(2f, 0f, mapId));
                    Assert.That(players.SetAttachedEntity(bystanderSession!, bystanderUid), Is.True);
                    players.JoinGame(bystanderSession!);

                    fxWasEnabled = Server.CfgMan.GetCVar(CCVars.SolreignFxCueV1Enabled);
                    if (!fxWasEnabled)
                    {
                        Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, true);
                        cvarFlipped = true;
                    }
                });
                await RunThreeWayTicks(bystander, 10);

                // Clean slate immediately before the scenario (same grk-mandated discipline as W4).
                await Client.WaitAssertion(() => Client.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());
                await bystander.WaitAssertion(() => bystander.EntMan.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());

                // The scenario: broadcast / EXTEND-pair / broadcast / RETRACT-pair / broadcast,
                // each on its own tick (the egress budget coalesces identical (EffectId, anchor)
                // raises only WITHIN a tick; spacing them out keeps every raise a real wire emit).
                await RaiseImpactLightBroadcast(changelingUid);
                await RunThreeWayTicks(bystander, 4);

                await ToggleArmBlade(changelingUid); // Extend
                await RunThreeWayTicks(bystander, 4);

                await RaiseImpactLightBroadcast(changelingUid);
                await RunThreeWayTicks(bystander, 4);

                await ToggleArmBlade(changelingUid); // Retract
                await RunThreeWayTicks(bystander, 4);

                await RaiseImpactLightBroadcast(changelingUid);
                await RunThreeWayTicks(bystander, 8);

                uint extendDetailSeed = 0;
                uint retractDetailSeed = 0;
                var actorImpactCorrelationIds = new List<uint>();

                await Client.WaitAssertion(() =>
                {
                    var system = Client.System<SolreignFxCueSystem>();

                    // Raw-wire proof the actor exclusion held for BOTH pair-emits (W4 only had one).
                    Assert.That(system.RawReceivedEffectIdsForTests.Contains("transformation_generic"), Is.False,
                        "The actor must never receive the redacted broadcast ON THE WIRE for either the Extend or the Retract emit (actor exclusion, spec §5.2 item 1).");

                    var details = system.AcceptedCuesForTests.Where(c => c.EffectId == "transformation").ToList();
                    Assert.That(details, Has.Count.EqualTo(2),
                        "Exactly ONE detail activation per emit (Extend + Retract = 2 total) — no double delivery, no dropped beat (spec §5.3.6).");

                    // Extend beat: the real, non-default reveal parameters from ArmBlade.cs.
                    Assert.That(details[0].Intensity, Is.EqualTo(0.85f).Within(0.001f));
                    Assert.That(details[0].Scale, Is.EqualTo(1.1f).Within(0.001f));
                    Assert.That(details[0].Duration, Is.EqualTo(1.2f).Within(0.001f));
                    Assert.That(details[0].PaletteIndex, Is.EqualTo((byte?) 1));
                    Assert.That(details[0].Phase, Is.EqualTo((byte?) 0));
                    extendDetailSeed = details[0].Seed;

                    // Retract beat: the lesser undo beat's own distinct real parameters.
                    Assert.That(details[1].Intensity, Is.EqualTo(0.5f).Within(0.001f));
                    Assert.That(details[1].Scale, Is.EqualTo(0.9f).Within(0.001f));
                    Assert.That(details[1].Duration, Is.EqualTo(0.6f).Within(0.001f));
                    Assert.That(details[1].PaletteIndex, Is.EqualTo((byte?) 2));
                    Assert.That(details[1].Phase, Is.EqualTo((byte?) 1));
                    retractDetailSeed = details[1].Seed;

                    // §5.3.5's counter-namespace property from the ACTOR's side: the two detail
                    // cues are consecutive in their OWN (targeted) namespace — minting them never
                    // consumed a broadcast-namespace id.
                    Assert.That(details[1].CorrelationId, Is.EqualTo(details[0].CorrelationId + 1),
                        "The two session-targeted detail cues must be consecutive in the targeted counter namespace.");

                    actorImpactCorrelationIds = system.AcceptedCuesForTests
                        .Where(c => c.EffectId == "impact_light")
                        .Select(c => c.CorrelationId)
                        .ToList();
                    Assert.That(actorImpactCorrelationIds, Has.Count.EqualTo(3),
                        "The actor is NOT excluded from plain (non-secret) broadcast cues — all three impact_light cues arrive.");
                });

                await bystander.WaitAssertion(() =>
                {
                    var system = bystander.EntMan.System<SolreignFxCueSystem>();

                    // Raw-wire proof (same strength as W4's, now across TWO secret-role emits).
                    Assert.That(system.RawReceivedEffectIdsForTests.Contains("transformation"), Is.False,
                        "A bystander must never receive the real 'transformation' id ON THE WIRE for either emit.");

                    // The bystander's whole received world, in receipt order — exactly the five
                    // broadcast cues, in raise order, nothing else.
                    Assert.That(system.RawReceivedEffectIdsForTests, Is.EqualTo(new[]
                    {
                        "impact_light", "transformation_generic", "impact_light", "transformation_generic", "impact_light",
                    }), "The bystander's raw wire stream must be exactly the five broadcast cues in raise order — nothing extra, nothing missing, nothing secret.");

                    var accepted = system.AcceptedCuesForTests.ToList();
                    Assert.That(accepted, Has.Count.EqualTo(5),
                        "An honest bystander client accepts all five broadcast cues (they are all well-formed).");

                    // Both generic cues scrubbed to transformation_generic's OWN fixed defaults
                    // (effects.yml: intensity 0.5 / scale 1.0 / duration 1.2, single neutral
                    // palette/phase entry). The RETRACT check is the sharper one: the real detail
                    // duration was 0.6s, so a leaked value would be plainly visible here.
                    foreach (var generic in accepted.Where(c => c.EffectId == "transformation_generic"))
                    {
                        Assert.That(generic.Intensity, Is.EqualTo(0.5f).Within(0.001f));
                        Assert.That(generic.Scale, Is.EqualTo(1.0f).Within(0.001f));
                        Assert.That(generic.Duration, Is.EqualTo(1.2f).Within(0.001f),
                            "Both generic cues must carry the generic prototype's default duration — the Retract detail's real 0.6s must never leak into its cover cue.");
                        Assert.That(generic.PaletteIndex, Is.Null.Or.EqualTo((byte?) 0));
                        Assert.That(generic.Phase, Is.Null.Or.EqualTo((byte?) 0));
                    }

                    var generics = accepted.Where(c => c.EffectId == "transformation_generic").ToList();
                    Assert.That(generics, Has.Count.EqualTo(2));

                    // Independent seeds per pair (spec §1.2/§5.2, grk #6) — for BOTH emits.
                    Assert.That(generics[0].Seed, Is.Not.EqualTo(extendDetailSeed),
                        "The Extend pair's generic and detail cues must be independently seeded.");
                    Assert.That(generics[1].Seed, Is.Not.EqualTo(retractDetailSeed),
                        "The Retract pair's generic and detail cues must be independently seeded.");

                    // THE W5 headline (spec §5.3.5, grk #6): the bystander's broadcast-stream
                    // CorrelationId sequence is strictly consecutive across the whole scenario.
                    // Two session-targeted detail cues were minted in the middle of it; had they
                    // shared the broadcast counter, ids would skip exactly where the secret emits
                    // happened and a traffic-inspecting bystander could count private cues firing
                    // near them. Assert +1 adjacency pairwise (not just monotonicity).
                    var broadcastIds = accepted.Select(c => c.CorrelationId).ToList();
                    for (var i = 1; i < broadcastIds.Count; i++)
                    {
                        Assert.That(broadcastIds[i], Is.EqualTo(broadcastIds[i - 1] + 1),
                            $"Broadcast CorrelationId sequence must be gap-free: position {i} received id {broadcastIds[i]} after {broadcastIds[i - 1]} — a gap here means a targeted cue consumed a broadcast-namespace id, an observable side channel (spec §1.3a.5).");
                    }

                    // Cross-client consistency: the impact cues the actor saw are the same wire
                    // objects (same broadcast-namespace ids) the bystander saw — the actor's
                    // stream differs from the bystander's ONLY at the secret-role pair boundary.
                    var bystanderImpactIds = accepted
                        .Where(c => c.EffectId == "impact_light")
                        .Select(c => c.CorrelationId)
                        .ToList();
                    Assert.That(bystanderImpactIds, Is.EqualTo(actorImpactCorrelationIds),
                        "Actor and bystander must observe the SAME three non-secret broadcast cues (same CorrelationIds).");
                });
            }
            finally
            {
                if (cvarFlipped || worldFeedbackCvarFlipped)
                {
                    await Server.WaitPost(() =>
                    {
                        if (cvarFlipped)
                            Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, fxWasEnabled);

                        if (worldFeedbackCvarFlipped)
                            Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled,
                                worldFeedbackWasEnabled);
                    });
                    await RunThreeWayTicks(bystander, 2);
                }
            }
        });
    }

    /// <summary>
    ///     Spec §7's "Integration — PVS scoping" row (grk #3): <c>Filter.Pvs</c> actually applied,
    ///     never assumed. The pooled test harness runs with <c>net.pvs = false</c>
    ///     (Robust.UnitTesting's PoolManager default), under which <c>Filter.Pvs</c> degenerates to
    ///     "all players" (<c>Filter.AddPlayersByPvs</c> checks the CVar first) — so W4's
    ///     confidentiality test, and any test like it, exercises the actor-exclusion and scrub
    ///     logic but can never falsify the PVS boundary itself. This test enables the engine's
    ///     real culling (<c>net.pvs = true</c>, restored in finally on the already-Dirty pool),
    ///     parks the second client's body ~500 world units away (PVS range: <c>net.pvs_range</c>
    ///     25 x Filter's 2.0 range multiplier = 50), fires both the secret-role pair-emit (arm-blade
    ///     Extend) AND a plain broadcast cue whose raise helper's return value proves it really
    ///     went out server-side, and asserts the far client's raw wire stream stays EMPTY — zero
    ///     cues of any kind — while the actor's detail delivery (session-targeted, PVS-independent)
    ///     still lands.
    /// </summary>
    [Test]
    public async Task ArmBladeExtend_WithRealPvsCulling_ClientOutsidePvsRangeReceivesZeroCuesOnTheWire()
    {
        const string farObserverUsername = "SolreignFxW5FarObserver";

        await WithSecondClient(farObserverUsername, async farObserver =>
        {
            EntityUid changelingUid = default;
            var fxCvarFlipped = false;
            var fxWasEnabled = false;
            var pvsCvarFlipped = false;
            var pvsWasEnabled = false;
            var worldFeedbackWasEnabled = false;
            var worldFeedbackCvarFlipped = false;

            try
            {
                await Server.WaitAssertion(() =>
                {
                    // This fixture proves PVS scoping for the two cues it raises explicitly.
                    // Isolate that wire stream from the independent world-feedback producer:
                    // a far observer can take ordinary environmental damage during a pooled
                    // real-ticker run, and that correctly emits an impact cue anchored to the
                    // observer rather than to the in-PVS source under test.
                    worldFeedbackWasEnabled =
                        Server.CfgMan.GetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled);
                    if (worldFeedbackWasEnabled)
                    {
                        Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled, false);
                        worldFeedbackCvarFlipped = true;
                    }

                    // Enable the engine's real PVS culling BEFORE spawning the scenario's entities,
                    // so the far client never even learns the changeling entity exists — the
                    // strictest honest version of "outside PVS range."
                    pvsWasEnabled = Server.CfgMan.GetCVar(CVars.NetPVS);
                    if (!pvsWasEnabled)
                    {
                        Server.CfgMan.SetCVar(CVars.NetPVS, true);
                        pvsCvarFlipped = true;
                    }

                    var players = Server.ResolveDependency<IPlayerManager>();
                    Assert.That(players.TryGetSessionByUsername(farObserverUsername, out var farSession), Is.True);
                    var actorSession = players.Sessions.Single(s => s.Name != farObserverUsername);

                    var mapSystem = Server.EntMan.System<SharedMapSystem>();
                    mapSystem.CreateMap(out var mapId);

                    changelingUid = Server.EntMan.SpawnEntity("MobLing", new MapCoordinates(0f, 0f, mapId));
                    Server.EntMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);
                    Assert.That(players.SetAttachedEntity(actorSession, changelingUid), Is.True);

                    // 500 world units away on the same map — an order of magnitude beyond
                    // Filter.Pvs's 50-unit effective radius, so no tuning drift can flake this.
                    var farUid = Server.EntMan.SpawnEntity("MobHuman", new MapCoordinates(500f, 0f, mapId));
                    Assert.That(players.SetAttachedEntity(farSession!, farUid), Is.True);
                    players.JoinGame(farSession!);

                    fxWasEnabled = Server.CfgMan.GetCVar(CCVars.SolreignFxCueV1Enabled);
                    if (!fxWasEnabled)
                    {
                        Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, true);
                        fxCvarFlipped = true;
                    }
                });
                await RunThreeWayTicks(farObserver, 10);

                await Client.WaitAssertion(() => Client.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());
                await farObserver.WaitAssertion(() => farObserver.EntMan.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());

                // The in-PVS fire: the secret-role pair emit (Extend) plus a plain broadcast cue.
                // RaiseCue's return value is the server-side ground truth that the broadcast cue
                // REALLY went out (TryCreate passed, egress budget consumed, RaiseNetworkEvent
                // called) — without it, "the far client received zero cues" could be vacuously
                // true because nothing was ever emitted at all.
                await ToggleArmBlade(changelingUid); // Extend
                await RunThreeWayTicks(farObserver, 4);
                await RaiseImpactLightBroadcast(changelingUid);
                await RunThreeWayTicks(farObserver, 10);

                await Client.WaitAssertion(() =>
                {
                    var system = Client.System<SolreignFxCueSystem>();

                    // Positive control 1: the session-targeted detail delivery is filter-independent
                    // and must land regardless of PVS culling (the actor IS the source).
                    Assert.That(system.AcceptedCuesForTests.Any(c => c.EffectId == "transformation"), Is.True,
                        "The actor must still receive the real detail cue with PVS culling enabled — session-targeted delivery does not ride the broadcast filter.");

                    // Positive control 2: the actor is inside its own PVS, so the plain broadcast
                    // cue reaches it — proof the broadcast fan-out path genuinely ran.
                    Assert.That(system.AcceptedCuesForTests.Any(c => c.EffectId == "impact_light"), Is.True,
                        "The actor (at the source) must receive the plain broadcast cue — proves the broadcast really went out while the far client got nothing.");

                    Assert.That(system.RawReceivedEffectIdsForTests.Contains("transformation_generic"), Is.False,
                        "Actor exclusion must hold under real PVS culling too.");
                });

                await farObserver.WaitAssertion(() =>
                {
                    var system = farObserver.EntMan.System<SolreignFxCueSystem>();

                    // THE assertion (spec §7 PVS-scoping row, grk #3): zero cues of ANY kind on the
                    // raw wire — checked before validation/guards/CVar gates, so this is literal
                    // transport-level non-delivery, not an honest client declining to render.
                    Assert.That(system.RawReceivedEffectIdsForTests, Is.Empty,
                        "A client outside PVS range must receive ZERO SolreignFxCueV1 events on the raw wire for an in-PVS fire — not the generic, not the detail, nothing (Filter.Pvs actually applied, spec §5.2 item 1 / grk #3).");
                });
            }
            finally
            {
                await Server.WaitPost(() =>
                {
                    if (fxCvarFlipped)
                        Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, fxWasEnabled);

                    if (worldFeedbackCvarFlipped)
                        Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled,
                            worldFeedbackWasEnabled);

                    // Restore the captured PVS state. Fresh pairs currently begin with culling
                    // disabled, but cleanup must remain correct if that harness posture changes.
                    if (pvsCvarFlipped)
                        Server.CfgMan.SetCVar(CVars.NetPVS, pvsWasEnabled);
                });
                await RunThreeWayTicks(farObserver, 2);
            }
        });
    }

    /// <summary>
    ///     Raises a plain, non-secret <c>impact_light</c> broadcast cue anchored to
    ///     <paramref name="anchor"/> via the real W2 raise path (<see cref="SolreignFxServerSystem.RaiseCue"/>,
    ///     the API half that REFUSES DetailOnly ids), asserting the raise genuinely consumed egress
    ///     budget and hit the wire. Parameters are the prototype's own defaults (effects.yml) —
    ///     nothing here is under confidentiality test, these cues exist to populate the broadcast
    ///     counter namespace around the secret-role emits.
    /// </summary>
    private async Task RaiseImpactLightBroadcast(EntityUid anchor)
    {
        await Server.WaitAssertion(() =>
        {
            var fx = Server.EntMan.System<SolreignFxServerSystem>();
            var raised = fx.RaiseCue("impact_light", null, Server.EntMan.GetNetEntity(anchor),
                intensity: 0.6f, scale: 1.0f, duration: 0.4f, paletteIndex: null, phase: null, pvsSource: anchor);
            Assert.That(raised, Is.True, "The plain impact_light broadcast raise must actually go out (TryCreate + egress budget + RaiseNetworkEvent).");
        });
    }

    /// <summary>Drives the real arm-blade toggle action event, exactly as W4's own tests do.</summary>
    private async Task ToggleArmBlade(EntityUid changelingUid)
    {
        await Server.WaitPost(() =>
        {
            var ev = new SolreignChangelingArmBladeToggleActionEvent { Performer = changelingUid, Handled = false };
            Server.EntMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
        });
    }

    // ---- Second-client lifecycle (reproduced from PlayerDelightTwoClientWireIntegrationTest's
    // template via W4's FxArmBladeConfidentialityIntegrationTest — both files' helpers are private
    // to their own classes, per their own documented rationale). ----

    private async Task WithSecondClient(string username, System.Func<RobustIntegrationTest.ClientIntegrationInstance, Task> test)
    {
        var second = CreateSecondClient();
        try
        {
            await second.WaitIdleAsync();
            second.SetConnectTarget(Server);
            await second.WaitPost(() =>
                ((IClientNetManager) second.NetMan).ClientConnect(null!, 0, username));
            await RunThreeWayTicks(second, 8);
            await test(second);
        }
        finally
        {
            if (second.IsAlive)
            {
                if (second.NetMan.IsConnected)
                {
                    await second.WaitPost(() =>
                        ((IClientNetManager) second.NetMan).ClientDisconnect("FX W5 confidentiality proof complete"));
                    await RunThreeWayTicks(second, 3);
                }
                second.Stop();
                await second.WaitIdleAsync(false);
            }
            second.Dispose();
        }
    }

    private static RobustIntegrationTest.ClientIntegrationInstance CreateSecondClient()
    {
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentAssemblies = PoolManager.Instance.ClientAssemblies,
            LoadTestAssembly = false,
            ContentStart = true,
            // Same rationale as PlayerDelightTwoClientWireIntegrationTest: content startup emits the
            // same benign ignored-prototype warning the pooled client's own setup filters; this
            // manually-owned client starts inside the test, so only fail it on actual errors.
            FailureLogLevel = LogLevel.Error,
            Options = new()
            {
                LoadConfigAndUserData = false,
            },
        };

        foreach (var (cvar, value) in PoolManager.Instance.DefaultCvars)
            options.CVarOverrides[cvar] = value;

        options.BeforeStart += () =>
        {
            IoCManager.Resolve<IModLoader>().SetModuleBaseCallbacks(new ClientModuleTestingCallbacks
            {
                ClientBeforeIoC = () => IoCManager.Register<Content.Client.Parallax.Managers.IParallaxManager,
                    DummyParallaxManager>(true),
            });
        };

        return new RobustIntegrationTest.ClientIntegrationInstance(options);
    }

    private async Task RunThreeWayTicks(RobustIntegrationTest.ClientIntegrationInstance second, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            await Server.WaitRunTicks(1);
            await Client.WaitRunTicks(1);
            await second.WaitRunTicks(1);
        }
    }
}
