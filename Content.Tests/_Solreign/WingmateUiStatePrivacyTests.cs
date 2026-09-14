using System;
using System.Linq;
using System.Reflection;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(WingmateUiState))]
public sealed class WingmateUiStatePrivacyTests
{
    private static readonly string[] ExpectedStateProperties =
    {
        nameof(WingmateUiState.Mode),
        nameof(WingmateUiState.Department),
        nameof(WingmateUiState.TeachingMode),
        nameof(WingmateUiState.RequesterToken),
        nameof(WingmateUiState.RequesterDisplayName),
        nameof(WingmateUiState.PartnerDisplayName),
        nameof(WingmateUiState.OfferNonce),
        nameof(WingmateUiState.CanRequest),
        nameof(WingmateUiState.CanVolunteer),
        nameof(WingmateUiState.IsVolunteering),
        nameof(WingmateUiState.StatusText),
        // UX-SIMPLE FIX 1: an .ftl loc-key string explaining why volunteering is currently blocked
        // (not-alive/is-a-ghost/pending-approval/etc) — never leaks WHO blocked whom or any
        // moderation/ban/history detail, so it belongs in the privacy-reviewed view. Named
        // "Ineligible" rather than "Blocked" specifically so it can never collide with the
        // PrivateTerms "Block" guard below, which exists to catch persistent-block-identity leaks.
        nameof(WingmateUiState.VolunteerIneligibleReason),
        // ALIVENESS P0 #3: a bare presence bit ("is anyone plausibly pairable connected right
        // now") for the Seeking view's honest solo-shift copy — never who, how many, or where,
        // so it carries no identity and belongs in the privacy-reviewed view.
        nameof(WingmateUiState.PeerAvailable),
        // P1.4 SILENT-DROP BATCH FIX: an .ftl loc-key string naming the most recent transition
        // failure (e.g. "rate-limited" / "stale-offer" / "not-paired") plus a monotonic counter.
        // Neither field carries identity — the key describes the rejection reason, the counter
        // dedupes the popup — so both belong in the privacy-reviewed view.
        nameof(WingmateUiState.LastTransitionStatus),
        nameof(WingmateUiState.LastTransitionSeq),
    };

    private static readonly string[] PrivateTerms =
    {
        "Moderation",
        "Ban",
        "Block",
        "History",
        "AccountAge",
        "DeclineReason",
        "Chat",
    };

    [Test]
    public void UiState_ExposesOnlyThePrivacyReviewedView()
    {
        var properties = typeof(WingmateUiState).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Multiple(() =>
        {
            Assert.That(properties.Select(property => property.Name), Is.EquivalentTo(ExpectedStateProperties));
            Assert.That(properties, Has.All.Matches<PropertyInfo>(property => property.CanRead));
            Assert.That(properties, Has.All.Matches<PropertyInfo>(property => !property.CanWrite));

            foreach (var property in properties)
            foreach (var privateTerm in PrivateTerms)
            {
                Assert.That(property.Name, Does.Not.Contain(privateTerm).IgnoreCase);
            }
        });
    }

    [Test]
    public void UiState_UsesTheReviewedTypesAndRetainsItsSnapshot()
    {
        var requesterToken = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var nonce = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var state = new WingmateUiState(
            WingmateUiMode.OfferReceived,
            "Engineering",
            WingmateTeachingMode.LearnByDoing,
            requesterToken,
            "Prospective crewmate",
            "Guide",
            nonce,
            canRequest: false,
            canVolunteer: false,
            "A crewmate has offered to help.");

        Assert.Multiple(() =>
        {
            Assert.That(state.Mode, Is.EqualTo(WingmateUiMode.OfferReceived));
            Assert.That(state.Department, Is.EqualTo("Engineering"));
            Assert.That(state.TeachingMode, Is.EqualTo(WingmateTeachingMode.LearnByDoing));
            Assert.That(state.RequesterToken, Is.EqualTo(requesterToken));
            Assert.That(state.RequesterDisplayName, Is.EqualTo("Prospective crewmate"));
            Assert.That(state.PartnerDisplayName, Is.EqualTo("Guide"));
            Assert.That(state.OfferNonce, Is.EqualTo(nonce));
            Assert.That(state.CanRequest, Is.False);
            Assert.That(state.CanVolunteer, Is.False);
            Assert.That(state.IsVolunteering, Is.False);
            Assert.That(state.StatusText, Is.EqualTo("A crewmate has offered to help."));
        });
    }

