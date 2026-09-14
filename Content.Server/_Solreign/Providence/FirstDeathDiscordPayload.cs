using System;
using System.Collections.Generic;
using Content.Server._Solreign.BugReport;
using Content.Server.Discord;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure shaping of a <see cref="FirstDeathObituary"/> into the §8D Discord webhook embed
///     (FD-W4, docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md) — mirrors
///     <see cref="BugReportDiscordPayload"/>'s testable-payload split: no ECS, no I/O, no Loc.
///
///     The embed text lives here as C# template constants rather than in first-death.ftl,
///     following the precedent <see cref="FirstDeathCopy.CauseLabelFor"/> set for §8D display
///     text: the .ftl copy pack's closed-variable law ($name/$tours/$title/$fee only) stays
///     intact, and the embed — which additionally needs the cause label — stays byte-testable.
///
///     Content safety (spec §6.3): everything below is template + verified game variables; the
///     only player-authored text is the character name (the BugReport precedent already relays
///     names raw to Discord, and names are subject to server naming rules). No GUIDs, no attacker
///     data, no location — the §8D format is a closed vocabulary, and the input record has no
///     fields for any of them. Default <see cref="WebhookMentions"/>: nobody gets pinged.
/// </summary>
public static class FirstDeathDiscordPayload
{
    /// <summary>§8D title template.</summary>
    public const string TitleFormat = "In Memoriam: {0} · First Departure";

    /// <summary>§8D body template — {0} name, {1} title, {2} tours, {3} cause display label.</summary>
    public const string BodyFormat =
        "Solreign records the first cessation of {0}, {1}, after {2} completed tour(s). " +
        "Cause category: {3}. The station stops for ninety seconds. The Ledger does not. " +
        "Their crypt entry is live; their file remains open for rehire.";

    /// <summary>§8D footer, verbatim.</summary>
    public const string FooterText =
        "PROVIDENCE · Solreign Crypt · Reinstatement available next shift · Fee schedule applies";

    /// <summary>§8D embed title from the composed obituary.</summary>
    public static string BuildTitle(FirstDeathObituary obituary)
    {
        return string.Format(TitleFormat, obituary.CharacterName);
    }

    /// <summary>§8D embed body from the composed obituary — the cause renders ONLY through the
    /// closed display map (<see cref="FirstDeathCopy.CauseLabelFor"/>), never the raw enum.</summary>
    public static string BuildBody(FirstDeathObituary obituary)
    {
        return string.Format(
            BodyFormat,
            obituary.CharacterName,
            obituary.Title,
            obituary.Tours,
            FirstDeathCopy.CauseLabelFor(obituary.Cause));
    }

    /// <summary>
    ///     The full §8D webhook payload: one embed, fork acid-green accent
    ///     (<see cref="BugReportDiscordPayload.EmbedColor"/> — the constant every fork-native
    ///     Discord embed reuses), default/empty allowed-mentions.
    /// </summary>
    public static WebhookPayload Build(FirstDeathObituary obituary)
    {
        var embed = new WebhookEmbed
        {
            Title = BuildTitle(obituary),
            Description = BuildBody(obituary),
            Color = BugReportDiscordPayload.EmbedColor,
            Footer = new WebhookEmbedFooter { Text = FooterText },
        };

        return new WebhookPayload { Embeds = new List<WebhookEmbed> { embed } };
    }

    /// <summary>
    ///     The player gate's math (spec §6.2, the recap-law analogue): the Discord advertisement
    ///     surface requires witnesses — connected players at death time must meet the CVar
    ///     threshold. Pure so the below/at/above cases pin in a unit test.
    /// </summary>
    public static bool MeetsPlayerGate(int playerCountAtDeath, int minPlayers)
    {
        return playerCountAtDeath >= minPlayers;
    }

    /// <summary>
    ///     Local, zero-egress parse of a Discord webhook URL into its id/token pair
    ///     (<c>.../webhooks/{id}/{token}</c>). Deliberately NOT the BugReport idiom
    ///     (<c>DiscordWebhook.GetWebhook</c>, an HTTP GET at CVar-set time): this lane's law is
    ///     zero egress until every send gate has passed, so configuration must not probe the
    ///     network. The send itself is the validation — a bad id/token fails there, logged
    ///     without the URL.
    ///
    ///     The out identifier is only produced for an https discord.com / discordapp.com webhook
    ///     path with a numeric id — anything else is rejected (fail closed, stays in
    ///     manual-paste mode).
    /// </summary>
    public static bool TryParseWebhookUrl(string? url, out WebhookIdentifier identifier)
    {
        identifier = default;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        if (host is not ("discord.com" or "discordapp.com" or "www.discord.com" or "www.discordapp.com"))
            return false;

        // AbsolutePath like /api/webhooks/{id}/{token} or /api/v10/webhooks/{id}/{token}.
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var webhooksIndex = Array.IndexOf(segments, "webhooks");
        if (webhooksIndex < 0 || segments.Length - webhooksIndex != 3)
            return false;

        var id = segments[webhooksIndex + 1];
        var token = segments[webhooksIndex + 2];

        if (id.Length == 0 || token.Length == 0)
            return false;

        foreach (var c in id)
        {
            if (c is < '0' or > '9')
                return false;
        }

        identifier = new WebhookIdentifier(id, token);
        return true;
    }
}
