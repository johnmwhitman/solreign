#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Providence;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     ECS-wiring coverage for <see cref="ProvidenceEventReactiveSystem"/>'s round-end summary path
///     (feat/providence-event-reactive — PROVIDENCE-VOICE-DESIGN.md). The death-reactive path's pure
///     decision logic (classification, memory eligibility, the cooldown/priority gate, the name
///     sanitizer) is exhaustively unit-tested without a server in Content.Tests/_Solreign/
///     ProvidenceReactive*Tests.cs and ProvidenceNameSanitizerTests.cs; this file only exercises the
///     one leg that is safe to drive synthetically: <c>GameRunLevelChangedEvent</c>. Deliberately does
///     NOT raise a synthetic <c>MobStateChangedEvent</c> for the death path — this codebase's own
///     <c>FirstDeathSceneIntegrationTest</c> documents that downstream alert systems reject a
///     hand-raised one with error logs; a real end-to-end death exercise belongs in a follow-up wave
///     using that file's real-damage idiom, not invented here without a build to verify it against.
///
///     Every test explicitly arranges the CVar(s), then calls <c>ResetRoundStateForTests</c>, in that order — the
///     precedented rationale from <c>ProvidenceFirstShiftWelcomeIntegrationTest</c>/
///     <c>FirstDeathSceneIntegrationTest</c>: the pool-connected session already goes through a real
///     round/spawn during setup. The feature now ships enabled, so disabled-path coverage must turn it
///     off explicitly rather than infer dormancy from the default. Each test restores the state it
///     inherited, while the reset still guarantees a clean gate/counter baseline.
/// </summary>
[TestFixture]
public sealed class ProvidenceEventReactiveSystemIntegrationTest : GameTest
{
    // Dirty: flips CCVars directly and raises a synthetic GameRunLevelChangedEvent — this server must
    // never be handed back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private static GameRunLevelChangedEvent RoundEndTransition() =>
        new(GameRunLevel.InRound, GameRunLevel.PostRound);

    [Test]
    public async Task Disabled_AtRuntime_RoundEndProducesNoDispatch()
    {
        var server = Server;
        var reactive = server.System<ProvidenceEventReactiveSystem>();
        var entMan = server.EntMan;
        var originalEnabled = server.CfgMan.GetCVar(CCVars.SolreignProvidenceReactiveEnabled);

        try
        {
            server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, false);
            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
            {
                reactive.ResetRoundStateForTests();
                entMan.EventBus.RaiseEvent(EventSource.Local, RoundEndTransition());

                Assert.That(reactive.ReactiveDispatchCountForTests, Is.EqualTo(0),
                    "CVar-off at runtime must mean total silence, even on a real round-boundary event.");
            });
        }
        finally
        {
            server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, originalEnabled);
        }
    }

    [Test]
    public async Task Enabled_RoundEndWithNoDeaths_DispatchesExactlyOneCalmLine()
    {
        var server = Server;
        var reactive = server.System<ProvidenceEventReactiveSystem>();
        var entMan = server.EntMan;
        var originalEnabled = server.CfgMan.GetCVar(CCVars.SolreignProvidenceReactiveEnabled);

        try
        {
            server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, true);
            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
            {
                reactive.ResetRoundStateForTests();
                entMan.EventBus.RaiseEvent(EventSource.Local, RoundEndTransition());

                Assert.Multiple(() =>
                {
                    Assert.That(reactive.ReactiveDispatchCountForTests, Is.EqualTo(1));
                    Assert.That(reactive.LastDispatchTextForTests, Is.Not.Null.And.Not.Empty);
                });
            });
        }
        finally
        {
            server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, originalEnabled);
        }
    }

    [Test]
    public async Task Enabled_TwoRoundEndTransitionsInQuickSuccession_OnlyTheFirstFires()
    {
        // Anti-spam proof for the round-end leg specifically: even a rapid double-fire of the same
        // event must collapse to one dispatch under the shared cooldown gate.
        var server = Server;
        var reactive = server.System<ProvidenceEventReactiveSystem>();
        var entMan = server.EntMan;
        var originalEnabled = server.CfgMan.GetCVar(CCVars.SolreignProvidenceReactiveEnabled);

        try
        {
            server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, true);
            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
            {
                reactive.ResetRoundStateForTests();
                entMan.EventBus.RaiseEvent(EventSource.Local, RoundEndTransition());
                entMan.EventBus.RaiseEvent(EventSource.Local, RoundEndTransition());

                Assert.That(reactive.ReactiveDispatchCountForTests, Is.EqualTo(1),
                    "A second round-end transition arriving immediately after the first must be dropped by the cooldown gate.");
            });
        }
        finally
        {
            server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, originalEnabled);
        }
    }
}
