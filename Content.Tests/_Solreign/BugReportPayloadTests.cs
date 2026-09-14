using System;
using System.Text.Json;
using Content.Server._Solreign.BugReport;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(BugReportJsonl))]
public sealed class BugReportJsonlTests
{
    private static BugReportEntry MakeEntry() => new(
        Timestamp: new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero),
        PlayerName: "Kolton",
        PlayerGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        RoundId: 42,
        Text: "The vending machine ate my ID card");

    [Test]
    public void ToLine_ProducesSingleLine_NoEmbeddedNewlines()
    {
        var line = BugReportJsonl.ToLine(MakeEntry());

        Assert.That(line, Does.Not.Contain("\n"));
        Assert.That(line, Does.Not.Contain("\r"));
    }

    [Test]
    public void ToLine_IsValidJson_WithExpectedFields()
    {
        var line = BugReportJsonl.ToLine(MakeEntry());

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("playerName").GetString(), Is.EqualTo("Kolton"));
        Assert.That(root.GetProperty("playerGuid").GetString(), Is.EqualTo("11111111-1111-1111-1111-111111111111"));
        Assert.That(root.GetProperty("roundId").GetInt32(), Is.EqualTo(42));
        Assert.That(root.GetProperty("text").GetString(), Is.EqualTo("The vending machine ate my ID card"));
        Assert.That(root.GetProperty("timestamp").GetString(), Does.StartWith("2026-07-11T12:00:00"));
    }

    [Test]
    public void ToLine_PreservesArbitraryPlayerText_WithoutMangling()
    {
        var entry = MakeEntry() with { Text = "Quotes \" and back\\slashes and emoji \U0001F41B" };

        var line = BugReportJsonl.ToLine(entry);
        using var doc = JsonDocument.Parse(line);

        Assert.That(doc.RootElement.GetProperty("text").GetString(),
            Is.EqualTo("Quotes \" and back\\slashes and emoji \U0001F41B"));
    }
}

[TestFixture]
[TestOf(typeof(BugReportDiscordPayload))]
public sealed class BugReportDiscordPayloadTests
{
    private static BugReportEntry MakeEntry() => new(
        Timestamp: DateTimeOffset.UnixEpoch,
        PlayerName: "Kolton",
        PlayerGuid: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        RoundId: 7,
        Text: "Reactor door won't close");

    private static BugReportDiscordLabels MakeLabels() => new(
        Title: "New defect report",
        PlayerFieldLabel: "Reported by",
        GuidFieldLabel: "Account GUID",
        RoundFieldLabel: "Round",
        FooterText: "Solreign Station 13 · Solreign QA defect intake");

    [Test]
    public void Build_EchoesReportTextAsEmbedDescription()
    {
        var payload = BugReportDiscordPayload.Build(MakeEntry(), MakeLabels());

        Assert.That(payload.Embeds, Has.Count.EqualTo(1));
        Assert.That(payload.Embeds![0].Description, Is.EqualTo("Reactor door won't close"));
        Assert.That(payload.Embeds[0].Title, Is.EqualTo("New defect report"));
    }

    [Test]
    public void Build_UsesAcidGreenAccentColor()
    {
        var payload = BugReportDiscordPayload.Build(MakeEntry(), MakeLabels());

        Assert.That(payload.Embeds![0].Color, Is.EqualTo(BugReportDiscordPayload.EmbedColor));
    }

    [Test]
    public void Build_IncludesPlayerNameGuidAndRoundAsFields()
    {
        var payload = BugReportDiscordPayload.Build(MakeEntry(), MakeLabels());
        var fields = payload.Embeds![0].Fields;

        Assert.That(fields, Has.Count.EqualTo(3));
        Assert.That(fields[0].Name, Is.EqualTo("Reported by"));
        Assert.That(fields[0].Value, Is.EqualTo("Kolton"));
        Assert.That(fields[1].Name, Is.EqualTo("Account GUID"));
        Assert.That(fields[1].Value, Is.EqualTo("22222222-2222-2222-2222-222222222222"));
        Assert.That(fields[2].Name, Is.EqualTo("Round"));
        Assert.That(fields[2].Value, Is.EqualTo("7"));
    }

    [Test]
    public void Build_SetsFooterFromSuppliedLabel()
    {
        var payload = BugReportDiscordPayload.Build(MakeEntry(), MakeLabels());

        Assert.That(payload.Embeds![0].Footer?.Text, Is.EqualTo("Solreign Station 13 · Solreign QA defect intake"));
    }

    [Test]
    public void Build_DoesNotOptIntoAnyMentionParsing()
    {
        // Default WebhookMentions() has an empty Parse set — no @everyone/@here/role pings.
        var payload = BugReportDiscordPayload.Build(MakeEntry(), MakeLabels());

        Assert.That(payload.AllowedMentions.Parse, Is.Empty);
    }
}
