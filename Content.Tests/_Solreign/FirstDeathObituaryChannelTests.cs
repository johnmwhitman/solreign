#nullable enable
using Content.Server._Solreign.Providence;
using Content.Server.Administration.Systems;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     FD-W4 regression pin for the FD-W3 review's merge-blocker lesson (grk r1 #1, generalized
///     into lane law): ANY new egress gets its OWN rate-limit channel. The Discord obituary send
///     must never share a window with either crypt channel — a first death fires the per-death
///     telemetry POST, the first-death memorial POST, and (when armed) the Discord send off the
///     same claim, all inside <c>DirectorChannel.MinRequestInterval</c>; a shared channel would
///     systematically drop whichever egress ran last, with no retry. Per-channel isolation itself
///     is already pinned by <see cref="FirstDeathCryptChannelTests.RateLimitChannelsAreIsolated"/>.
/// </summary>
[TestFixture]
public sealed class FirstDeathObituaryChannelTests
{
    [Test]
    public void DiscordObituaryChannelIsDistinctFromBothCryptChannels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstDeathObituarySystem.RateLimitChannel,
                Is.Not.EqualTo(SolreignCryptSystem.Channel),
                "sharing the per-death telemetry channel would drop the once-ever obituary send");
            Assert.That(FirstDeathObituarySystem.RateLimitChannel,
                Is.Not.EqualTo(SolreignCryptSystem.FirstDeathChannel),
                "the crypt memorial POST and the Discord send fire off the SAME claim, back to " +
                "back — they must never contend for one window");
        });
    }
}
