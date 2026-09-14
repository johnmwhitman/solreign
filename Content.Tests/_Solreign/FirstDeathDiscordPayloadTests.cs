#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Content.Server._Solreign.BugReport;
using Content.Server._Solreign.Providence;
using Content.Server.Discord;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     FD-W4 (spec §6/§8D): the pure Discord-obituary composer — embed shape from fixed inputs,
///     the closed §8D cause-label map riding the body, the player-gate math, the zero-egress
///     webhook URL parser, and the closed-vocabulary laws (PG-13, no attacker data, handle-based
///     identity only — the character name is the ONLY player-derived identity in the payload; no
///     GUIDs, no real names, no location). Pure, no harness — the
///     <see cref="BugReportDiscordPayload"/> testability idiom.
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathDiscordPayload))]
public sealed class FirstDeathDiscordPayloadTests
{
    private static readonly FirstDeathCause[] AllCauses =
        (FirstDeathCause[]) Enum.GetValues(typeof(FirstDeathCause));

    private static FirstDeathObituary MakeObituary(
        string name = "Juno Pike",
        string title = "Probationary Asset",
        int tours = 0,
        FirstDeathCause cause = FirstDeathCause.Misadventure,
        int playerCount = 3)
    {
        return new FirstDeathObituary(
            new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            RoundId: 42,
            name, title, tours, cause, playerCount);
    }

    // --- §8D embed shape ----------------------------------------------------------------------------

    [Test]
    public void Build_ComposesTheSpec8dEmbed()
    {
        var payload = FirstDeathDiscordPayload.Build(
            MakeObituary(name: "Juno Pike", title: "Amortized Asset", tours: 7, cause: FirstDeathCause.Vacuum));

        Assert.That(payload.Embeds, Is.Not.Null.And.Count.EqualTo(1), "exactly one embed");
        var embed = payload.Embeds![0];

        Assert.Multiple(() =>
        {
            Assert.That(embed.Title, Is.EqualTo("In Memoriam: Juno Pike · First Departure"));
            Assert.That(embed.Description, Is.EqualTo(
                "Solreign records the first cessation of Juno Pike, Amortized Asset, after 7 " +
                "completed tour(s). Cause category: Environmental / atmospheric event. The station " +
                "stops for ninety seconds. The Ledger does not. Their crypt entry is live; their " +
                "file remains open for rehire."));
            Assert.That(embed.Footer, Is.Not.Null);
            Assert.That(embed.Footer?.Text, Is.EqualTo(
                "PROVIDENCE · Solreign Crypt · Reinstatement available next shift · Fee schedule applies"));
            Assert.That(embed.Color, Is.EqualTo(BugReportDiscordPayload.EmbedColor),
                "the fork's acid-green accent, the constant every fork-native embed reuses (§6.1)");
        });
    }

    [Test]
    public void Build_NobodyGetsPinged()
    {
        var payload = FirstDeathDiscordPayload.Build(MakeObituary());

        Assert.That(payload.AllowedMentions.Parse, Is.Empty,
            "default/empty allowed-mentions — an obituary must never ping anyone");
    }

    [Test]
    public void Build_EveryCauseRendersThroughTheClosedDisplayMap_NeverTheRawEnum()
    {
        foreach (var cause in AllCauses)
        {
            var body = FirstDeathDiscordPayload.BuildBody(MakeObituary(cause: cause));

            Assert.That(body, Does.Contain($"Cause category: {FirstDeathCopy.CauseLabelFor(cause)}."),
                $"{cause}: the §8D display label is the only cause vocabulary allowed on the embed");
            Assert.That(body, Does.Not.Contain(cause.ToString().ToUpperInvariant()),
                $"{cause}: the raw enum name must never leak into the public surface");
        }
    }

    [Test]
    public void Build_AllTemplateSlotsAreFilled()
    {
        foreach (var cause in AllCauses)
        {
            var payload = FirstDeathDiscordPayload.Build(MakeObituary(cause: cause));
            var serialized = JsonSerializer.Serialize(payload);

            Assert.That(serialized, Does.Not.Contain("{0}").And.Not.Contain("{1}")
                    .And.Not.Contain("{2}").And.Not.Contain("{3}"),
                "no unfilled format slot may survive composition");
        }
    }

    // --- Closed-vocabulary laws (spec §6.3 / §3.3) --------------------------------------------------

    /// <summary>Attacker/forensic vocabulary that must never appear on any obituary surface —
    /// the FD-W3 denylist idiom. The redaction is enforced by the template set itself.</summary>
    private static readonly string[] AttackerDenylist =
    {
        "attacker", "killer", "murder", "weapon", "syndicate", "agent", "assailant",
        "shot", "stabbed", "slain", "perpetrator",
    };

