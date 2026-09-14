using System;
using System.Collections.Generic;
using System.Globalization;
using Content.Server._Solreign.SeasonLedger;

namespace Content.Server._Solreign.ShiftArchive;

/// <summary>One rendered-to-be line of an archive entry: a Fluent key plus its variables.</summary>
public readonly record struct ShiftArchiveLine(string Key, (string, object)[] Args);

/// <summary>
///     Pure copy-selection tables for the Shift Archive board — the loc-key side of the copy pack
///     (Resources/Locale/en-US/_solreign/shift-archive.ftl), unit-tested in
///     Content.Tests/_Solreign/ShiftArchiveCopyTests.cs. No ECS, no I/O, no Loc — this class only
///     picks KEYS from a closed vocabulary; <c>ShiftArchiveSystem</c> renders them. Every value on
///     the board comes from a real <c>station_audit_log</c> row (plus a real completed-contract
///     count); there is deliberately no free-text slot anywhere. The only name that can appear is
///     <c>commendation_name</c>, which the round-end commendation already announced publicly —
///     the first-death "public by prior disclosure" tier.
/// </summary>
public static class ShiftArchiveCopy
{
    public const string HeaderKey = "solreign-shift-archive-entry-header";
    public const string CrewKey = "solreign-shift-archive-line-crew";
    public const string NoDeathsKey = "solreign-shift-archive-line-no-deaths";
    public const string DeathsKey = "solreign-shift-archive-line-deaths";
    public const string DeathsCommemoratedKey = "solreign-shift-archive-line-deaths-commemorated";
    public const string DirectiveFulfilledKey = "solreign-shift-archive-line-directive-fulfilled";
    public const string DirectiveFailedKey = "solreign-shift-archive-line-directive-failed";
    public const string DirectiveUnreportedKey = "solreign-shift-archive-line-directive-unreported";
    public const string ContractsKey = "solreign-shift-archive-line-contracts";
    public const string CommendationKey = "solreign-shift-archive-line-commendation";

    /// <summary>Every key the copy pack can emit — the copy test walks this list against the .ftl.</summary>
    public static readonly string[] AllKeys =
    {
        HeaderKey, CrewKey, NoDeathsKey, DeathsKey, DeathsCommemoratedKey,
        DirectiveFulfilledKey, DirectiveFailedKey, DirectiveUnreportedKey,
        ContractsKey, CommendationKey,
    };

    /// <summary>
    ///     Header line for one archived shift. The ledger stores <c>ended_utc</c> as an ISO-8601
    ///     string; an unparseable value degrades to a dateless header rather than throwing —
    ///     an ambient surface must never crash on a corrupt row (the Echo skip-don't-throw law).
    /// </summary>
    public static ShiftArchiveLine HeaderFor(StationAuditRecord audit)
    {
        var date = DateTime.TryParse(audit.EndedAtUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ended)
            ? ended.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "----";
        return new ShiftArchiveLine(HeaderKey, new[] { ("round", (object) audit.RoundId), ("date", (object) date) });
    }

    /// <summary>
    ///     Detail lines for one archived shift, in fixed board order: crew, deaths, directive,
    ///     contracts, commendation. Lines whose fact is absent (no directive ran, no contracts
    ///     completed, nobody commended) are omitted, never fabricated as zeroes — absence of a row
    ///     is not an event.
    /// </summary>
    public static List<ShiftArchiveLine> LinesFor(StationAuditRecord audit, int contractCount)
    {
        var lines = new List<ShiftArchiveLine>
        {
            new(CrewKey, new[] { ("crew", (object) audit.CrewCount), ("minutes", (object) audit.ShiftDurationMinutes) }),
        };

        if (audit.DeathCount <= 0)
            lines.Add(new ShiftArchiveLine(NoDeathsKey, Array.Empty<(string, object)>()));
        else
            lines.Add(new ShiftArchiveLine(
                audit.FirstDeathCommemorated ? DeathsCommemoratedKey : DeathsKey,
                new[] { ("count", (object) audit.DeathCount) }));

        if (!string.IsNullOrWhiteSpace(audit.DirectiveTitle))
        {
            var key = !audit.DirectiveOutcomeReported
                ? DirectiveUnreportedKey
                : audit.DirectiveOutcomeFulfilled ? DirectiveFulfilledKey : DirectiveFailedKey;
            lines.Add(new ShiftArchiveLine(key, new[] { ("title", (object) audit.DirectiveTitle!) }));
        }

        if (contractCount > 0)
            lines.Add(new ShiftArchiveLine(ContractsKey, new[] { ("count", (object) contractCount) }));

        if (!string.IsNullOrWhiteSpace(audit.CommendationName))
            lines.Add(new ShiftArchiveLine(CommendationKey,
                new[] { ("name", (object) audit.CommendationName!), ("score", (object) audit.CommendationScore) }));

        return lines;
    }
}
