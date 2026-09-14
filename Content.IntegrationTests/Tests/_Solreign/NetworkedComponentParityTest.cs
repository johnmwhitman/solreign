#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Guards the invariant that a server-only [NetworkedComponent] silently destroys.
///
///     NetIds are assigned by ordinal-sorting the NAMES of every networked component and numbering
///     them 0..N-1 (ComponentFactory.GenerateNetIds). Both sides must therefore register an
///     identical SET of names, or every id after the first divergence shifts on one side only.
///
///     When that happens the client removes any component whose local NetId is absent from the
///     server's id set (ClientGameStateManager.HandleEntityState). Because "Solreign..." sorts
///     before "Transform" ordinally, a batch of server-only Solreign components shifts Transform's
///     id and the client deletes TransformComponent from live entities, which trips
///     "Tried to remove a protected component" deep inside RobustToolbox.
///
///     That is not a hypothetical. It happened: 22 server-only [NetworkedComponent] attributes on
///     the sr-w-033..057 branch chain took the suite to roughly 5,593 failing tests, spread across
///     every test that connects a client, with a stack trace pointing at the engine rather than at
///     any Solreign file. The cause took a day to find and was once recorded as "disproven".
///
///     This test exists so that the same mistake costs ONE named failure that says what to do,
///     instead of a five-thousand-test cascade that says nothing. Solreign deliberately replaces
///     vanilla subsystems, so new Content.Server-only components get written here often. Keep it.
/// </summary>
[TestFixture]
public sealed class NetworkedComponentParityTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
    };

    private static List<string> NetworkedNames(IComponentFactory factory)
    {
        // Read the AUTHORITATIVE registry rather than re-deriving it by scanning attributes.
        // Re-deriving means this test could agree with itself while disagreeing with the engine,
        // which is the failure mode it exists to prevent.
        var registrations = factory.NetworkedComponents
            ?? throw new InvalidOperationException(
                "Networked component IDs were not generated before parity inspection.");

        return registrations
            .Select(registration => registration.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    [Test]
    public async Task ServerAndClientRegisterTheSameNetworkedComponents()
    {
        List<string> serverNames = null!;
        List<string> clientNames = null!;

        await Server.WaitPost(() =>
            serverNames = NetworkedNames(Server.ResolveDependency<IComponentFactory>()));
        await Client.WaitPost(() =>
            clientNames = NetworkedNames(Client.ResolveDependency<IComponentFactory>()));

        var serverOnly = serverNames.Except(clientNames).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var clientOnly = clientNames.Except(serverNames).OrderBy(n => n, StringComparer.Ordinal).ToList();

        if (serverOnly.Count == 0 && clientOnly.Count == 0)
            return;

        // Report the first shifted id too: it is the component that will actually be deleted off
        // live entities, and naming it is what makes this failure self-explanatory.
        var shared = serverNames.Intersect(clientNames).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var firstDivergence = serverOnly.Concat(clientOnly).OrderBy(n => n, StringComparer.Ordinal).First();
        var firstShifted = shared.FirstOrDefault(
            name => string.Compare(name, firstDivergence, StringComparison.Ordinal) > 0);

        Assert.Fail(
            "Server and client register different [NetworkedComponent] sets, so their NetIds are "
            + "misaligned and the client will delete mismatched components off live entities.\n\n"
            + $"  Registered on SERVER only ({serverOnly.Count}): "
            + $"{(serverOnly.Count == 0 ? "(none)" : string.Join(", ", serverOnly))}\n"
            + $"  Registered on CLIENT only ({clientOnly.Count}): "
            + $"{(clientOnly.Count == 0 ? "(none)" : string.Join(", ", clientOnly))}\n\n"
            + $"  First divergence: {firstDivergence}\n"
            + $"  First component whose NetId shifts because of it: {firstShifted ?? "(none)"}\n\n"
            + "FIX, in order of preference:\n"
            + "  1. Drop [NetworkedComponent]. 'Declares no ComponentState' is NECESSARY evidence\n"
            + "     but NOT sufficient: the attribute is still load-bearing for presence-only\n"
            + "     replication (PVS creates/removes the component by NetId even with null state),\n"
            + "     manual ComponentGetState/HandleState handlers declared elsewhere, owner- or\n"
            + "     session-filtered visibility, client prediction of add/remove, and any explicit\n"
            + "     consumer of NetID / NetworkedComponents / GetNetComponents / Dirty(). Check\n"
            + "     those before removing. SyncSpriteComponent is a real stateless-but-networked\n"
            + "     example in the engine.\n"
            + "  2. Move the component to Content.Shared, so both sides register it.\n"
            + "  3. Add a same-named counterpart in the other assembly. See ThrusterComponent,\n"
            + "     which exists in both Content.Server and Content.Client for exactly this reason.\n\n"
            + "Do NOT silence this test. It replaces a ~5,593-test cascade whose stack trace points\n"
            + "into RobustToolbox and names none of the components above.");
    }
}
