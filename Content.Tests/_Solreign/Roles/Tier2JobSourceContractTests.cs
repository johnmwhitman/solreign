#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._Solreign.Roles;

[TestFixture]
public sealed class Tier2JobSourceContractTests
{
    private static readonly string[] MapIds =
    {
        "solreign_oasis",
        "solreign_nocturne",
        "solreign_perihelion",
        "solreign_verdant",
        "solreign_meridian",
        "solreign_leviathan",
        "solreign_terminus",
    };

    private static readonly JobContract[] Jobs =
    {
        new(
            "SolreignSectorFreightMaster",
            "JobSolreignSectorFreightMaster",
            "SolreignSectorFreightMasterGear",
            "ClothingOuterHardsuitSpatio",
            "SpawnPointSolreignSectorFreightMaster",
            "Roles/Jobs/Cargo/sector_freight_master.yml"),
        new(
            "SolreignExpeditionSpecialist",
            "JobSolreignExpeditionSpecialist",
            "SolreignExpeditionSpecialistGear",
            "ClothingOuterHardsuitSalvage",
            "SpawnPointSolreignExpeditionSpecialist",
            "Roles/Jobs/Cargo/expedition_specialist.yml"),
    };

    [TestCaseSource(nameof(Jobs))]
    public void JobSourceIsBoundedAndComplete(JobContract contract)
    {
        var prototypes = LoadSequence(PrototypePath(contract.RelativePath));
        var job = FindPrototype(prototypes, "job", contract.Id);
        var gear = FindPrototype(prototypes, "startingGear", contract.GearId);
        var tracker = FindPrototype(prototypes, "playTimeTracker", contract.TrackerId);
        var icon = FindPrototype(prototypes, "jobIcon", $"JobIcon{contract.Id}");
        var loadout = FindPrototype(prototypes, "roleLoadout", $"Job{contract.Id}");
        var defaultWeights = Mapping(
            FindPrototype(
                LoadSequence(Path.Combine(RepoRoot(), "Resources", "Prototypes", "Roles", "job_weights.yml")),
                "jobWeight",
                "Default"),
            "weights");

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(job, "playTimeTracker"), Is.EqualTo(contract.TrackerId));
            Assert.That(Scalar(job, "startingGear"), Is.EqualTo(contract.GearId));
            Assert.That(Scalar(job, "icon"), Is.EqualTo($"JobIcon{contract.Id}"));
            Assert.That(Scalar(defaultWeights, contract.Id), Is.EqualTo("5"));
            Assert.That(Sequence(job, "access"), Is.EquivalentTo(new[] { "Cargo", "Salvage", "Maintenance", "External" }));
            Assert.That(Scalar(Mapping(gear, "equipment"), "outerClothing"), Is.EqualTo(contract.HardsuitId));
            Assert.That(Sequence(loadout, "groups"), Does.Not.Contain("GroupTankHarness"),
                "A tank harness occupies outerClothing before StartingGear and prevents Vox from receiving the required hardsuit.");
            Assert.That(tracker, Is.Not.Null);
            Assert.That(icon, Is.Not.Null);
            Assert.That(loadout, Is.Not.Null);
        });
    }

    [Test]
    public void EveryProductionMapAdvertisesAndPlacesBothJobs()
    {
        foreach (var mapId in MapIds)
        {
            var mapPrototype = File.ReadAllText(Path.Combine(RepoRoot(), "Resources", "Prototypes", "Maps", "_Solreign", $"{mapId}.yml"));
            var serializedMap = File.ReadAllText(Path.Combine(RepoRoot(), "Resources", "Maps", "_Solreign", $"{mapId}.yml"));

            Assert.Multiple(() =>
            {
                foreach (var job in Jobs)
                {
                    Assert.That(mapPrototype, Does.Contain($"{job.Id}: [ 1, 1 ]"), $"{mapId}: missing exact one-slot roster entry for {job.Id}");
                    Assert.That(serializedMap, Does.Contain($"- proto: {job.SpawnPrototypeId}"), $"{mapId}: missing spawn marker for {job.Id}");
                }
            });
        }
    }

    [Test]
    public void SolreignDepartmentListsBothJobs()
    {
        var department = File.ReadAllText(PrototypePath("Roles/solreign_department.yml"));
        foreach (var job in Jobs)
            Assert.That(department, Does.Contain($"  - {job.Id}"));
    }

    [Test]
    public void CargoDepartmentOwnsBothJobs()
    {
        var department = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Resources",
            "Prototypes",
            "Roles",
            "Jobs",
            "departments.yml"));
        var cargoBlock = department.Split("- type: department", StringSplitOptions.RemoveEmptyEntries)
            .Single(block => block.Contains("\n  id: Cargo\n", StringComparison.Ordinal));

        foreach (var job in Jobs)
            Assert.That(cargoBlock, Does.Contain($"  - {job.Id}"));
    }

    private static IReadOnlyList<YamlMappingNode> LoadSequence(string path)
    {
        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return ((YamlSequenceNode) yaml.Documents.Single().RootNode).Children.OfType<YamlMappingNode>().ToArray();
    }

    private static YamlMappingNode FindPrototype(IReadOnlyList<YamlMappingNode> prototypes, string type, string id)
    {
        return prototypes.Single(p => Scalar(p, "type") == type && Scalar(p, "id") == id);
    }

    private static string? Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? ((YamlScalarNode) value).Value
            : null;
    }

    private static string[] Sequence(YamlMappingNode node, string key)
    {
        return ((YamlSequenceNode) node.Children[new YamlScalarNode(key)]).Children
            .Cast<YamlScalarNode>()
            .Select(value => value.Value ?? string.Empty)
            .ToArray();
    }

    private static YamlMappingNode Mapping(YamlMappingNode node, string key)
    {
        return (YamlMappingNode) node.Children[new YamlScalarNode(key)];
    }

    private static string PrototypePath(string relativePath)
    {
        return Path.Combine(RepoRoot(), "Resources", "Prototypes", "_Solreign", relativePath);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SpaceStation14.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the game repository root.");
    }

    public sealed record JobContract(
        string Id,
        string TrackerId,
        string GearId,
        string HardsuitId,
        string SpawnPrototypeId,
        string RelativePath)
    {
        public override string ToString() => Id;
    }
}
