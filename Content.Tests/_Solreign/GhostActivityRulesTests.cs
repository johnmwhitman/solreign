using System.Collections.Generic;
using Content.Shared._Solreign.Ghost;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(GhostActivityRules))]
public sealed class GhostActivityRulesTests
{
    [Test]
    public void Sanitize_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.That(GhostActivityRules.Sanitize(null, GhostActivityRules.MaxNameLength), Is.Empty);
        Assert.That(GhostActivityRules.Sanitize("   ", GhostActivityRules.MaxNameLength), Is.Empty);
    }

    [Test]
    public void Sanitize_TrimsSurroundingWhitespace()
    {
        var result = GhostActivityRules.Sanitize("  Arcade Cabinet  ", GhostActivityRules.MaxNameLength);

        Assert.That(result, Is.EqualTo("Arcade Cabinet"));
    }

    [Test]
    public void Sanitize_ClampsToMaxLength()
    {
        var result = GhostActivityRules.Sanitize(new string('x', 100), 60);

        Assert.That(result, Has.Length.EqualTo(60));
    }

    [Test]
    public void Sanitize_ShortInput_IsUnchanged()
    {
        var result = GhostActivityRules.Sanitize("Noodle Sparring", GhostActivityRules.MaxNameLength);

        Assert.That(result, Is.EqualTo("Noodle Sparring"));
    }

    [Test]
    public void SortForDisplay_OrdersCaseInsensitivelyByName()
    {
        var activities = new List<GhostActivityInfo>
        {
            new(NetEntity.Invalid, "noodle sparring", ""),
            new(NetEntity.Invalid, "Arcade Cabinet", ""),
            new(NetEntity.Invalid, "Companion Cube", ""),
        };

        GhostActivityRules.SortForDisplay(activities);

        Assert.That(activities[0].Name, Is.EqualTo("Arcade Cabinet"));
        Assert.That(activities[1].Name, Is.EqualTo("Companion Cube"));
        Assert.That(activities[2].Name, Is.EqualTo("noodle sparring"));
    }

    [Test]
    public void SortForDisplay_EmptyList_DoesNotThrow()
    {
        var activities = new List<GhostActivityInfo>();

        Assert.DoesNotThrow(() => GhostActivityRules.SortForDisplay(activities));
        Assert.That(activities, Is.Empty);
    }
}
