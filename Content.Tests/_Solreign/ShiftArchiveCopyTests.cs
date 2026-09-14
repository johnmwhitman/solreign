#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.ShiftArchive;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Tests for <see cref="ShiftArchiveCopy"/>'s closed-vocabulary copy pack against the REAL
///     shift-archive.ftl on disk (the <c>LibraryCopyTests</c> real-locale-file harness) plus the
///     pure key-selection branching: absent facts are omitted, never fabricated as zeroes, and a
///     corrupt timestamp degrades instead of throwing.
/// </summary>
[TestFixture]
[TestOf(typeof(ShiftArchiveCopy))]
public sealed class ShiftArchiveCopyTests
{
    private static StationAuditRecord Audit(
        int roundId = 100,
        string endedAtUtc = "2026-07-25T04:00:00.0000000Z",
        int durationMinutes = 45,
        int crewCount = 3,
        int deathCount = 0,
        bool firstDeathCommemorated = false,
        string? directiveTitle = null,
        bool directiveOutcomeReported = false,
        bool directiveOutcomeFulfilled = false,
        string? commendationName = null,
        int commendationScore = 0)
    {
        return new StationAuditRecord(
            roundId, endedAtUtc, durationMinutes, crewCount, deathCount, firstDeathCommemorated,
            directiveTitle, directiveOutcomeReported, directiveOutcomeFulfilled,
            StipendsProcessed: 0, BountyVerdicts: 0, NotableEventCount: 0,
            commendationName, commendationScore, ItemOfConcernId: "none");
    }

    [Test]
    public void EveryEmittableKey_ExistsInTheLocaleFile()
    {
        var locale = ParseLocaleKeys();
        var windowKeys = new[]
        {
            "solreign-shift-archive-window-title",
            "solreign-shift-archive-empty",
            "solreign-shift-archive-offline",
        };

        Assert.Multiple(() =>
        {
            Assert.That(ShiftArchiveCopy.AllKeys.Where(key => !locale.ContainsKey(key)), Is.Empty,
                "a ShiftArchiveCopy key is unresolved in shift-archive.ftl");
            Assert.That(windowKeys.Where(key => !locale.ContainsKey(key)), Is.Empty,
                "a window-chrome key is unresolved in shift-archive.ftl");
        });
    }

    [Test]
    public void Header_CarriesRoundAndDate()
    {
        var line = ShiftArchiveCopy.HeaderFor(Audit(roundId: 108));
        Assert.Multiple(() =>
        {
            Assert.That(line.Key, Is.EqualTo(ShiftArchiveCopy.HeaderKey));
            Assert.That(line.Args, Does.Contain(("round", (object) 108)));
            Assert.That(line.Args, Does.Contain(("date", (object) "2026-07-25")));
        });
    }

    [Test]
    public void Header_CorruptTimestamp_DegradesInsteadOfThrowing()
    {
        // The Echo skip-don't-throw law: an ambient surface never crashes on a corrupt row.
        var line = ShiftArchiveCopy.HeaderFor(Audit(endedAtUtc: "not-a-timestamp"));
        Assert.That(line.Args, Does.Contain(("date", (object) "----")));
    }

    [Test]
    public void Lines_MinimalShift_IsCrewPlusZeroFatalities()
    {
        var keys = ShiftArchiveCopy.LinesFor(Audit(), contractCount: 0).Select(l => l.Key).ToList();
        Assert.That(keys, Is.EqualTo(new[] { ShiftArchiveCopy.CrewKey, ShiftArchiveCopy.NoDeathsKey }),
            "a shift with no directive, no contracts and no commendation renders exactly crew + zero-fatalities");
    }

    [Test]
    public void Lines_Deaths_PickTheCommemoratedVariantOnlyWhenCommemorated()
    {
        var plain = ShiftArchiveCopy.LinesFor(Audit(deathCount: 2), 0).Select(l => l.Key);
        var commemorated = ShiftArchiveCopy.LinesFor(Audit(deathCount: 2, firstDeathCommemorated: true), 0).Select(l => l.Key);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Does.Contain(ShiftArchiveCopy.DeathsKey));
            Assert.That(commemorated, Does.Contain(ShiftArchiveCopy.DeathsCommemoratedKey));
        });
    }

    [Test]
    public void Lines_Directive_MapsReportedAndFulfilledToTheRightKey()
    {
        ShiftArchiveLine DirectiveLine(bool reported, bool fulfilled) =>
            ShiftArchiveCopy.LinesFor(
                    Audit(directiveTitle: "Mandatory Fun", directiveOutcomeReported: reported, directiveOutcomeFulfilled: fulfilled), 0)
                .Single(l => l.Key.Contains("directive"));

        Assert.Multiple(() =>
        {
            Assert.That(DirectiveLine(reported: true, fulfilled: true).Key, Is.EqualTo(ShiftArchiveCopy.DirectiveFulfilledKey));
            Assert.That(DirectiveLine(reported: true, fulfilled: false).Key, Is.EqualTo(ShiftArchiveCopy.DirectiveFailedKey));
            Assert.That(DirectiveLine(reported: false, fulfilled: false).Key, Is.EqualTo(ShiftArchiveCopy.DirectiveUnreportedKey));
        });
    }

    [Test]
    public void Lines_AbsentFacts_AreOmittedNeverZeroed()
    {
        var keys = ShiftArchiveCopy.LinesFor(Audit(), contractCount: 0).Select(l => l.Key).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(keys, Does.Not.Contain(ShiftArchiveCopy.DirectiveFulfilledKey));
            Assert.That(keys, Does.Not.Contain(ShiftArchiveCopy.DirectiveFailedKey));
            Assert.That(keys, Does.Not.Contain(ShiftArchiveCopy.DirectiveUnreportedKey));
            Assert.That(keys, Does.Not.Contain(ShiftArchiveCopy.ContractsKey));
            Assert.That(keys, Does.Not.Contain(ShiftArchiveCopy.CommendationKey));
        });
    }

    [Test]
    public void Lines_ContractsAndCommendation_AppearWhenPresent()
    {
        var lines = ShiftArchiveCopy.LinesFor(
            Audit(commendationName: "M. Vance", commendationScore: 12), contractCount: 3);

        var contracts = lines.Single(l => l.Key == ShiftArchiveCopy.ContractsKey);
        var commendation = lines.Single(l => l.Key == ShiftArchiveCopy.CommendationKey);

        Assert.Multiple(() =>
        {
            Assert.That(contracts.Args, Does.Contain(("count", (object) 3)));
            Assert.That(commendation.Args, Does.Contain(("name", (object) "M. Vance")));
            Assert.That(commendation.Args, Does.Contain(("score", (object) 12)));
        });
    }

    // Same real-locale-file harness as LibraryCopyTests, simplified to the single-line values this
    // pack uses (Fluent selector blocks parse as indented continuations of their key line).
    private static Dictionary<string, string> ParseLocaleKeys(
        string relativePath = "Locale/en-US/_solreign/shift-archive.ftl")
    {
        var path = Path.Combine(LocateResourcesDirectory(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"Missing copy pack at {path}");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadLines(path))
        {
            if (rawLine.Length == 0 || rawLine[0] is ' ' or '\t' || rawLine.TrimStart().StartsWith('#'))
                continue;

            var eq = rawLine.IndexOf('=');
            if (eq <= 0)
                continue;

            result[rawLine[..eq].Trim()] = rawLine[(eq + 1)..].Trim();
        }

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
