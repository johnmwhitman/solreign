#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Antags.Vampire;
using Content.Shared._Solreign.Antags;
using Content.Shared.DoAfter;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     System-layer coverage for <see cref="SolreignVampireSystem"/>'s real donation-feeding cycle
///     (spec docs/specs/2026-07-11-werewolf-vampire-spec.md §4.2-§4.3) and its environmental fail-safe
///     gate. Pure meter arithmetic already lives in <c>VampireThirstMathTests</c> (Content.Tests); this
///     file drives the actual ECS do-after + proximity-detection path.
///
///     Both tests start a REAL <see cref="Content.Shared.DoAfter.DoAfterArgs"/> via the real
///     <see cref="Content.Shared.DoAfter.SharedDoAfterSystem.TryStartDoAfter"/> — the same call
///     <c>SolreignVampireSystem.Feeding.cs</c>'s private <c>StartDonationDoAfter</c> makes — rather than
///     hand-constructing a <see cref="SolreignVampireDonationDoAfterEvent"/> and faking its
///     <c>.User</c>/<c>.Target</c>: those are computed from a <c>DoAfterEvent.DoAfter</c> back-reference
///     the real <c>DoAfterSystem</c> stamps on completion, not something a test can cheaply fabricate.
///     <c>NeedHand = false</c> on the donation do-after (unlike the blood-pack path) means no Hands-system
///     setup is needed either. Both wait exactly <see cref="SolreignVampireComponent.FeedDoAfterSeconds"/>
///     (a Read-permitted field access — <c>[Access(typeof(SolreignVampireSystem))]</c> defaults
///     <c>Other</c> to Read, only Write is blocked) via <c>WaitRunTicks</c>, never wall-clock.
///
///     Neither test needs to cross a thirst BAND (Sated/Peckish/Thirsty/Ravenous, ~minutes of simulated
///     game time each at the default 1 unit/minute accrual rate — impractical to simulate deterministically
///     in a test without writing to the Access-locked <c>Thirst</c>/<c>LastThirstTick</c> fields, which
///     this test deliberately does not do). The donation drink itself is observable at the DEFAULT spawn
///     thirst (10) with no simulated time at all: <c>VampireThirstMath.Drink(10, DrinkAmount=25)</c>
///     clamps to exactly 0.
/// </summary>
[TestFixture]
public sealed class SolreignVampireFeedingCycleIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // Force a brand-new pair rather than a recycled one -- a real battery run showed a different
        // long-running Solreign test in this same file suite occasionally inheriting a leftover async
        // Content.Server.NPC.HTN job from whatever earlier test last used a reused pair (see
        // SolreignWerewolfCycleAndFailSafeIntegrationTest's doc comment for the concrete repro). Cheap
        // insurance against the same class of flake here too.
        Fresh = true,
    };

    [Test]
    public async Task DonationDoAfter_CompletesForReal_DrinksThirstToZero()
    {
        var server = Server;
        var entMan = server.EntMan;
        var doAfter = server.System<SharedDoAfterSystem>();

        EntityUid vampireUid = default;
        EntityUid donorUid = default;
        float feedSeconds = 0f;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            vampireUid = entMan.SpawnEntity("MobHuman", coords);
            donorUid = entMan.SpawnEntity("MobHuman", coords);

            var vampire = entMan.EnsureComponent<SolreignVampireComponent>(vampireUid);
            feedSeconds = vampire.FeedDoAfterSeconds;

            Assert.That(vampire.Thirst, Is.EqualTo(10d),
                "This test relies on the declared default spawn thirst (10) to make the post-drink " +
                "assertion (clamped to exactly 0) meaningful without writing to any Access-locked field.");

            var args = new DoAfterArgs(entMan, donorUid, feedSeconds,
                new SolreignVampireDonationDoAfterEvent(), vampireUid, target: vampireUid)
            {
                BreakOnMove = true,
                NeedHand = false,
            };

            Assert.That(doAfter.TryStartDoAfter(args), Is.True,
                "The real donation do-after failed to start at all — nothing downstream can be exercised.");
        });

        // feedSeconds (default 3s = 90 ticks) plus a generous buffer for the do-after's own bookkeeping tick.
        await server.WaitRunTicks((int) (feedSeconds * 30f) + 30);

        await server.WaitAssertion(() =>
        {
            var vampire = entMan.GetComponent<SolreignVampireComponent>(vampireUid);
            Assert.That(vampire.Thirst, Is.EqualTo(0d),
                "The real donation do-after completed but SolreignVampireSystem.Feeding's OnDonationDoAfter " +
                "never applied VampireThirstMath.Drink — thirst should have clamped from 10 to 0.");
        });
    }

    [Test]
    public async Task GarlicWardNearby_RealProximityDetection_BlocksDonationFeed()
    {
        var server = Server;
        var entMan = server.EntMan;
        var doAfter = server.System<SharedDoAfterSystem>();

        EntityUid vampireUid = default;
        EntityUid donorUid = default;
        float feedSeconds = 0f;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            vampireUid = entMan.SpawnEntity("MobHuman", coords);
            donorUid = entMan.SpawnEntity("MobHuman", coords);

            // Same coordinates as the vampire -> well within SolreignGarlicWardComponent's default
            // 2-tile Radius, so the real RefreshEnvironment proximity scan (run once per
            // SolreignVampireComponent.ThirstTickSeconds) is guaranteed to pick it up.
            var ward = entMan.SpawnEntity(null, coords);
            entMan.EnsureComponent<SolreignGarlicWardComponent>(ward);

            var vampire = entMan.EnsureComponent<SolreignVampireComponent>(vampireUid);
            feedSeconds = vampire.FeedDoAfterSeconds;
        });

        // Default ThirstTickSeconds is 5s (150 ticks); pad generously for the first Update tick to notice.
        await server.WaitRunTicks(210);

        double thirstBeforeFeedAttempt = 0d;
        await server.WaitAssertion(() =>
        {
            var vampire = entMan.GetComponent<SolreignVampireComponent>(vampireUid);
            Assert.That(vampire.GarlicNearby, Is.True,
                "The real proximity scan (RefreshEnvironment) never detected the garlic ward — the gate " +
                "this test exercises below would pass for the wrong reason (nothing to block) if this " +
                "didn't fire.");

            // Captured here, not assumed to still be the spawn default (10): the wait above already let
            // Update accrue some thirst for real (at the garlic-boosted 1.5x rate, per
            // VampireThirstMath.Accumulate) — a Drink(10, 25) clamp-to-0 is still trivially
            // distinguishable from that, but asserting an exact post-wait constant would be guessing at
            // the accrual math this test isn't trying to pin down.
            thirstBeforeFeedAttempt = vampire.Thirst;
        });

        await server.WaitPost(() =>
        {
            var args = new DoAfterArgs(entMan, donorUid, feedSeconds,
                new SolreignVampireDonationDoAfterEvent(), vampireUid, target: vampireUid)
            {
                BreakOnMove = true,
                NeedHand = false,
            };

            Assert.That(doAfter.TryStartDoAfter(args), Is.True,
                "Starting the do-after itself isn't gated on CanFeed — only completion re-checks it " +
                "(spec: gate re-runs at completion since garlic/chapel state can change mid do-after) — " +
                "so it must still start.");
        });

        await server.WaitRunTicks((int) (feedSeconds * 30f) + 30);

        await server.WaitAssertion(() =>
        {
            var vampire = entMan.GetComponent<SolreignVampireComponent>(vampireUid);
            Assert.That(vampire.Thirst, Is.GreaterThanOrEqualTo(thirstBeforeFeedAttempt),
                "VampireThirstMath.CanFeed must block the drink while a garlic ward is in range — thirst " +
                $"only ever climbing (natural accrual) from its pre-attempt value ({thirstBeforeFeedAttempt}) " +
                "is the signature of a blocked feed; any drop would mean VampireThirstMath.Drink ran " +
                "despite the gate, most dramatically a clamp to 0.");
        });
    }
}
