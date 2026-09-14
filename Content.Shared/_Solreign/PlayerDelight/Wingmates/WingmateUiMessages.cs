using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.PlayerDelight.Wingmates;

[Serializable, NetSerializable]
public enum WingmateUiKey : byte
{
    Beacon,
}

[Serializable, NetSerializable]
public enum WingmateUiMode : byte
{
    Idle,
    Seeking,
    OfferAvailable,
    OfferReceived,
    Paired,
    Disabled,
    Paused,
}

/// <summary>
/// The requester-selected style transported by the BUI. The server maps this value into its domain
/// state only after authenticating the actor and validating the request.
/// </summary>
[Serializable, NetSerializable]
public enum WingmateTeachingMode : byte
{
    Tour,
    LearnByDoing,
    ShadowMe,
}

/// <summary>
/// Entity-wide state may be observed by any client in range, so it contains only the feature switch.
/// All viewer-derived data travels in a targeted <see cref="WingmatePrivateSnapshotEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class WingmatePublicShellState : BoundUserInterfaceState
{
    public bool Enabled { get; }

    public WingmatePublicShellState(bool enabled)
    {
        Enabled = enabled;
    }
}

/// <summary>
/// A viewer-filtered snapshot containing only information needed to render the Wingmates UI.
/// Moderation, eligibility, history, blocks, decline reasons, and chat are deliberately absent.
/// </summary>
[Serializable, NetSerializable]
public sealed class WingmateUiState : BoundUserInterfaceState
{
    public WingmateUiMode Mode { get; }
    public string Department { get; }
    public WingmateTeachingMode TeachingMode { get; }

    // Opaque, unpredictable, viewer-bound capability used only to address the currently visible
    // request. It is a random token minted for this specific viewer — never a sequential/guessable
    // value, never derived from an account UUID — and it is invalidated when round state is cleared.
    public Guid? RequesterToken { get; }
    public string? RequesterDisplayName { get; }
    public string? PartnerDisplayName { get; }
    public Guid? OfferNonce { get; }
    public bool CanRequest { get; }
    public bool CanVolunteer { get; }
    public string StatusText { get; }
    public bool IsVolunteering { get; }

    // FIX 1 (UX-SIMPLE): the reason this viewer currently cannot volunteer as a guide (an .ftl loc
    // key), or null when they can (or already are volunteering, or the feature itself is disabled
    // — DisabledPanel already covers that case). Populated so the client never has to render a
    // silently-greyed Volunteer button with no explanation of why.
    public string? VolunteerIneligibleReason { get; }

    // ALIVENESS P0 #3: while Seeking, whether ANY other connected player is currently a plausible
    // pairing peer (attached, alive, non-ghost — never who, never how many, no identity leak). A
    // solo seeker must see an honest "no crew available to pair this shift" state instead of the
    // open-ended "your request is open" framing, which at pop 1 is a trap. Defaults true so every
    // non-Seeking state (and every pre-existing constructor call) keeps the normal copy.
    public bool PeerAvailable { get; }

    // P1.4 SILENT-DROP BATCH FIX: the .ftl loc key of the most recent transition failure for this
    // viewer (an opaque reason code like "rate-limited" / "stale-offer" / "not-paired"), or null.
    // Populated by the server on every BUI action whose transition result is non-success (see
    // WingmateSystem.ExpireApplyAndPublish), cleared on the next success so stale failures do not
    // resurface on later snapshots. Default null keeps every pre-existing constructor call stable.
    public string? LastTransitionStatus { get; }

    // P1.4 SILENT-DROP BATCH FIX: monotonic per-viewer counter that increments every time
    // LastTransitionStatus is set. The client renders a popup ONLY when this strictly increases
    // past the last value it rendered, so a replayed snapshot re-emits no popup, and a
    // cleared-then-reset status still pops exactly once per distinct failure.
    public uint LastTransitionSeq { get; }

    public WingmateUiState(
        WingmateUiMode mode,
        string department,
        WingmateTeachingMode teachingMode,
        Guid? requesterToken,
        string? requesterDisplayName,
        string? partnerDisplayName,
        Guid? offerNonce,
        bool canRequest,
        bool canVolunteer,
        string statusText,
        bool isVolunteering = false,
        string? volunteerIneligibleReason = null,
        bool peerAvailable = true,
        string? lastTransitionStatus = null,
        uint lastTransitionSeq = 0)
    {
        Mode = mode;
        Department = department;
        TeachingMode = teachingMode;
        RequesterToken = requesterToken;
        RequesterDisplayName = requesterDisplayName;
        PartnerDisplayName = partnerDisplayName;
        OfferNonce = offerNonce;
        CanRequest = canRequest;
        CanVolunteer = canVolunteer;
        StatusText = statusText;
        IsVolunteering = isVolunteering;
        VolunteerIneligibleReason = volunteerIneligibleReason;
        PeerAvailable = peerAvailable;
        LastTransitionStatus = lastTransitionStatus;
        LastTransitionSeq = lastTransitionSeq;
    }
}

[Serializable, NetSerializable]
public sealed class WingmatePrivateSnapshotEvent : EntityEventArgs
{
    public NetEntity Beacon { get; }
    public ulong Generation { get; }
    public WingmateUiState State { get; }

    public WingmatePrivateSnapshotEvent(NetEntity beacon, ulong generation, WingmateUiState state)
    {
        Beacon = beacon;
        Generation = generation;
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class WingmateRequestMessage : BoundUserInterfaceMessage
{
    public string Department { get; }
    public WingmateTeachingMode TeachingMode { get; }

    public WingmateRequestMessage(string department, WingmateTeachingMode teachingMode)
    {
        Department = department;
        TeachingMode = teachingMode;
    }
}

[Serializable, NetSerializable]
public sealed class WingmateCancelRequestMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class WingmateOfferMessage : BoundUserInterfaceMessage
{
    public Guid RequesterToken { get; }

    public WingmateOfferMessage(Guid requesterToken)
    {
        RequesterToken = requesterToken;
    }
}

[Serializable, NetSerializable]
public sealed class WingmateAcceptMessage : BoundUserInterfaceMessage
{
    public Guid OfferNonce { get; }

    public WingmateAcceptMessage(Guid offerNonce)
    {
        OfferNonce = offerNonce;
    }
}

/// <summary>
/// Declines an incoming offer. <see cref="BlockGuide"/> is a same-message opt-in: if set, and the
/// decline actually applies to the currently-held offer, the server layers a durable cross-round
/// block onto the offering guide identified by the server's own offer state — never a client-selected
/// account id.
/// </summary>
[Serializable, NetSerializable]
public sealed class WingmateDeclineMessage : BoundUserInterfaceMessage
{
    public Guid OfferNonce { get; }
    public bool BlockGuide { get; }

    public WingmateDeclineMessage(Guid offerNonce, bool blockGuide = false)
    {
        OfferNonce = offerNonce;
        BlockGuide = blockGuide;
    }
}

[Serializable, NetSerializable]
public sealed class WingmateDissolveMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class WingmatePauseMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class WingmateResumeMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class WingmateBlockCurrentPartnerMessage : BoundUserInterfaceMessage
{
}

/// <summary>Sets or withdraws current-round guide consent. Opt-in requires charter acceptance.</summary>
[Serializable, NetSerializable]
public sealed class WingmateVolunteerMessage : BoundUserInterfaceMessage
{
    public bool Volunteering { get; }
    public bool CharterAccepted { get; }

    public WingmateVolunteerMessage(bool volunteering, bool charterAccepted)
    {
        Volunteering = volunteering;
        CharterAccepted = charterAccepted;
    }
}
