using Content.Server.Administration.Systems;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignLiveMapPrivacyRules))]
public sealed class SolreignLiveMapPrivacyRulesTests
{
    [Test]
    public void BuildCells_AggregatesAndQuantizesWithoutIdentity()
    {
        var cells = SolreignLiveMapPrivacyRules.BuildCells(new[]
        {
            new SolreignLiveMapPrivacyRules.Observation(0.1f, 7.9f),
            new SolreignLiveMapPrivacyRules.Observation(7.99f, 0.1f),
            new SolreignLiveMapPrivacyRules.Observation(8f, -0.1f),
        });

        Assert.That(cells, Is.EqualTo(new[]
        {
            new SolreignLiveMapPrivacyRules.Cell(0, 0, 2),
            new SolreignLiveMapPrivacyRules.Cell(1, -1, 1),
        }));
        Assert.That(cells[0].CellId, Is.EqualTo("0:0"));
    }

    [Test]
    public void BuildCells_RejectsNonFiniteAndOutOfBoundsObservations()
    {
        var cells = SolreignLiveMapPrivacyRules.BuildCells(new[]
        {
            new SolreignLiveMapPrivacyRules.Observation(float.NaN, 0),
            new SolreignLiveMapPrivacyRules.Observation(0, float.PositiveInfinity),
            new SolreignLiveMapPrivacyRules.Observation(
                (SolreignLiveMapPrivacyRules.MaximumCellCoordinate + 1) *
                SolreignLiveMapPrivacyRules.CellSizeTiles,
                0),
            new SolreignLiveMapPrivacyRules.Observation(-8, -8),
        });

        Assert.That(cells, Is.EqualTo(new[]
        {
            new SolreignLiveMapPrivacyRules.Cell(-1, -1, 1),
        }));
    }

    [Test]
    public void BuildCells_BoundsOccupantsAndCellCardinality()
    {
        var observations = Enumerable.Range(0, SolreignLiveMapPrivacyRules.MaximumOccupants + 50)
            .Select(index => new SolreignLiveMapPrivacyRules.Observation(
                index * SolreignLiveMapPrivacyRules.CellSizeTiles,
                0));

        var cells = SolreignLiveMapPrivacyRules.BuildCells(observations);

        Assert.That(cells, Has.Count.EqualTo(SolreignLiveMapPrivacyRules.MaximumCells));
        Assert.That(cells.Sum(cell => cell.Count), Is.LessThanOrEqualTo(
            SolreignLiveMapPrivacyRules.MaximumOccupants));
    }

    [Test]
    public void SnapshotJson_HasExactAnonymousContractAndNoProhibitedIdentityFields()
    {
        var snapshot = SolreignLiveMapPrivacyRules.BuildSnapshot(
            roundId: 42,
            generatedAtUnixMs: 123456789,
            new[] { new SolreignLiveMapPrivacyRules.Observation(16, 24) });

        var json = JsonSerializer.Serialize(snapshot);

        Assert.That(json, Is.EqualTo(
            "{\"schemaVersion\":1,\"roundId\":42,\"generatedAtUnixMs\":123456789," +
            "\"cells\":[{\"x\":2,\"y\":3,\"count\":1,\"cellId\":\"2:3\"}]}"));
        Assert.That(json, Does.Not.Contain("player").IgnoreCase);
        Assert.That(json, Does.Not.Contain("user").IgnoreCase);
        Assert.That(json, Does.Not.Contain("session").IgnoreCase);
        Assert.That(json, Does.Not.Contain("rotation").IgnoreCase);
        Assert.That(json, Does.Not.Contain("entity").IgnoreCase);
        Assert.That(json, Does.Not.Contain("name").IgnoreCase);
        Assert.That(json, Does.Not.Contain("role").IgnoreCase);
    }

    [Test]
    public void ProductionPublisher_HasOneAnonymousImplementation()
    {
        var sourceDirectory = Path.Combine(
            FindRepositoryRoot(),
            "Content.Server", "Administration", "Systems");
        var publisherFiles = Directory.GetFiles(sourceDirectory, "*LiveMapSystem.cs");

        Assert.That(publisherFiles, Has.Length.EqualTo(1),
            "Only one auto-discovered LiveMap EntitySystem may exist.");

        var source = File.ReadAllText(publisherFiles.Single());
        Assert.That(source, Does.Contain("public sealed partial class LiveMapSystem"));
        Assert.That(source, Does.Contain("SolreignLiveMapPrivacyRules.BuildSnapshot"));
        Assert.That(source, Does.Not.Contain("session.UserId"));
        Assert.That(source, Does.Not.Contain("playerId"));
        Assert.That(source, Does.Not.Contain("LocalRotation"));
        Assert.That(source, Does.Not.Contain("TimeSpan.FromSeconds(2)"));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Content.Server",
                    "Content.Server.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Game repository root.");
    }
}