    [Test]
    public void UiState_DoesNotExposeNetUserId()
    {
        var stateProperties = typeof(WingmateUiState).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var offerMessageProperties = typeof(WingmateOfferMessage).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(stateProperties, Has.None.Matches<PropertyInfo>(property => property.PropertyType == typeof(NetUserId) || property.PropertyType == typeof(NetUserId?)));
            Assert.That(offerMessageProperties, Has.None.Matches<PropertyInfo>(property => property.PropertyType == typeof(NetUserId) || property.PropertyType == typeof(NetUserId?)));
        });
    }

    [Test]
    public void RequesterTokenIsAnOpaqueGuidNotAGuessableSequentialInt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(WingmateUiState).GetProperty(nameof(WingmateUiState.RequesterToken))!.PropertyType,
                Is.EqualTo(typeof(Guid?)));
            Assert.That(typeof(WingmateOfferMessage).GetProperty(nameof(WingmateOfferMessage.RequesterToken))!.PropertyType,
                Is.EqualTo(typeof(Guid)));
        });
    }

    [Test]
    public void DeclineBlockIntentCarriesNoClientSelectedIdentity()
    {
        var properties = typeof(WingmateDeclineMessage)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            // The decline-time block affordance is a Boolean intent plus the server-issued offer nonce
            // only — the client never selects or transmits an account identity for who gets blocked.
            Assert.That(properties.Select(property => property.Name), Is.EquivalentTo(new[]
            {
                nameof(WingmateDeclineMessage.OfferNonce),
                nameof(WingmateDeclineMessage.BlockGuide),
            }));
            Assert.That(properties, Has.None.Matches<PropertyInfo>(property =>
                property.PropertyType == typeof(NetUserId) || property.PropertyType == typeof(NetUserId?)));
        });
    }

    [Test]
    public void ProtocolTypes_AreRegisteredForNetworkSerialization()
    {
        var protocolTypes = new[]
        {
            typeof(WingmateUiKey),
            typeof(WingmateUiMode),
            typeof(WingmateTeachingMode),
            typeof(WingmateUiState),
            typeof(WingmateRequestMessage),
            typeof(WingmateCancelRequestMessage),
            typeof(WingmateOfferMessage),
            typeof(WingmateAcceptMessage),
            typeof(WingmateDeclineMessage),
            typeof(WingmateDissolveMessage),
            typeof(WingmateBlockCurrentPartnerMessage),
            typeof(WingmateVolunteerMessage),
            typeof(WingmatePublicShellState),
            typeof(WingmatePrivateSnapshotEvent),
        };

        Assert.Multiple(() =>
        {
            foreach (var type in protocolTypes)
            {
                Assert.That(type.IsDefined(typeof(SerializableAttribute)), Is.True, $"{type.Name} is not serializable");
                Assert.That(type.IsDefined(typeof(NetSerializableAttribute)), Is.True, $"{type.Name} is not network serializable");
            }

            Assert.That(typeof(BoundUserInterfaceState).IsAssignableFrom(typeof(WingmateUiState)), Is.True);
            foreach (var messageType in protocolTypes.Where(type => type.Name.EndsWith("Message", StringComparison.Ordinal)))
                Assert.That(typeof(BoundUserInterfaceMessage).IsAssignableFrom(messageType), Is.True);
        });
    }

    [Test]
    public void BlockIntentCarriesNoClientSelectedIdentity()
    {
        Assert.That(typeof(WingmateBlockCurrentPartnerMessage).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            Is.Empty);
    }

    [Test]
    public void PublicShellContainsNoViewerSpecificData()
    {
        var propertyNames = typeof(WingmatePublicShellState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(propertyNames, Is.EquivalentTo(new[] { nameof(WingmatePublicShellState.Enabled) }));
            Assert.That(propertyNames, Has.None.Contains("PartnerDisplayName"));
            Assert.That(propertyNames, Has.None.Contains("RequesterDisplayName"));
            Assert.That(propertyNames, Has.None.Contains("RequesterId"));
            Assert.That(propertyNames, Has.None.Contains("OfferNonce"));
            Assert.That(propertyNames, Has.None.Contains("StatusText"));
        });
    }

    [Test]
    public void PrivateSnapshotIdentifiesBeaconAndGeneration()
    {
        var state = new WingmateUiState(WingmateUiMode.Idle, string.Empty, WingmateTeachingMode.Tour,
            null, null, null, null, true, false, "Idle");
        var snapshot = new WingmatePrivateSnapshotEvent(default, 42, state);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Beacon, Is.EqualTo(default(NetEntity)));
            Assert.That(snapshot.Generation, Is.EqualTo(42));
            Assert.That(snapshot.State, Is.SameAs(state));
        });
    }
}
