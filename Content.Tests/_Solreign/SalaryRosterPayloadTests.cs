using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.GameTicking;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._Solreign;

/// <summary>
///     SR-W-082: unit tests for the pure salary-roster payload builder
///     (<see cref="SalaryRosterPayload"/>). Mirrors <c>DirectorChannelCanonicalizationTests</c>'s
///     idiom of testing the pure/internal payload logic directly rather than standing up the ECS
///     system — <c>SolreignRoundOrchestrationSystem</c>'s network/DB glue is exercised
///     indirectly by construction: it delegates every decision tested here to this class.
/// </summary>
[TestFixture]
[TestOf(typeof(SalaryRosterPayload))]
public sealed class SalaryRosterPayloadTests
{
    private static RoundEndMessageEvent.RoundEndPlayerInfo MakePlayerInfo(
        Guid? userId,
        bool observer = false,
        bool connected = true)
    {
        return new RoundEndMessageEvent.RoundEndPlayerInfo
        {
            PlayerOOCName = "Test Subject",
            PlayerICName = "Test Subject",
            PlayerGuid = userId is { } id ? new NetUserId(id) : null,
            Role = "Passenger",
            JobPrototypes = Array.Empty<string>(),
            AntagPrototypes = Array.Empty<string>(),
            PlayerNetEntity = null,
            Antag = false,
            Observer = observer,
            Connected = connected,
        };
    }

    // --- CVar-off shape: byte-identical to the pre-SR-W-082 payload ---

    [Test]
    public void Build_NullRoster_OmitsRosterKeyEntirely()
    {
        var body = SalaryRosterPayload.Build(42, null);

        Assert.That(body, Is.EqualTo("{\"round_number\":42}"));
        Assert.That(body, Does.Not.Contain("roster"));
    }

    [Test]
    public void Build_NullRoster_MatchesPreSrW082SerializationByteForByte()
    {
        // The exact call site SolreignRoundOrchestrationSystem.OnRoundEnd used before SR-W-082.
        var legacy = JsonSerializer.Serialize(new { round_number = 42 });

        var body = SalaryRosterPayload.Build(42, null);

        Assert.That(body, Is.EqualTo(legacy));
    }

    // --- CVar-on shape: roster key present, even when empty ---

    [Test]
    public void Build_EmptyRosterList_RosterKeyStillPresent()
    {
        var body = SalaryRosterPayload.Build(7, new List<SalaryRosterPayload.RosterEntry>());

        Assert.That(body, Does.Contain("\"roster\":[]"));
    }

    [Test]
    public void Build_WithEntries_ProducesExpectedShape()
    {
        var roster = new List<SalaryRosterPayload.RosterEntry>
        {
            new("11111111-1111-1111-1111-111111111111", 3, true),
        };

        var body = SalaryRosterPayload.Build(7, roster);

        Assert.That(body, Is.EqualTo(
            "{\"round_number\":7,\"roster\":[{\"guid\":\"11111111-1111-1111-1111-111111111111\",\"rank_tier\":3,\"salary_eligible\":true}]}"));
    }

    // --- RankTier: 11-value CorporateRank ladder mapped monotonically onto 0-9 ---

    [TestCase(CorporateRank.ProbationaryAsset, 0)]
    [TestCase(CorporateRank.Associate, 1)]
    [TestCase(CorporateRank.SeniorAssociate, 2)]
    [TestCase(CorporateRank.Manager, 3)]
    [TestCase(CorporateRank.Director, 4)]
    [TestCase(CorporateRank.VicePresident, 5)]
    [TestCase(CorporateRank.BoardMember, 6)]
    [TestCase(CorporateRank.SeniorBoardMember, 7)]
    [TestCase(CorporateRank.ChiefExecutiveOfficer, 8)]
    [TestCase(CorporateRank.ChairmanOfTheBoard, 9)]
    [TestCase(CorporateRank.FounderEmeritus, 9)]
    public void RankTier_MapsEachCorporateRank(CorporateRank rank, int expectedTier)
    {
        Assert.That(SalaryRosterPayload.RankTier(rank), Is.EqualTo(expectedTier));
    }

