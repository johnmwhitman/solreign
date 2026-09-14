#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._Solreign.Roles;

/// <summary>
///     Every SOLREIGN job must have a <c>roleLoadout</c> prototype, because without one the
///     player spawns with no clothing at all.
///
///     THIS GUARDS A DEFECT A PLAYER FOUND, NOT ONE WE IMAGINED. On 2026-07-30 a real player
///     reported "all the assistant/intern roles seem to be naked". They were, and nothing in the
///     suite noticed, because the failure is a SILENT SKIP rather than an error:
///
///         LoadoutSystem.GetJobPrototype(id) => "Job" + id      (Content.Shared/Clothing/LoadoutSystem.cs:39)
///         StationSpawningSystem.SpawnPlayerMob:
///             if (ProtoMan.TryIndex(jobLoadout, out RoleLoadoutPrototype? roleProto))   // :98
///                 ...
///                 EquipRoleLoadout(entity.Value, loadout, roleProto!);                  // :149
///
///     EquipRoleLoadout sits INSIDE that TryIndex guard. Each SOLREIGN job shipped a
///     <c>playTimeTracker</c> named JobSolreign* but no <c>roleLoadout</c> of that name, so
///     TryIndex returned false, the block was skipped, and the character spawned in underwear.
///     No exception. No warning. A green suite.
///
///     Note this cannot be caught by checking <c>startingGear</c>: vanilla's own MedicalInternGear
///     has no jumpsuit either. Body clothing comes from the loadout groups, which is exactly why
///     the missing prototype was invisible.
///
///     This test reads the shipped YAML rather than the runtime registry on purpose — it must fail
///     for a job whose loadout was never authored, which a runtime lookup would simply skip.
/// </summary>
[TestFixture]
public sealed class SolreignJobRoleLoadoutContractTests
{
    /// <summary>
    ///     Prototype-type name for the job definition itself.
    /// </summary>
    private const string JobType = "job";

    /// <summary>
    ///     Prototype-type name for the loadout the spawner resolves as "Job" + jobId.
    /// </summary>
    private const string RoleLoadoutType = "roleLoadout";

    /// <summary>
    ///     A job clothes its player through EITHER a roleLoadout OR a jumpsuit named directly in
    ///     its startingGear. Having neither is what "naked" actually means.
    ///
    ///     Both halves matter. Asserting only "has a roleLoadout" would fail GreenshieldOfficer
    ///     and NTRepresentative, which are fully clothed by their startingGear — the test would
    ///     be reporting a nakedness that does not exist.
    /// </summary>
    [Test]
    public void EverySolreignJobClothesItsPlayer()
    {
        var jobs = CollectJobs(SolreignPrototypeRoot());

        Assert.That(
            jobs,
            Is.Not.Empty,
            "Found no SOLREIGN job prototypes at all — this test would pass vacuously. " +
            "Check that Resources/Prototypes/_Solreign/Roles still holds the job definitions.");

        // Either source may be authored anywhere under Resources/Prototypes: a SOLREIGN job is
        // allowed to reuse a loadout or a gear prototype defined upstream.
        var loadoutIds = CollectIds(AllPrototypeRoot(), RoleLoadoutType);
        var gearWithJumpsuit = CollectGearProvidingJumpsuit(AllPrototypeRoot());

        var naked = jobs
            .Where(job => !loadoutIds.Contains("Job" + job.Id))
            .Where(job => job.StartingGear == null || !gearWithJumpsuit.Contains(job.StartingGear))
            .Select(job => job.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            naked,
            Is.Empty,
            "These SOLREIGN jobs supply no body clothing from either source, so the player spawns " +
            "in underwear. StationSpawningSystem skips EquipRoleLoadout when the roleLoadout is " +
            "absent, and their startingGear names no jumpsuit either: " +
            string.Join(", ", naked.Select(j => $"{j} (add roleLoadout Job{j}, or a jumpsuit in its startingGear)")));
    }

    private sealed record JobEntry(string Id, string? StartingGear);

    /// <summary>
    ///     Collect SOLREIGN job prototypes along with the startingGear each one names.
    /// </summary>
    private static IReadOnlyList<JobEntry> CollectJobs(string root)
    {
        var jobs = new List<JobEntry>();

        foreach (var path in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            foreach (var node in LoadSequence(path))
            {
                if (Scalar(node, "type") != JobType)
                    continue;

                var id = Scalar(node, "id");
                if (!string.IsNullOrWhiteSpace(id))
                    jobs.Add(new JobEntry(id!, Scalar(node, "startingGear")));
            }
        }

        return jobs;
    }

    /// <summary>
    ///     Ids of every startingGear prototype that equips something in the jumpsuit slot.
    /// </summary>
    private static HashSet<string> CollectGearProvidingJumpsuit(string root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            foreach (var node in LoadSequence(path))
            {
                if (Scalar(node, "type") != "startingGear")
                    continue;

                var id = Scalar(node, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (node.Children.TryGetValue(new YamlScalarNode("equipment"), out var equipment)
                    && equipment is YamlMappingNode slots
                    && !string.IsNullOrWhiteSpace(Scalar(slots, "jumpsuit")))
                {
                    ids.Add(id!);
                }
            }
        }

        return ids;
    }

    /// <summary>
    ///     Collect every prototype id of the given type under a directory tree.
    /// </summary>
    private static HashSet<string> CollectIds(string root, string type)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            foreach (var node in LoadSequence(path))
            {
                if (Scalar(node, "type") != type)
                    continue;

                var id = Scalar(node, "id");
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id!);
            }
        }

        return ids;
    }

    private static IReadOnlyList<YamlMappingNode> LoadSequence(string path)
    {
        var stream = new YamlStream();

        try
        {
            using var reader = new StreamReader(path);
            stream.Load(reader);
        }
        catch (Exception)
        {
            // A file this test cannot parse is not this test's subject. The YAML linter owns
            // malformed prototypes and will fail on them independently.
            return Array.Empty<YamlMappingNode>();
        }

        if (stream.Documents.Count == 0)
            return Array.Empty<YamlMappingNode>();

        return stream.Documents[0].RootNode is YamlSequenceNode sequence
            ? sequence.Children.OfType<YamlMappingNode>().ToArray()
            : Array.Empty<YamlMappingNode>();
    }

    private static string? Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }

    private static string SolreignPrototypeRoot()
    {
        return Path.Combine(RepoRoot(), "Resources", "Prototypes", "_Solreign");
    }

    private static string AllPrototypeRoot()
    {
        return Path.Combine(RepoRoot(), "Resources", "Prototypes");
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
}
