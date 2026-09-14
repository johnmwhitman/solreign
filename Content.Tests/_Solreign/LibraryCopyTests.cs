#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Solreign.Library;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Tests for <see cref="LibraryCopy"/>'s seed-work corpus (this lane's design brief §4) against
///     the REAL library.ftl on disk — the <c>NoticeboardCopyTests</c> real-locale-file harness,
///     extended with a multi-line-value-aware parser since library seed bodies (unlike
///     Noticeboard's single-line notes) are genuine multi-paragraph Fluent block values (the
///     <c>lore-papers.ftl</c> house idiom).
/// </summary>
[TestFixture]
[TestOf(typeof(LibraryCopy))]
public sealed class LibraryCopyTests
{
    [Test]
    public void SeedWorks_HasBetweenThreeAndFiveEntries()
    {
        // This lane's design brief §4: "3-5 in-fiction works from the closed template corpus."
        Assert.That(LibraryCopy.SeedWorks.Length, Is.InRange(3, 5));
    }

    [Test]
    public void EveryReferencedKey_ExistsInTheLocaleFile()
    {
        var locale = ParseLocaleKeys();
        var keys = new[]
        {
            LibraryCopy.SubmitBusyKey, LibraryCopy.NotAcceptingKey, LibraryCopy.WithheldKey,
            LibraryCopy.SuccessKey, LibraryCopy.QuotaKey, LibraryCopy.ReportedKey,
            LibraryCopy.VerbSubmitKey, LibraryCopy.VerbReportKey,
        };

        Assert.That(keys.Where(key => !locale.ContainsKey(key)), Is.Empty,
            "a LibraryCopy key is unresolved in library.ftl");
    }

    [Test]
    public void EverySeedWork_TitleBodyAndAuthorKeys_ExistSomewhereInLocale()
    {
        // The handbook seed's body key (solreign-lore-handbook-page-3) deliberately lives in
        // lore-papers.ftl, not library.ftl — it's the SAME already-public-railed page 3 text, reused
        // rather than duplicated. Check across both files the corpus actually draws from.
        var locale = ParseLocaleKeys();
        var loreLocale = ParseLocaleKeys("Locale/en-US/_solreign/lore-papers.ftl");

        Assert.Multiple(() =>
        {
            foreach (var seed in LibraryCopy.SeedWorks)
            {
                Assert.That(locale.ContainsKey(seed.TitleKey), $"{seed.TitleKey} missing from library.ftl");
                Assert.That(locale.ContainsKey(seed.BodyKey) || loreLocale.ContainsKey(seed.BodyKey),
                    $"{seed.BodyKey} missing from both library.ftl and lore-papers.ftl");
                Assert.That(locale.ContainsKey(seed.AuthorKey), $"{seed.AuthorKey} missing from library.ftl");
            }
        });
    }

    [Test]
    public void SeedWorks_EveryBody_FitsTheLengthCap()
    {
        // Seed bodies pass through the SAME classifier + length gate as any player submission — a
        // seed that already exceeds the cap would silently truncate mid-sentence.
        var locale = ParseLocaleKeys();
        var loreLocale = ParseLocaleKeys("Locale/en-US/_solreign/lore-papers.ftl");

        Assert.Multiple(() =>
        {
            foreach (var seed in LibraryCopy.SeedWorks)
            {
                var body = locale.TryGetValue(seed.BodyKey, out var v) ? v : loreLocale[seed.BodyKey];
                Assert.That(body.Length, Is.LessThanOrEqualTo(LibraryRules.MaxBodyLength),
                    $"{seed.BodyKey} exceeds the body length cap");
                Assert.That(body.Length, Is.GreaterThan(0), $"{seed.BodyKey} parsed empty — multi-line parser regression");
            }
        });
    }

    [Test]
    public void SeedWorks_EveryTitle_FitsTheLengthCap()
    {
        var locale = ParseLocaleKeys();
        Assert.Multiple(() =>
        {
            foreach (var seed in LibraryCopy.SeedWorks)
                Assert.That(locale[seed.TitleKey].Length, Is.LessThanOrEqualTo(LibraryRules.MaxTitleLength),
                    $"{seed.TitleKey} exceeds the title length cap");
        });
    }

    [Test]
    public void SeedAuthors_AllCarryTheProvidenceMarker()
    {
        // Every PROVIDENCE-authored row must be clearly labeled, never blended in as if a player
        // wrote it (the Noticeboard posture memo's labeling principle, applied here).
        var locale = ParseLocaleKeys();
        Assert.Multiple(() =>
        {
            foreach (var seed in LibraryCopy.SeedWorks)
                Assert.That(locale[seed.AuthorKey], Does.StartWith("PROVIDENCE"),
                    $"{seed.AuthorKey} does not carry the PROVIDENCE marker");
        });
    }

    // --- Locale harness (the NoticeboardCopyTests/MarkCopyTests idiom, extended for multi-line
    // Fluent block values — the lore-papers.ftl multi-paragraph pattern) ------------------------

    private static readonly Regex TopLevelKeyRegex =
        new(@"^([a-z0-9][a-z0-9-]*)\s*=\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    ///     A deliberately small Fluent-lite parser: a non-indented "key = value" line starts an
    ///     entry (inline value, if any); subsequent indented or blank lines are appended (trimmed,
    ///     newline-joined) until the next top-level key/comment/section-header line. Good enough for
    ///     these tests' own regression pins — not a general Fluent parser, and not the runtime one.
    /// </summary>
    private static Dictionary<string, string> ParseLocaleKeys(string relativePath = "Locale/en-US/_solreign/library.ftl")
    {
        var path = Path.Combine(LocateResourcesDirectory(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"Missing copy pack at {path}");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentKey = null;
        var currentLines = new List<string>();

        void Flush()
        {
            if (currentKey is null)
                return;

            // Trim leading/trailing blank lines only — interior blank lines (paragraph breaks) stay.
            while (currentLines.Count > 0 && currentLines[0].Length == 0)
                currentLines.RemoveAt(0);
            while (currentLines.Count > 0 && currentLines[^1].Length == 0)
                currentLines.RemoveAt(currentLines.Count - 1);

            result[currentKey] = string.Join('\n', currentLines);
            currentKey = null;
            currentLines = new List<string>();
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            if (rawLine.Length == 0)
            {
                if (currentKey is not null)
                    currentLines.Add(string.Empty);
                continue;
            }

            var isIndented = rawLine[0] is ' ' or '\t';
            if (!isIndented)
            {
                // A comment or section header ends whatever entry was in progress.
                if (rawLine.TrimStart().StartsWith('#'))
                {
                    Flush();
                    continue;
                }

                var match = TopLevelKeyRegex.Match(rawLine);
                if (!match.Success)
                {
                    Flush();
                    continue;
                }

                Flush();
                currentKey = match.Groups[1].Value;
                var inline = match.Groups[2].Value;
                if (inline.Length > 0)
                    currentLines.Add(inline);
                continue;
            }

            if (currentKey is not null)
                currentLines.Add(rawLine.Trim());
        }

        Flush();
        return result;
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
