#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class FirstShiftContentTests
{
    private const string DeckPath = "Prototypes/_Solreign/PlayerDelight/first_shift_assignments.yml";
    private const string LocalePath = "Locale/en-US/_solreign/first-shift.ftl";
    private const string Arrivals = "DefaultStationBeaconArrivals";

    private sealed record ReviewedCard(
        string Department,
        string Category,
        string Title,
        string Why,
        string Orient,
        string Try,
        string IfStuck,
        string Safety,
        string RequiredSafetyPhrase,
        IReadOnlyList<string> Flavor);

    private static readonly Dictionary<string, ReviewedCard> Reviewed = new(StringComparer.Ordinal)
    {
        ["FirstShiftEngineeringThreshold"] = Card("Engineering", "Orientation", "engineering-threshold", "engineering"),
        ["FirstShiftEngineeringApcExterior"] = Card("Engineering", "Observation", "engineering-apc", "engineering"),
        ["FirstShiftEngineeringPublicTool"] = Card("Engineering", "GuideReference", "engineering-tool", "engineering"),
        ["FirstShiftMedicalThreshold"] = Card("Medical", "Orientation", "medical-threshold", "medical"),
        ["FirstShiftMedicalEmergencyLabels"] = Card("Medical", "Observation", "medical-labels", "medical"),
        ["FirstShiftMedicalFirstAidReference"] = Card("Medical", "GuideReference", "medical-reference", "medical"),
        ["FirstShiftScienceThreshold"] = Card("Science", "Orientation", "science-threshold", "science"),
        ["FirstShiftScienceInertExterior"] = Card("Science", "Observation", "science-inert", "science"),
        ["FirstShiftScienceResearchSurface"] = Card("Science", "GuideReference", "science-surface", "science"),
        ["FirstShiftCargoThreshold"] = Card("Cargo", "Orientation", "cargo-threshold", "cargo"),
        ["FirstShiftCargoBenignAppraisal"] = Card("Cargo", "BenignAppraisal", "cargo-appraisal", "cargo"),
        ["FirstShiftCargoDeliverySurface"] = Card("Cargo", "Observation", "cargo-delivery", "cargo"),
        ["FirstShiftServiceThreshold"] = Card("Service", "Orientation", "service-threshold", "service"),
        ["FirstShiftServicePantryLabel"] = Card("Service", "Observation", "service-pantry", "service"),
        ["FirstShiftServiceCleaningSurface"] = Card("Service", "Observation", "service-cleaning", "service"),
        ["FirstShiftUniversalWelcome"] = Card("Universal", "GuideReference", "universal", "universal"),
    };

    private static readonly Dictionary<string, string> SafeJobs = new()
    {
        ["Engineering"] = "SolreignEngineeringApprentice",
        ["Medical"] = "SolreignMedicalIntern",
        ["Science"] = "SolreignResearchAssistant",
        ["Cargo"] = "SolreignCargoTrainee",
        ["Service"] = "SolreignKitchenHand",
    };

    private static readonly Dictionary<string, string> SafetyPhrases = new()
    {
        ["engineering"] = "Do not open equipment, touch wires, or change power or atmospherics. Observation is enough.",
        ["medical"] = "Do not treat anyone, remove supplies, inject, or consume anything. Reading and observation are enough.",
        ["science"] = "Do not activate equipment, run experiments, or handle hazardous materials. Exterior observation is enough.",
        ["cargo"] = "Do not accept orders, spend funds, or move station property. Observation and your own belongings are enough.",
        ["service"] = "Do not use heat, knives, chemicals, or consume anything. Labels and observation are enough.",
        ["universal"] = "Do not enter restricted areas, handle weapons, make arrests, investigate antagonists, or leave the station. Staying at Arrivals is always valid.",
    };

    private static readonly string[] RequiredClientLocIds =
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

    private static readonly Regex ForbiddenLiteral = new(
        @"\b(?:open(?:ing)? (?:(?:an?|the) )?apc|cut(?:ting)? (?:a )?wire|pulse (?:a )?wire|hack|rewire|" +
        @"inject|consume|drink|treat (?:a |the |another )?(?:person|patient|crewmember)|operate on|" +
        @"arrest|detain|attack|shoot|stab|investigate (?:an? )?(?:antag|antagonist)|" +
        @"leave (?:the )?station|go off[- ]station|change (?:the )?(?:power|atmospherics|atmos)|" +
        @"vent (?:the )?(?:room|station)|fire (?:a )?weapon|use (?:a )?(?:knife|weapon))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ForbiddenFlavor = new(
        @"\b(?:we know you|your history|your profile|we remember you|newbie|newcomer|first[- ]time player|" +
        @"publicly announce|public announcement|earn (?:a )?(?:reward|rank|title)|grants? (?:a )?(?:reward|rank|title)|" +
        @"score|leaderboard|currency)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Test]
    public void DeckExactlyMatchesTheReviewedCardContract()
    {
        var cards = LoadCards().ToDictionary(CardId, StringComparer.Ordinal);
        Assert.That(cards.Keys, Is.EquivalentTo(Reviewed.Keys), "A reviewed card was removed, replaced, or added.");

        foreach (var (id, expected) in Reviewed)
        {
            var card = cards[id];
            Assert.Multiple(() =>
            {
                Assert.That(Scalar(card, "enabled"), Is.EqualTo("true"), id);
                Assert.That(Scalar(card, "department"), Is.EqualTo(expected.Department), id);
                Assert.That(Scalar(card, "taskCategory"), Is.EqualTo(expected.Category), id);
                Assert.That(Scalar(card, "title"), Is.EqualTo(expected.Title), id);
                Assert.That(Scalar(card, "why"), Is.EqualTo(expected.Why), id);
                Assert.That(Scalar(card, "orient"), Is.EqualTo(expected.Orient), id);
                Assert.That(Scalar(card, "try"), Is.EqualTo(expected.Try), id);
                Assert.That(Scalar(card, "ifStuck"), Is.EqualTo(expected.IfStuck), id);
                Assert.That(Scalar(card, "safetyStop"), Is.EqualTo(expected.Safety), id);
                Assert.That(Sequence(card, "flavor"), Is.EqualTo(expected.Flavor),
                    $"{id}: ordered reviewed flavor sequence changed");
            });
        }
    }

    [Test]
    public void EveryCardResolvesReferencesAndUsesTheReviewedSafetyBoundary()
    {
        var root = LocateResourcesDirectory();
        var locale = ParseLocaleKeys(Path.Combine(root, LocalePath));
        var prototypes = CollectPrototypeIds(Path.Combine(root, "Prototypes"));

        foreach (var card in LoadCards())
        {
            var id = CardId(card);
            var expected = Reviewed[id];
            var jobs = Sequence(card, "eligibleSafeJobs");
            var anchors = Sequence(card, "anchors");
            Assert.That(Scalar(card, "safetyClassification"), Is.EqualTo("SafeOrientation"), id);
            Assert.That(anchors, Is.Not.Empty, id);
            Assert.That(anchors[^1], Is.EqualTo(Arrivals), id);
            Assert.That(Scalar(card, "guideEntry"),
                Is.EqualTo(expected.Department == "Universal" ? "NewPlayer" : expected.Department), id);
            Assert.That(jobs, expected.Department == "Universal"
                ? Is.Empty
                : Is.EqualTo(new[] { SafeJobs[expected.Department] }), id);

            foreach (var field in LiteralFields())
                Assert.That(locale.ContainsKey(Scalar(card, field)), Is.True, $"{id}: unresolved {field}");
            foreach (var flavor in Sequence(card, "flavor"))
                Assert.That(locale.ContainsKey(flavor), Is.True, $"{id}: unresolved flavor");
            foreach (var job in jobs)
                Assert.That(prototypes["job"], Does.Contain(job), $"{id}: unknown job {job}");
            Assert.That(prototypes["guideEntry"], Does.Contain(Scalar(card, "guideEntry")), id);
            foreach (var anchor in anchors)
                Assert.That(prototypes["entity"], Does.Contain(anchor), $"{id}: unknown anchor {anchor}");

            Assert.That(locale[expected.Safety], Is.EqualTo(expected.RequiredSafetyPhrase),
                $"{id}: safety text must be exact so an unsafe appended instruction cannot hide after a negation.");
            foreach (var field in LiteralFields())
                Assert.That(ViolatesResolvedFieldPolicy(field, locale[Scalar(card, field)], expected.RequiredSafetyPhrase),
                    Is.False, $"{id}: {field} violated an instruction or dignity policy");
            foreach (var flavor in Sequence(card, "flavor").Select(key => locale[key]))
                Assert.That(ViolatesResolvedFieldPolicy("flavor", flavor, expected.RequiredSafetyPhrase),
                    Is.False, $"{id}: flavor violated an instruction or dignity policy");
        }
    }

    [Test]
    public void EveryStaticClientLocalizationKeyExists()
    {
        var locale = ParseLocaleKeys(Path.Combine(LocateResourcesDirectory(), LocalePath));
        Assert.That(RequiredClientLocIds.Where(key => !locale.ContainsKey(key)), Is.Empty,
            "A static First Shift or Player Delight tab LocId is unresolved.");
        Assert.That(locale["first-shift-skip"], Is.EqualTo("Skip this step"),
            "Skip advances only the current stage; its copy must not imply that it ends the assignment.");
    }

    [Test]
    public void SafetyHelperRejectsEveryForbiddenVariantInEveryLiteralField()
    {
        var attacks = new[]
        {
            "Open the APC.", "Cut a wire.", "Pulse a wire.", "Hack the console.", "Rewire this panel.",
            "Inject the patient.", "Consume this pill.", "Drink the beaker.", "Treat another person.",
            "Operate on the patient.", "Arrest the suspect.", "Detain a crewmember.", "Attack them.",
            "Shoot the target.", "Stab the target.", "Investigate an antagonist.", "Leave the station.",
            "Go off-station.", "Change the power.", "Change atmospherics.", "Vent the room.",
            "Fire a weapon.", "Use a knife.",
        };
        foreach (var field in LiteralFields())
        foreach (var attack in attacks)
        {
            var text = field == "safetyStop"
                ? SafetyPhrases["engineering"] + " " + attack
                : "Approved observation. " + attack;
            Assert.That(IsUnsafeLiteral(field, text, SafetyPhrases["engineering"]), Is.True,
                $"{field} accepted adversarial instruction: {attack}");
        }
    }

    [TestCase("We know you prefer Engineering.")]
    [TestCase("Your history suggests Cargo.")]
    [TestCase("Publicly announce this newcomer.")]
    [TestCase("First-time player detected.")]
    [TestCase("Earn a reward for completion.")]
    [TestCase("This grants a rank.")]
    [TestCase("Add this to the leaderboard score.")]
    public void FlavorHelperRejectsProfilingPublicLabelsAndRewards(string flavor) =>
        Assert.That(IsUnsafeFlavor(flavor), Is.True);

    [Test]
    public void EveryResolvedFieldRejectsBothInstructionAndDignityPolicyAttacks()
    {
        foreach (var field in LiteralFields().Append("flavor"))
        {
            Assert.That(ViolatesResolvedFieldPolicy(field, "Open the APC.", SafetyPhrases["engineering"]), Is.True,
                $"{field} accepted an unsafe imperative");
            Assert.That(ViolatesResolvedFieldPolicy(field, "Earn a reward on the newcomer leaderboard.", SafetyPhrases["engineering"]), Is.True,
                $"{field} accepted dignity/public-label/reward language");
        }
    }

    private static ReviewedCard Card(string department, string category, string stem, string safety) => new(
        department,
        category,
        $"first-shift-card-{stem}-title",
        $"first-shift-card-{stem}-why",
        $"first-shift-card-{stem}-orient",
        $"first-shift-card-{stem}-try",
        $"first-shift-card-{stem}-stuck",
        $"first-shift-safety-{safety}",
        SafetyText(safety),
        Enumerable.Range(1, 3).Select(number => $"first-shift-omen-{safety}-{number}").ToArray());

    private static string SafetyText(string safety) => safety switch
    {
        "engineering" => "Do not open equipment, touch wires, or change power or atmospherics. Observation is enough.",
        "medical" => "Do not treat anyone, remove supplies, inject, or consume anything. Reading and observation are enough.",
        "science" => "Do not activate equipment, run experiments, or handle hazardous materials. Exterior observation is enough.",
        "cargo" => "Do not accept orders, spend funds, or move station property. Observation and your own belongings are enough.",
        "service" => "Do not use heat, knives, chemicals, or consume anything. Labels and observation are enough.",
        "universal" => "Do not enter restricted areas, handle weapons, make arrests, investigate antagonists, or leave the station. Staying at Arrivals is always valid.",
        _ => throw new ArgumentOutOfRangeException(nameof(safety)),
    };

    private static bool IsUnsafeLiteral(string field, string text, string approvedSafety) =>
        field == "safetyStop" ? !string.Equals(text, approvedSafety, StringComparison.Ordinal) : ForbiddenLiteral.IsMatch(text);

    private static bool IsUnsafeFlavor(string text) => ForbiddenFlavor.IsMatch(text);
    private static bool ViolatesResolvedFieldPolicy(string field, string text, string approvedSafety) =>
        IsUnsafeLiteral(field, text, approvedSafety) || IsUnsafeFlavor(text);
    private static string[] LiteralFields() => ["title", "why", "orient", "try", "ifStuck", "safetyStop"];

    private static List<YamlMappingNode> LoadCards()
    {
        var path = Path.Combine(LocateResourcesDirectory(), DeckPath);
        Assert.That(File.Exists(path), Is.True, $"Missing reviewed First Shift deck at {path}");
        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return ((YamlSequenceNode) yaml.Documents.Single().RootNode).Children.Cast<YamlMappingNode>()
            .Where(node => Scalar(node, "type") == "firstShiftAssignment").ToList();
    }

    private static string CardId(YamlMappingNode card) => Scalar(card, "id");
    private static string Scalar(YamlMappingNode node, string key) =>
        ((YamlScalarNode) node.Children[new YamlScalarNode(key)]).Value ?? string.Empty;
    private static List<string> Sequence(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? ((YamlSequenceNode) value).Children.Cast<YamlScalarNode>().Select(v => v.Value ?? string.Empty).ToList()
            : new List<string>();

    // RA0026: pre-parsed static instances instead of static Regex functions with pattern strings.
    private static readonly Regex LocaleKeyRegex =
        new(@"^([a-z0-9][a-z0-9-]*)\s*=\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrototypeTypeRegex = new(@"^- type:\s*([A-Za-z0-9]+)\s*$", RegexOptions.Compiled);
    private static readonly Regex PrototypeIdRegex = new(@"^  id:\s*([A-Za-z0-9_]+)\s*$", RegexOptions.Compiled);

    private static Dictionary<string, string> ParseLocaleKeys(string path) => File.ReadLines(path)
        .Select(line => LocaleKeyRegex.Match(line))
        .Where(match => match.Success)
        .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);

    private static Dictionary<string, HashSet<string>> CollectPrototypeIds(string root)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["entity"] = new(StringComparer.Ordinal), ["job"] = new(StringComparer.Ordinal),
            ["guideEntry"] = new(StringComparer.Ordinal),
        };
        foreach (var file in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            string? type = null;
            foreach (var line in File.ReadLines(file))
            {
                var typeMatch = PrototypeTypeRegex.Match(line);
                if (typeMatch.Success) { type = typeMatch.Groups[1].Value; continue; }
                var idMatch = PrototypeIdRegex.Match(line);
                if (idMatch.Success && type != null && result.TryGetValue(type, out var ids))
                    ids.Add(idMatch.Groups[1].Value);
            }
        }
        return result;
    }

    private static string LocateResourcesDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Resources");
            if (Directory.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository Resources directory.");
    }
}
