#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for the deterministic epitaph plate picker (spec §3.5/§8B/§7): determinism (same
///     inputs → same plate, to the character), family selection by tours, cause override only when
///     tours &gt; 0, the 90-char cap's short-variant fallback, every plate id resolvable, and the
///     closed-vocabulary rail as a test (no {name} on any plate; no attacker/weapon/location
///     vocabulary anywhere in the library).
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathEpitaphPicker))]
public sealed class FirstDeathEpitaphPickerTests
{
    private static readonly FirstDeathCause[] AllCauses =
        (FirstDeathCause[]) Enum.GetValues(typeof(FirstDeathCause));

    [Test]
    public void SameInputs_SamePlate_Forever()
    {
        foreach (var cause in AllCauses)
        {
            foreach (var tours in new[] { 0, 1, 3, 5, 12, 19, 20, 100 })
            {
                var first = FirstDeathEpitaphPicker.Pick(tours, cause, "Probationary Asset");
                var second = FirstDeathEpitaphPicker.Pick(tours, cause, "Probationary Asset");
                Assert.That(second, Is.EqualTo(first),
                    $"an engraving must be deterministic (tours={tours}, cause={cause})");
            }
        }
    }

    [Test]
    public void DayOneDeaths_AlwaysStayOrientation_NoCauseOverride()
    {
        // "Death-on-day-one is the joke" — tours == 0 must yield an ORIENTATION plate (01/02) for
        // EVERY cause; the cause plate never enters the pool.
        foreach (var cause in AllCauses)
        {
            var epitaph = FirstDeathEpitaphPicker.Pick(0, cause, "Probationary Asset");
            Assert.That(epitaph.Id, Is.EqualTo("01").Or.EqualTo("02"),
                $"a tours=0 death must engrave an ORIENTATION plate (got {epitaph.Id} for {cause})");
        }
    }

    [TestCase(1, new[] { "03", "04", "04s" })]
    [TestCase(4, new[] { "03", "04", "04s" })]
    [TestCase(5, new[] { "05", "05s", "06" })]
    [TestCase(19, new[] { "05", "05s", "06" })]
    [TestCase(20, new[] { "07", "07s", "08" })]
    [TestCase(500, new[] { "07", "07s", "08" })]
    public void FamilySelection_ByTours_PlusCausePlate(int tours, string[] familyIds)
    {
        // With tours > 0 the pool is the tours family PLUS the cause plate — every pick must land in
        // exactly that closed set.
        var causeIds = new[] { "09", "10", "11", "12", "13" };
        foreach (var cause in AllCauses)
        {
            var epitaph = FirstDeathEpitaphPicker.Pick(tours, cause, "Probationary Asset");
            Assert.That(familyIds.Concat(causeIds), Does.Contain(epitaph.Id),
                $"tours={tours}, cause={cause} picked plate {epitaph.Id} outside its candidate pool");
        }
    }

    [Test]
    public void CausePlates_AreReachable_WhenToursPositive()
    {
        // The cause plate must actually be selectable (not just theoretically pooled): sweep the
        // deterministic index over many tours/title-length combinations and demand at least one hit
        // per cause plate.
        var seen = new HashSet<string>();
        foreach (var cause in AllCauses)
        {
            for (var tours = 1; tours <= 60; tours++)
            {
                foreach (var title in new[] { "A", "AB", "ABC" })
                {
                    seen.Add(FirstDeathEpitaphPicker.Pick(tours, cause, title).Id);
                }
            }
        }

        Assert.Multiple(() =>
        {
            foreach (var causeId in new[] { "09", "10", "11", "12", "13" })
            {
                Assert.That(seen, Does.Contain(causeId), $"cause plate {causeId} is unreachable");
            }
        });
    }

    [Test]
    public void TitleIsRendered_IntoTitledPlates()
    {
        // Find a combination that yields a {title} plate and confirm the substitution happened.
        for (var tours = 1; tours <= 60; tours++)
        {
            var epitaph = FirstDeathEpitaphPicker.Pick(tours, FirstDeathCause.Unknown, "Chain Closer");
            if (epitaph.Id is "04" or "05" or "07")
            {
                Assert.That(epitaph.Text, Does.StartWith("Chain Closer."));
                Assert.That(epitaph.Text, Does.Not.Contain("{title}"), "the variable must be substituted");
                return;
            }
        }

        Assert.Fail("no titled plate was ever selected across the sweep — the pool construction is broken");
    }

    [Test]
    public void NinetyCharCap_FallsBackToShortVariant()
    {
        // A pathologically long title pushes any {title} plate over 90 chars; the pick must fall
        // back to the starred short variant (which carries no {title} at all).
        var longTitle = new string('X', 120);

        var sawShort = false;
        for (var tours = 1; tours <= 60 && !sawShort; tours++)
        {
            foreach (var cause in AllCauses)
            {
                var epitaph = FirstDeathEpitaphPicker.Pick(tours, cause, longTitle);
                Assert.That(epitaph.Text.Length, Is.LessThanOrEqualTo(FirstDeathEpitaphPicker.PlateMaxLength),
                    $"plate {epitaph.Id} rendered over the 90-char cap");
                if (epitaph.Id.EndsWith("s", StringComparison.Ordinal))
                {
                    sawShort = true;
                    Assert.That(epitaph.Text, Does.Not.Contain(longTitle),
                        "the short variant must not interpolate the title");
                }
            }
        }

        Assert.That(sawShort, Is.True, "the sweep never exercised a short-variant fallback");
    }

    [Test]
    public void EveryPlateId_IsResolvable()
    {
        var ids = new List<string>();
        foreach (var plate in FirstDeathEpitaphPicker.AllPlates())
        {
            ids.Add(plate.Id);
            if (plate.ShortId is { } shortId)
                ids.Add(shortId);
        }

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Unique, "plate ids must be unique — they are persisted keys");
            foreach (var id in ids)
            {
                Assert.That(FirstDeathEpitaphPicker.TryGetPlateTemplate(id, out var template), Is.True,
                    $"plate id {id} must resolve");
                Assert.That(template, Is.Not.Empty);
            }
        });

        Assert.That(FirstDeathEpitaphPicker.TryGetPlateTemplate("nonexistent", out _), Is.False);
    }

    [Test]
    public void ClosedVocabulary_NoNameOnAnyPlate_NoAttackerWordsAnywhere()
    {
        // The confidentiality rail as a test (spec §3.3): the template set itself enforces the
        // redaction. {title} is the ONLY variable; no plate may carry {name} or any
        // attacker/weapon/location vocabulary.
        var denylist = new[]
        {
            "attacker", "killer", "murder", "assailant", "syndicate", "weapon",
            "gun", "knife", "shot by", "stabbed", "location",
        };

        Assert.Multiple(() =>
        {
            foreach (var plate in FirstDeathEpitaphPicker.AllPlates())
            {
                foreach (var template in new[] { plate.Template, plate.ShortTemplate })
                {
                    if (template is null)
                        continue;

                    Assert.That(template, Does.Not.Contain("{name}"),
                        $"plate {plate.Id}: never put {{name}} on the plaque — the crypt UI header is the headstone");

                    var stripped = template.Replace("{title}", string.Empty);
                    Assert.That(stripped, Does.Not.Contain("{"),
                        $"plate {plate.Id}: {{title}} is the only permitted variable");

                    foreach (var word in denylist)
                    {
                        Assert.That(template.ToLowerInvariant(), Does.Not.Contain(word),
                            $"plate {plate.Id} contains denylisted vocabulary: {word}");
                    }
                }
            }
        });
    }
}
