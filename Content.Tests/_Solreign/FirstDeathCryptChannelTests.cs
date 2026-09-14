#nullable enable
using Content.Server.Administration.Systems;
using Content.Server._Solreign.Director;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Regression pins for the FD-W3 review's merge-blocker (grk r1 #1): the same death fires the
///     per-death telemetry POST and then, within the rate window, the once-per-account-EVER
///     first-death memorial POST — they must ride SEPARATE rate-limit channels or the memorial is
///     systematically dropped with no retry.
/// </summary>
[TestFixture]
public sealed class FirstDeathCryptChannelTests
{
    [Test]
    public void TelemetryAndFirstDeathChannelsAreDistinct()
    {
        Assert.That(SolreignCryptSystem.FirstDeathChannel, Is.Not.EqualTo(SolreignCryptSystem.Channel),
            "shared channel = the sibling telemetry POST always consumes the window first and the " +
            "once-ever memorial is dropped (FD-W3 review finding #1)");
    }

    [Test]
    public void RateLimitChannelsAreIsolated()
    {
        // Unique names so static rate-limit state from other tests can't interfere.
        var a = $"test_iso_a_{System.Guid.NewGuid():N}";
        var b = $"test_iso_b_{System.Guid.NewGuid():N}";

        Assert.Multiple(() =>
        {
            Assert.That(DirectorChannel.TryEnterRateLimit(a), Is.True, "fresh channel A enters");
            Assert.That(DirectorChannel.TryEnterRateLimit(a), Is.False, "A again inside the window is limited");
            Assert.That(DirectorChannel.TryEnterRateLimit(b), Is.True,
                "channel B must be unaffected by A's window — per-channel isolation is what makes " +
                "the distinct-channel fix above sufficient");
        });
    }
}
