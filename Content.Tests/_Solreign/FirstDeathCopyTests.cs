#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Copy-pack integrity for the authored first death (spec §7's closed-vocabulary rail as a test,
///     the FX manifest-consistency idiom):
///       * every .ftl key referenced by the copy tables (eulogies, private line, fee, rehire lines,
///         sender) EXISTS in Resources/Locale/en-US/_solreign/first-death.ftl;
///       * every template's Fluent variable set ⊆ {name, tours, title, fee} — there is no attacker
///         variable and none may ever be added;
///       * no template contains attacker/weapon vocabulary (denylist assertion);
///       * the deterministic rehire pick honors the R2-waiver-for-day-one rule.
///     Locale parsing reuses the <see cref="FirstShiftContentTests"/> harness idiom (locate the repo
///     Resources directory by walking up from the test bin).
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathCopy))]
public sealed class FirstDeathCopyTests
{
    private const string LocalePath = "Locale/en-US/_solreign/first-death.ftl";

    private static readonly string[] AllowedVariables = { "name", "tours", "title", "fee" };

    private static readonly FirstDeathCause[] AllCauses =
        (FirstDeathCause[]) Enum.GetValues(typeof(FirstDeathCause));

    private static IEnumerable<string> AllReferencedKeys()
    {
        yield return FirstDeathCopy.SenderKey;
        yield return FirstDeathCopy.PrivateLineKey;
        yield return FirstDeathCopy.FeeKey;
        foreach (var cause in AllCauses)
            yield return FirstDeathCopy.EulogyKeyFor(cause);
        foreach (var key in FirstDeathCopy.GenericEulogyKeys)
            yield return key;
        foreach (var key in FirstDeathCopy.RehireKeys)
            yield return key;
    }

    // --- Key existence ----------------------------------------------------------------------------

    [Test]
    public void EveryReferencedKey_ExistsInTheLocaleFile()
    {
        var locale = ParseLocaleKeys();
        Assert.That(AllReferencedKeys().Where(key => !locale.ContainsKey(key)), Is.Empty,
            "a copy-table key is unresolved in first-death.ftl");
    }

    [Test]
    public void EveryLocaleKey_IsReferencedByACopyTable()
    {
        // The inverse direction: no orphan keys accumulate in the pack file.
        var referenced = AllReferencedKeys().ToHashSet(StringComparer.Ordinal);
        var locale = ParseLocaleKeys();
        Assert.That(locale.Keys.Where(key => !referenced.Contains(key)), Is.Empty,
            "first-death.ftl contains a key no copy table references");
    }

    // --- Closed vocabulary ------------------------------------------------------------------------

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
                    Assert.That(AllowedVariables, Does.Contain(match.Groups[1].Value),
                        $"{key} uses a variable outside the closed vocabulary: ${match.Groups[1].Value}");
                }
            }
        });
    }

    [Test]
    public void NoTemplate_ContainsAttackerVocabulary()
    {
        // The confidentiality rule (spec §3.3): the eulogy, epitaph, and rehire copy NEVER name,
        // describe, or count the attacker — enforced by the template set itself.
        var denylist = new[]
        {
            "attacker", "killer", "murderer", "assailant", "syndicate",
            "weapon", "gun", "knife", "shot by", "stabbed by", "killed by",
        };

        Assert.Multiple(() =>
        {
            foreach (var (key, template) in ParseLocaleKeys())
            {
                var lower = template.ToLowerInvariant();
                foreach (var word in denylist)
                {
                    Assert.That(lower, Does.Not.Contain(word),
                        $"{key} contains denylisted attacker vocabulary: {word}");
                }
            }
        });
    }

    [Test]
    public void EveryEulogy_IncludesTheName()
    {
        // Spec §8A: the eulogy is a PUBLIC ceremony BY NAME — every eulogy template must
        // interpolate $name.
        var locale = ParseLocaleKeys();
        var eulogyKeys = AllCauses.Select(FirstDeathCopy.EulogyKeyFor)
            .Concat(FirstDeathCopy.GenericEulogyKeys)
            .Distinct();

        Assert.Multiple(() =>
        {
            foreach (var key in eulogyKeys)
            {
                Assert.That(locale[key], Does.Contain("$name"), $"{key} must eulogize by name");
            }
        });
    }

    [Test]
    public void TheFee_IsTheLiteralFortyFiveCredits()
    {
        // Spec §4.4: {fee} renders the literal string from the template. The joke is load-bearing.
        Assert.That(ParseLocaleKeys()[FirstDeathCopy.FeeKey], Is.EqualTo("45cr"));
    }

    // --- Selection rules --------------------------------------------------------------------------

    [Test]
    public void EulogySelection_IsCauseMatched_ForEveryCause()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstDeathCopy.EulogyKeyFor(FirstDeathCause.Violence), Does.EndWith("violence"));
            Assert.That(FirstDeathCopy.EulogyKeyFor(FirstDeathCause.Vacuum), Does.EndWith("vacuum"));
            Assert.That(FirstDeathCopy.EulogyKeyFor(FirstDeathCause.Burn), Does.EndWith("burn"));
            Assert.That(FirstDeathCopy.EulogyKeyFor(FirstDeathCause.Misadventure), Does.EndWith("misadventure"));
            Assert.That(FirstDeathCopy.EulogyKeyFor(FirstDeathCause.Unknown), Does.EndWith("unknown"));
        });
    }

    [Test]
    public void RehireSelection_DayOneDeath_AlwaysGetsTheWaiver()
    {
        // R2 forced for tours_at_death == 0: the first failure must become a keepsake, not a bill.
        foreach (var cause in AllCauses)
        {
            foreach (var title in new[] { "Probationary Asset", "Chain Closer", "" })
            {
                Assert.That(FirstDeathCopy.RehireKeyFor(0, title, cause),
                    Is.EqualTo("solreign-first-death-rehire-2"),
                    $"tours=0 (cause={cause}, title='{title}') must always take the waiver line");
            }
        }
    }

    [Test]
    public void RehireSelection_IsDeterministic_AndStaysInR1ThroughR6()
    {
        foreach (var cause in AllCauses)
        {
            for (var tours = 1; tours <= 30; tours++)
            {
                var first = FirstDeathCopy.RehireKeyFor(tours, "Probationary Asset", cause);
                var second = FirstDeathCopy.RehireKeyFor(tours, "Probationary Asset", cause);
                Assert.Multiple(() =>
                {
                    Assert.That(second, Is.EqualTo(first), "the rehire pick must be deterministic");
                    Assert.That(FirstDeathCopy.RehireKeys, Does.Contain(first));
                });
            }
        }
    }

    [Test]
    public void RehireSelection_ToleratesANullTitle()
    {
        Assert.DoesNotThrow(() => FirstDeathCopy.RehireKeyFor(3, null!, FirstDeathCause.Unknown));
    }

    // --- Locale harness (the FirstShiftContentTests idiom) -----------------------------------------

    private static readonly Regex LocaleKeyRegex =
        new(@"^([a-z0-9][a-z0-9-]*)\s*=\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Dictionary<string, string> ParseLocaleKeys()
    {
        var path = Path.Combine(LocateResourcesDirectory(), LocalePath);
        Assert.That(File.Exists(path), Is.True, $"Missing first-death copy pack at {path}");
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
