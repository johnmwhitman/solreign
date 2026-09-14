#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._Solreign.PlayerDelight.FirstShift;
using Content.Client._Solreign.PlayerDelight.Wingmates;
using Content.Client.IoC;
using Content.Client.Parallax.Managers;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Server._Solreign.PlayerDelight.Wingmates;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Exercises the Player Delight private-snapshot boundary with two real clients connected to the
/// same initialized server. The second client is deliberately outside the normal pooled TestPair,
/// so every test owns its connection and shutdown explicitly.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PlayerDelightTwoClientWireIntegrationTest : GameTest
{
    private const string SecondUsername = "SolreignWireB";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task Wingmates_TwoRealClientsReceiveOnlyTheirOwnPrivateState()
    {
        await WithSecondClient(async second =>
        {
            var context = await PrepareBeacon(second);
            var system = Server.System<WingmateSystem>();

            await Server.WaitPost(() =>
            {
                Server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
                system.ApproveGuide(context.SecondSession.UserId);
            });
            await RunThreeWayTicks(second, 2);

            await SendBui(Client, second, context.BeaconNet,
                new WingmateRequestMessage("Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing));
            await SendBui(second, second, context.BeaconNet, new WingmateVolunteerMessage(true, true));

            // SendBui's standard two ticks are enough for the volunteer message itself to reach the
            // server and be applied (proven by server-side diagnostics during investigation: the
            // opaque token is computed and handed to RaiseNetworkEvent within that window). But this
            // is the one place in the suite where the *sender* of a BUI message is also the intended
            // recipient of the private snapshot that message triggers, so an extra network round trip
            // is needed for the resulting targeted WingmatePrivateSnapshotEvent to actually land on the
            // guide's own client before we read it back. Without this, the assertion below observes a
            // stale pre-volunteer snapshot. This is a test-timing gap, not a server gap.
            await RunThreeWayTicks(second, 2);

            // The guide's client never learns the requester's NetUserId — it only ever sees the opaque
            // per-round token the server put in its own private snapshot. Read that token exactly like
            // the real WingmateWindow UI does (WingmateWindow.xaml.cs EmitOffer) instead of reaching
            // into server-side account identity, so this wire test proves the actual wire contract.
            var requesterToken = default(System.Guid?);
            await second.WaitAssertion(() =>
            {
                Assert.That(second.System<WingmateClientSystem>().TryGetSnapshot(context.BeaconNet, out var seekerSnapshot), Is.True);
                requesterToken = seekerSnapshot.State.RequesterToken;
            });
            Assert.That(requesterToken, Is.Not.Null,
                "the volunteering guide's own client-received state must carry the opaque requester token to offer against");

            await SendBui(second, second, context.BeaconNet, new WingmateOfferMessage(requesterToken!.Value));
            await RunThreeWayTicks(second, 3);

            WingmateSnapshotIndex.Entry firstSnapshot = default;
            WingmateSnapshotIndex.Entry secondSnapshot = default;
            await Client.WaitAssertion(() =>
            {
                Assert.That(Client.System<WingmateClientSystem>().TryGetSnapshot(context.BeaconNet, out firstSnapshot), Is.True);
            });
            await second.WaitAssertion(() =>
            {
                Assert.That(second.System<WingmateClientSystem>().TryGetSnapshot(context.BeaconNet, out secondSnapshot), Is.True);
            });

            await Server.WaitAssertion(() => Assert.Multiple(() =>
            {
                var expectedFirst = system.BuildPrivateStateForTests(context.FirstSession.UserId);
                var expectedSecond = system.BuildPrivateStateForTests(context.SecondSession.UserId);
                AssertWingmateState(firstSnapshot.State, expectedFirst);
                AssertWingmateState(secondSnapshot.State, expectedSecond);
                Assert.That(firstSnapshot.State.Mode, Is.EqualTo(WingmateUiMode.OfferReceived));
                Assert.That(firstSnapshot.State.OfferNonce, Is.Not.Null);
                Assert.That(secondSnapshot.State.Mode, Is.EqualTo(WingmateUiMode.Idle));
                Assert.That(secondSnapshot.State.OfferNonce, Is.Null,
                    "The guide must never receive the requester's acceptance nonce.");
            }));
        });
    }

    [Test]
    public async Task FirstShift_TwoRealClientsKeepAssignmentsSeparated()
    {
        await WithSecondClient(async second =>
        {
            var context = await PrepareBeacon(second);
            var system = Server.System<FirstShiftSystem>();

            await Server.WaitPost(() =>
                Server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true));
            await RunThreeWayTicks(second, 5);

            FirstShiftSnapshotIndex.Entry firstIdle = default;
            FirstShiftSnapshotIndex.Entry secondIdle = default;
            await Client.WaitAssertion(() =>
                Assert.That(Client.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out firstIdle), Is.True));
            await second.WaitAssertion(() =>
                Assert.That(second.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out secondIdle), Is.True));
            await Server.WaitAssertion(() => Assert.Multiple(() =>
            {
                Assert.That(firstIdle.Generation,
                    Is.EqualTo(system.GetTransportGenerationForTests(context.FirstSession.UserId)));
                Assert.That(secondIdle.Generation,
                    Is.EqualTo(system.GetTransportGenerationForTests(context.SecondSession.UserId)));
            }));

            await SendBui(Client, second, context.BeaconNet,
                new FirstShiftStartMessage(FirstShiftDepartment.Engineering, firstIdle.Generation));
            await SendBui(second, second, context.BeaconNet,
                new FirstShiftStartMessage(FirstShiftDepartment.Medical, secondIdle.Generation));
            await RunThreeWayTicks(second, 3);

            FirstShiftSnapshotIndex.Entry firstAssigned = default;
            FirstShiftSnapshotIndex.Entry secondAssigned = default;
            await Client.WaitAssertion(() =>
                Assert.That(Client.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out firstAssigned), Is.True));
            await second.WaitAssertion(() =>
                Assert.That(second.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out secondAssigned), Is.True));

            await Server.WaitAssertion(() => Assert.Multiple(() =>
            {
                AssertFirstShiftState(firstAssigned.State, system.BuildStateForTests(context.FirstSession.UserId));
                AssertFirstShiftState(secondAssigned.State, system.BuildStateForTests(context.SecondSession.UserId));
                Assert.That(firstAssigned.State.SelectedDepartment, Is.EqualTo(FirstShiftDepartment.Engineering));
                Assert.That(secondAssigned.State.SelectedDepartment, Is.EqualTo(FirstShiftDepartment.Medical));
                Assert.That(firstAssigned.State.CardId, Is.Not.EqualTo(secondAssigned.State.CardId));
            }));

            await SendBui(Client, second, context.BeaconNet,
                new FirstShiftAdvanceMessage(firstAssigned.Generation, firstAssigned.State.Generation));
            await RunThreeWayTicks(second, 3);

            FirstShiftSnapshotIndex.Entry firstAdvanced = default;
            FirstShiftSnapshotIndex.Entry secondUnchanged = default;
            await Client.WaitAssertion(() =>
                Assert.That(Client.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out firstAdvanced), Is.True));
            await second.WaitAssertion(() =>
                Assert.That(second.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out secondUnchanged), Is.True));
            Assert.Multiple(() =>
            {
                Assert.That(firstAdvanced.Generation, Is.GreaterThan(firstAssigned.Generation));
                Assert.That(secondUnchanged, Is.EqualTo(secondAssigned),
                    "Advancing one player's assignment must not publish into the other client's index.");
            });
        });
    }

    [Test]
    public async Task ClosedBui_SpoofedClientReplicaCannotMutateServerState()
    {
        await WithSecondClient(async second =>
        {
            var context = await PrepareBeacon(second);
            var wingmates = Server.System<WingmateSystem>();
            var firstShift = Server.System<FirstShiftSystem>();

            await Server.WaitPost(() =>
            {
                Server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
                Server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            });
            await RunThreeWayTicks(second, 5);

            FirstShiftSnapshotIndex.Entry secondIdle = default;
            WingmateSnapshotIndex.Entry firstBefore = default;
            await second.WaitAssertion(() =>
                Assert.That(second.System<FirstShiftClientSystem>().TryGetSnapshot(context.BeaconNet, out secondIdle), Is.True));
            await Client.WaitAssertion(() =>
                Assert.That(Client.System<WingmateClientSystem>().TryGetSnapshot(context.BeaconNet, out firstBefore), Is.True));

            await CloseBui(second, context.BeaconNet);
            await RunThreeWayTicks(second, 4);

            ulong closedGeneration = 0;
            await Server.WaitAssertion(() =>
            {
                Assert.That(Server.System<SharedUserInterfaceSystem>()
                    .IsUiOpen(context.Beacon, WingmateUiKey.Beacon, context.SecondActor), Is.False,
                    "The authoritative server subscription must be closed before the hostile probe.");
                closedGeneration = firstShift.GetTransportGenerationForTests(context.SecondSession.UserId);
            });

            await second.WaitPost(() =>
            {
                var clientBeacon = second.EntMan.GetEntity(context.BeaconNet);
                var ui = second.EntMan.GetComponent<UserInterfaceComponent>(clientBeacon);
                var actorSets = GetActorSetsForHostileWireProbe(ui);
                if (!actorSets.TryGetValue(WingmateUiKey.Beacon, out var localActors))
                {
                    localActors = new HashSet<EntityUid>();
                    actorSets[WingmateUiKey.Beacon] = localActors;
                }
                localActors.Add(second.PlayerMan.LocalEntity!.Value);

                var uiSystem = second.System<UserInterfaceSystem>();
                uiSystem.ClientSendUiMessage(clientBeacon, WingmateUiKey.Beacon,
                    new WingmateRequestMessage("Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour));
                uiSystem.ClientSendUiMessage(clientBeacon, WingmateUiKey.Beacon,
                    new FirstShiftStartMessage(FirstShiftDepartment.Medical, secondIdle.Generation));
            });
            await RunThreeWayTicks(second, 5);

            await Server.WaitAssertion(() => Assert.Multiple(() =>
            {
                Assert.That(wingmates.GetStatusForTests(context.SecondSession.UserId), Is.EqualTo(WingmateStatus.Idle));
                Assert.That(firstShift.GetAssignmentForTests(context.SecondSession.UserId), Is.Null);
                Assert.That(firstShift.GetTransportGenerationForTests(context.SecondSession.UserId),
                    Is.EqualTo(closedGeneration));
            }));

            WingmateSnapshotIndex.Entry firstAfter = default;
            await Client.WaitAssertion(() =>
                Assert.That(Client.System<WingmateClientSystem>().TryGetSnapshot(context.BeaconNet, out firstAfter), Is.True));
            Assert.That(firstAfter, Is.EqualTo(firstBefore),
                "A closed client's hostile wire message must not publish to another viewer.");
        });
    }

    private async Task WithSecondClient(Func<RobustIntegrationTest.ClientIntegrationInstance, Task> test)
    {
        var second = CreateSecondClient();
        try
        {
            await second.WaitIdleAsync();
            second.SetConnectTarget(Server);
            await second.WaitPost(() =>
                ((IClientNetManager) second.NetMan).ClientConnect(null!, 0, SecondUsername));
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
                        ((IClientNetManager) second.NetMan).ClientDisconnect("Player Delight wire proof complete"));
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
            // Content startup emits the same benign ignored-prototype warning as the pooled client.
            // The pool filters it before handing the client to a test; this manually owned client
            // starts inside the test, so only fail it on actual errors.
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
                ClientBeforeIoC = () => IoCManager.Register<IParallaxManager, DummyParallaxManager>(true),
            });
        };

        return new RobustIntegrationTest.ClientIntegrationInstance(options);
    }

    private async Task<WireContext> PrepareBeacon(RobustIntegrationTest.ClientIntegrationInstance second)
    {
        ICommonSession firstSession = null!;
        ICommonSession secondSession = null!;
        EntityUid firstActor = default;
        EntityUid secondActor = default;
        EntityUid beacon = default;
        NetEntity beaconNet = default;

        await Server.WaitAssertion(() =>
        {
            var players = Server.ResolveDependency<IPlayerManager>();
            Assert.That(players.TryGetSessionByUsername(SecondUsername, out var resolvedSecond), Is.True);
            secondSession = resolvedSecond!;
            firstSession = players.Sessions.Single(session => session.Name != SecondUsername);
            firstActor = firstSession.AttachedEntity!.Value;
            var coordinates = Server.Transform(firstActor).Coordinates;
            secondActor = Server.EntMan.SpawnEntity("MobHuman", coordinates);
            Assert.That(players.SetAttachedEntity(secondSession, secondActor), Is.True);
            players.JoinGame(secondSession);
            beacon = Server.EntMan.SpawnEntity("SolreignWingmateBeacon", coordinates);
            beaconNet = Server.EntMan.GetNetEntity(beacon);
        });
        await RunThreeWayTicks(second, 8);

        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            ui.OpenUi(beacon, WingmateUiKey.Beacon, firstSession);
            ui.OpenUi(beacon, WingmateUiKey.Beacon, secondSession);
        });
        await RunThreeWayTicks(second, 6);

        await Client.WaitAssertion(() => AssertClientBuiOpen(Client, beaconNet));
        await second.WaitAssertion(() => AssertClientBuiOpen(second, beaconNet));
        return new WireContext(firstSession, secondSession, firstActor, secondActor, beacon, beaconNet);
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

    private async Task SendBui(RobustIntegrationTest.ClientIntegrationInstance client,
        RobustIntegrationTest.ClientIntegrationInstance second, NetEntity beacon,
        BoundUserInterfaceMessage message)
    {
        await client.WaitPost(() => GetBui(client, beacon).SendMessage(message));
        await RunThreeWayTicks(second, 2);
    }

    private async Task CloseBui(RobustIntegrationTest.ClientIntegrationInstance client, NetEntity beacon)
    {
        await client.WaitPost(() => GetBui(client, beacon).Close());
    }

    private static WingmateBoundUserInterface GetBui(
        RobustIntegrationTest.ClientIntegrationInstance client, NetEntity beacon)
    {
        var uid = client.EntMan.GetEntity(beacon);
        var ui = client.EntMan.GetComponent<UserInterfaceComponent>(uid);
        return (WingmateBoundUserInterface) ui.ClientOpenInterfaces[WingmateUiKey.Beacon];
    }

    private static void AssertClientBuiOpen(
        RobustIntegrationTest.ClientIntegrationInstance client, NetEntity beacon)
    {
        var uid = client.EntMan.GetEntity(beacon);
        Assert.That(client.EntMan.TryGetComponent<UserInterfaceComponent>(uid, out var ui), Is.True);
        Assert.That(ui!.ClientOpenInterfaces.ContainsKey(WingmateUiKey.Beacon), Is.True);
    }

    private static void AssertWingmateState(WingmateUiState actual, WingmateUiState expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Mode, Is.EqualTo(expected.Mode));
            Assert.That(actual.Department, Is.EqualTo(expected.Department));
            Assert.That(actual.TeachingMode, Is.EqualTo(expected.TeachingMode));
            Assert.That(actual.RequesterToken, Is.EqualTo(expected.RequesterToken));
            Assert.That(actual.RequesterDisplayName, Is.EqualTo(expected.RequesterDisplayName));
            Assert.That(actual.PartnerDisplayName, Is.EqualTo(expected.PartnerDisplayName));
            Assert.That(actual.OfferNonce, Is.EqualTo(expected.OfferNonce));
            Assert.That(actual.StatusText, Is.EqualTo(expected.StatusText));
            Assert.That(actual.IsVolunteering, Is.EqualTo(expected.IsVolunteering));
        });
    }

    private static void AssertFirstShiftState(FirstShiftUiState actual, FirstShiftUiState expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Enabled, Is.EqualTo(expected.Enabled));
            Assert.That(actual.Active, Is.EqualTo(expected.Active));
            Assert.That(actual.SelectedDepartment, Is.EqualTo(expected.SelectedDepartment));
            Assert.That(actual.CardId, Is.EqualTo(expected.CardId));
            Assert.That(actual.Stage, Is.EqualTo(expected.Stage));
            Assert.That(actual.Generation, Is.EqualTo(expected.Generation));
        });
    }

    private static Dictionary<Enum, HashSet<EntityUid>> GetActorSetsForHostileWireProbe(
        UserInterfaceComponent component)
    {
        var field = typeof(UserInterfaceComponent).GetField(
            nameof(UserInterfaceComponent.Actors), BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null);
        return (Dictionary<Enum, HashSet<EntityUid>>) field!.GetValue(component)!;
    }

    private readonly record struct WireContext(
        ICommonSession FirstSession,
        ICommonSession SecondSession,
        EntityUid FirstActor,
        EntityUid SecondActor,
        EntityUid Beacon,
        NetEntity BeaconNet);
}
