using System;
using Content.Server._Solreign.Providence;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceReactiveDeathMemory))]
public sealed class ProvidenceReactiveDeathMemoryTests
{
    private static FirstDeathRecord MakeRecord(int roundId) => new(
        User: Guid.NewGuid(),
        RoundId: roundId,
        CharacterName: "Test Dummy",
        Cause: "VIOLENCE",
        ToursAtDeath: 3,
        TitleAtDeath: "Seasoned Asset",
        EpitaphId: "epitaph-1",
        DiedAtUtc: "2026-07-22T00:00:00Z",
        RehireShown: true,
        CryptReported: true);

    [Test]
    public void NullRecord_IsNotEligible_LineDegradesToSilence()
    {
        // The "no history" case — an account with no first-death record at all. There is nothing to
        // remember, so the memory line must never fire (the caller falls back to Generic/Repeat).
        Assert.That(ProvidenceReactiveDeathMemory.IsEligible(null, currentRoundId: 5), Is.False);
    }

    [Test]
    public void RecordFromAnEarlierRound_IsEligible()
    {
        var record = MakeRecord(roundId: 4);

        Assert.That(ProvidenceReactiveDeathMemory.IsEligible(record, currentRoundId: 5), Is.True);
    }

    [Test]
    public void RecordFromTheCurrentRound_IsNotEligible_SameRoundRaceGuard()
    {
        // A record claimed in the SAME round currently in progress is (or may be) the very death that
        // just happened — citing it as "memory" would be citing the present as the past. Must not be
        // eligible, sidestepping the race with ProvidenceFirstDeathSystem's own same-tick claim.
        var record = MakeRecord(roundId: 5);

        Assert.That(ProvidenceReactiveDeathMemory.IsEligible(record, currentRoundId: 5), Is.False);
    }

    [Test]
    public void RecordFromAMuchEarlierRound_IsStillEligible()
    {
        var record = MakeRecord(roundId: 1);

        Assert.That(ProvidenceReactiveDeathMemory.IsEligible(record, currentRoundId: 500), Is.True);
    }
}
