using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Content.Shared.CCVar;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class WingmatePrototypeTests
{
    private static readonly string[] RequiredLocaleKeys =
    [
        "wingmates-title",
        "wingmates-decline",
        "wingmates-status-expired",
        "wingmates-disabled-explanation",
        "wingmates-end-pairing",
        "wingmates-block-current-partner",
        "wingmates-block-persistence-failed-count",
        "wingmates-block-persistence-failed-previous-shift",
        "wingmates-moderator-help",
        "wingmates-no-guide-fallback",
        "cmd-wingmatestatus-desc",
        "cmd-wingmatestatus-help",
        "cmd-wingmateapprove-desc",
        "cmd-wingmateapprove-help",
        "cmd-wingmaterevoke-desc",
        "cmd-wingmaterevoke-help",
        "cmd-wingmatedissolve-desc",
        "cmd-wingmatedissolve-help",
        "cmd-wingmate-player-not-found",
        "cmd-wingmate-status",
        "cmd-wingmate-approve-result",
        "cmd-wingmate-revoke-result",
        "cmd-wingmate-dissolve-result",
        "cmd-wingmate-yes",
        "cmd-wingmate-no",
    ];

    [Test]
    public void AdminCanaryBeaconOwnsRequiredUiComponents()
    {
        var root = LocateRepositoryRoot();
        var path = Path.Combine(root, "Resources/Prototypes/_Solreign/PlayerDelight/wingmate_beacon.yml");

        Assert.That(File.Exists(path), Is.True, "The admin-spawnable Wingmates canary prototype is missing.");
        var yaml = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(yaml, Does.Contain("id: SolreignWingmateBeacon"));
            Assert.That(yaml, Does.Contain("parent: BaseWallmountMetallic"));
            Assert.That(yaml, Does.Contain("- type: WingmateBeacon"));
            Assert.That(yaml, Does.Contain("- type: FirstShiftBeacon"),
                "The same physical canary needs a distinct First Shift ECS marker to avoid duplicate event subscriptions.");
            Assert.That(yaml, Does.Contain("- type: ActivatableUI"));
            Assert.That(yaml, Does.Contain("key: enum.WingmateUiKey.Beacon"));
            Assert.That(yaml, Does.Contain("- type: UserInterface"));
            Assert.That(yaml, Does.Contain("type: WingmateBoundUserInterface"));
        });
    }

    [Test]
    public void FirstShiftOwnsDistinctBeaconSubscriptionBoundary()
    {
        var path = Path.Combine(LocateRepositoryRoot(),
            "Content.Server/_Solreign/PlayerDelight/FirstShift/FirstShiftSystem.cs");
        var source = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("SubscribeLocalEvent<FirstShiftBeaconComponent, BoundUIOpenedEvent>"));
            Assert.That(source, Does.Not.Contain("SubscribeLocalEvent<WingmateBeaconComponent"));
            Assert.That(source, Does.Not.Contain("HasComp<WingmateBeaconComponent>"));
        });
    }

    /// <summary>
    ///     Wingmate pairing ships ON as of v14.2.
    /// </summary>
    /// <remarks>
    ///     Was <c>WingmatesRemainsDefaultOffForAdminCanaryRollout</c>. The admin canary ended
    ///     when wingmates and the first-shift tutorial were deliberately enabled for the v14.2
    ///     release — the newcomer onboarding players had been asking for. The old assertion
    ///     outlived the decision and sat red in the suite.
    ///
    ///     Kept as an assertion rather than deleted: turning pairing back off is a legitimate
    ///     choice, and it should be a visible edit here rather than a default that quietly
    ///     drifts. See the sibling first-shift CVar test, which is switched separately on
    ///     purpose so either half of onboarding can be disabled alone.
    /// </remarks>
    [Test]
    public void WingmatesDefaultOnSinceV142()
    {
        Assert.That(CCVars.SolreignWingmatesEnabled.DefaultValue, Is.True,
            "Wingmate pairing ships ON as of v14.2. If this is being turned off, change it "
            + "here too so the decision is recorded rather than inferred.");
    }

    [Test]
    public void WingmateLocaleContainsPlayerAndModeratorCanaryCopy()
    {
        var path = Path.Combine(LocateRepositoryRoot(), "Resources/Locale/en-US/_solreign/wingmates.ftl");
        Assert.That(File.Exists(path), Is.True);
        var defined = File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Split('=', 2)[0].Trim())
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(RequiredLocaleKeys.Where(key => !defined.Contains(key)), Is.Empty,
            "Wingmates locale is incomplete.");
    }

    [Test]
    public void BlockCurrentPartnerCopyDisclosesPermanenceHonestly()
    {
        // Consent regression (cdx r3 finding 2): this button drives BeginPersistentBlock — a durable,
        // cross-round SQLite-backed block. It is the ONLY block affordance available against a current
        // partner (the decline-time checkbox only exists for incoming offers), so it must stay wired to
        // the permanent path for the safety model — which means its label must disclose permanence and
        // must never claim a merely round-local effect.
        var path = Path.Combine(LocateRepositoryRoot(), "Resources/Locale/en-US/_solreign/wingmates.ftl");
        var line = File.ReadLines(path)
            .Single(entry => entry.StartsWith("wingmates-block-current-partner =", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("all future shifts"),
                "the label must disclose that the block is permanent, not round-local");
            Assert.That(line, Does.Not.Contain("this round"),
                "the label must not promise a round-only effect while creating a permanent block");
        });
    }

    [Test]
    public void NoGuideFallbackIsNeutralAndOffersSelfGuidedNextSteps()
    {
        var path = Path.Combine(LocateRepositoryRoot(), "Resources/Locale/en-US/_solreign/wingmates.ftl");
        var locale = File.ReadAllText(path);

        Assert.That(locale, Does.Contain(
            "wingmates-no-guide-fallback = No crew guide is free right now. Try the self-guided assignment below, or check again later."));
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Resources")) &&
                File.Exists(Path.Combine(directory.FullName, "SpaceStation14.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