    [Test]
    public void RankTier_IsMonotonicAcrossTheWholeLadder()
    {
        var ranks = Enum.GetValues<CorporateRank>().OrderBy(r => (int) r).ToArray();

        var previousTier = -1;
        foreach (var rank in ranks)
        {
            var tier = SalaryRosterPayload.RankTier(rank);
            Assert.That(tier, Is.GreaterThanOrEqualTo(previousTier), $"tier regressed at {rank}");
            Assert.That(tier, Is.InRange(0, 9), $"tier out of [0,9] at {rank}");
            previousTier = tier;
        }
    }

    // --- IsSalaryEligible: conservative presence signal ---

    [Test]
    public void IsSalaryEligible_ConnectedNonObserverWithGuid_IsTrue()
    {
        var info = MakePlayerInfo(Guid.NewGuid(), observer: false);
        Assert.That(SalaryRosterPayload.IsSalaryEligible(info), Is.True);
    }

    [Test]
    public void IsSalaryEligible_ObserverOnlySession_IsFalse()
    {
        var info = MakePlayerInfo(Guid.NewGuid(), observer: true);
        Assert.That(SalaryRosterPayload.IsSalaryEligible(info), Is.False);
    }

    [Test]
    public void IsSalaryEligible_NoAccountGuid_IsFalse()
    {
        var info = MakePlayerInfo(null, observer: false);
        Assert.That(SalaryRosterPayload.IsSalaryEligible(info), Is.False);
    }

    [Test]
    public void IsSalaryEligible_DisconnectedButPlayed_StillTrue()
    {
        // Presence this round is what matters, not whether the account is still connected at the
        // exact moment round-end fires — a player who played the whole shift and quit seconds
        // before the summary should not be denied salary for it.
        var info = MakePlayerInfo(Guid.NewGuid(), observer: false, connected: false);
        Assert.That(SalaryRosterPayload.IsSalaryEligible(info), Is.True);
    }

    // --- BuildEligibleEntries: end-to-end filter+map, GUIDs are NetUserIds not entity uids ---

    [Test]
    public void BuildEligibleEntries_ExcludesObserversAndGuidlessPlayers()
    {
        var eligibleGuid = Guid.NewGuid();
        var players = new (RoundEndMessageEvent.RoundEndPlayerInfo, CorporateRank)[]
        {
            (MakePlayerInfo(eligibleGuid, observer: false), CorporateRank.Manager),
            (MakePlayerInfo(Guid.NewGuid(), observer: true), CorporateRank.BoardMember),
            (MakePlayerInfo(null, observer: false), CorporateRank.Associate),
        };

        var entries = SalaryRosterPayload.BuildEligibleEntries(players);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Guid, Is.EqualTo(eligibleGuid.ToString()));
        Assert.That(entries[0].RankTier, Is.EqualTo(3));
        Assert.That(entries[0].SalaryEligible, Is.True);
    }

    [Test]
    public void BuildEligibleEntries_GuidIsTheAccountNetUserId_NotAnEntityIdentifier()
    {
        // The exact bug already fixed once in rivalry telemetry (v11 backbone #3): cross-round
        // identity must key off the account GUID, never the round-scoped entity/mob. Assert the
        // emitted guid is literally the NetUserId's own GUID string, independent of any entity.
        var accountGuid = Guid.NewGuid();
        var info = MakePlayerInfo(accountGuid, observer: false);

        var entries = SalaryRosterPayload.BuildEligibleEntries(new[] { (info, CorporateRank.Associate) });

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Guid, Is.EqualTo(accountGuid.ToString()));
        Assert.That(Guid.TryParse(entries[0].Guid, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(accountGuid));
    }

    [Test]
    public void BuildEligibleEntries_EmptyInput_ProducesEmptyRoster()
    {
        var entries = SalaryRosterPayload.BuildEligibleEntries(
            Array.Empty<(RoundEndMessageEvent.RoundEndPlayerInfo, CorporateRank)>());

        Assert.That(entries, Is.Empty);
    }
}
