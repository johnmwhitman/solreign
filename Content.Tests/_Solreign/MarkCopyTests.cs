#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Solreign.PlayerDelight.Mark;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Structural integrity for the Mark copy tables (MARK-SPEC §8) and the closed kind set:
///       * the 3x4 stage-key table is complete, closed, and collision-free;
///       * every selection is deterministic and every deterministic pick stays in range for any
///         seed, including negatives (the RehireKeyFor idiom);
///       * the closed variable CONTRACT is exactly {name, kind, cm, days} — $stage/$tours stay
///         contract-reserved, unshipped;
///       * MarkKind ledger strings round-trip and reject everything outside the closed set.
///     W2 extension (the debt the MG-W1 receipt flagged, discharged with mark.ftl): the
///     <see cref="FirstDeathCopyTests"/> locale harness over the real templates — key existence in
///     both directions (no unresolved keys, no orphan keys), every template's Fluent variables
///     ⊆ {name, kind, cm, days}, and the rail-5 denylist: $name renders ONLY in the C3
///     owner-examine suffixes, never on any other surface.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkCopy))]
public sealed class MarkCopyTests
{
    private static IEnumerable<string> AllReferencedKeys()
    {
        yield return MarkCopy.GardenNameKey;
        yield return MarkCopy.GardenDescriptionKey;
        yield return MarkCopy.AlreadyClaimedKey;
        foreach (var kind in MarkKinds.All)
        {
            yield return MarkCopy.PlantConfirmKeyFor(kind);
            yield return MarkCopy.ReturnKeyFor(kind);
            yield return MarkCopy.KindWordKeyFor(kind);
            yield return MarkCopy.VerbKeyFor(kind);
            for (var stage = 0; stage <= MarkAgeRules.MaxStage; stage++)
                yield return MarkCopy.StageKeyFor(kind, stage);
        }

        foreach (var key in MarkCopy.OwnerSuffixKeys
                     .Concat(MarkCopy.StrangerSuffixKeys)
                     .Concat(MarkCopy.GenericPlantConfirmKeys)
                     .Concat(MarkCopy.GenericReturnKeys)
                     .Concat(MarkCopy.OverflowKeys)
                     .Concat(MarkCopy.NudgeKeys))
        {
            yield return key;
        }
    }

    // --- The closed kind set ----------------------------------------------------------------------

