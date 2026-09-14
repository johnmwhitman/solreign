#nullable enable
using System;
using System.IO;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Feedback;
using Content.Shared._Solreign.Feedback;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Roadmap D2.1 (docs/ROADMAP-PLAYER-DELIGHT.md, "Structured player feedback"): drives
///     <see cref="FeedbackSystem.Submit"/> -- the real handler behind the "feedback" window's
///     networked submission event -- through a connected player session and asserts the JSONL record
///     actually lands on disk with a reference ID, and that the per-round rate limit
///     (<see cref="FeedbackConstants.MaxSubmissionsPerRound"/>) is enforced end-to-end.
///
///     Calls <see cref="FeedbackSystem.Submit"/> directly rather than round-tripping a real
///     <see cref="SubmitFeedbackEvent"/> over the network -- same "real, only public entry point"
///     idiom <c>ContractClaimFlowIntegrationTest</c> uses for BUI messages (that file raises the
///     message handler's real target directly instead of simulating a client click); here the
///     equivalent real target is <c>Submit</c>, since <c>OnSubmitFeedbackEvent</c> is a one-line
///     forward to it plus the reply envelope.
/// </summary>
[TestFixture]
public sealed class FeedbackSystemIntegrationTest : GameTest
{
    // Dirty: writes directly to the real on-disk data/feedback/*.jsonl file (outside GameTest's
    // entity-cleanup tracking) -- same precedent as SeasonLedgerSystemIntegrationTest for exactly
    // this class of side effect.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected session has a real AttachedEntity, so Submit's
        // job/location lookups exercise their real (if empty-handed) code paths instead of just the
        // "no attached entity" fallback branch every time.
        DummyTicker = false,
    };

    private static string ResolveFeedbackDir(IResourceManager res)
    {
        var dir = res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        return Path.Combine(dir, "feedback");
    }

    [Test]
    public async Task Submit_ValidReport_ReturnsReferenceId_AndAppendsJsonlRecord()
    {
        var server = Server;
        var res = server.ResolveDependency<IResourceManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var feedbackDir = ResolveFeedbackDir(res);

        ICommonSession session = default!;
        FeedbackSubmitResult result = default;
        var marker = Guid.NewGuid().ToString("N");
        var text = $"Wave24 integration marker {marker}: the vending machine ate my ID card";

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions.First();
            var system = server.System<FeedbackSystem>();
            result = system.Submit(session, FeedbackCategory.Bug, text);
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "A fresh, non-empty, under-limit submission must succeed.");
            Assert.That(result.ReferenceId, Does.StartWith("SF-"), "Reference IDs are prefixed 'SF-'.");
            Assert.That(result.ErrorMessage, Is.Null);
        });

        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var path = Path.Combine(feedbackDir, $"feedback-{today}.jsonl");

        await server.WaitAssertion(() =>
        {
            Assert.That(File.Exists(path), Is.True, $"Expected JSONL file at {path} after a successful submission.");

            var matchingLine = File.ReadAllLines(path).LastOrDefault(l => l.Contains(marker));
            Assert.That(matchingLine, Is.Not.Null, $"No line in {path} contains marker {marker}.");

            using var doc = System.Text.Json.JsonDocument.Parse(matchingLine!);
            var root = doc.RootElement;

            Assert.That(root.GetProperty("referenceId").GetString(), Is.EqualTo(result.ReferenceId));
            Assert.That(root.GetProperty("playerGuid").GetString(), Is.EqualTo(session.UserId.UserId.ToString()));
            Assert.That(root.GetProperty("category").GetString(), Is.EqualTo("bug"));
            Assert.That(root.GetProperty("text").GetString(), Is.EqualTo(text));
            Assert.That(root.GetProperty("map").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("serverBuild").GetString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task Submit_EmptyOrWhitespaceText_FailsWithoutWritingARecord()
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        FeedbackSubmitResult result = default;

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<FeedbackSystem>();
            result = system.Submit(session, FeedbackCategory.Idea, "   ");
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ReferenceId, Is.Null);
            Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Submit_PastPerRoundLimit_IsRateLimited()
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var marker = Guid.NewGuid().ToString("N");

        var results = new FeedbackSubmitResult[FeedbackConstants.MaxSubmissionsPerRound + 1];

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<FeedbackSystem>();

            for (var i = 0; i < results.Length; i++)
                results[i] = system.Submit(session, FeedbackCategory.Confusion, $"Wave24 rate-limit marker {marker} #{i}");
        });

        Assert.Multiple(() =>
        {
            for (var i = 0; i < FeedbackConstants.MaxSubmissionsPerRound; i++)
                Assert.That(results[i].Success, Is.True, $"Submission #{i} (within the per-round cap) should succeed.");

            var overLimit = results[^1];
            Assert.That(overLimit.Success, Is.False, "Submission past the per-round cap must be rejected.");
            Assert.That(overLimit.ErrorMessage, Is.Not.Null.And.Not.Empty);
        });
    }
}
