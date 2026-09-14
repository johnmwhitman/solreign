#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     FD-W3 wire contract (spec §5.1): <see cref="FirstDeathCryptReport"/> serializes to EXACTLY
///     the JSON the daemon's <c>CryptDeathEvent</c> pydantic model parses — snake_case field names,
///     the legacy pair present (empty attacker), <c>first_death: true</c>, and the additive fields.
///     Pure, no harness. Also pins the §8D closed cause-label map (display-only, no forensic or
///     attacker vocabulary) that <see cref="FirstDeathCopy.CauseLabelFor"/> ships on this payload.
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathCryptReport))]
public sealed class FirstDeathCryptReportTests
{
    private static readonly Guid Victim = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");

    private static readonly FirstDeathCause[] AllCauses =
        (FirstDeathCause[]) Enum.GetValues(typeof(FirstDeathCause));

    // --- Build: field correctness ------------------------------------------------------------------

    [Test]
    public void Build_ComposesTheClaimedSceneSnapshot()
    {
        var report = FirstDeathCryptReport.Build(
            Victim, "Juno Pike", "Day-one orientation complete.", FirstDeathCause.Vacuum,
            tours: 0, title: "Probationary Asset");

        Assert.Multiple(() =>
        {
            Assert.That(report.VictimGuid, Is.EqualTo(Victim.ToString()));
            Assert.That(report.AttackerGuid, Is.Empty,
                "redaction law: the first-death surface never carries attacker data");
            Assert.That(report.FirstDeath, Is.True);
            Assert.That(report.CharacterName, Is.EqualTo("Juno Pike"));
            Assert.That(report.Epitaph, Is.EqualTo("Day-one orientation complete."));
            Assert.That(report.CauseLabel, Is.EqualTo("Environmental / atmospheric event"));
            Assert.That(report.Tours, Is.EqualTo(0));
            Assert.That(report.Title, Is.EqualTo("Probationary Asset"));
        });
    }

    // --- Serialization: the daemon-facing byte contract ---------------------------------------------

    [Test]
    public void Serialized_UsesTheDaemonsSnakeCaseFieldNames_AndCarriesTheLegacyPair()
    {
        var report = FirstDeathCryptReport.Build(
            Victim, "Juno Pike", "Out of scope for life support.", FirstDeathCause.Vacuum,
            tours: 3, title: "Amortized Asset");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(report));
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            // The exact property set — a rename here is a daemon contract break, not a refactor.
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.That(names, Is.EquivalentTo(new[]
            {
                "victim_guid", "attacker_guid", "first_death", "character_name",
                "epitaph", "cause_label", "tours", "title",
            }));

            Assert.That(root.GetProperty("victim_guid").GetString(), Is.EqualTo(Victim.ToString()));
            Assert.That(root.GetProperty("attacker_guid").GetString(), Is.Empty);
            Assert.That(root.GetProperty("first_death").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("character_name").GetString(), Is.EqualTo("Juno Pike"));
            Assert.That(root.GetProperty("epitaph").GetString(), Is.EqualTo("Out of scope for life support."));
            Assert.That(root.GetProperty("cause_label").GetString(), Is.EqualTo("Environmental / atmospheric event"));
            Assert.That(root.GetProperty("tours").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("Amortized Asset"));
        });
    }

    // --- The §8D closed cause-label map --------------------------------------------------------------

    [Test]
    public void CauseLabelMap_MatchesTheSpecsClosedDisplayMap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Violence),
                Is.EqualTo("Third-party liability event"));
            Assert.That(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Vacuum),
                Is.EqualTo("Environmental / atmospheric event"));
            Assert.That(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Burn),
                Is.EqualTo("Thermal compliance event"));
            Assert.That(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Misadventure),
                Is.EqualTo("Misadventure"));
            Assert.That(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Unknown),
                Is.EqualTo("Pending classification"));
        });
    }

    [Test]
    public void CauseLabelMap_IsTotal_NonEmpty_AndFreeOfAttackerVocabulary()
    {
        // The same denylist discipline the copy-pack tests apply: a display label may never leak
        // forensic or attacker facts (spec §3.3 confidentiality rule).
        string[] denylist = { "killer", "murder", "attacker", "syndicate", "agent", "weapon", "gun", "knife" };

        foreach (var cause in AllCauses)
        {
            var label = FirstDeathCopy.CauseLabelFor(cause);
            Assert.That(label, Is.Not.Empty, $"cause {cause} must map to a label");
            Assert.That(denylist.Where(term => label.Contains(term, StringComparison.OrdinalIgnoreCase)),
                Is.Empty, $"label for {cause} leaks redacted vocabulary: \"{label}\"");
        }
    }

    [Test]
    public void EveryCauseLabel_RoundTripsThroughBuild()
    {
        foreach (var cause in AllCauses)
        {
            var report = FirstDeathCryptReport.Build(Victim, "n", "e", cause, 1, "t");
            Assert.That(report.CauseLabel, Is.EqualTo(FirstDeathCopy.CauseLabelFor(cause)));
        }
    }
}
