using System.Collections.Generic;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     Raw, unvalidated prototype lookup — the thin seam between
///     <see cref="SolreignFxPrototypeSource"/> and a real <c>IPrototypeManager</c>. Kept as its own
///     interface (rather than depending on <c>IPrototypeManager</c> directly) so
///     <see cref="SolreignFxPrototypeSource"/>'s load-time-validation-gating behavior (spec §1.5,
///     grk M5) is unit-testable with a fake loader, the same engine-decoupling rationale W1 already
///     established for <see cref="ISolreignFxCuePrototypeSource"/>/<see cref="ISolreignFxAnchorResolver"/>.
///
///     Contract: MUST NOT throw for any input, including unknown/malformed ids — return
///     <c>false</c> instead.
/// </summary>
public interface ISolreignFxRawPrototypeLoader
{
    bool TryGetRaw(string effectId, out SolreignFxCuePrototype? prototype);
}

/// <summary>
///     Closes grk adversarial review finding M5 from the W1 receipt ("Palette/phase fallback under a
///     <c>count &lt;= 0</c> prototype can mint a meaningless index... the correct fix is keeping a
///     malformed prototype out of the allowlist at load time"). Wraps a raw loader with
///     <see cref="SolreignFxCuePrototypeValidation"/>: a prototype that fails ANY §1.5 rule is
///     treated as UNRESOLVABLE — <see cref="TryResolve"/> returns <c>false</c> for it exactly as if
///     the id didn't exist at all — so <c>SolreignFxCueV1.TryCreate</c>/<c>TryValidateReceived</c>
///     reject at the earlier "EffectIdNotResolved"/"EffectIdRejected" step and never reach the
///     palette/phase fallback with a prototype whose <c>PaletteCount</c>/<c>PhaseCount</c> could be
///     zero or negative. "The game still boots" (spec §1.5) — a broken prototype is silently
///     excluded, not a load-time crash.
///
///     Validation results are cached per prototype id and invalidated only on an explicit
///     <see cref="Invalidate"/> call (the real client/server systems call this from their
///     <c>IPrototypeManager.PrototypesReloaded</c> subscription) — re-running the full §1.5 rule set
///     on every single received-cue resolution would be wasteful; validating once per load/reload
///     and caching the verdict is the same shape <c>IPrototypeManager</c> itself uses for its own
///     internal indices.
/// </summary>
public sealed class SolreignFxPrototypeSource : ISolreignFxCuePrototypeSource
{
    private readonly ISolreignFxRawPrototypeLoader _loader;
    private readonly Dictionary<string, bool> _validCache = new();

    public SolreignFxPrototypeSource(ISolreignFxRawPrototypeLoader loader)
    {
        _loader = loader;
    }

    /// <summary>Drops every cached validation verdict. Call after any prototype load/reload.</summary>
    public void Invalidate()
    {
        _validCache.Clear();
    }

    /// <inheritdoc/>
    public bool TryResolve(string effectId, out SolreignFxCuePrototype? prototype)
    {
        prototype = null;

        if (string.IsNullOrEmpty(effectId))
            return false;

        if (!_loader.TryGetRaw(effectId, out var raw) || raw is null)
            return false;

        if (!IsValid(effectId, raw))
            return false;

        prototype = raw;
        return true;
    }

    private bool IsValid(string effectId, SolreignFxCuePrototype raw)
    {
        if (_validCache.TryGetValue(effectId, out var cached))
            return cached;

        var valid = SolreignFxCuePrototypeValidation.Validate(raw, out _);
        _validCache[effectId] = valid;
        return valid;
    }
}
