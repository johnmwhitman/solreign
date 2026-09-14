using System.Linq;
using Content.Server._Solreign.StationDirective;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Structural integrity checks for the SR-W-081 "random round modifiers" catalog expansion (3 -&gt;
///     12 directives). Pure data checks — no ECS, no Loc — so they run fast and catch the cheap mistakes
///     (a copy-pasted id, a directive with no ticker lines, a runaway screen-FX duration) before they ever
///     reach the integration-test layer that actually resolves the locale strings.
/// </summary>
[TestFixture]
[TestOf(typeof(StationDirectiveCatalog))]
public sealed class StationDirectiveCatalogTests
{
    [Test]
    public void Directives_HasAtLeastTwelveEntries()
    {
        // SR-W-081: the original 3 (q3-incident-quota, productivity-mandate, compliance-week) plus 9 new
        // corporate modifiers (7 required flavors + 2 of the team's own in the same voice).
        Assert.That(StationDirectiveCatalog.Directives.Count, Is.GreaterThanOrEqualTo(12));
    }

    [Test]
    public void Directives_AllIdsAreUnique()
    {
        var ids = StationDirectiveCatalog.Directives.Select(d => d.Id).ToList();

        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void Directives_AllIdsAreNonEmpty()
    {
        Assert.That(StationDirectiveCatalog.Directives, Is.All.Matches<StationDirectiveDefinition>(d => !string.IsNullOrWhiteSpace(d.Id)));
    }

    [Test]
    public void Directives_AllHaveAnAnnouncementLocKey()
    {
        Assert.That(StationDirectiveCatalog.Directives,
            Is.All.Matches<StationDirectiveDefinition>(d => !string.IsNullOrWhiteSpace(d.AnnouncementLocKey)));
    }

    [Test]
    public void Directives_AllAnnouncementLocKeysAreUnique()
    {
        var keys = StationDirectiveCatalog.Directives.Select(d => d.AnnouncementLocKey).ToList();

        Assert.That(keys, Is.Unique);
    }

    [Test]
    public void Directives_AllHaveAtLeastOneTickerLine()
    {
        Assert.That(StationDirectiveCatalog.Directives, Is.All.Matches<StationDirectiveDefinition>(d => d.TickerLocKeys.Count > 0));
    }

    [Test]
    public void Directives_NoDirectiveHasDuplicateTickerLocKeysWithinItself()
    {
        foreach (var directive in StationDirectiveCatalog.Directives)
        {
            Assert.That(directive.TickerLocKeys, Is.Unique, $"{directive.Id} has a repeated ticker loc key.");
        }
    }

    [Test]
    public void Directives_AllTickerLocKeysAcrossCatalogAreGloballyUnique()
    {
        // Every ticker loc key belongs to exactly one directive — guards against a copy-paste that wires
        // one directive's flavor lines into another's slot.
        var allTickerKeys = StationDirectiveCatalog.Directives.SelectMany(d => d.TickerLocKeys).ToList();

        Assert.That(allTickerKeys, Is.Unique);
    }

    // --- ScreenFxDurationSeconds: the one mechanical hook (SolreignScreenFxEvent). Most directives leave
    //     it null (flavor-only); the few that set it must stay within the engine's own clamp window so the
    //     hard "no dangerous mechanics" constraint holds even if a future edit fat-fingers the value. ---

    [Test]
    public void Directives_ScreenFxDuration_WhenSet_IsWithinSaneOverlayWindow()
    {
        // Mirrors SolreignScreenFxTiming's [MinDuration, MaxDuration] = [0.1, 5] clamp — asserted here as a
        // catalog-authoring sanity check independent of the engine clamp itself, which always applies too.
        foreach (var directive in StationDirectiveCatalog.Directives)
        {
            if (directive.ScreenFxDurationSeconds is not { } duration)
                continue;

            Assert.That(duration, Is.InRange(0.1f, 5f), $"{directive.Id}'s screen-FX duration is outside the sane overlay window.");
        }
    }

    [Test]
    public void Directives_QuarterlyAudit_HasNoMechanicalHook()
    {
        // Backlog hard constraint: Quarterly Audit is strictly flavor-only and must never be wired to a
        // mechanical primitive (screen-FX or otherwise) that could someday be mistaken for touching Standing.
        var quarterlyAudit = StationDirectiveCatalog.Directives.Single(d => d.Id == "quarterly-audit");

        Assert.That(quarterlyAudit.ScreenFxDurationSeconds, Is.Null);
    }

    [Test]
    public void Directives_ExpectedFlavorSetIsPresent()
    {
        // SR-W-081's required flavor set (house-voice names, backlog concepts): Mandatory Overtime, Budget
        // Freeze, Ration Audit, Safety Inspection, Quarterly Audit, Surveillance Sweep, Casual Friday.
        var ids = StationDirectiveCatalog.Directives.Select(d => d.Id).ToHashSet();

        Assert.That(ids, Does.Contain("mandatory-overtime"));
        Assert.That(ids, Does.Contain("budget-freeze"));
        Assert.That(ids, Does.Contain("ration-audit"));
        Assert.That(ids, Does.Contain("safety-inspection"));
        Assert.That(ids, Does.Contain("quarterly-audit"));
        Assert.That(ids, Does.Contain("surveillance-sweep"));
        Assert.That(ids, Does.Contain("casual-friday"));
    }
}
