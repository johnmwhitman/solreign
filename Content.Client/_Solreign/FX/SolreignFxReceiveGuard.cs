using Content.Shared._Solreign.FX;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._Solreign.FX;

/// <summary>
///     Closes grk adversarial review finding H1 from the W1 receipt on the RECEIVE side (the send
///     side is closed by <c>SolreignFxServerSystem.RaiseCue</c> refusing to broadcast a
///     <see cref="SolreignFxAudienceClassification.DetailOnly"/> id at all — spec §5.2's
///     API-enforced split). The W1 receipt correctly noted that a receiving client cannot, from a
///     cue's fields alone, tell whether IT was the only recipient of a given delivery — Robust's
///     networking does not tag "this arrived via Filter.Pvs" vs. "this arrived via a
///     session-targeted send" anywhere the payload itself can see.
///
///     What a receiving client CAN still verify, and what this type checks: v1's only
///     <see cref="SolreignFxAudienceClassification.DetailOnly"/> primitive (<c>transformation</c>)
///     is, by spec §5's own design, ALWAYS entity-anchored to the secret-role actor's own body and
///     ALWAYS delivered session-targeted to that same actor. A well-behaved server therefore never
///     sends a <see cref="SolreignFxAudienceClassification.DetailOnly"/> cue whose
///     <see cref="SolreignFxCueV1.EntityAnchor"/> resolves to anything other than the receiving
///     client's own currently-controlled entity. If one ever arrives anchored to someone else (a
///     coordinate anchor, a different entity, or no anchor at all), that is a genuine confidentiality
///     anomaly — a buggy or compromised server mis-targeting a secret detail cue — and this client
///     must drop it, never render it, regardless of whether the wire-level validation
///     (<c>TryValidateReceived</c>) already accepted the cue as structurally well-formed. This is
///     defense in depth on top of the send-side API enforcement, not a replacement for it: a
///     compromised server can still bypass <c>SolreignFxServerSystem</c> entirely (reflection,
///     a modified build) exactly as cdx #1's core reframe already established for the wire schema
///     itself — this guard is the same "the client must not simply trust the server" posture applied
///     to audience/confidentiality instead of numeric bounds.
/// </summary>
public static class SolreignFxReceiveGuard
{
    public enum DetailOnlyVerdict
    {
        /// <summary>Not a DetailOnly-classified id — this guard does not apply; proceed normally.</summary>
        NotApplicable,

        /// <summary>DetailOnly, entity-anchored, and the anchor resolves to this client's own controlled entity — legitimate.</summary>
        Legitimate,

        /// <summary>DetailOnly but coordinate-anchored (never legal — spec §5's only DetailOnly primitive is always entity-anchored to the actor).</summary>
        RejectedCoordinateAnchored,

        /// <summary>DetailOnly but carries no anchor at all.</summary>
        RejectedNoAnchor,

        /// <summary>DetailOnly, entity-anchored, but the anchor did not resolve to a live local entity.</summary>
        RejectedAnchorUnresolvable,

        /// <summary>DetailOnly, entity-anchored, resolves fine — but to an entity other than this client's own controlled entity. The sharpest possible confidentiality anomaly signal.</summary>
        RejectedAnchorMismatch,
    }

    /// <summary>
    ///     Total, exception-free (no engine calls at all — every input is already-resolved data the
    ///     caller supplies). <paramref name="resolvedAnchorEntity"/> is the caller's own
    ///     already-attempted <c>TryGetEntity</c> resolution of <paramref name="entityAnchor"/> (null
    ///     if it didn't resolve); <paramref name="localControlledEntity"/> is this client's own
    ///     <c>IPlayerManager.LocalEntity</c> (null if none, e.g. a lobby/ghost/unattached client,
    ///     which can therefore never legitimately be the target of a DetailOnly cue).
    /// </summary>
    public static DetailOnlyVerdict CheckDetailOnlyAnchor(
        SolreignFxAudienceClassification classification,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        EntityUid? resolvedAnchorEntity,
        EntityUid? localControlledEntity)
    {
        if (classification != SolreignFxAudienceClassification.DetailOnly)
            return DetailOnlyVerdict.NotApplicable;

        if (coordinates.HasValue)
            return DetailOnlyVerdict.RejectedCoordinateAnchored;

        if (!entityAnchor.HasValue)
            return DetailOnlyVerdict.RejectedNoAnchor;

        if (resolvedAnchorEntity is not { } resolved)
            return DetailOnlyVerdict.RejectedAnchorUnresolvable;

        if (localControlledEntity is not { } local || resolved != local)
            return DetailOnlyVerdict.RejectedAnchorMismatch;

        return DetailOnlyVerdict.Legitimate;
    }
}
