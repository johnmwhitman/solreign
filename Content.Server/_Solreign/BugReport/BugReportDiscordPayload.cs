using System.Collections.Generic;
using Content.Server.Discord;

namespace Content.Server._Solreign.BugReport;

/// <summary>
///     Localized label strings for the Discord embed built by <see cref="BugReportDiscordPayload"/>.
///     Resolved via <c>Loc.GetString</c> by <see cref="BugReportSystem"/> and passed in, so the
///     actual payload shaping below has no localization/IoC dependency and is unit-testable directly.
/// </summary>
public sealed record BugReportDiscordLabels(
    string Title,
    string PlayerFieldLabel,
    string GuidFieldLabel,
    string RoundFieldLabel,
    string FooterText);

/// <summary>
///     Pure shaping of a <see cref="BugReportEntry"/> into a Discord webhook embed payload — mirrors
///     the embed conventions used by <c>NewsSystem</c> / <c>WatchlistWebhookManager</c> (title +
///     description + fields + footer, default/empty allowed-mentions so nobody gets pinged).
/// </summary>
public static class BugReportDiscordPayload
{
    /// <summary>Solreign acid-green, used for every fork-native Discord embed accent.</summary>
    public const int EmbedColor = 0x39FF14;

    public static WebhookPayload Build(BugReportEntry entry, BugReportDiscordLabels labels)
    {
        var embed = new WebhookEmbed
        {
            Title = labels.Title,
            Description = entry.Text,
            Color = EmbedColor,
            Fields = new List<WebhookEmbedField>
            {
                new() { Name = labels.PlayerFieldLabel, Value = entry.PlayerName, Inline = true },
                new() { Name = labels.GuidFieldLabel, Value = entry.PlayerGuid.ToString(), Inline = true },
                new() { Name = labels.RoundFieldLabel, Value = entry.RoundId.ToString(), Inline = true },
            },
            Footer = new WebhookEmbedFooter { Text = labels.FooterText },
        };

        return new WebhookPayload { Embeds = new List<WebhookEmbed> { embed } };
    }
}
