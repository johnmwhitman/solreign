#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.EasterEggs;
using Content.Shared.Item;
using NUnit.Framework;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Beta feedback items 3+4 (docs/BETA-FEEDBACK-01.md Lane A): regression coverage for the two
///     easter-egg items that went from decor to "real functionality" this wave.
///
///     Pattern studied from <c>WerewolfPolymorphTriggerTest</c> (bare <c>MapCoordinates.Nullspace</c>
///     spawns, no need for a real map/round). Deliberately light: the pure cooldown/formatting math
///     is exhaustively unit-tested in Content.Tests/_Solreign (SolreignWristOrganizerRulesTests);
///     what's worth an integration test is that the PROTOTYPE actually carries the component with
///     the right sign/direction — a regression class this codebase already named once (the werewolf
///     "built but never instantiated" lesson, <c>SolreignPrototypeIdIntegrityTest</c>) generalized to
///     "built but the multiplier points the wrong way."
/// </summary>
[TestFixture]
public sealed class EasterEggFunctionalityIntegrationTest : GameTest
{
    [Test]
    public async Task SolreignEgg12_HasHeldSpeedModifier_AndItIsAFasterNotSlowerCapsule()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var capsule = entMan.SpawnEntity("SolreignEgg12", MapCoordinates.Nullspace);

            Assert.That(entMan.TryGetComponent<HeldSpeedModifierComponent>(capsule, out var mod), Is.True,
                "SolreignEgg12 (spherical personal transit capsule) must carry a HeldSpeedModifierComponent " +
                "-- the 'cheapest fun one' pick documented in easter_eggs.yml -- or gripping it does nothing.");

            Assert.Multiple(() =>
            {
                Assert.That(mod!.WalkModifier, Is.GreaterThan(1f),
                    "The transit capsule is documented as a FAST-move item; a modifier <= 1 would silently " +
                    "make it a slow/heavy item instead (the companion cube's effect, inverted by accident).");
                Assert.That(mod.SprintModifier, Is.GreaterThan(1f));
            });

            entMan.DeleteEntity(capsule);
        });
    }

    [Test]
    public async Task SolreignEgg15_HasWristOrganizerComponent()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var organizer = entMan.SpawnEntity("SolreignEgg15", MapCoordinates.Nullspace);

            Assert.That(entMan.HasComponent<SolreignWristOrganizerComponent>(organizer), Is.True,
                "SolreignEgg15 (wrist-mounted organizer) must carry SolreignWristOrganizerComponent or " +
                "using it in-hand does nothing but fall through to the inherited GPS examine text.");

            entMan.DeleteEntity(organizer);
        });
    }
}
