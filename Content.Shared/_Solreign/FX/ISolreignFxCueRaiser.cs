using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     The API-enforced two-method raise-path split (spec §5.2, grk #2A) — the confidentiality
///     boundary is enforced by the API surface itself, not caller discipline. Gameplay systems must
///     depend on this interface and NEVER call <c>RaiseNetworkEvent</c> directly on a
///     <see cref="SolreignFxCueV1"/> payload; a static-analysis test (grep for direct
///     <c>RaiseNetworkEvent</c> of the cue type outside the implementing system) closes the
///     "forgotten manual raise" hole once that system exists.
///
///     No implementation ships in W1 — this is the forward-declared contract shape only. W2's
///     <c>Content.Server._Solreign.FX.SolreignFxServerSystem</c> is the sole implementer and the
///     only place PVS filtering, actor exclusion, the generic+detail pair emission, and the
///     egress-budget check (spec §3.1) actually get wired up. Declaring the shape now means every
///     later consumer worktree (W4's changeling fix, future W6 primitives) codes against this
///     contract from day one instead of inventing its own raise path.
/// </summary>
public interface ISolreignFxCueRaiser
{
    /// <summary>
    ///     Raises a single non-secret cue, broadcast via <c>Filter.Pvs(pvsSource, ...)</c> (spec
    ///     §5.2 item 1 — PVS scoping is mandated explicitly, never assumed). MUST refuse (hard
    ///     error in debug, drop+metric in release) any <paramref name="effectId"/> whose manifest
    ///     classification (<see cref="SolreignFxWireAllowlist.AudienceClassificationV1"/>) is
    ///     <see cref="SolreignFxAudienceClassification.DetailOnly"/> — that split is enforced here,
    ///     not by caller discipline.
    /// </summary>
    bool RaiseCue(
        ProtoId<SolreignFxCuePrototype> effectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        EntityUid pvsSource);

    /// <summary>
    ///     The ONLY path permitted to put a <see cref="SolreignFxAudienceClassification.DetailOnly"/>
    ///     id on the wire (spec §5.2). ALWAYS emits the generic+detail pair itself: a redacted
    ///     broadcast cue (scrubbed numeric fields at the generic prototype's fixed defaults,
    ///     independently-rolled <see cref="SolreignFxCueV1.Seed"/>, broadcast-counter
    ///     <see cref="SolreignFxCueV1.CorrelationId"/>, actor excluded via
    ///     <c>Filter.Pvs(...).RemovePlayer(actorSession)</c>) AND the real detail cue,
    ///     session-targeted to <paramref name="actorSession"/> only (targeted-counter
    ///     CorrelationId). Any use of ANY primitive by a secret-role ability routes through this
    ///     method regardless of that primitive's own <see cref="SolreignFxAudienceClassification"/>
    ///     — the split keys off the CALLER's secrecy (spec §5.2, grk #2D), not the id.
    /// </summary>
    bool RaiseSecretRoleCue(
        ProtoId<SolreignFxCuePrototype> detailEffectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        EntityUid pvsSource,
        ICommonSession actorSession);
}
