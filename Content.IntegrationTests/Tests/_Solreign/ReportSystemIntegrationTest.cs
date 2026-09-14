#nullable enable
using System;
using System.IO;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Report;
using Content.Shared._Solreign.Report;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Drives <see cref="ReportSystem.Submit"/> -- the real handler behind the "report" window's
///     networked submission event -- through a connected player session and asserts the JSONL record
///     actually lands on disk, the per-round rate limit is enforced end-to-end, self-reports are
///     refused, and target-name sanitization behaves as expected. Mirrors
///     <c>FeedbackSystemIntegrationTest</c>: calls <see cref="ReportSystem.Submit"/> directly rather than
///     round-tripping a real <see cref="SubmitReportEvent"/> over the network, since
///     <c>OnSubmitReportEvent</c> is a one-line forward to it plus the reply envelope.
/// </summary>
[TestFixture]
public sealed class ReportSystemIntegrationTest : GameTest
{
    // Dirty: writes directly to the real on-disk data/reports/*.jsonl file (outside GameTest's
    // entity-cleanup tracking) -- same precedent as FeedbackSystemIntegrationTest for exactly this
    // class of side effect.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static string ResolveReportsDir(IResourceManager res)
    {
        var dir = res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        return Path.Combine(dir, "reports");
    }

    [Test]
    public async Task Submit_ValidReport_ReturnsReferenceId_AndAppendsJsonlRecord()
    {
        var server = Server;
        var res = server.ResolveDependency<IResourceManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var reportsDir = ResolveReportsDir(res);

        ICommonSession session = default!;
        ReportSubmitResult result = default;
        var marker = Guid.NewGuid().ToString("N");
        var text = $"Wave-report marker {marker}: welded the airlock shut and flooded medbay";

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions.First();
            var system = server.System<ReportSystem>();
            result = system.Submit(session, "SomeGriefer", ReportCategory.Griefing, text);
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "A fresh, non-empty, under-limit report against someone else must succeed.");
            Assert.That(result.ReferenceId, Does.StartWith("SR-"), "Reference IDs are prefixed 'SR-'.");
            Assert.That(result.ErrorMessage, Is.Null);
        });

        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var path = Path.Combine(reportsDir, $"reports-{today}.jsonl");

        await server.WaitAssertion(() =>
        {
            Assert.That(File.Exists(path), Is.True, $"Expected JSONL file at {path} after a successful submission.");

            var matchingLine = File.ReadAllLines(path).LastOrDefault(l => l.Contains(marker));
            Assert.That(matchingLine, Is.Not.Null, $"No line in {path} contains marker {marker}.");

            using var doc = System.Text.Json.JsonDocument.Parse(matchingLine!);
            var root = doc.RootElement;

            Assert.That(root.GetProperty("referenceId").GetString(), Is.EqualTo(result.ReferenceId));
            Assert.That(root.GetProperty("reporterGuid").GetString(), Is.EqualTo(session.UserId.UserId.ToString()));
            Assert.That(root.GetProperty("category").GetString(), Is.EqualTo("griefing"));
            Assert.That(root.GetProperty("text").GetString(), Is.EqualTo(text));
            Assert.That(root.GetProperty("targetAsTyped").GetString(), Is.EqualTo("SomeGriefer"));
            // No connected session is named "SomeGriefer" -- target should be logged unresolved, not rejected.
            Assert.That(root.GetProperty("targetResolvedName").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null));
            Assert.That(root.GetProperty("map").GetString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task Submit_EmptyOrWhitespaceText_FailsWithoutWritingARecord()
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        ReportSubmitResult result = default;

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<ReportSystem>();
            result = system.Submit(session, "SomeGriefer", ReportCategory.Rules, "   ");
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ReferenceId, Is.Null);
            Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Submit_EmptyOrWhitespaceTarget_FailsWithoutWritingARecord()
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        ReportSubmitResult result = default;

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<ReportSystem>();
            result = system.Submit(session, "   ", ReportCategory.Rules, "Some valid reason text");
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ReferenceId, Is.Null);
            Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Submit_TargetingSelf_IsRefused()
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        ReportSubmitResult result = default;

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<ReportSystem>();
            // Reporting yourself by your own OOC username must resolve to yourself and be refused.
            result = system.Submit(session, session.Name, ReportCategory.Griefing, "Trying to report myself");
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "Self-reports must be refused.");
            Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Submit_TextLongerThanCap_IsTruncatedInTheStoredRecord()
    {
        var server = Server;
        var res = server.ResolveDependency<IResourceManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var reportsDir = ResolveReportsDir(res);

        var marker = Guid.NewGuid().ToString("N");
        var overlong = $"Wave-report marker {marker}: " + new string('x', ReportConstants.MaxTextLength + 200);
        ReportSubmitResult result = default;

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<ReportSystem>();
            result = system.Submit(session, "SomeGriefer", ReportCategory.Harassment, overlong);
        });

        Assert.That(result.Success, Is.True);

        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var path = Path.Combine(reportsDir, $"reports-{today}.jsonl");

        await server.WaitAssertion(() =>
        {
            var matchingLine = File.ReadAllLines(path).LastOrDefault(l => l.Contains(marker));
            Assert.That(matchingLine, Is.Not.Null);

            using var doc = System.Text.Json.JsonDocument.Parse(matchingLine!);
            var storedText = doc.RootElement.GetProperty("text").GetString();

            Assert.That(storedText!.Length, Is.LessThanOrEqualTo(ReportConstants.MaxTextLength));
            Assert.That(overlong.Length, Is.GreaterThan(ReportConstants.MaxTextLength), "Sanity: the input must actually exceed the cap.");
        });
    }

    [Test]
    public async Task Submit_PastPerRoundLimit_IsRateLimited()
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var marker = Guid.NewGuid().ToString("N");

        var results = new ReportSubmitResult[ReportConstants.MaxSubmissionsPerRound + 1];

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var system = server.System<ReportSystem>();

            for (var i = 0; i < results.Length; i++)
                results[i] = system.Submit(session, "SomeGriefer", ReportCategory.Rules, $"Wave-report rate-limit marker {marker} #{i}");
        });

        Assert.Multiple(() =>
        {
            for (var i = 0; i < ReportConstants.MaxSubmissionsPerRound; i++)
                Assert.That(results[i].Success, Is.True, $"Submission #{i} (within the per-round cap) should succeed.");

            var overLimit = results[^1];
            Assert.That(overLimit.Success, Is.False, "Submission past the per-round cap must be rejected.");
            Assert.That(overLimit.ErrorMessage, Is.Not.Null.And.Not.Empty);
        });
    }
}
