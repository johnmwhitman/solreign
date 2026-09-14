using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     Raised on the server and broadcast to all clients to play the Solreign signature screen-FX
///     moment: a pulsing acid-green screen-border overlay (see
///     Content.Client._Solreign.FX.SolreignAcidBorderOverlay). This is a general-purpose "mark this
///     moment" hook — any Solreign system can raise it to get the brand sting for free instead of
///     rolling its own overlay plumbing. The Corporate rule's quarterly audit
///     (Content.Server._Solreign.Corporate.SolreignCorporateRuleSystem.BroadcastEarningsCall) is the
///     first wired trigger, as proof.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignScreenFxEvent : EntityEventArgs
{
    /// <summary>
    ///     How long the overlay should stay up, in seconds. Clamped on construction via
    ///     <see cref="SolreignScreenFxTiming.ClampDuration"/> so no caller can accidentally hold it for
    ///     an imperceptible instant or indefinitely.
    /// </summary>
    public readonly float Duration;

    public SolreignScreenFxEvent(float duration = SolreignScreenFxTiming.DefaultDuration)
    {
        Duration = SolreignScreenFxTiming.ClampDuration(duration);
    }
}
