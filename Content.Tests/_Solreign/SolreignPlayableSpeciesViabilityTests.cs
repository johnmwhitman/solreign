#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._Solreign;

/// <summary>
///     LIVE-INCIDENT GATE (2026-07-26): a player picked the <b>Shadow</b> species, spawned, and
///     immediately began "gasping for air and dying". Root cause: <c>AppearanceShadow</c>'s
///     <c>InitialBody.organs</c> declared body parts, a brain and eyes — <b>and no internal organs
///     at all</b>. No <c>Lungs</c>. A Shadow had no respiratory system, so asphyxiation started the
///     moment the body existed. Human declares twenty organs; Shadow declared twelve.
///
///     WHY NOTHING CAUGHT IT — the reason this test is a cheap YAML check and not another
///     integration test:
///     <list type="bullet">
///       <item>The YAML linter validated every prototype: they were all well-formed and every
///             parent resolved. An organ that is simply ABSENT is not a malformed prototype.</item>
///       <item><see cref="SolreignRoundStartJobSpreadGateTest"/> and the map-health scorecard drive
///             real rounds on all seven maps — with DEFAULT HUMAN dummies. They never seat a
///             Shadow, so they cannot see a Shadow-only defect.</item>
///       <item>Species selection is per-PLAYER data. Every gate in this repo spawns the default
///             species, so any species-specific breakage is invisible to all of them.</item>
///     </list>
///     A round-start-selectable species is content every player can pick. It deserves a gate that
///     does not depend on someone happening to pick it in a test.
///
///     THE INVARIANT, calibrated against the real corpus rather than asserted from intuition:
///     every <c>roundStart: true</c> species must declare the MEASURED 12-slot intersection of all
///     playable species (see RequiredOrgans) — body parts, Brain and Lungs.
///     Deliberately NOT requiring Heart/Stomach/Liver/Kidneys/Tongue/Appendix/Ears — measured across
///     all ten playable species, <c>Diona</c> lacks Heart, Liver, Tongue, Appendix, Ears and Kidneys,
///     and <c>SlimePerson</c> additionally lacks Stomach and even Eyes. Requiring any of those would
///     false-fail correct content. <c>Lungs</c> is precisely what Shadow was missing.
/// </summary>
[TestFixture]
public sealed class SolreignPlayableSpeciesViabilityTests
{
    /// <summary>
    ///     The MEASURED intersection of every round-start species' organ set — not a guess. All ten
    ///     playable species declare these twelve today, so requiring them false-fails nothing, while
    ///     being strictly stronger than the Lungs+Brain pair this gate originally asserted.
    ///     Deliberately excluded because real species legitimately omit them: Heart, Stomach, Liver,
    ///     Kidneys, Tongue, Appendix, Ears (Diona has no Heart/Liver; SlimePerson no
    ///     Heart/Stomach/Liver and no Eyes either).
    /// </summary>
    private static readonly string[] RequiredOrgans =
    {
        "Torso", "Head",
        "ArmLeft", "ArmRight", "HandLeft", "HandRight",
        "LegLeft", "LegRight", "FootLeft", "FootRight",
        "Brain", "Lungs",
    };

    /// <summary>Ten playable species exist today; a floor well under that catches a parser that
    /// silently stops finding species without flaking on normal content churn.</summary>
    private const int MinPlayableSpecies = 8;

