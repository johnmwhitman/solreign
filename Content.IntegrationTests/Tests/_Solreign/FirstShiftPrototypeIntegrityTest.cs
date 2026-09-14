#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared.Guidebook;
using Content.Shared.Roles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>Engine-backed complement to the cheap filesystem contract test.</summary>
[TestFixture]
public sealed class FirstShiftPrototypeIntegrityTest : GameTest
{
    private static readonly string[] SolreignMaps =
    [
        "solreign_leviathan.yml", "solreign_meridian.yml", "solreign_nocturne.yml",
        "solreign_oasis.yml", "solreign_perihelion.yml", "solreign_terminus.yml",
        "solreign_verdant.yml",
    ];
    private static readonly string[] StaticUiLocIds =
    [
        "first-shift-ahelp", "first-shift-another-card", "first-shift-card-if-stuck",
        "first-shift-card-orient", "first-shift-card-safety-stop", "first-shift-card-try",
        "first-shift-card-why", "first-shift-department-cargo", "first-shift-department-engineering",
        "first-shift-department-label", "first-shift-department-medical", "first-shift-department-science",
        "first-shift-department-service", "first-shift-department-universal", "first-shift-disabled",
        "first-shift-end", "first-shift-find-crewmate", "first-shift-heading", "first-shift-idle-intro",
        "first-shift-map-arrivals-marker", "first-shift-map-department-marker", "first-shift-map-title",
        "first-shift-marker-arrivals-fallback", "first-shift-marker-department", "first-shift-marker-unavailable",
        "first-shift-marker-unknown", "first-shift-open-guide", "first-shift-open-map",
        "first-shift-plain-instructions", "first-shift-providence-omen-heading", "first-shift-reroll-cooldown",
        "first-shift-skip", "first-shift-stage-complete", "first-shift-stage-next-debrief",
        "first-shift-stage-next-orient", "first-shift-stage-next-try", "first-shift-start",
        "first-shift-suggested-department", "first-shift-tab-title", "wingmates-tab-title",
    ];

    [Test]
    public async Task ReviewedDeckDeserializesAndAllTypedReferencesAndLocIdsResolve()
    {
        var server = Server;
        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var localization = server.ResolveDependency<ILocalizationManager>();
            var cards = prototypes.EnumeratePrototypes<FirstShiftAssignmentPrototype>()
                .Where(card => card.Enabled)
                .OrderBy(card => card.ID)
                .ToArray();

            Assert.That(cards, Has.Length.EqualTo(16),
                "The engine must deserialize the complete reviewed First Shift deck.");
            foreach (var card in cards)
            {
                Assert.Multiple(() =>
                {
                    foreach (var job in card.EligibleSafeJobs)
                        Assert.That(prototypes.HasIndex<JobPrototype>(job.Id), Is.True, $"{card.ID}: missing job {job}");
                    Assert.That(prototypes.HasIndex<GuideEntryPrototype>(card.GuideEntry.Id), Is.True,
                        $"{card.ID}: missing guide {card.GuideEntry}");
                    foreach (var anchor in card.Anchors)
                        Assert.That(prototypes.HasIndex<EntityPrototype>(anchor.Id), Is.True,
                            $"{card.ID}: missing anchor {anchor}");
                    foreach (var loc in new[] { card.Title, card.Why, card.Orient, card.Try, card.IfStuck, card.SafetyStop }
                                 .Concat(card.Flavor))
                        Assert.That(localization.TryGetString(loc.Id, out _), Is.True,
                            $"{card.ID}: missing localization {loc.Id}");
                });
            }

            foreach (var key in StaticUiLocIds)
            {
                Assert.That(localization.TryGetString(key, out var value,
                    ("department", "Engineering"), ("instruction", "Test instruction"), ("label", "Engineering")),
                    Is.True, $"Static client localization key did not resolve: {key}");
                Assert.That(value, Is.Not.Null.And.Not.Empty, $"Static client localization was empty: {key}");
            }

            Assert.That(localization.TryGetString("first-shift-skip", out var skip), Is.True);
            Assert.That(skip, Is.EqualTo("Skip this step"),
                "Skip advances one stage and must not claim to end the assignment.");
        });
    }

    [Test]
    public async Task EverySolreignMapContainsADeclaredAnchorOrExplicitArrivalsFallbackForEveryDeck()
    {
        var server = Server;
        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var resources = server.ResolveDependency<IResourceManager>();
            var decks = prototypes.EnumeratePrototypes<FirstShiftAssignmentPrototype>()
                .Where(card => card.Enabled && card.Department != FirstShiftDepartment.Universal)
                .GroupBy(card => card.Department)
                .ToDictionary(group => group.Key,
                    group => group.SelectMany(card => card.Anchors).Select(anchor => anchor.Id).Distinct().ToArray());

            Assert.That(decks.Keys, Is.EquivalentTo(new[]
            {
                FirstShiftDepartment.Engineering, FirstShiftDepartment.Medical,
                FirstShiftDepartment.Science, FirstShiftDepartment.Cargo, FirstShiftDepartment.Service,
            }));
            foreach (var map in SolreignMaps)
            {
                var path = new ResPath($"/Maps/_Solreign/{map}");
                using var reader = resources.ContentFileReadText(path);
                var source = reader.ReadToEnd();
                foreach (var (department, anchors) in decks)
                {
                    Assert.That(anchors, Does.Contain(FirstShiftAssignmentValidator.ArrivalsFallback),
                        $"{department} deck must explicitly declare the Arrivals fallback.");
                    Assert.That(anchors.Any(source.Contains), Is.True,
                        $"{map}: {department} resolves neither a declared department anchor nor Arrivals fallback.");
                }
            }
        });
    }
}