    [Test]
    public void KindSet_IsExactlyTheThreeSpecKinds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarkKinds.All, Is.EqualTo(new[] { MarkKind.Sapling, MarkKind.Lamp, MarkKind.NamePlate }),
                "the v1 menu is sapling/lamp/name-plate, closed (river-stone stays banked)");
            Assert.That(Enum.GetValues<MarkKind>(), Has.Length.EqualTo(3),
                "adding a kind is a deliberate spec change, not a drive-by");
        });
    }

    [Test]
    public void KindLedgerStrings_RoundTrip_ForEveryKind()
    {
        foreach (var kind in MarkKinds.All)
        {
            var ledger = MarkKinds.ToLedgerString(kind);
            Assert.Multiple(() =>
            {
                Assert.That(ledger, Is.EqualTo(ledger.ToUpperInvariant()),
                    "ledger kind strings are UPPERCASE by spec (§3.1)");
                Assert.That(MarkKinds.TryParse(ledger, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(kind), $"{ledger} must round-trip");
            });
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("sapling", Description = "case matters — the ledger form is canonical")]
    [TestCase("RIVERSTONE", Description = "banked, not shipped")]
    [TestCase("GARDEN")]
    public void KindParse_RejectsEverythingOutsideTheClosedSet(string? ledger)
    {
        Assert.That(MarkKinds.TryParse(ledger, out _), Is.False);
    }

    // --- Key tables -------------------------------------------------------------------------------

    [Test]
    public void StageKeys_CoverTheClosedThreeByFourTable_WithoutCollisions()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in MarkKinds.All)
        {
            for (var stage = 0; stage <= MarkAgeRules.MaxStage; stage++)
            {
                Assert.That(keys.Add(MarkCopy.StageKeyFor(kind, stage)), Is.True,
                    $"stage key collision at {kind}/{stage}");
            }
        }

        Assert.That(keys, Has.Count.EqualTo(12), "3 kinds x 4 stages, exactly");
    }

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(int.MaxValue)]
    public void StageKeyFor_OutsideTheClosedTable_Throws(int stage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarkCopy.StageKeyFor(MarkKind.Sapling, stage));
    }

    [Test]
    public void EveryReferencedKey_IsNamespaced_Unique_AndDeterministic()
    {
        var keys = AllReferencedKeys().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(keys, Is.All.Matches<string>(k => k.StartsWith("solreign-mark-", StringComparison.Ordinal)),
                "every mark key lives under the solreign-mark- namespace");
            Assert.That(keys, Is.Unique, "no two surfaces may share a loc key");
            Assert.That(AllReferencedKeys().ToList(), Is.EqualTo(keys),
                "key enumeration must be deterministic");
        });
    }

    [Test]
    public void KindTrueKeys_AreDistinctPerKind()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarkKinds.All.Select(MarkCopy.PlantConfirmKeyFor), Is.Unique);
            Assert.That(MarkKinds.All.Select(MarkCopy.ReturnKeyFor), Is.Unique);
            Assert.That(MarkKinds.All.Select(MarkCopy.KindWordKeyFor), Is.Unique);
            Assert.That(MarkKinds.All.Select(MarkCopy.VerbKeyFor), Is.Unique);
        });
    }

    [Test]
    public void VariantTables_AreNonEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarkCopy.OwnerSuffixKeys, Is.Not.Empty);
            Assert.That(MarkCopy.StrangerSuffixKeys, Is.Not.Empty);
            Assert.That(MarkCopy.GenericPlantConfirmKeys, Is.Not.Empty);
            Assert.That(MarkCopy.GenericReturnKeys, Is.Not.Empty);
            Assert.That(MarkCopy.OverflowKeys, Is.Not.Empty);
            Assert.That(MarkCopy.NudgeKeys, Is.Not.Empty);
        });
    }

    // --- Deterministic pick -----------------------------------------------------------------------

    [Test]
    public void Pick_IsDeterministic_AndInRange_ForAnySeed()
    {
        foreach (var seed in new[] { int.MinValue, -37, -1, 0, 1, 7, int.MaxValue })
        {
            var first = MarkCopy.Pick(MarkCopy.OwnerSuffixKeys, seed);
            var second = MarkCopy.Pick(MarkCopy.OwnerSuffixKeys, seed);
            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo(first), $"pick must be deterministic (seed {seed})");
                Assert.That(MarkCopy.OwnerSuffixKeys, Does.Contain(first),
                    $"pick must stay inside the table (seed {seed})");
            });
        }
    }

    [Test]
    public void Pick_EmptyTable_Throws()
    {
        Assert.Throws<ArgumentException>(() => MarkCopy.Pick(Array.Empty<string>(), 1));
    }

    // --- The closed variable contract -------------------------------------------------------------

    [Test]
    public void VariableContract_IsExactlyTheClosedFour()
    {
        // {stage} and {tours} are contract-reserved but UNSHIPPED (grk's own closing note, spec §8);
        // widening this set is a spec decision, and the template test below enforces subset-of
        // against the real .ftl.
        Assert.That(MarkCopy.AllowedVariables, Is.EquivalentTo(new[] { "name", "kind", "cm", "days" }));
    }

    // --- The W2 locale extension: the real templates (the FirstDeathCopyTests harness) ------------

    [Test]
    public void EveryReferencedKey_ExistsInTheLocaleFile()
    {
        var locale = ParseLocaleKeys();
        Assert.That(AllReferencedKeys().Where(key => !locale.ContainsKey(key)), Is.Empty,
            "a copy-table key is unresolved in mark.ftl");
    }

    [Test]
    public void EveryLocaleKey_IsReferencedByACopyTable()
    {
        // The inverse direction: no orphan keys accumulate in the pack file.
        var referenced = AllReferencedKeys().ToHashSet(StringComparer.Ordinal);
        var locale = ParseLocaleKeys();
        Assert.That(locale.Keys.Where(key => !referenced.Contains(key)), Is.Empty,
            "mark.ftl contains a key no copy table references");
    }

    [Test]
    public void EveryTemplate_UsesOnlyTheClosedVariableVocabulary()
    {
        var variableRegex = new Regex(@"\{\s*\$([A-Za-z0-9_-]+)\s*\}");
        Assert.Multiple(() =>
        {
            foreach (var (key, template) in ParseLocaleKeys())
            {
                foreach (Match match in variableRegex.Matches(template))
                {
                    Assert.That(MarkCopy.AllowedVariables, Does.Contain(match.Groups[1].Value),
                        $"{key} uses a variable outside the closed vocabulary: ${match.Groups[1].Value}");
                }
            }
        });
    }

    [Test]
    public void NameRenders_OnlyInTheOwnerSuffixes()
    {
        // Rail 5 as a denylist (spec §4.2/§8): the planted character name is OWNER-PRIVATE. The C3
        // owner-examine suffixes are the only templates allowed to interpolate $name — every stage
        // line, stranger suffix, verb, confirmation, return, nudge, overflow, and garden string
        // must be name-free, so no public surface can ever leak it.
        var ownerKeys = MarkCopy.OwnerSuffixKeys.ToHashSet(StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            foreach (var (key, template) in ParseLocaleKeys())
            {
                if (ownerKeys.Contains(key))
                {
                    Assert.That(template, Does.Contain("$name"),
                        $"{key} is an owner suffix and must address the owner by name");
                    continue;
                }

                Assert.That(template, Does.Not.Contain("$name"),
                    $"{key} must never interpolate $name — owner suffixes are the only $name surface");
            }
        });
    }

    [Test]
    public void KindTrueReturnLines_CarryTheirUnitWords()
    {
        // Spec §8C2: one shared scalar, per-kind unit words baked into the kind-true templates
        // ("2.1 cm" and "2.1 lumens" are the same number wearing different uniforms).
        var locale = ParseLocaleKeys();
        Assert.Multiple(() =>
        {
            Assert.That(locale[MarkCopy.ReturnKeyFor(MarkKind.Sapling)], Does.Contain("cm"));
            Assert.That(locale[MarkCopy.ReturnKeyFor(MarkKind.Lamp)], Does.Contain("lumens"));
            Assert.That(locale[MarkCopy.ReturnKeyFor(MarkKind.NamePlate)], Does.Contain("sheen"));
        });
    }

    [Test]
    public void StageLines_AreExamineLength()
    {
        // Spec §8A: stage lines are the examine text, <= 140 chars, no variables (variable-freedom
        // is already covered by NameRenders_OnlyInTheOwnerSuffixes + the vocabulary test).
        var locale = ParseLocaleKeys();
        Assert.Multiple(() =>
        {
            foreach (var kind in MarkKinds.All)
            {
                for (var stage = 0; stage <= MarkAgeRules.MaxStage; stage++)
                {
                    var key = MarkCopy.StageKeyFor(kind, stage);
                    Assert.That(locale[key].Length, Is.LessThanOrEqualTo(140),
                        $"{key} exceeds the 140-char examine budget");
                }
            }
        });
    }

    // --- Locale harness (the FirstDeathCopyTests idiom, verbatim) ---------------------------------

    private const string LocalePath = "Locale/en-US/_solreign/mark.ftl";

    private static readonly Regex LocaleKeyRegex =
        new(@"^([a-z0-9][a-z0-9-]*)\s*=\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Dictionary<string, string> ParseLocaleKeys()
    {
        var path = Path.Combine(LocateResourcesDirectory(), LocalePath);
        Assert.That(File.Exists(path), Is.True, $"Missing mark copy pack at {path}");
        return File.ReadLines(path)
            .Select(line => LocaleKeyRegex.Match(line))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);
    }

    private static string LocateResourcesDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Resources");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository Resources directory.");
    }
}
