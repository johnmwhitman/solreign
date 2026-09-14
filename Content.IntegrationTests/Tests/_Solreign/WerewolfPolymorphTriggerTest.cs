#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Antags.Werewolf;
using Content.Shared.Polymorph;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Regression test for the Phase-2 Track A1 headline defect (the "werewolf lesson",
///     docs/plans/2026-07-11-ROADMAP-PHASE2.md): <see cref="SolreignWerewolfComponent.WolfPolymorphPrototype"/>
///     pointed at "SolreignWerewolfPolymorph", a <see cref="PolymorphPrototype"/> id that did not
///     exist anywhere under Resources/Prototypes — so a drafted werewolf who reached
///     <see cref="WerewolfState.Transformed"/> got nothing (a silently-logged
///     <c>TryIndex</c> miss in <c>SolreignWerewolfSystem.Transform.OnEnterTransformed</c>).
///
///     Three things are asserted: (1) the prototype resolves at all — the narrow regression guard
///     that alone would have caught the original bug; (2) driving the REAL state machine through a
///     full Stirring window via the system's own <see cref="SolreignWerewolfSystem.MoonWindowActive"/>
///     flag actually produces a <see cref="SolreignWolfFormComponent"/>-tagged entity, proving the
///     trigger fires end to end and not just in theory; and (3) the gate the other way — with the
///     moon window never opened, nothing transforms.
///
///     Pattern studied from <c>SolreignZoneGateIntegrationTest</c> (bare <c>MapCoordinates.Nullspace</c>
///     spawns, no need for a real map/round) and <c>SolreignPerfBaselineTest</c>'s own precedent for
///     legitimately calling <c>EnsureComponent&lt;T&gt;()</c> on an [Access]-locked antag component
///     and leaving every field at its declared default rather than poking members directly.
/// </summary>
[TestFixture]
public sealed class WerewolfPolymorphTriggerTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true, // mutates SolreignWerewolfSystem.MoonWindowActive, a shared system-level flag
    };

    private const string WolfPolymorphId = "SolreignWerewolfPolymorph";

    [Test]
    public void WolfPolymorphPrototype_Resolves()
    {
        Assert.That(SProtoMan.TryIndex<PolymorphPrototype>(WolfPolymorphId, out _), Is.True,
            $"'{WolfPolymorphId}' must exist under Resources/Prototypes or every Full Moon Window " +
            "silently no-ops at Transformed (the werewolf lesson).");
    }

    [Test]
    public async Task MoonWindowOpen_StirringElapses_ActuallyPolymorphsToWolf()
    {
        var server = Server;
        var entMan = server.EntMan;
        var werewolf = server.System<SolreignWerewolfSystem>();

        await server.WaitPost(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignWerewolfComponent>(human);
            werewolf.MoonWindowActive = true;
        });

        // Default StirringSeconds is 30s. At the default 30 tick/s server rate that's 900 ticks;
        // pad generously for the Dormant->Stirring hop and tick-boundary rounding.
        await server.WaitRunTicks(960);

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<SolreignWolfFormComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.True,
                "Stirring elapsed under an open moon window but no SolreignWolfFormComponent entity " +
                "was created — either the polymorph prototype failed to resolve again, or the " +
                "transform path regressed.");
        });
    }

    [Test]
    public async Task MoonWindowNeverOpens_NeverTransforms()
    {
        var server = Server;
        var entMan = server.EntMan;
        var werewolf = server.System<SolreignWerewolfSystem>();

        await server.WaitPost(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignWerewolfComponent>(human);
            werewolf.MoonWindowActive = false;
        });

        await server.WaitRunTicks(960);

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<SolreignWolfFormComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.False,
                "A werewolf transformed even though the Full Moon Window was never opened — the " +
                "trigger gating regressed.");
        });
    }
}
