#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     FD-W4 (spec §6.2 manual-paste mode): the data/first_deaths.jsonl line shaping — one line
///     per claimed first death, GUID-free by construction (the paste block travels to a public
///     Discord channel by hand), the gate decision recorded write-before-dispatch, and the
///     ready-to-paste block byte-identical to the embed text the webhook would have sent. Pure,
///     no harness — the BugReportJsonl testability idiom.
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathObituaryJsonl))]
public sealed class FirstDeathObituaryJsonlTests
{
    private static FirstDeathObituary MakeObituary()
    {
        return new FirstDeathObituary(
            new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            RoundId: 42,
            CharacterName: "Juno Pike",
            Title: "Probationary Asset",
            Tours: 0,
            Cause: FirstDeathCause.Vacuum,
            PlayerCountAtDeath: 3);
    }

    [Test]
    public void ToLine_IsOneSingleJsonLine()
    {
        var line = FirstDeathObituaryJsonl.ToLine(MakeObituary(), FirstDeathObituaryDispatch.WebhookEmpty);

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Not.Contain('\n').And.Not.Contain('\r'),
                "one obituary = exactly one .jsonl line (embedded newlines must stay JSON-escaped)");
            Assert.DoesNotThrow(() => JsonDocument.Parse(line), "the line must be valid JSON");
        });
    }

    [Test]
    public void ToLine_CarriesTheClaimedSnapshotAndTheDecision()
    {
        var line = FirstDeathObituaryJsonl.ToLine(MakeObituary(), FirstDeathObituaryDispatch.BelowPlayerGate);

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("timestamp").GetString(), Is.EqualTo("2026-07-17T12:00:00.0000000+00:00"),
                "ISO 8601 'O' — round-trippable, sortable, unambiguous timezone");
            Assert.That(root.GetProperty("roundId").GetInt32(), Is.EqualTo(42));
            Assert.That(root.GetProperty("characterName").GetString(), Is.EqualTo("Juno Pike"));
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("Probationary Asset"));
            Assert.That(root.GetProperty("tours").GetInt32(), Is.EqualTo(0));
            Assert.That(root.GetProperty("cause").GetString(), Is.EqualTo("VACUUM"),
                "the claim row's closed cause vocabulary, verbatim");
            Assert.That(root.GetProperty("causeLabel").GetString(), Is.EqualTo("Environmental / atmospheric event"),
                "the §8D display label rides along so the paste needs zero lookup");
            Assert.That(root.GetProperty("playerCountAtDeath").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("dispatch").GetString(), Is.EqualTo("below-player-gate"));
        });
    }

    [Test]
    public void ToLine_PasteBlockIsExactlyTheEmbedText()
    {
        var obituary = MakeObituary();
        var line = FirstDeathObituaryJsonl.ToLine(obituary, FirstDeathObituaryDispatch.WebhookEmpty);

        using var doc = JsonDocument.Parse(line);
        var paste = doc.RootElement.GetProperty("paste").GetString();

        Assert.That(paste, Is.EqualTo(
                FirstDeathDiscordPayload.BuildTitle(obituary) + "\n" +
                FirstDeathDiscordPayload.BuildBody(obituary) + "\n" +
                FirstDeathDiscordPayload.FooterText),
            "manual mode and webhook mode must publish the SAME words — John pastes what the " +
            "embed would have said, not a second copy voice");
    }

    [Test]
    public void ToLine_PropertySetIsClosed_AndGuidFree()
    {
        var line = FirstDeathObituaryJsonl.ToLine(MakeObituary(), FirstDeathObituaryDispatch.Dispatched);

        using var doc = JsonDocument.Parse(line);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.That(names, Is.EquivalentTo(new[]
            {
                "timestamp", "roundId", "characterName", "title", "tours",
                "cause", "causeLabel", "playerCountAtDeath", "dispatch", "paste",
            }),
            "the line's property set is CLOSED — in particular no account GUID may ever appear " +
            "in a file whose content is hand-pasted into public Discord");
    }

    [Test]
    public void DispatchNames_AreTotalAndDistinct()
    {
        var all = (FirstDeathObituaryDispatch[]) Enum.GetValues(typeof(FirstDeathObituaryDispatch));
        var names = all.Select(FirstDeathObituaryJsonl.DispatchName).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.All.Not.Empty);
            Assert.That(names, Is.Unique, "every gate decision must be distinguishable in the ledger");
        });
    }
}
