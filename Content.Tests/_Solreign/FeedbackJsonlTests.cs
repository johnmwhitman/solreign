using System;
using System.Text.Json;
using Content.Server._Solreign.Feedback;
using Content.Shared._Solreign.Feedback;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(FeedbackJsonl))]
public sealed class FeedbackJsonlTests
{
    private static FeedbackEntry MakeEntry() => new(
        Timestamp: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero),
        ReferenceId: "SF-DEADBEEF",
        PlayerName: "Kolton",
        PlayerGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        RoundId: 99,
        Category: FeedbackCategory.Bug,
        Text: "The vending machine ate my ID card",
        Map: "Solreign Oasis",
        Job: "Passenger",
        Location: "Solreign Station",
        ServerBuild: "d0804d29d5");

    [Test]
    public void ToLine_ProducesSingleLine_NoEmbeddedNewlines()
    {
        var line = FeedbackJsonl.ToLine(MakeEntry());

        Assert.That(line, Does.Not.Contain("\n"));
        Assert.That(line, Does.Not.Contain("\r"));
    }

    [Test]
    public void ToLine_IsValidJson_WithExpectedFields()
    {
        var line = FeedbackJsonl.ToLine(MakeEntry());

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("referenceId").GetString(), Is.EqualTo("SF-DEADBEEF"));
        Assert.That(root.GetProperty("playerName").GetString(), Is.EqualTo("Kolton"));
        Assert.That(root.GetProperty("playerGuid").GetString(), Is.EqualTo("11111111-1111-1111-1111-111111111111"));
        Assert.That(root.GetProperty("roundId").GetInt32(), Is.EqualTo(99));
        Assert.That(root.GetProperty("category").GetString(), Is.EqualTo("bug"));
        Assert.That(root.GetProperty("text").GetString(), Is.EqualTo("The vending machine ate my ID card"));
        Assert.That(root.GetProperty("map").GetString(), Is.EqualTo("Solreign Oasis"));
        Assert.That(root.GetProperty("job").GetString(), Is.EqualTo("Passenger"));
        Assert.That(root.GetProperty("location").GetString(), Is.EqualTo("Solreign Station"));
        Assert.That(root.GetProperty("serverBuild").GetString(), Is.EqualTo("d0804d29d5"));
        Assert.That(root.GetProperty("timestamp").GetString(), Does.StartWith("2026-07-12T12:00:00"));
    }

    [Test]
    public void ToLine_PreservesArbitraryPlayerText_WithoutMangling()
    {
        var entry = MakeEntry() with { Text = "Quotes \" and back\\slashes and emoji \U0001F41B" };

        var line = FeedbackJsonl.ToLine(entry);
        using var doc = JsonDocument.Parse(line);

        Assert.That(doc.RootElement.GetProperty("text").GetString(),
            Is.EqualTo("Quotes \" and back\\slashes and emoji \U0001F41B"));
    }

    [TestCase(FeedbackCategory.Bug, "bug")]
    [TestCase(FeedbackCategory.Idea, "idea")]
    [TestCase(FeedbackCategory.Balance, "balance")]
    [TestCase(FeedbackCategory.Fun, "fun")]
    [TestCase(FeedbackCategory.Confusion, "confusion")]
    public void ToLine_SerializesCategory_AsLowercaseName(FeedbackCategory category, string expected)
    {
        var entry = MakeEntry() with { Category = category };

        var line = FeedbackJsonl.ToLine(entry);
        using var doc = JsonDocument.Parse(line);

        Assert.That(doc.RootElement.GetProperty("category").GetString(), Is.EqualTo(expected));
    }
}
