#nullable enable
using System.Collections.Generic;
using Content.Server._Solreign.Bounties;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure input/output-shaping tests for <see cref="BountyClaimRules"/> — the Liability
///     Board's claim-text clamp/sanitize rule and public-listing sanitizer, added alongside the
///     v11 front-door #2 wave (Content.Server/_Solreign/Bounties/SolreignBountySystem.cs). No
///     ECS, no HTTP: these exercise the pure static class directly, the <c>ContractRulesTests</c>
///     precedent.
/// </summary>
[TestFixture]
[TestOf(typeof(BountyClaimRules))]
public sealed class BountyClaimRulesTests
{
    // --- TrySanitizeClaimText ---

    [Test]
    public void TrySanitizeClaimText_Blank_Rejected()
    {
        var ok = BountyClaimRules.TrySanitizeClaimText(string.Empty, out var sanitized);

        Assert.That(ok, Is.False);
        Assert.That(sanitized, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TrySanitizeClaimText_WhitespaceOnly_Rejected()
    {
        var ok = BountyClaimRules.TrySanitizeClaimText("   \t  \n  ", out _);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void TrySanitizeClaimText_TrimsSurroundingWhitespace()
    {
        var ok = BountyClaimRules.TrySanitizeClaimText("  I fixed the reactor  ", out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized, Is.EqualTo("I fixed the reactor"));
    }

    [Test]
    public void TrySanitizeClaimText_ExactlyAtLimit_NotTruncated()
    {
        var text = new string('x', BountyClaimRules.MaxClaimTextLength);

        var ok = BountyClaimRules.TrySanitizeClaimText(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(BountyClaimRules.MaxClaimTextLength));
    }

    [Test]
    public void TrySanitizeClaimText_OverLimit_TruncatesRatherThanRejects()
    {
        var text = new string('x', BountyClaimRules.MaxClaimTextLength + 50);

        var ok = BountyClaimRules.TrySanitizeClaimText(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(BountyClaimRules.MaxClaimTextLength));
    }

    // --- ClampStatusMessage ---

    [Test]
    public void ClampStatusMessage_UnderLimit_Unchanged()
    {
        Assert.That(BountyClaimRules.ClampStatusMessage("claimed"), Is.EqualTo("claimed"));
    }

    [Test]
    public void ClampStatusMessage_OverLimit_Truncated()
    {
        var msg = new string('y', BountyClaimRules.MaxStatusMessageLength + 20);

        var clamped = BountyClaimRules.ClampStatusMessage(msg);

        Assert.That(clamped.Length, Is.EqualTo(BountyClaimRules.MaxStatusMessageLength));
    }

    // --- SanitizeListings ---

    [Test]
    public void SanitizeListings_Empty_ReturnsEmpty()
    {
        var result = BountyClaimRules.SanitizeListings(new List<int>(), new List<string?>(), new List<string?>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeListings_BlankTitle_DroppedAsFailure()
    {
        var result = BountyClaimRules.SanitizeListings(
            new List<int> { 1, 2 },
            new List<string?> { string.Empty, "Fix the reactor" },
            new List<string?> { "desc", "desc2" });

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo(2));
    }

    [Test]
    public void SanitizeListings_NullDescription_BecomesEmptyString()
    {
        var result = BountyClaimRules.SanitizeListings(
            new List<int> { 1 },
            new List<string?> { "Title" },
            new List<string?> { null });

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Description, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SanitizeListings_OverlongTitle_TruncatedToCap()
    {
        var longTitle = new string('t', BountyClaimRules.MaxListingTitleLength + 30);

        var result = BountyClaimRules.SanitizeListings(
            new List<int> { 1 },
            new List<string?> { longTitle },
            new List<string?> { "desc" });

        Assert.That(result[0].Title.Length, Is.EqualTo(BountyClaimRules.MaxListingTitleLength));
    }

    [Test]
    public void SanitizeListings_OverlongDescription_TruncatedToCap()
    {
        var longDesc = new string('d', BountyClaimRules.MaxListingDescriptionLength + 30);

        var result = BountyClaimRules.SanitizeListings(
            new List<int> { 1 },
            new List<string?> { "Title" },
            new List<string?> { longDesc });

        Assert.That(result[0].Description.Length, Is.EqualTo(BountyClaimRules.MaxListingDescriptionLength));
    }

    [Test]
    public void SanitizeListings_MoreThanMaxCount_CappedAtMax()
    {
        var ids = new List<int>();
        var titles = new List<string?>();
        var descriptions = new List<string?>();

        for (var i = 0; i < BountyClaimRules.MaxListingCount + 25; i++)
        {
            ids.Add(i);
            titles.Add($"Bounty {i}");
            descriptions.Add("desc");
        }

        var result = BountyClaimRules.SanitizeListings(ids, titles, descriptions);

        Assert.That(result.Count, Is.EqualTo(BountyClaimRules.MaxListingCount));
    }

    // --- ALIVENESS P0 #1: claim-failure popup key selection ---

    [Test]
    public void ClaimFailurePopupLocKeys_AreDistinctNonBlankAndPlural()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BountyClaimRules.ClaimFailurePopupLocKeys.Length, Is.GreaterThanOrEqualTo(2),
                "repeated failures must not read as one canned error dialog — the Oracle-trio idiom");
            Assert.That(BountyClaimRules.ClaimFailurePopupLocKeys, Is.Unique);
            Assert.That(BountyClaimRules.ClaimFailurePopupLocKeys, Has.All.Not.Empty);
        });
    }

    [Test]
    public void PickClaimFailureLocKey_IsTotalForAnyRollIncludingNegatives()
    {
        foreach (var roll in new[] { int.MinValue, -7, -1, 0, 1, 2, 3, 1000, int.MaxValue })
        {
            var key = BountyClaimRules.PickClaimFailureLocKey(roll);
            Assert.That(BountyClaimRules.ClaimFailurePopupLocKeys, Does.Contain(key),
                $"roll {roll} must map onto a real popup key, never index out of range");
        }
    }

    [Test]
    public void PickClaimFailureLocKey_CoversEveryVariant()
    {
        var seen = new HashSet<string>();
        for (var roll = 0; roll < BountyClaimRules.ClaimFailurePopupLocKeys.Length; roll++)
            seen.Add(BountyClaimRules.PickClaimFailureLocKey(roll));

        Assert.That(seen, Is.EquivalentTo(BountyClaimRules.ClaimFailurePopupLocKeys));
    }

    // --- ALIVENESS P0 #2: offline-vs-empty board state ---

    [Test]
    public void ShapeBoardListings_FailedFetch_IsOfflineWithZeroRows()
    {
        var (listings, offline) = BountyClaimRules.ShapeBoardListings<string>(null);

        Assert.Multiple(() =>
        {
            Assert.That(offline, Is.True);
            Assert.That(listings, Is.Empty);
        });
    }

    [Test]
    public void ShapeBoardListings_SuccessfulEmptyFetch_IsTrueEmptyNeverOffline()
    {
        var (listings, offline) = BountyClaimRules.ShapeBoardListings(new List<string>());

        Assert.Multiple(() =>
        {
            Assert.That(offline, Is.False, "a genuinely empty pool must never wear the offline state");
            Assert.That(listings, Is.Empty);
        });
    }

    [Test]
    public void ShapeBoardListings_SuccessfulFetch_PassesRowsThroughUnchanged()
    {
        var fetched = new List<string> { "a", "b" };

        var (listings, offline) = BountyClaimRules.ShapeBoardListings(fetched);

        Assert.Multiple(() =>
        {
            Assert.That(offline, Is.False);
            Assert.That(listings, Is.SameAs(fetched));
        });
    }
}
