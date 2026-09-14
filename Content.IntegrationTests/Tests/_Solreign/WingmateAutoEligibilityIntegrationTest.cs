#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PlayerDelight.Wingmates;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.CCVar;
using Content.Shared.Ghost.Components;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     UX-SIMPLE FIX 1: end-to-end coverage for <see cref="CCVars.SolreignWingmatesAutoGuideEligibility"/>
///     against a REAL <see cref="IPlayerManager"/>/<see cref="IEntityManager"/> — the rules-test
///     suite (<c>WingmateSystemRulesTests</c>) cannot exercise the live-entity criteria
///     (ghost/ tenure) at all, since a bare <c>new WingmateSystem()</c> harness never wires
///     IoC/_players. This drives the same beacon-event/BUI path the live game uses.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WingmateAutoEligibilityIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static async Task<(EntityUid Actor, NetUserId User)> GetPlayer(
        RobustIntegrationTest.ServerIntegrationInstance server,
        IPlayerManager players)
    {
        EntityUid actor = default;
        NetUserId user = default;
        await server.WaitPost(() =>
        {
            var session = players.Sessions.First();
            actor = session.AttachedEntity!.Value;
            user = session.UserId;
        });
        return (actor, user);
    }

    [Test]
    public async Task AliveNonGhostAccountWithEnoughTenure_BecomesEligibleWithoutModeratorApproval()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (_, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // Bypasses the real SQLite-backed ledger for a deterministic tenure value, same seam
            // idiom as SetPersistentBlockWriterForTests elsewhere in this file tree.
            system.SetCareerToursFetcherForTests(_ => Task.FromResult(999));

            Assert.That(system.IsGuideEligibleForTests(requester), Is.False,
                "test precondition: tenure has not been loaded/cached yet");

            system.DrainLoadedCareerToursForTests();
        });

        // The tenure fetch is a real (fake-backed) async Task; give it a tick to complete and
        // enqueue before draining again.
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            system.DrainLoadedCareerToursForTests();

            Assert.Multiple(() =>
            {
                Assert.That(system.IsGuideEligibleForTests(requester), Is.True,
                    "alive, non-ghost, sufficient tenure must be auto-eligible without wingmateapprove");
                Assert.That(system.ComputeVolunteerIneligibleReasonForTests(requester), Is.Null);
                Assert.That(system.SetVolunteeringForTests(requester, volunteering: true, charterAccepted: true).Changed,
                    Is.True, "volunteering must succeed with no prior moderator approval");
            });
        });
    }

    [Test]
    public async Task GhostAccount_StaysIneligibleWithObserverReason()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, true);
            server.EntMan.EnsureComponent<GhostComponent>(actor);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            system.SetCareerToursFetcherForTests(_ => Task.FromResult(999));
            system.SeedCareerToursForTests(requester, 999);

            Assert.Multiple(() =>
            {
                Assert.That(system.IsGuideEligibleForTests(requester), Is.False,
                    "a ghost/observer must never be auto-eligible regardless of tenure");
                Assert.That(system.ComputeVolunteerIneligibleReasonForTests(requester),
                    Is.EqualTo("wingmates-guide-ineligible-observer"));
            });
        });
    }

    [Test]
    public async Task BelowMinimumShifts_StaysIneligibleWithTenureReason()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (_, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesMinimumShifts, 10);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // Alive, not a ghost, but well below the configured tenure floor.
            system.SeedCareerToursForTests(requester, 2);

            Assert.Multiple(() =>
            {
                Assert.That(system.IsGuideEligibleForTests(requester), Is.False);
                Assert.That(system.ComputeVolunteerIneligibleReasonForTests(requester),
                    Is.EqualTo("wingmates-guide-ineligible-tenure"));
            });
        });
    }

    [Test]
    public async Task CVarOff_FallsBackToManualPostureEvenForAnOtherwiseQualifyingAccount()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (_, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, false);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // Alive, non-ghost, plenty of tenure — would qualify under the auto posture, but the
            // CVar is off, so ONLY an explicit moderator grant may work.
            system.SeedCareerToursForTests(requester, 999);

            Assert.Multiple(() =>
            {
                Assert.That(system.IsGuideEligibleForTests(requester), Is.False);
                Assert.That(system.ComputeVolunteerIneligibleReasonForTests(requester),
                    Is.EqualTo("wingmates-guide-ineligible-manual"));
            });

            // wingmateapprove's underlying call (ApproveGuide) keeps working as the override path
            // in the moderated posture — the whole point of FIX 1's "either way" guarantee.
            system.ApproveGuide(requester);

            Assert.Multiple(() =>
            {
                Assert.That(system.IsGuideEligibleForTests(requester), Is.True);
                Assert.That(system.ComputeVolunteerIneligibleReasonForTests(requester), Is.Null);
            });
        });
    }

    [Test]
    public async Task RaisingTheMinimumShiftsFloorMidRound_ImmediatelyDemotesACachedButNowInsufficientAccount()
    {
        // grk design-sanity pass (pre-implementation review) flagged "lifetime tenure cache never
        // invalidates" as a risk. Only the raw career-tours COUNT is cached
        // (WingmateSystem._careerToursCache) — the pass/fail comparison against
        // CCVars.SolreignWingmatesMinimumShifts is re-read and re-evaluated live on every single
        // eligibility check, so raising the floor mid-round takes effect on the very next check,
        // with no cache-bust or restart required. This pins that behavior against a REAL
        // IConfigurationManager rather than leaving it as an inferred claim from reading the code.
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (_, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesMinimumShifts, 10);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            system.SeedCareerToursForTests(requester, 15);
            Assert.That(system.IsGuideEligibleForTests(requester), Is.True,
                "test precondition: 15 career tours clears a floor of 10");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.SolreignWingmatesMinimumShifts, 50));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(system.IsGuideEligibleForTests(requester), Is.False,
                    "raising the floor mid-round must immediately demote a now-insufficient cached account");
                Assert.That(system.ComputeVolunteerIneligibleReasonForTests(requester),
                    Is.EqualTo("wingmates-guide-ineligible-tenure"));
            });
        });
    }

    [Test]
    public async Task LiveEnforcement_NeverTrustsAStaleClientSnapshot_DeathBetweenSnapshotAndVolunteerPressStillRejects()
    {
        // grk design-sanity pass: the UI snapshot for an open beacon isn't continuously repolled
        // every tick (same idiom as every other Wingmates UI field — it refreshes on your own
        // actions or on being an affected party of someone else's), so a player's displayed
        // CanVolunteer could be momentarily stale if their alive/ghost state changes while they are
        // just sitting with the window open. This asserts the actual SERVER-SIDE GATE never relies
        // on that same cache — SetVolunteering re-checks eligibility fresh at the moment of the
        // real action, so a stale-looking button can never let an ineligible account through.
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            system.SeedCareerToursForTests(requester, 999);
            Assert.That(system.IsGuideEligibleForTests(requester), Is.True,
                "test precondition: eligible while alive and not a ghost");

            // Simulates the account becoming ineligible AFTER the last UI snapshot was built (e.g.
            // they died) without any intervening beacon-reopen to refresh the client's view.
            server.EntMan.EnsureComponent<GhostComponent>(actor);

            Assert.That(system.SetVolunteeringForTests(requester, volunteering: true, charterAccepted: true).Changed,
                Is.False, "the real action must re-check eligibility live, never trust an earlier snapshot");
        });
    }

    [Test]
    public async Task RevokeGuideDeniesAutoEligibilityEvenForAnAliveNonGhostSufficientlyTenuredAccount()
    {
        // grk code review finding (HIGH): a bare _approvedGuides.Remove in RevokeGuide is not a
        // real revoke once auto-eligibility is on — an account that still meets the basic/tenure
        // criteria would otherwise simply re-qualify on its very next check. This drives the fix
        // (the _deniedGuides hard-deny list) against a REAL live, alive, non-ghost, sufficiently
        // tenured account — the exact account auto-eligibility would otherwise grant.
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (_, requester) = await GetPlayer(server, players);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesAutoGuideEligibility, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            system.SeedCareerToursForTests(requester, 999);
            Assert.That(system.IsGuideEligibleForTests(requester), Is.True,
                "test precondition: would qualify via the auto path with no moderator action at all");

            var revoked = system.RevokeGuide(requester);

            Assert.Multiple(() =>
            {
                Assert.That(revoked.Changed, Is.True);
                Assert.That(system.IsGuideEligibleForTests(requester), Is.False,
                    "a moderator revoke must be a HARD deny — it must not be silently overridden by " +
                    "the same auto-eligibility criteria that were true a moment ago");
                Assert.That(system.SetVolunteeringForTests(requester, volunteering: true, charterAccepted: true).Changed,
                    Is.False, "the denied account must not be able to re-volunteer via the auto path either");
            });

            // An explicit re-approval overrides the denial (the CVar's "either way" override
            // guarantee working in the OTHER direction).
            system.ApproveGuide(requester);
            Assert.That(system.IsGuideEligibleForTests(requester), Is.True);
        });
    }
}
