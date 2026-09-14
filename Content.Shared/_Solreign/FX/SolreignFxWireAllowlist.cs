using System.Collections.Frozen;
using System.Collections.Generic;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     Who may receive a given <see cref="SolreignFxCueV1"/> effect id (spec §5.2). This is a
///     per-id classification, not a per-caller one: <see cref="Broadcast"/> ids may travel via the
///     plain <c>RaiseCue</c> path; <see cref="DetailOnly"/> ids may ONLY ever be raised through
///     <c>RaiseSecretRoleCue</c>'s paired generic+detail emission (that helper is the only code
///     path permitted to put a <see cref="DetailOnly"/> id on the wire). This does not exempt
///     non-<see cref="DetailOnly"/> ids from the secret-role split when the CALLER is secret
///     (spec §5.2, grk #2D: "the rule keys off the caller's secrecy, not the primitive") — a
///     changeling-power <c>cast_ring</c> still routes through <c>RaiseSecretRoleCue</c> even
///     though <c>cast_ring</c> itself is classified <see cref="Broadcast"/> here.
/// </summary>
public enum SolreignFxAudienceClassification : byte
{
    /// <summary>May be raised broadcast (PVS-filtered) directly via <c>RaiseCue</c>.</summary>
    Broadcast = 0,

    /// <summary>
    ///     May ONLY be raised via <c>RaiseSecretRoleCue</c> as the session-targeted detail half of
    ///     a generic+detail pair. A direct <c>RaiseCue</c> call with this id is refused at the API
    ///     level (hard error in debug, drop+metric in release) — see the raise-API contract
    ///     <see cref="ISolreignFxCueRaiser"/>.
    /// </summary>
    DetailOnly = 1,
}

/// <summary>
///     The sealed, code-side wire allowlist for FX Language v1 (spec §1.3a/§2.2) — the exact 10-id
///     enumeration of every <see cref="SolreignFxCueV1.EffectId"/> value permitted to cross the
///     network, including redacted broadcast variants. <c>ProtoId&lt;SolreignFxCuePrototype&gt;</c>
///     resolution via <c>IPrototypeManager.TryIndex</c> alone is NOT this allowlist (cdx #3): any
///     loaded prototype of the class would pass <c>TryIndex</c> regardless of authorial intent
///     (e.g. a forgotten test-fixture prototype in an unrelated YAML file). <see cref="V1"/> is the
///     second, independent gate <c>SolreignFxCueV1.TryCreate</c>/<c>TryValidateReceived</c> both
///     check — membership here is required IN ADDITION TO successful prototype resolution, never
///     instead of it.
///
///     A unit test (<c>Content.Tests._Solreign.FX.SolreignFxCueV1Tests</c>) pins this set's exact
///     membership and, once W3's <c>effects.yml</c> exists, cross-checks it against the YAML
///     allowlist per cdx #19/grk #5 — that cross-check is necessarily deferred past W1 (the YAML
///     file does not exist yet), tracked explicitly in the W1 receipt rather than silently skipped.
/// </summary>
public static class SolreignFxWireAllowlist
{
    /// <summary>The complete v1 wire-id inventory (spec §2.2), 10 ids, frozen for O(1) lookups.</summary>
    public static readonly FrozenSet<string> V1 = new[]
    {
        "impact_light",
        "impact_heavy",
        "electrical",
        "dust",
        "smoke",
        "cast_ring",
        "transformation",
        "transformation_generic",
        "stamina_break",
        "body_shock_generic",
    }.ToFrozenSet();

    /// <summary>
    ///     Per-id audience classification mirrored from each id's prototype (spec §2.2: "Each id's
    ///     audience classification... is a field on its prototype AND mirrored in the sealed
    ///     manifest with a consistency unit test"). <c>transformation</c> is the only
    ///     <see cref="SolreignFxAudienceClassification.DetailOnly"/> member in v1 — every other id,
    ///     including both redacted generics, is broadcast-eligible by classification (secret-role
    ///     callers of a Broadcast-classified id still route through <c>RaiseSecretRoleCue</c> by
    ///     caller-secrecy, per <see cref="SolreignFxAudienceClassification.DetailOnly"/>'s remarks).
    /// </summary>
    public static readonly FrozenDictionary<string, SolreignFxAudienceClassification> AudienceClassificationV1 =
        new Dictionary<string, SolreignFxAudienceClassification>
        {
            ["impact_light"] = SolreignFxAudienceClassification.Broadcast,
            ["impact_heavy"] = SolreignFxAudienceClassification.Broadcast,
            ["electrical"] = SolreignFxAudienceClassification.Broadcast,
            ["dust"] = SolreignFxAudienceClassification.Broadcast,
            ["smoke"] = SolreignFxAudienceClassification.Broadcast,
            ["cast_ring"] = SolreignFxAudienceClassification.Broadcast,
            ["transformation"] = SolreignFxAudienceClassification.DetailOnly,
            ["transformation_generic"] = SolreignFxAudienceClassification.Broadcast,
            ["stamina_break"] = SolreignFxAudienceClassification.Broadcast,
            ["body_shock_generic"] = SolreignFxAudienceClassification.Broadcast,
        }.ToFrozenDictionary();

    /// <summary>Total, exception-free membership check — never throws on null/empty input.</summary>
    public static bool IsAllowlisted(string? effectId)
    {
        return !string.IsNullOrEmpty(effectId) && V1.Contains(effectId);
    }

    /// <summary>Total, exception-free classification lookup — never throws on null/empty input.</summary>
    public static bool TryGetAudienceClassification(string? effectId, out SolreignFxAudienceClassification classification)
    {
        if (string.IsNullOrEmpty(effectId))
        {
            classification = default;
            return false;
        }

        return AudienceClassificationV1.TryGetValue(effectId, out classification);
    }
}
