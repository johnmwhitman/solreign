#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client._Solreign.FX;
using Content.Client.IoC;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Changeling;
using Content.Shared._Solreign.Changeling;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Server.Player;
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
///     W4's minimal preview of W5's confidentiality proof (spec
///     <c>docs/specs/FX-LANGUAGE-V1-SPEC-2026-07-16.md</c> §5.3, and the mission's own explicit
///     "do the minimal version" instruction) — the first real, end-to-end proof that the changeling
///     arm-blade's <c>transformation</c> FxCue (behind the enabled-by-default
///     <c>solreign.fx.cue_v1</c> runtime kill switch) does not out a hidden-role changeling to a
///     nearby bystander. This is NOT the full W5
///     scenario (no gap-free-CorrelationId-sequence assertion, no PVS-boundary-exclusion scenario,
///     no admin-observability-is-not-a-third-path check) — those are named explicitly in spec §5.3
///     and belong to W5, which depends on this worktree being merged first. What this test DOES
///     prove, with two real connected clients against a real server:
///
///     1. The actor's own client receives the REAL <c>transformation</c> detail cue, with the real
///        (non-default) intensity/palette/phase this worktree's <c>ExtendArmBlade</c> call raises.
///     2. The actor's client does NOT also receive the redacted broadcast (actor exclusion, spec
///        §5.2 item 1).
///     3. A bystander within PVS range receives ONLY the redacted <c>transformation_generic</c>
///        cue — never the real <c>transformation</c> id — with every numeric field scrubbed to
///        that prototype's own fixed defaults (spec §5.2 item 1 / grk #2's "parameter
///        fingerprinting"), never the detail cue's real values.
///
///     Built directly on <c>PlayerDelightTwoClientWireIntegrationTest.cs</c>'s own template (a
///     second real client connected outside the normal pooled <c>TestPair</c>) — that file's own
///     helpers are private to its class, so the minimal subset needed here (second-client
///     lifecycle + lockstep ticking) is reproduced rather than imported.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class FxArmBladeConfidentialityIntegrationTest : GameTest
{
    private const string BystanderUsername = "SolreignFxBystander";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task ExtendArmBlade_BystanderSeesOnlyScrubbedGenericCue_ActorSeesRealDetailCue()
    {
        await WithSecondClient(async bystander =>
        {
            EntityUid changelingUid = default;
            var originalCueV1Enabled = false;
            var cvarCaptured = false;

            try
            {
                await Server.WaitAssertion(() =>
                {
                    var players = Server.ResolveDependency<IPlayerManager>();
                    Assert.That(players.TryGetSessionByUsername(BystanderUsername, out var bystanderSession), Is.True);
                    var actorSession = players.Sessions.Single(s => s.Name != BystanderUsername);

                    var mapSystem = Server.EntMan.System<SharedMapSystem>();
                    mapSystem.CreateMap(out var mapId);

                    // MobLing here is just a convenient humanoid body for this test -- it carries
                    // NO Fx-cue-relevant wiring of its own (the sprite-layer fix lives in
                    // Content.Client._Solreign.Changeling.SolreignChangelingArmBladeVisualsSystem,
                    // universal to any body; the sibling sprite-layer test in
                    // SolreignChangelingTransformCycleIntegrationTest.cs covers that fix
                    // specifically, including on a real antag-granted MobHuman). This test is
                    // entirely about the FX cue, which is raised identically regardless of body.
                    changelingUid = Server.EntMan.SpawnEntity("MobLing", new MapCoordinates(0f, 0f, mapId));
                    Server.EntMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);
                    Assert.That(players.SetAttachedEntity(actorSession, changelingUid), Is.True);

                    // Bystander spawned well within PVS range of the changeling, same map.
                    var bystanderUid = Server.EntMan.SpawnEntity("MobHuman", new MapCoordinates(2f, 0f, mapId));
                    Assert.That(players.SetAttachedEntity(bystanderSession!, bystanderUid), Is.True);
                    players.JoinGame(bystanderSession!);

                    // The FX-cue path now ships enabled. Arrange that state explicitly rather than
                    // relying on a default, then restore the inherited value in the finally block
                    // below so this Dirty pool cannot leak CVar state across tests.
                    originalCueV1Enabled = Server.CfgMan.GetCVar(CCVars.SolreignFxCueV1Enabled);
                    cvarCaptured = true;
                    Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, true);
                });
                await RunThreeWayTicks(bystander, 10);

                // Clean baseline immediately before the action under test (grk round-1 finding:
                // asserting LastOrDefault/Any against a ring buffer that may already hold entries
                // from pool/fixture setup risks a false pass on stale state).
                await Client.WaitAssertion(() => Client.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());
                await bystander.WaitAssertion(() => bystander.EntMan.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());

                await Server.WaitPost(() =>
                {
                    var ev = new SolreignChangelingArmBladeToggleActionEvent { Performer = changelingUid, Handled = false };
                    Server.EntMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
                });
                await RunThreeWayTicks(bystander, 8);

                uint detailSeed = 0;
                uint genericSeed = 0;

                await Client.WaitAssertion(() =>
                {
                    var system = Client.System<SolreignFxCueSystem>();

                    var detail = system.AcceptedCuesForTests.LastOrDefault(c => c.EffectId == "transformation");
                    Assert.That(detail.EffectId, Is.EqualTo("transformation"),
                        "The actor's own client must receive the REAL 'transformation' detail cue.");
                    Assert.That(detail.Intensity, Is.EqualTo(0.85f).Within(0.001f),
                        "The actor's detail cue must carry the real Extend-side intensity, not a scrubbed default.");
                    Assert.That(detail.PaletteIndex, Is.EqualTo((byte?) 1));
                    Assert.That(detail.Phase, Is.EqualTo((byte?) 0));
                    detailSeed = detail.Seed;

                    Assert.That(system.AcceptedCuesForTests.Any(c => c.EffectId == "transformation_generic"), Is.False,
                        "The actor must NOT also receive the redacted broadcast (actor exclusion, spec §5.2 item 1) -- no double-render, no client-side dedup heuristic needed.");
                });

                await bystander.WaitAssertion(() =>
                {
                    var system = bystander.EntMan.System<SolreignFxCueSystem>();

                    // Wire-level proof (grk round-1 High finding: AcceptedCuesForTests alone only
                    // reflects an HONEST client's post-guard view, which would still read "no
                    // transformation" even if a hypothetical server bug broadcast the real detail
                    // cue and this client's own guard then correctly dropped it before recording
                    // it as accepted). RawReceivedEffectIdsForTests is recorded at the very top of
                    // OnCueReceived, before the master CVar check, TryValidateReceived, or the H1
                    // guard -- literal proof this client's transport layer never even received the
                    // real 'transformation' id at all, not just that it chose not to render it.
                    Assert.That(system.RawReceivedEffectIdsForTests.Contains("transformation"), Is.False,
                        "A bystander must never even receive the real 'transformation' id ON THE WIRE, not merely fail to render it after an honest client-side drop.");

                    Assert.That(system.AcceptedCuesForTests.Any(c => c.EffectId == "transformation"), Is.False,
                        "A bystander must NEVER receive the real 'transformation' detail cue -- this is the whole point of the confidentiality split.");

                    var generic = system.AcceptedCuesForTests.LastOrDefault(c => c.EffectId == "transformation_generic");
                    Assert.That(generic.EffectId, Is.EqualTo("transformation_generic"),
                        "The bystander must receive the redacted broadcast cover cue instead.");

                    // Every numeric field must equal transformation_generic's OWN fixed defaults
                    // (effects.yml: intensity 0.5, scale 1.0, duration 1.2, paletteCount 1/phaseCount 1
                    // -> default index 0) -- NEVER the real detail cue's Extend-side values (0.85 /
                    // 1.1 / 1.2, palette 1, phase 0). This is spec §5.2's "parameter fingerprinting"
                    // (grk #2) closure made concrete: a bystander cannot distinguish this from any
                    // other generic transformation by intensity/palette/phase.
                    Assert.That(generic.Intensity, Is.EqualTo(0.5f).Within(0.001f));
                    Assert.That(generic.Scale, Is.EqualTo(1.0f).Within(0.001f));
                    Assert.That(generic.Duration, Is.EqualTo(1.2f).Within(0.001f));
                    Assert.That(generic.PaletteIndex, Is.Null.Or.EqualTo((byte?) 0));
                    Assert.That(generic.Phase, Is.Null.Or.EqualTo((byte?) 0));
                    genericSeed = generic.Seed;
                });

                // Independent seed roll (spec §1.2/§5.2, grk #6): the generic and detail cues of one
                // logical emit must never share a seed, or the pair would trivially correlate on the
                // wire even with every other field scrubbed.
                Assert.That(genericSeed, Is.Not.EqualTo(detailSeed),
                    "The redacted broadcast cue and the real detail cue must be independently seeded.");
            }
            finally
            {
                if (cvarCaptured)
                {
                    await Server.WaitPost(() =>
                        Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, originalCueV1Enabled));
                    await RunThreeWayTicks(bystander, 2);
                }
            }
        });
    }

    private async Task WithSecondClient(System.Func<RobustIntegrationTest.ClientIntegrationInstance, Task> test)
    {
        var second = CreateSecondClient();
        try
        {
            await second.WaitIdleAsync();
            second.SetConnectTarget(Server);
            await second.WaitPost(() =>
                ((IClientNetManager) second.NetMan).ClientConnect(null!, 0, BystanderUsername));
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
                        ((IClientNetManager) second.NetMan).ClientDisconnect("FX arm-blade confidentiality proof complete"));
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