    [Test]
    public void EveryRoundStartSpecies_DeclaresTheOrgansItNeedsToSurvive()
    {
        var protoRoot = Path.Combine(LocateRepoRoot(), "Resources", "Prototypes");

        var species = new List<(string Id, string? MobProto)>();
        var organsByEntity = new Dictionary<string, Dictionary<string, string>?>(StringComparer.Ordinal);
        var parentsByEntity = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unreadable = new List<string>();

        foreach (var file in Directory.EnumerateFiles(protoRoot, "*.yml", SearchOption.AllDirectories))
        {
            YamlStream stream;
            try
            {
                stream = new YamlStream();
                stream.Load(new StringReader(File.ReadAllText(file, Encoding.UTF8)));
            }
            catch (Exception e)
            {
                unreadable.Add($"{file}: {e.GetType().Name}");
                continue;
            }

            foreach (var doc in stream.Documents)
            {
                if (doc.RootNode is not YamlSequenceNode root)
                    continue;

                foreach (var node in root.Children.OfType<YamlMappingNode>())
                {
                    var type = Scalar(node, "type");

                    if (type == "species" && Scalar(node, "id") is { } sid)
                    {
                        if (string.Equals(Scalar(node, "roundStart"), "true", StringComparison.OrdinalIgnoreCase))
                            species.Add((sid, Scalar(node, "prototype")));
                        continue;
                    }

                    if (type != "entity" || Scalar(node, "id") is not { } eid)
                        continue;

                    parentsByEntity[eid] = ParentsOf(node);
                    organsByEntity[eid] = OrgansOf(node);
                }
            }
        }

        Assert.That(unreadable, Is.Empty,
            "Prototype files failed to parse — coverage is silently reduced:" + Environment.NewLine +
            string.Join(Environment.NewLine, unreadable.Take(10)));

        Assert.That(species, Has.Count.GreaterThanOrEqualTo(MinPlayableSpecies),
            $"Only {species.Count} round-start species found — the reader is probably not finding them.");

        var failures = new List<string>();

        foreach (var (id, mobProto) in species.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (mobProto is null)
            {
                failures.Add($"{id}: species declares no `prototype:` mob entity");
                continue;
            }

            var organs = ResolveOrgans(mobProto, organsByEntity, parentsByEntity);
            if (organs is null)
            {
                failures.Add($"{id}: could not resolve InitialBody.organs from '{mobProto}'");
                continue;
            }

            var missing = RequiredOrgans.Where(o => !organs.ContainsKey(o)).ToList();
            if (missing.Count > 0)
            {
                failures.Add(
                    $"{id} (via '{mobProto}') is missing {string.Join(", ", missing)} — " +
                    $"declares {organs.Count} organ(s): {string.Join(", ", organs.Keys.OrderBy(k => k, StringComparer.Ordinal))}");
            }
        }

        Assert.That(failures, Is.Empty,
            "Round-start-selectable species are missing survival-critical organs. A player who picks " +
            "one of these spawns unable to breathe and dies within seconds — exactly the 2026-07-26 " +
            "Shadow incident. Add the organ to that species' InitialBody.organs." +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", failures));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Organs are declared on the appearance entity and inherited by the mob, so this
    /// walks parents exactly like SS14's per-datafield component merge.</summary>
    private static Dictionary<string, string>? ResolveOrgans(
        string id,
        IReadOnlyDictionary<string, Dictionary<string, string>?> organs,
        IReadOnlyDictionary<string, List<string>> parents,
        HashSet<string>? seen = null)
    {
        seen ??= new HashSet<string>(StringComparer.Ordinal);
        if (!seen.Add(id))
            return null;

        // A PRESENT `organs:` key is authoritative even when EMPTY. SS14 replaces the parent's
        // dictionary rather than merging into it, so `organs: { }` means zero organs — instant
        // asphyxiation — and must fail this gate. Testing `Count > 0` instead let an empty map fall
        // through to the parent and PASS: a false pass shaped exactly like the bug this gate exists
        // for. Caught by an independent QA pass and mutation-proved with `organs: { }` on MobShadow.
        // The `{ }` idiom is already live in this repo (Body/Species/dwarf.yml:8).
        if (organs.TryGetValue(id, out var own) && own is not null)
            return own;

        if (!parents.TryGetValue(id, out var ps))
            return null;

        foreach (var parent in ps)
        {
            // Each branch gets its OWN visited set. Sharing one across siblings means an ancestor
            // first reached down a DEAD branch is marked visited and returns null on a LIVE one —
            // the wrong answer from the function whose only job is modelling inheritance. A per-path
            // copy still catches real cycles. Latent on current content; found by QA review.
            var branchSeen = new HashSet<string>(seen, StringComparer.Ordinal);
            if (ResolveOrgans(parent, organs, parents, branchSeen) is { } inherited)
                return inherited;
        }

        return null;
    }

    private static Dictionary<string, string>? OrgansOf(YamlMappingNode entity)
    {
        if (Get(entity, "components") is not YamlSequenceNode comps)
            return null;

        foreach (var comp in comps.Children.OfType<YamlMappingNode>())
        {
            if (Scalar(comp, "type") != "InitialBody")
                continue;

            if (Get(comp, "organs") is not YamlMappingNode organs)
                continue;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in organs.Children)
            {
                if (k is YamlScalarNode { Value: { } slot } && v is YamlScalarNode { Value: { } proto })
                    result[slot] = proto;
            }

            return result;
        }

        return null;
    }

    private static List<string> ParentsOf(YamlMappingNode node)
    {
        var result = new List<string>();
        switch (Get(node, "parent"))
        {
            case YamlScalarNode { Value: { } one } when !string.IsNullOrWhiteSpace(one):
                result.Add(one);
                break;
            case YamlSequenceNode seq:
                result.AddRange(seq.Children.OfType<YamlScalarNode>()
                    .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))!);
                break;
        }
        return result;
    }

    private static YamlNode? Get(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v : null;

    private static string? Scalar(YamlMappingNode node, string key) =>
        Get(node, key) is YamlScalarNode s ? s.Value : null;

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Resources", "Prototypes")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repo root from '{AppContext.BaseDirectory}'.");
    }
}