    /// <summary>PG-13 screen (John ruling 5): profanity/gore vocabulary denylist over the whole
    /// template surface.</summary>
    private static readonly string[] Pg13Denylist =
    {
        "fuck", "shit", "bitch", "bastard", "asshole", "goddamn",
        "gore", "corpse", "entrails", "mutilat", "dismember", "blood",
    };

    [Test]
    public void Templates_CarryNoAttackerAndNoPg13Vocabulary()
    {
        // The templates themselves, plus every fully-composed surface for every cause: the copy
        // is a closed vocabulary, so scanning the composed output with neutral inputs covers the
        // entire reachable text.
        var surfaces = AllCauses
            .Select(c => JsonSerializer.Serialize(FirstDeathDiscordPayload.Build(MakeObituary(cause: c))))
            .Append(FirstDeathDiscordPayload.TitleFormat)
            .Append(FirstDeathDiscordPayload.BodyFormat)
            .Append(FirstDeathDiscordPayload.FooterText)
            .ToArray();

        foreach (var surface in surfaces)
        {
            var lower = surface.ToLowerInvariant();
            foreach (var banned in AttackerDenylist)
            {
                Assert.That(lower, Does.Not.Contain(banned),
                    $"attacker-redaction law: '{banned}' must never appear on an obituary surface");
            }

            foreach (var banned in Pg13Denylist)
            {
                Assert.That(lower, Does.Not.Contain(banned),
                    $"PG-13 law: '{banned}' must never appear on an obituary surface");
            }
        }
    }

    [Test]
    public void Build_TheCharacterHandleIsTheOnlyIdentity_NoGuidsAnywhere()
    {
        // The input record has no GUID/account field AT ALL (enforced here structurally): the
        // §8D format's identity is the character handle, nothing else.
        var properties = typeof(FirstDeathObituary).GetProperties().Select(p => p.PropertyType);
        Assert.That(properties, Does.Not.Contain(typeof(Guid)),
            "no GUID-typed field may exist on the obituary snapshot — closed vocabulary by construction");

        var serialized = JsonSerializer.Serialize(FirstDeathDiscordPayload.Build(MakeObituary(name: "Juno Pike")));
        Assert.That(serialized, Does.Contain("Juno Pike"),
            "§8D explicitly eulogizes by character handle — the keepsake needs the name");
    }

    // --- Player gate math (spec §6.2) ---------------------------------------------------------------

    [Test]
    public void PlayerGate_BelowAtAndAboveThreshold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstDeathDiscordPayload.MeetsPlayerGate(1, 2), Is.False,
                "a solo station is below the default gate — never advertise an empty station");
            Assert.That(FirstDeathDiscordPayload.MeetsPlayerGate(2, 2), Is.True, "at threshold passes");
            Assert.That(FirstDeathDiscordPayload.MeetsPlayerGate(3, 2), Is.True, "above threshold passes");
            Assert.That(FirstDeathDiscordPayload.MeetsPlayerGate(0, 2), Is.False, "empty station never passes");
            Assert.That(FirstDeathDiscordPayload.MeetsPlayerGate(0, 0), Is.True,
                "min_players=0 disables the gate entirely (operator's explicit choice)");
        });
    }

    // --- Webhook URL parsing (zero-egress config, §6.2 webhook gate) --------------------------------

    [Test]
    public void ParseWebhook_AcceptsCanonicalDiscordWebhookUrls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstDeathDiscordPayload.TryParseWebhookUrl(
                "https://discord.com/api/webhooks/1234567890/abcDEF_-123", out var id1), Is.True);
            Assert.That(id1, Is.EqualTo(new WebhookIdentifier("1234567890", "abcDEF_-123")));

            Assert.That(FirstDeathDiscordPayload.TryParseWebhookUrl(
                "https://discord.com/api/v10/webhooks/42/tok", out var id2), Is.True,
                "versioned API path parses too");
            Assert.That(id2, Is.EqualTo(new WebhookIdentifier("42", "tok")));

            Assert.That(FirstDeathDiscordPayload.TryParseWebhookUrl(
                "https://discordapp.com/api/webhooks/42/tok/", out _), Is.True,
                "legacy host + trailing slash");
        });
    }

    [Test]
    public void ParseWebhook_RejectsEverythingElse_FailClosed()
    {
        var rejected = new[]
        {
            null,
            "",
            "   ",
            "not a url",
            "http://discord.com/api/webhooks/42/tok", // not https
            "https://evil.example.com/api/webhooks/42/tok", // wrong host
            "https://discord.com/api/webhooks/42", // no token
            "https://discord.com/api/webhooks/notnumeric/tok", // non-numeric id
            "https://discord.com/api/webhooks/42/tok/extra", // trailing segments
            "https://discord.com/api/channels/42/tok", // not a webhook path
        };

        foreach (var url in rejected)
        {
            Assert.That(FirstDeathDiscordPayload.TryParseWebhookUrl(url, out _), Is.False,
                $"'{url ?? "<null>"}' must be rejected — unparseable config stays in manual-paste mode");
        }
    }
}
