#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Solreign.Noticeboards;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure input-shaping tests for <see cref="NoticeboardRules"/> and the
///     <see cref="NoticeboardCopy"/> seed-key picker — the crew Noticeboards' post-text clamp/
///     sanitize rule (v14 wave-1 #2, docs/council/2026-07-17-player-text-safety.md). No ECS, no
///     I/O: exercises the pure static classes directly, the <c>BountyClaimRulesTests</c> precedent.
/// </summary>
[TestFixture]
[TestOf(typeof(NoticeboardRules))]
public sealed class NoticeboardRulesTests
{
    // --- TrySanitizePostText ---

    [Test]
    public void TrySanitizePostText_Blank_Rejected()
    {
        var ok = NoticeboardRules.TrySanitizePostText(string.Empty, out var sanitized);

        Assert.That(ok, Is.False);
        Assert.That(sanitized, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TrySanitizePostText_Null_TreatedAsBlank()
    {
        var ok = NoticeboardRules.TrySanitizePostText(null, out var sanitized);

        Assert.That(ok, Is.False);
        Assert.That(sanitized, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TrySanitizePostText_WhitespaceOnly_Rejected()
    {
        var ok = NoticeboardRules.TrySanitizePostText("   \t  \n  ", out _);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void TrySanitizePostText_TrimsSurroundingWhitespace()
    {
        var ok = NoticeboardRules.TrySanitizePostText("  found a wrench in the bar  ", out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized, Is.EqualTo("found a wrench in the bar"));
    }

    [Test]
    public void TrySanitizePostText_ExactlyAtLimit_NotTruncated()
    {
        var text = new string('x', NoticeboardRules.MaxNoteLength);

        var ok = NoticeboardRules.TrySanitizePostText(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(NoticeboardRules.MaxNoteLength));
    }

    [Test]
    public void TrySanitizePostText_OverLimit_TruncatesRatherThanRejects()
    {
        var text = new string('x', NoticeboardRules.MaxNoteLength + 50);

        var ok = NoticeboardRules.TrySanitizePostText(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(NoticeboardRules.MaxNoteLength));
    }

    // --- the closed spec numbers themselves (regression pin — spec rule 2/5) ---

    [Test]
    public void SpecNumbers_MatchThePostureMemo()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NoticeboardRules.MaxNoteLength, Is.EqualTo(260));
            Assert.That(NoticeboardRules.BoardCapacity, Is.EqualTo(12));
            Assert.That(NoticeboardRules.ProvidenceCapacity, Is.EqualTo(2));
            Assert.That(NoticeboardRules.CooldownHours, Is.EqualTo(24));
        });
    }
}

/// <summary>
///     Tests for <see cref="NoticeboardCopy"/>'s seed-key rotation (the
///     <c>BountyClaimRules.PickClaimFailureLocKey</c> bounds-check idiom) and, against the REAL
///     noticeboards.ftl on disk, spec rule 5's hard prohibition on a PROVIDENCE seed line
///     referencing a specific player/report/decision/hidden note (the <c>MarkCopyTests</c>
///     real-locale-file harness).
/// </summary>
[TestFixture]
[TestOf(typeof(NoticeboardCopy))]
public sealed class NoticeboardCopyTests
{
    [Test]
    public void PickProvidenceSeedKey_NeverIndexesOutOfRange([Values(-100, -1, 0, 1, 2, 3, 100)] int roll)
    {
        var key = NoticeboardCopy.PickProvidenceSeedKey(roll);

        Assert.That(NoticeboardCopy.ProvidenceSeedKeys, Does.Contain(key));
    }

    [Test]
    public void EveryReferencedKey_ExistsInTheLocaleFile()
    {
        var locale = ParseLocaleKeys();
        var keys = new[]
        {
            NoticeboardCopy.SubmitBusyKey, NoticeboardCopy.NotAcceptingKey, NoticeboardCopy.WithheldKey,
            NoticeboardCopy.SuccessKey, NoticeboardCopy.QuotaKey, NoticeboardCopy.CooldownKey,
            NoticeboardCopy.BoardFullKey, NoticeboardCopy.ReportedKey, NoticeboardCopy.ProvidenceAuthorKey,
        }.Concat(NoticeboardCopy.ProvidenceSeedKeys);

        Assert.That(keys.Where(key => !locale.ContainsKey(key)), Is.Empty,
            "a NoticeboardCopy key is unresolved in noticeboards.ftl");
    }

    [Test]
    public void ProvidenceSeedLines_NeverReferenceAPlayerReportOrHiddenNote()
    {
        // Spec rule 5's hard prohibition, checked against the ACTUAL seed line text (not just the
        // key name) — the MarkCopyTests "$name-only-in-owner-suffixes denylist" idiom, applied to
        // the real noticeboards.ftl on disk.
        var locale = ParseLocaleKeys();
        string[] denylist = { "report", "hidden", "moderator", "sanction", "player " };

        Assert.Multiple(() =>
        {
            foreach (var key in NoticeboardCopy.ProvidenceSeedKeys)
            {
                var text = locale[key].ToLowerInvariant();
                foreach (var bad in denylist)
                    Assert.That(text, Does.Not.Contain(bad), $"{key} reads like it references '{bad}'");
            }
        });
    }

    [Test]
    public void ProvidenceSeedLines_FitTheNoteLengthCap()
    {
        // Seed lines pass through the SAME classifier + length gate as any player note — a seed
        // that already exceeds the cap would silently truncate mid-sentence.
        var locale = ParseLocaleKeys();
        Assert.Multiple(() =>
        {
            foreach (var key in NoticeboardCopy.ProvidenceSeedKeys)
                Assert.That(locale[key].Length, Is.LessThanOrEqualTo(NoticeboardRules.MaxNoteLength), $"{key} exceeds the note length cap");
        });
    }

    // --- Locale harness (the MarkCopyTests/FirstDeathCopyTests idiom, verbatim) --------------------

    private const string LocalePath = "Locale/en-US/_solreign/noticeboards.ftl";

    private static readonly Regex LocaleKeyRegex =
        new(@"^([a-z0-9][a-z0-9-]*)\s*=\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Dictionary<string, string> ParseLocaleKeys()
    {
        var path = Path.Combine(LocateResourcesDirectory(), LocalePath);
        Assert.That(File.Exists(path), Is.True, $"Missing noticeboards copy pack at {path}");
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
