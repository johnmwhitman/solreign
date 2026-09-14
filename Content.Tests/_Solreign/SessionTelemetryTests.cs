using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Content.Server._Solreign.SessionTelemetry;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure tests for the session-telemetry ledger (ASK-3): the hash's leak properties, the
///     derived math the pre-registered criteria consume, and the closed line vocabulary.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class SessionTelemetryTests
{
    private static readonly Guid KnownUser = Guid.Parse("a1b2c3d4-e5f6-4a0b-8c0d-1e2f3a4b5c6d");

    // --- Hash properties ---

    [Test]
    public void HashIs16LowercaseHex()
    {
        var h = SessionTelemetryHash.AcctHash("pepper-1", KnownUser);
        Assert.That(h, Does.Match("^[0-9a-f]{16}$"));
    }

    [Test]
    public void HashIsDeterministicUnderOnePepper()
    {
        Assert.That(
            SessionTelemetryHash.AcctHash("pepper-1", KnownUser),
            Is.EqualTo(SessionTelemetryHash.AcctHash("pepper-1", KnownUser)));
    }

    [Test]
    public void DifferentPepperUnlinksTheHash()
    {
        Assert.That(
            SessionTelemetryHash.AcctHash("pepper-1", KnownUser),
            Is.Not.EqualTo(SessionTelemetryHash.AcctHash("pepper-2", KnownUser)));
    }

    [Test]
    public void EmptyOrWhitespacePepperRefusesToHash()
    {
        Assert.Throws<ArgumentException>(() => SessionTelemetryHash.AcctHash("", KnownUser));
        Assert.Throws<ArgumentException>(() => SessionTelemetryHash.AcctHash("   ", KnownUser));
    }

    // --- Leak suite: the serialized line never contains the raw account id ---

    [Test]
    public void LineNeverContainsTheRawUserId()
    {
        var scratch = NewScratch("pepper-1", KnownUser);
        scratch.ActiveSamples = 3;
        scratch.NoteRound(142);

        var line = SessionTelemetryJsonl.ToLine(scratch, scratch.StartedAtUtc.AddMinutes(5), 30);

        var lower = line.ToLowerInvariant();
        foreach (var form in new[] { KnownUser.ToString("N"), KnownUser.ToString("D") })
            Assert.That(lower, Does.Not.Contain(form.ToLowerInvariant()),
                $"raw user id (form '{form}') leaked into the JSONL line");
    }

    [Test]
    public void LineVocabularyIsClosed()
    {
        var allowed = new HashSet<string>
        {
            "v", "ts_start", "ts_end", "session_id", "acct", "dur_s", "active_s", "pct_active",
            "samples", "active_samples", "interval_s", "lobby", "spawned", "ready_n", "rounds",
            "max_human_peers", "admin_aboard",
        };

        var names = typeof(SessionTelemetryJsonLine).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .ToList();

        Assert.That(names, Is.SubsetOf(allowed),
            "a new property joined the telemetry line outside the closed vocabulary — " +
            "privacy review required before extending it");
    }

    // --- Derived math the pre-registered criteria consume ---

    [Test]
    public void ActiveSecondsAndPctDeriveFromSamples()
    {
        var scratch = NewScratch("pepper-1", KnownUser);
        scratch.ActiveSamples = 80;
        scratch.IdleSamples = 20;

        var line = SessionTelemetryJsonl.ToLine(scratch, scratch.StartedAtUtc.AddMinutes(50), 30);

        Assert.That(line, Does.Contain("\"active_s\":2400"));
        Assert.That(line, Does.Contain("\"pct_active\":0.8"));
        Assert.That(line, Does.Contain("\"samples\":100"));
    }

    [Test]
    public void ZeroSamplesMeansZeroPctNotDivisionByZero()
    {
        var scratch = NewScratch("pepper-1", KnownUser);
        var line = SessionTelemetryJsonl.ToLine(scratch, scratch.StartedAtUtc, 30);
        Assert.That(line, Does.Contain("\"pct_active\":0"));
    }

    [Test]
    public void TrueSoloRequiresNoPeersAndNoAdmin()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SessionTelemetryJsonl.TrueSolo(0, false), Is.True);
            Assert.That(SessionTelemetryJsonl.TrueSolo(1, false), Is.False);
            Assert.That(SessionTelemetryJsonl.TrueSolo(0, true), Is.False);
            Assert.That(SessionTelemetryJsonl.TrueSolo(2, true), Is.False);
        });
    }

    [Test]
    public void RoundZeroIsNeverRecorded()
    {
        var scratch = NewScratch("pepper-1", KnownUser);
        scratch.NoteRound(0);
        scratch.NoteRound(142);
        scratch.NoteRound(142);
        scratch.NoteRound(143);

        Assert.That(scratch.Rounds, Is.EquivalentTo(new[] { 142, 143 }));
    }

    [Test]
    public void ReadyFlipsCountTransitionsNotObservations()
    {
        var scratch = NewScratch("pepper-1", KnownUser);
        scratch.NoteReady(false); // first observation — not a flip
        scratch.NoteReady(false);
        scratch.NoteReady(true);  // flip 1
        scratch.NoteReady(true);
        scratch.NoteReady(false); // flip 2

        Assert.That(scratch.ReadyTransitions, Is.EqualTo(2));
    }

    private static SessionTelemetryScratch NewScratch(string pepper, Guid user)
        => new(SessionTelemetryHash.AcctHash(pepper, user), DateTimeOffset.UtcNow);
}
