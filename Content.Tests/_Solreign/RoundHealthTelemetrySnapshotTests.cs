using System.Text.Json;
using Content.Server.Administration;
using NUnit.Framework;

#nullable enable

namespace Content.Tests._Solreign;

/// <summary>
/// Behavioral/serialization tests for the authenticated round-health telemetry snapshot
/// exposed via /admin/info. Verifies the DTO carries every field needed to distinguish
/// auto-call timer stall, shuttle-arrival completion stall, and explicit delayroundend.
/// </summary>
[TestFixture]
[TestOf(typeof(RoundHealthSnapshot))]
public sealed class RoundHealthTelemetrySnapshotTests
{
    /// <summary>
    /// A fully-populated snapshot (InRound, auto-call overdue, shuttle not arrived)
    /// must round-trip through System.Text.Json with all fields intact and correctly named.
    /// </summary>
    [Test]
    public void Serialize_AllFieldsPresent_RoundTrip()
    {
        var snapshot = new RoundHealthSnapshot
        {
            RunLevel = "InRound",
            AutoCallDelayMinutes = 90,
            AutoCallElapsedMinutes = 106.5,
            AutoCallAttempted = false,
            RoundEndRequested = false,
            CountdownRemainingSeconds = null,
            EmergencyShuttleArrived = false,
            ShuttlesLeft = false,
            RoundEndCompletionTokenArmed = true
        };

        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<RoundHealthSnapshot>(json)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized.RunLevel, Is.EqualTo("InRound"));
            Assert.That(deserialized.AutoCallDelayMinutes, Is.EqualTo(90));
            Assert.That(deserialized.AutoCallElapsedMinutes, Is.EqualTo(106.5));
            Assert.That(deserialized.AutoCallAttempted, Is.False);
            Assert.That(deserialized.RoundEndRequested, Is.False);
            Assert.That(deserialized.CountdownRemainingSeconds, Is.Null);
            Assert.That(deserialized.EmergencyShuttleArrived, Is.False);
            Assert.That(deserialized.ShuttlesLeft, Is.False);
            Assert.That(deserialized.RoundEndCompletionTokenArmed, Is.True);
        }
    }

    /// <summary>
    /// The JSON output must contain every required field name so an external consumer
    /// (Director daemon) can distinguish stall modes.
    /// </summary>
    [Test]
    public void Serialize_ContainsAllRequiredFieldNames()
    {
        var snapshot = new RoundHealthSnapshot
        {
            RunLevel = "InRound",
            AutoCallDelayMinutes = 45,
            AutoCallElapsedMinutes = 50.0,
            AutoCallAttempted = true,
            RoundEndRequested = true,
            CountdownRemainingSeconds = -120.0,
            EmergencyShuttleArrived = true,
            ShuttlesLeft = true,
            RoundEndCompletionTokenArmed = false
        };

        var json = JsonSerializer.Serialize(snapshot);
        using var doc = JsonDocument.Parse(json);

        // Every required field must be present in the JSON.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.RootElement.TryGetProperty("RunLevel", out _), Is.True, "Missing RunLevel");
            Assert.That(doc.RootElement.TryGetProperty("AutoCallDelayMinutes", out _), Is.True, "Missing AutoCallDelayMinutes");
            Assert.That(doc.RootElement.TryGetProperty("AutoCallElapsedMinutes", out _), Is.True, "Missing AutoCallElapsedMinutes");
            Assert.That(doc.RootElement.TryGetProperty("AutoCallAttempted", out _), Is.True, "Missing AutoCallAttempted");
            Assert.That(doc.RootElement.TryGetProperty("RoundEndRequested", out _), Is.True, "Missing RoundEndRequested");
            Assert.That(doc.RootElement.TryGetProperty("CountdownRemainingSeconds", out _), Is.True, "Missing CountdownRemainingSeconds");
            Assert.That(doc.RootElement.TryGetProperty("EmergencyShuttleArrived", out _), Is.True, "Missing EmergencyShuttleArrived");
            Assert.That(doc.RootElement.TryGetProperty("ShuttlesLeft", out _), Is.True, "Missing ShuttlesLeft");
            Assert.That(doc.RootElement.TryGetProperty("RoundEndCompletionTokenArmed", out _), Is.True, "Missing RoundEndCompletionTokenArmed");
        }
    }

    /// <summary>
    /// When auto-call is disabled (CVar 0), AutoCallDelayMinutes must be null,
    /// and AutoCallElapsedMinutes must also be null — so a consumer can distinguish
    /// "timer disabled" from "timer running but not yet elapsed."
    /// </summary>
    [Test]
    public void Serialize_AutoCallDisabled_NullFields()
    {
        var snapshot = new RoundHealthSnapshot
        {
            RunLevel = "InRound",
            AutoCallDelayMinutes = null,
            AutoCallElapsedMinutes = null,
            AutoCallAttempted = false,
            RoundEndRequested = false,
            CountdownRemainingSeconds = null,
            EmergencyShuttleArrived = false,
            ShuttlesLeft = false,
            RoundEndCompletionTokenArmed = true
        };

        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<RoundHealthSnapshot>(json)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized.AutoCallDelayMinutes, Is.Null);
            Assert.That(deserialized.AutoCallElapsedMinutes, Is.Null);
        }
    }

    /// <summary>
    /// Overdue countdown (remaining seconds negative) must survive round-trip
    /// so a consumer can distinguish "request present, time remaining" from
    /// "request present, time overdue."
    /// </summary>
    [Test]
    public void Serialize_OverdueCountdown_NegativeRemaining()
    {
        var snapshot = new RoundHealthSnapshot
        {
            RunLevel = "InRound",
            AutoCallDelayMinutes = 90,
            AutoCallElapsedMinutes = 95.0,
            AutoCallAttempted = true,
            RoundEndRequested = true,
            CountdownRemainingSeconds = -300.5,
            EmergencyShuttleArrived = true,
            ShuttlesLeft = false,
            RoundEndCompletionTokenArmed = true
        };

        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<RoundHealthSnapshot>(json)!;

        Assert.That(deserialized.CountdownRemainingSeconds, Is.EqualTo(-300.5));
    }
}