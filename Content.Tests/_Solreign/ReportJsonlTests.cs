using System;
using System.Text.Json;
using Content.Server._Solreign.Report;
using Content.Shared._Solreign.Report;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ReportJsonl))]
public sealed class ReportJsonlTests
{
    private static ReportEntry MakeEntry() => new(
        Timestamp: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero),
        ReferenceId: "SR-DEADBEEF",
        ReporterName: "Kolton",
        ReporterGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        TargetAsTyped: "Bob",
        TargetResolvedName: "Bob_OOC",
        TargetGuid: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        RoundId: 99,
        Category: ReportCategory.Griefing,
        Text: "Welded the airlock shut and flooded medbay",
        Map: "Solreign Oasis");

    [Test]
    public void ToLine_ProducesSingleLine_NoEmbeddedNewlines()
    {
        var line = ReportJsonl.ToLine(MakeEntry());

        Assert.That(line, Does.Not.Contain("\n"));
        Assert.That(line, Does.Not.Contain("\r"));
    }

    [Test]
    public void ToLine_IsValidJson_WithExpectedFields()
    {
        var line = ReportJsonl.ToLine(MakeEntry());

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("referenceId").GetString(), Is.EqualTo("SR-DEADBEEF"));
        Assert.That(root.GetProperty("reporterName").GetString(), Is.EqualTo("Kolton"));
        Assert.That(root.GetProperty("reporterGuid").GetString(), Is.EqualTo("11111111-1111-1111-1111-111111111111"));
        Assert.That(root.GetProperty("targetAsTyped").GetString(), Is.EqualTo("Bob"));
        Assert.That(root.GetProperty("targetResolvedName").GetString(), Is.EqualTo("Bob_OOC"));
        Assert.That(root.GetProperty("targetGuid").GetString(), Is.EqualTo("22222222-2222-2222-2222-222222222222"));
        Assert.That(root.GetProperty("roundId").GetInt32(), Is.EqualTo(99));
        Assert.That(root.GetProperty("category").GetString(), Is.EqualTo("griefing"));
        Assert.That(root.GetProperty("text").GetString(), Is.EqualTo("Welded the airlock shut and flooded medbay"));
        Assert.That(root.GetProperty("map").GetString(), Is.EqualTo("Solreign Oasis"));
        Assert.That(root.GetProperty("timestamp").GetString(), Does.StartWith("2026-07-12T12:00:00"));
    }

    [Test]
    public void ToLine_UnresolvedTarget_SerializesNullFields_NotCrash()
    {
        var entry = MakeEntry() with { TargetResolvedName = null, TargetGuid = null };

        var line = ReportJsonl.ToLine(entry);
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("targetResolvedName").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(root.GetProperty("targetGuid").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(root.GetProperty("targetAsTyped").GetString(), Is.EqualTo("Bob"));
    }

    [Test]
    public void ToLine_PreservesArbitraryPlayerText_WithoutMangling()
    {
        var entry = MakeEntry() with { Text = "Quotes \" and back\\slashes and emoji \U0001F41B" };

        var line = ReportJsonl.ToLine(entry);
        using var doc = JsonDocument.Parse(line);

        Assert.That(doc.RootElement.GetProperty("text").GetString(),
            Is.EqualTo("Quotes \" and back\\slashes and emoji \U0001F41B"));
    }

    [TestCase(ReportCategory.Griefing, "griefing")]
    [TestCase(ReportCategory.Harassment, "harassment")]
    [TestCase(ReportCategory.Rules, "rules")]
    public void ToLine_SerializesCategory_AsLowercaseName(ReportCategory category, string expected)
    {
        var entry = MakeEntry() with { Category = category };

        var line = ReportJsonl.ToLine(entry);
        using var doc = JsonDocument.Parse(line);

        Assert.That(doc.RootElement.GetProperty("category").GetString(), Is.EqualTo(expected));
    }
}
