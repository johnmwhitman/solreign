using System.Collections.Generic;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     Maps a <c>StationDirectiveCatalog</c> directive id (<c>Content.Server._Solreign.StationDirective</c>)
///     onto a short, audit-appropriate title. Deliberately NOT a reference to
///     <c>StationDirectiveDefinition</c> itself: that record carries a chatty PA announcement +
///     ticker lines meant to be read aloud over several minutes, not a compact one-line audit
///     header — this is its own closed vocabulary, kept in Station Audits' own file so the two
///     features can evolve independently. Falls back to a generic "on file" label for any id this
///     table hasn't been updated to cover yet (never throws on an unknown/future directive id).
/// </summary>
public static class StationAuditDirectiveTitles
{
    private static readonly Dictionary<string, string> Titles = new()
    {
        ["q3-incident-quota"] = "Q3 Incident Quota",
        ["productivity-mandate"] = "Productivity Mandate",
        ["compliance-week"] = "Compliance Week",
        ["mandatory-overtime"] = "Mandatory Overtime",
        ["budget-freeze"] = "Budget Freeze",
        ["ration-audit"] = "Ration Audit",
        ["safety-inspection"] = "Safety Inspection",
        ["quarterly-audit"] = "Quarterly Audit",
        ["surveillance-sweep"] = "Surveillance Sweep",
        ["casual-friday"] = "Casual Friday",
        ["synergy-retreat"] = "Synergy Retreat",
        ["open-door-policy"] = "Open Door Policy",
    };

    private const string Fallback = "Corporate Directive";

    public static string Resolve(string directiveId)
    {
        return Titles.TryGetValue(directiveId, out var title) ? title : Fallback;
    }
}
