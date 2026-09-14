#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Chat.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     ECS wiring for the "social cheap adds" package (council memo
///     docs/council/2026-07-16-design-magnetism.md item 5): the SolreignChirp greet emote, the
///     once-per-account social-first milestone toasts, and the third-visit wingmate volunteer
///     prompt. The pure pairing/resolution/claim rules are exhaustively unit-tested without a
///     server (Content.Tests/_Solreign/ChirpAnswerTrackerTests, HandOffTrackerTests,
///     SocialFirstsStoreTests) — this file only covers what those cannot reach:
///       * the emote prototype actually loads, is radial-discoverable, keeps the global
///         trigger-word-uniqueness law, and is usable by the real connected player's mob;
///       * a real emote through the real chat system reaches SolreignSocialFirstsSystem's
///         subscription (mind resolution + in-round gating included);
///       * real hand equip/unequip events feed the hand-off tracker, and a self-pickup never
///         awards;
///       * the claim→deliver path lands a social_firsts row exactly once EVER against the SAME
///         SQLite file the live system opens (resolved through <see cref="SeasonLedgerDbPath.Resolve"/>,
///         the law every ledger consumer follows);
///       * the wingmate prompt fires once ever, only at career Tours >= 3, through the real
///         PlayerSpawnCompleteEvent path.
///
///     Every test resets the systems' round/process state up front (the
///     ProvidenceFirstShiftWelcome rationale): the pool-connected session already went through a
///     REAL spawn during setup with this feature's CVars at their production default (on), so
///     trackers may hold state the test never asked for.
/// </summary>
[TestFixture]
public sealed class SolreignSocialCheapAddsIntegrationTest : GameTest
{
    // Dirty: flips CVars, writes directly to the per-instance ledger DB, fires synthetic spawn
    // events at the real connected session — never hand this server back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // Real attached body required (HotPotato/SeasonLedger/Welcome precedent).
        DummyTicker = false,
    };

    private const string ChirpEmoteId = "SolreignChirp";

    /// <summary>Every loc key the feature's copy pack ships — one missing key is a silent
    /// "solreign-..." literal in a player's face.</summary>
    private static readonly string[] CopyKeys =
    {
        "chat-emote-name-solreign-chirp",
        "chat-emote-msg-solreign-chirp",
        "solreign-award-milestone-popup",
        "solreign-social-first-chirp-answered-reason",
        "solreign-social-first-chirp-answered-chat",
        "solreign-social-first-healed-reason",
        "solreign-social-first-healed-chat",
        "solreign-social-first-item-received-reason",
        "solreign-social-first-item-received-chat",
        "solreign-wingmate-prompt-popup",
        "solreign-wingmate-prompt-chat",
    };

    /// <summary>Same resolver the live systems use — the test must open exactly the file they
    /// write (in tests: the unique per-instance temp path the pool injects).</summary>
    private string ResolveLedgerDbPath()
    {
        return SeasonLedgerDbPath.Resolve(
            Server.ResolveDependency<IConfigurationManager>(),
            Server.ResolveDependency<IResourceManager>());
    }

    /// <summary>Fresh round ids per run — the ledger's round-envelope replay protection rejects a
    /// repeated (round id, account) pair the moment the same db sees a second run.</summary>
    private static int UniqueRoundId()
    {
        return Random.Shared.Next(100_000, int.MaxValue - 16);
    }

    /// <summary>Bounded poll for async-void completion — ticks the server between attempts so
    /// Update loops drain; never sleep-and-pray.</summary>
    private async Task PollUntilAsync(Func<Task<bool>> predicate, string failureMessage, int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await predicate())
                return;

            await Server.WaitRunTicks(1);
        }

        Assert.Fail(failureMessage);
    }

    private (EntityUid Mob, ICommonSession Session, Guid Account) GetPlayer()
    {
        var session = ServerSession
                      ?? throw new InvalidOperationException("Pool must provide a connected session.");
        var mob = session.AttachedEntity
                  ?? throw new InvalidOperationException(
                      "Connected player has no AttachedEntity -- DummyTicker must be false.");
        return (mob, session, session.UserId.UserId);
    }

    private static PlayerSpawnCompleteEvent MakeSpawnEvent(EntityUid mob, ICommonSession session, bool silent = false)
    {
        return new PlayerSpawnCompleteEvent(
            mob,
            session,
            jobId: "Passenger",
            lateJoin: false,
            silent: silent,
            joinOrder: 1,
            station: EntityUid.Invalid,
            profile: new HumanoidCharacterProfile());
    }

    [Test]
    public async Task CVarDefaultsOn_AndEveryCopyKeyResolves()
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var loc = Server.ResolveDependency<ILocalizationManager>();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(cfg.GetCVar(CCVars.SolreignSocialCheapAdds), Is.True,
                    "solreign.social_cheap_adds ships default TRUE (inert without other players — see the CVar doc).");

                foreach (var key in CopyKeys)
                {
                    Assert.That(loc.HasString(key), Is.True,
                        $"Copy key '{key}' must exist in social-cheap-adds.ftl — a missing key renders as a raw literal.");
                }
            });
        });
    }

    [Test]
    public async Task ChirpEmote_WellFormed_TriggerspaceUnique_AndUsableByThePlayerMob()
    {
        var (mob, _, _) = GetPlayer();
        var chat = Server.System<ChatSystem>();

        await Server.WaitAssertion(() =>
        {
            var proto = SProtoMan.Index<EmotePrototype>(ChirpEmoteId);

            Assert.Multiple(() =>
            {
                Assert.That(proto.Category, Is.EqualTo(EmoteCategory.Vocal),
                    "the chirp is a vocal emote — that is what puts it on the vocal radial page");
                Assert.That(proto.Available, Is.True,
                    "menu-discoverable is the whole point: the emote must be generally available");
                Assert.That(proto.ChatMessages, Is.Not.Empty,
                    "an emote with no chat message renders as nothing");
                Assert.That(proto.Whitelist, Is.Not.Null,
                    "the chirp is whitelisted to Vocal bodies like every general vocal emote");

                // The CacheEmotes law: trigger words must be unique across ALL emote prototypes —
                // a duplicate is only a runtime Log.Error, so pin it here where it fails loudly.
                var seen = new Dictionary<string, string>();
                foreach (var emote in SProtoMan.EnumeratePrototypes<EmotePrototype>())
                {
                    foreach (var word in emote.ChatTriggers)
                    {
                        var lower = word.ToLowerInvariant();
                        Assert.That(seen.TryAdd(lower, emote.ID), Is.True,
                            $"Emote trigger '{lower}' is claimed by both {seen.GetValueOrDefault(lower)} and {emote.ID}.");
                    }
                }

                Assert.That(SEntMan.HasComponent<HumanoidProfileComponent>(mob), Is.True,
                    "the milestone subscription keys on HumanoidProfileComponent — the real player mob must carry it");
                Assert.That(chat.AllowedToUseEmote(mob, proto), Is.True,
                    "the real connected player's mob (Vocal, non-silicon) must be allowed to chirp");
            });
        });
    }

    [Test]
    public async Task ChirpEmote_ThroughTheRealChatSystem_RecordsAPendingChirp()
    {
        var (mob, _, _) = GetPlayer();
        var chat = Server.System<ChatSystem>();
        var social = Server.System<SolreignSocialFirstsSystem>();

        await Server.WaitPost(() => social.ResetStateForTests());

        await Server.WaitPost(() =>
        {
            Assert.That(chat.TryEmoteWithChat(mob, ChirpEmoteId), Is.True,
                "the real chat system must perform the SolreignChirp emote for the player mob");
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(social.PendingChirpCountForTests, Is.EqualTo(1),
                "one real chirp through the real emote event must leave exactly one pending chirp " +
                "(proves the EmoteEvent subscription, mind resolution, and in-round gating end-to-end)");
        });
    }

    [Test]
    public async Task HandOff_DropAndSelfPickup_TracksButNeverAwards()
    {
        var (mob, _, account) = GetPlayer();
        var hands = Server.System<Content.Server.Hands.Systems.HandsSystem>();
        var social = Server.System<SolreignSocialFirstsSystem>();

        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            social.ResetStateForTests();

            var coords = SEntMan.GetComponent<TransformComponent>(mob).Coordinates;
            item = SEntMan.SpawnEntity("Crowbar", coords);

            Assert.That(SEntMan.HasComponent<HandsComponent>(mob), Is.True,
                "the player mob must have hands for the transfer surface");
            Assert.That(hands.TryForcePickupAnyHand(mob, item), Is.True,
                "setup: the mob must be able to pick the item up");
        });

        await Server.WaitRunTicks(1);

        // The release: a real drop through the real hands system raises DidUnequipHandEvent.
        await Server.WaitPost(() =>
        {
            Assert.That(hands.TryDrop(mob, item), Is.True, "setup: the drop must succeed");
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(social.PendingHandOffCountForTests, Is.EqualTo(1),
                "a real drop must leave exactly one in-flight release (proves the DidUnequipHandEvent wire)");
        });

        // Self-pickup: consumes the release (DidEquipHandEvent wire), awards nothing — taking your
        // own item back is housekeeping, not a gift.
        await Server.WaitPost(() =>
        {
            Assert.That(hands.TryForcePickupAnyHand(mob, item), Is.True,
                "setup: the mob must be able to pick its own item back up");
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(social.PendingHandOffCountForTests, Is.Zero,
                "the self-pickup must consume the release (proves the DidEquipHandEvent wire)");
        });

        var store = new SeasonLedgerStore(ResolveLedgerDbPath());
        var flags = await store.GetSocialFirstFlagsAsync(account);
        Assert.That(flags, Does.Not.Contain(SolreignSocialFirstFlags.ItemReceived),
            "a same-account round trip must never claim the item_received milestone");

        // Cleanup: QueueDel then a tick, the pooled-server law.
        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(item));
        await Server.WaitRunTicks(1);
    }

    [Test]
    public async Task SocialFirstClaim_LandsExactlyOnceEver_InTheLiveLedgerFile()
    {
        var (_, _, account) = GetPlayer();
        var social = Server.System<SolreignSocialFirstsSystem>();
        var dbPath = ResolveLedgerDbPath();

        await Server.WaitPost(() =>
        {
            social.ResetStateForTests();
            social.TryAwardForTests(account, SolreignSocialFirstFlags.ChirpAnswered);
        });

        // The claim is async void (write-before-dispatch): poll the real file until the row lands.
        await PollUntilAsync(
            async () =>
            {
                var store = new SeasonLedgerStore(dbPath);
                var flags = await store.GetSocialFirstFlagsAsync(account);
                return flags.Contains(SolreignSocialFirstFlags.ChirpAnswered);
            },
            $"TryAward never landed the chirp_answered claim row for {account:N} in {dbPath}.");

        // Second detection, same process: the in-memory dedupe swallows it. Third detection after a
        // process-cache wipe: the DB row itself must hold the line (the once-per-account-EVER law).
        await Server.WaitPost(() =>
        {
            social.TryAwardForTests(account, SolreignSocialFirstFlags.ChirpAnswered);
            social.ResetStateForTests();
            social.TryAwardForTests(account, SolreignSocialFirstFlags.ChirpAnswered);
        });

        await Server.WaitRunTicks(10);

        var finalStore = new SeasonLedgerStore(dbPath);
        var finalFlags = await finalStore.GetSocialFirstFlagsAsync(account);
        // ⚠️ Deliberately NOT exact-set equality. This test's claim is about ITS flag — repeat
        // detections must never add a second chirp_answered row. Exact equality also asserted,
        // implicitly, that no OTHER system ever claims a flag for the pool-connected account —
        // which became false 2026-08-02 when FirstShiftSpawnPromptSystem (default-on) started
        // claiming first_shift_spawn_prompt during pool setup's own real spawn. Count-of-one on
        // the flag under test keeps the once-ever law; a duplicate of ANY flag would still fail
        // the store's own uniqueness (and this count).
        Assert.That(finalFlags.Count(f => f == SolreignSocialFirstFlags.ChirpAnswered), Is.EqualTo(1),
            "repeat detections — even across a process-cache wipe — must never add a second row " +
            "(and delivery must never error against the real connected session)");
    }

    [Test]
    public async Task WingmatePrompt_ThirdVisitOnly_AndOncePerAccountEver()
    {
        var (mob, session, account) = GetPlayer();
        var prompts = Server.System<SolreignWingmatePromptSystem>();
        var dbPath = ResolveLedgerDbPath();

        await OverrideCVar(Side.Server, CCVars.SolreignWingmatesEnabled, true);

        // Visit with career Tours = 0: no prompt, and — critically — no burned claim.
        await Server.WaitPost(() =>
        {
            prompts.ResetRoundStateForTests();
            SEntMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });

        await Server.WaitRunTicks(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero,
                "below three career tours there is no prompt — the council said third visit");
        });

        var storeBefore = new SeasonLedgerStore(dbPath);
        Assert.That(await storeBefore.GetSocialFirstFlagsAsync(account),
            Does.Not.Contain(SolreignSocialFirstFlags.WingmatePrompt),
            "an ineligible visit must never burn the once-ever claim");

        // Seed the third visit: three completed tours in the SAME file the live system reads.
        var seedStore = new SeasonLedgerStore(dbPath);
        var seedRoundBase = UniqueRoundId();
        for (var i = 0; i < 3; i++)
        {
            await seedStore.AddRoundRecordAsync(
                account,
                new RoundContribution(
                    WasCaptainClean: false,
                    AntagWin: false,
                    EarlyDeath: false,
                    RoundId: seedRoundBase + i,
                    Gamemode: "CheapAddsWingmateSeed"));
        }

        var seeded = await seedStore.GetStatsAsync(account);
        Assert.That(seeded.Tours, Is.GreaterThanOrEqualTo(3),
            "Setup failed: the account must have >= 3 career tours before the eligible spawn fires.");

        // The eligible spawn: prompt is claimed (write-before-dispatch) and scheduled.
        await Server.WaitPost(() =>
        {
            prompts.ResetRoundStateForTests();
            SEntMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });

        await PollUntilAsync(
            () => Task.FromResult(prompts.PendingPromptCountForTests == 1),
            "the third-visit spawn never scheduled the wingmate prompt");

        // Fire the due beat against the real session (popup + private chat — an error fails the pair).
        await Server.WaitPost(() =>
        {
            prompts.FireDuePromptsForTests(SGameTiming.CurTime + TimeSpan.FromSeconds(60));
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero,
                "the due prompt must drain exactly once");
        });

        var storeAfter = new SeasonLedgerStore(dbPath);
        Assert.That(await storeAfter.GetSocialFirstFlagsAsync(account),
            Does.Contain(SolreignSocialFirstFlags.WingmatePrompt),
            "the fired prompt must be backed by its once-ever claim row");

        // A fourth visit: the copy promised "this suggestion will not be repeated" — the claim row
        // keeps that promise even across a round-state reset (fresh-round simulation).
        await Server.WaitPost(() =>
        {
            prompts.ResetRoundStateForTests();
            SEntMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });

        await Server.WaitRunTicks(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero,
                "the prompt must never fire twice for one account, ever");
        });
    }
}
