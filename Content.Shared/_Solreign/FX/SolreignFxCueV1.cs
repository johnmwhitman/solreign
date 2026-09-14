using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     Which server-side monotonic counter namespace mints a cue's <see cref="SolreignFxCueV1.CorrelationId"/>
///     (spec §1.3a.5, grk #6): a shared counter between broadcast and targeted emissions would let
///     a bystander observe *gaps* in the broadcast sequence exactly where a session-targeted detail
///     cue was minted, revealing that a private cue fired. Two independent namespaces keep the
///     broadcast sequence gap-free by construction.
/// </summary>
public enum SolreignFxCueStream : byte
{
    /// <summary>PVS-broadcast cue; draws from the broadcast-stream counter.</summary>
    Broadcast = 0,

    /// <summary>Session-targeted detail cue; draws from the targeted-stream counter.</summary>
    Targeted = 1,
}

/// <summary>Structured rejection reasons from <see cref="SolreignFxCueV1.TryCreate"/> (spec §1.3a, cdx #15 — never a bare bool).</summary>
public enum SolreignFxCueCreateFailureReason : byte
{
    None = 0,
    EffectIdNotAllowlisted,
    EffectIdNotResolved,
    AnchorMissingOrBothSet,
    AnchorEntityInvalid,
    AnchorCoordinatesInvalid,
    AnchorCoordinatesOutOfCeiling,
    NonFiniteNumericField,
    NegativeNumericField,
    GrosslyOutOfRangeNumericField,

    /// <summary>
    ///     The resolved prototype's own declared [Min,Max] for a numeric field is itself malformed
    ///     — non-finite, inverted (Min &gt; Max), or outside the hardcoded wire ceiling (spec §1.5)
    ///     — independent of whatever value the caller supplied. Defense in depth (grk adversarial
    ///     review finding M3): §1.5's load-time validation is what's SUPPOSED to keep a prototype
    ///     this broken out of the effective allowlist before it ever reaches here, but that hook
    ///     isn't wired until a later worktree, and a NaN bound would otherwise make every
    ///     comparison-based check silently pass.
    /// </summary>
    MalformedPrototypeBounds,
}

/// <summary>Structured rejection reasons from <see cref="SolreignFxCueV1.TryValidateReceived"/> (spec §1.3b).</summary>
public enum SolreignFxCueValidateFailureReason : byte
{
    None = 0,
    SchemaVersionMismatch,
    EffectIdRejected,
    NonFiniteNumericField,
    OutOfRangeNumericField,
    AnchorMissingOrBothSet,
    AnchorEntityUnresolvable,
    AnchorCoordinatesUnresolvable,
    AnchorNonFiniteOrOutOfCeiling,

    /// <summary>Same rationale as <see cref="SolreignFxCueCreateFailureReason.MalformedPrototypeBounds"/>, client-side.</summary>
    MalformedPrototypeBounds,
}

/// <summary>
///     Resolves a <see cref="SolreignFxCueV1.EffectId"/> string against locally loaded prototypes.
///     Kept as an abstraction (rather than a direct <c>IPrototypeManager</c> dependency) so
///     <see cref="SolreignFxCueV1.TryCreate"/>/<c>TryValidateReceived</c> stay engine-free and
///     directly unit-testable; the server system (W2) and client system (W2) each supply a real
///     implementation backed by <c>IPrototypeManager.TryIndex</c>.
///
///     Contract: implementations MUST NOT throw for any <paramref name="effectId"/> input,
///     including unknown/malformed strings — return <c>false</c> instead (grk adversarial review
///     finding #6: <c>TryValidateReceived</c>'s total, exception-free contract is only as strong
///     as the real resolver W2 wires in honoring this).
/// </summary>
public interface ISolreignFxCuePrototypeSource
{
    /// <summary>Never throws — returns <c>false</c> for any unresolvable <paramref name="effectId"/>, including malformed/empty strings.</summary>
    bool TryResolve(string effectId, out SolreignFxCuePrototype? prototype);
}

/// <summary>
///     Resolves anchor validity — coordinates OR an entity anchor, each to a finite world-space
///     position (spec §1.3a.2, §1.3b.4). Both anchor kinds get the SAME ceiling/finiteness
///     scrutiny — an entity anchor is not a lesser-checked path just because "the entity exists"
///     is easier to confirm than "the coordinates resolve" (grk adversarial review of this
///     worktree, finding H2: the original shape only confirmed entity existence, never the
///     entity's actual world position, letting a hostile server anchor a cue to an entity with a
///     non-finite or absurd transform and skip the exact ceiling coordinates enforce). Same
///     engine-decoupling rationale as <see cref="ISolreignFxCuePrototypeSource"/>: the real
///     implementation (server-side <c>ITransformSystem</c>/<c>IEntityManager</c>-backed, client-side
///     the same via <c>TryGetEntity</c>/<c>TryResolve</c>) is supplied by W2's systems.
///
///     Contract: implementations MUST NOT throw for any input, including a deleted/never-existed
///     <see cref="NetEntity"/> or nullspace/unloaded-map <see cref="NetCoordinates"/> — return
///     <c>false</c> instead. <see cref="SolreignFxCueV1.TryValidateReceived"/>'s total,
///     exception-free contract depends on this holding for whatever real resolver W2 wires in.
/// </summary>
public interface ISolreignFxAnchorResolver
{
    /// <summary>Resolves <paramref name="coordinates"/> to a finite world-space position on a live, loaded map/grid. False for nullspace/unloaded/non-finite. Never throws.</summary>
    bool TryResolveCoordinatesWorldPosition(NetCoordinates coordinates, out Vector2 worldPosition);

    /// <summary>Resolves <paramref name="entity"/>'s CURRENT world-space position. False if the entity doesn't exist, has no transform, or resolves to a non-finite/unloaded-map position. Never throws.</summary>
    bool TryResolveEntityWorldPosition(NetEntity entity, out Vector2 worldPosition);
}

/// <summary>
///     Server-assigned seed/correlation-id source for <see cref="SolreignFxCueV1.TryCreate"/> (spec
///     §1.3a.5). There is deliberately no caller parameter for any of these on <c>TryCreate</c> —
///     callers never supply their own. The real implementation (W2's tiny
///     <c>SolreignFxDiagnosticsSystem</c>) wraps <c>IRobustRandom</c> for the seed and a per-round
///     monotonic counter, reset at round start, for each <see cref="SolreignFxCueStream"/>
///     namespace.
/// </summary>
public interface ISolreignFxCorrelationSource
{
    /// <summary>A fresh raw seed. Broadcast and detail cues of one logical emit MUST call this independently (spec §1.2 — never share a seed between the two).</summary>
    uint NextSeed();

    /// <summary>Next id from the broadcast-stream counter namespace.</summary>
    uint NextBroadcastCorrelationId();

    /// <summary>Next id from the targeted-stream counter namespace.</summary>
    uint NextTargetedCorrelationId();
}

/// <summary>
///     The FX Language v1 semantic cue (spec §1). Says WHAT happened, WHERE, HOW STRONG, HOW LONG,
///     and with WHAT diagnostic handle — never HOW to render it. Generalizes the shape already
///     shipped by <see cref="SolreignScreenFxEvent"/> and <c>ProvidenceVoiceSystem.PlayLineTo</c>
///     into one reusable contract.
///
///     <see cref="TryCreate"/> is producer ergonomics, NOT the wire trust boundary (spec §1.3,
///     cdx #1's core reframe): a private constructor stops ordinary callers, but reflection,
///     serializer-level construction, or a buggy/malicious server-side assembly can bypass it
///     entirely. <see cref="TryValidateReceived"/> is the actual trust boundary — a total,
///     exception-free revalidation of every field the stock client runs before touching any
///     prototype, transform, allocation, or renderer.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignFxCueV1 : EntityEventArgs
{
    /// <summary>Wire schema version. Always 1 for this type — a breaking change ships as a new sealed <c>SolreignFxCueV2</c> type (spec §1.4), never a mutation of this field's meaning.</summary>
    public const byte CurrentSchemaVersion = 1;

    /// <summary>
    ///     The as-received/as-constructed schema version. A REAL serialized field (not just a
    ///     compile-time constant) precisely so a hostile or buggy server can send a wrong value and
    ///     <see cref="TryValidateReceived"/> has something on the wire to check (spec §1.4/§1.3b.1).
    ///     Every construction path this type exposes burns in <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    public readonly byte SchemaVersion = CurrentSchemaVersion;

    /// <summary>Allowlisted effect identifier — must resolve AND be a <see cref="SolreignFxWireAllowlist"/> member (spec §1.3a.1).</summary>
    public readonly ProtoId<SolreignFxCuePrototype> EffectId;

    /// <summary>Coordinate anchor. Exactly one of this and <see cref="EntityAnchor"/> is populated.</summary>
    public readonly NetCoordinates? Coordinates;

    /// <summary>Entity anchor. Exactly one of this and <see cref="Coordinates"/> is populated.</summary>
    public readonly NetEntity? EntityAnchor;

    /// <summary>
    ///     Server-assigned deterministic seed driving client-local pseudo-random cosmetic variation.
    ///     Renderer code MUST derive indices via <see cref="SolreignFxSeedMixing.Index"/> — never
    ///     <c>Math.Abs(seed) % n</c> or signed-modulo indexing (spec §1.2, cdx #18).
    /// </summary>
    public readonly uint Seed;

    /// <summary>Normalized 0.0-1.0 intensity, clamped to the resolved prototype's bounds at construction.</summary>
    public readonly float Intensity;

    /// <summary>Scale multiplier, clamped to the resolved prototype's bounds.</summary>
    public readonly float Scale;

    /// <summary>Duration in seconds, clamped to the resolved prototype's bounds.</summary>
    public readonly float Duration;

    /// <summary>Optional palette entry index. Null means "use the prototype's declared default."</summary>
    public readonly byte? PaletteIndex;

    /// <summary>Optional phase entry index. Null means "use the prototype's declared default." Orthogonal to <see cref="PaletteIndex"/> by contract (spec §1.5, cdx #12) — never tuple-indexed.</summary>
    public readonly byte? Phase;

    /// <summary>Server-generated monotonic diagnostics id, salted per round, from the stream-appropriate counter namespace (spec §1.3a.5). Never derived from identity/role.</summary>
    public readonly uint CorrelationId;

    private SolreignFxCueV1(
        ProtoId<SolreignFxCuePrototype> effectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        uint seed,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        uint correlationId)
    {
        EffectId = effectId;
        Coordinates = coordinates;
        EntityAnchor = entityAnchor;
        Seed = seed;
        Intensity = intensity;
        Scale = scale;
        Duration = duration;
        PaletteIndex = paletteIndex;
        Phase = phase;
        CorrelationId = correlationId;
    }

    /// <summary>
    ///     TEST-ONLY escape hatch. On the real wire, a <c>[NetSerializable]</c> type is populated by
    ///     the serializer directly writing/reading each field by reflection — it never goes through
    ///     a constructor or field initializer. That means a hostile or buggy server's serialized
    ///     bytes really can produce a <see cref="SchemaVersion"/> other than
    ///     <see cref="CurrentSchemaVersion"/>, or any other field combination, regardless of what
    ///     every in-process constructor enforces. This overload models that precisely — including
    ///     letting <see cref="SchemaVersion"/> vary, which no other constructor path permits — so
    ///     <c>TryValidateReceived</c>'s fuzz suite (spec §7's "wire validator fuzz" row) can exercise
    ///     it without a live client/server harness. Gated by
    ///     <c>[InternalsVisibleTo("Content.Tests")]</c> (<c>Content.Shared/AssemblyInfo.cs</c>) —
    ///     Content.Server/Content.Client do NOT get that visibility, so production code can never
    ///     call this.
    /// </summary>
    internal SolreignFxCueV1(
        byte schemaVersion,
        ProtoId<SolreignFxCuePrototype> effectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        uint seed,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        uint correlationId)
        : this(effectId, coordinates, entityAnchor, seed, intensity, scale, duration, paletteIndex, phase, correlationId)
    {
        // A readonly field may be reassigned in any instance constructor of its declaring type,
        // including one reached via constructor chaining — this is the one legal way to make
        // SchemaVersion anything other than CurrentSchemaVersion.
        SchemaVersion = schemaVersion;
    }

    /// <summary>
    ///     Server-side producer factory (spec §1.3a). Performs, in order: allowlist+manifest
    ///     membership, anchor exclusivity/validity, finiteness-then-bounds numeric validation
    ///     (hard-reject NaN/Inf and &gt;10x-out-of-range, clamp small drift), and palette/phase
    ///     fallback-to-declared-default. Seed and CorrelationId are always server-assigned here —
    ///     there is no caller parameter for either. Returns a structured failure reason on
    ///     rejection, never a bare bool (cdx #15).
    /// </summary>
    public static bool TryCreate(
        string effectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        ISolreignFxCuePrototypeSource prototypeSource,
        ISolreignFxAnchorResolver anchorResolver,
        ISolreignFxCorrelationSource correlationSource,
        SolreignFxCueStream stream,
        out SolreignFxCueV1? cue,
        out SolreignFxCueCreateFailureReason failureReason)
    {
        cue = null;

        // 1. EffectId resolution AND allowlist-manifest membership — both required (spec §1.3a.1, cdx #3).
        if (!SolreignFxWireAllowlist.IsAllowlisted(effectId))
        {
            failureReason = SolreignFxCueCreateFailureReason.EffectIdNotAllowlisted;
            return false;
        }

        if (!prototypeSource.TryResolve(effectId, out var prototype) || prototype is null)
        {
            failureReason = SolreignFxCueCreateFailureReason.EffectIdNotResolved;
            return false;
        }

        // 2. Anchor exclusivity and validity (spec §1.3a.2, cdx #6).
        if (!TryValidateAnchor(coordinates, entityAnchor, anchorResolver, out var anchorFailure))
        {
            failureReason = anchorFailure;
            return false;
        }

        // 3. Finiteness gate, then bounds clamp (spec §1.3a.3, cdx #2/#13) — each field's hardcoded
        //    wire ceiling (spec §1.5) is dual-enforced here too (grk review finding M3), never
        //    trusting the resolved prototype's own bounds are well-formed.
        if (!TryClampNumericField(intensity, prototype.MinIntensity, prototype.MaxIntensity,
                SolreignFxCuePrototypeValidation.WireIntensityMin, SolreignFxCuePrototypeValidation.WireIntensityMax,
                out var clampedIntensity, out var numericFailure)
            || !TryClampNumericField(scale, prototype.MinScale, prototype.MaxScale,
                SolreignFxCuePrototypeValidation.WireScaleMin, SolreignFxCuePrototypeValidation.WireScaleMax,
                out var clampedScale, out numericFailure)
            || !TryClampNumericField(duration, prototype.MinDuration, prototype.MaxDuration,
                SolreignFxCuePrototypeValidation.WireDurationMin, SolreignFxCuePrototypeValidation.WireDurationMax,
                out var clampedDuration, out numericFailure))
        {
            failureReason = numericFailure;
            return false;
        }

        // 4. Palette/Phase validation — out-of-range replaces with the prototype's declared default, never rejects (spec §1.3a.4, cdx #11).
        var resolvedPalette = ResolvePaletteOrPhase(paletteIndex, prototype.PaletteCount, prototype.DefaultPaletteIndex);
        var resolvedPhase = ResolvePaletteOrPhase(phase, prototype.PhaseCount, prototype.DefaultPhaseIndex);

        // 5. Seed and CorrelationId are always server-assigned (spec §1.3a.5) — independent counter
        //    namespaces per stream so the broadcast sequence stays gap-free (grk #6).
        var seed = correlationSource.NextSeed();
        var correlationId = stream == SolreignFxCueStream.Broadcast
            ? correlationSource.NextBroadcastCorrelationId()
            : correlationSource.NextTargetedCorrelationId();

        cue = new SolreignFxCueV1(
            effectId,
            coordinates,
            entityAnchor,
            seed,
            clampedIntensity,
            clampedScale,
            clampedDuration,
            resolvedPalette,
            resolvedPhase,
            correlationId);
        failureReason = SolreignFxCueCreateFailureReason.None;
        return true;
    }

    /// <summary>
    ///     Client-side trust boundary (spec §1.3b). Total and exception-free over ANY payload a
    ///     hostile or inconsistent server can serialize — malformed input must never throw, only
    ///     reject. Covers steps 1-5 of §1.3b; step 6 (atomic budget-lease acquisition) is NOT part
    ///     of this method — no <c>SolreignFxLeaseManager</c> exists yet (that's W2's file boundary),
    ///     so lease acquisition is the caller's job once W2 lands. Returns a re-validated cue (with
    ///     palette/phase possibly substituted to the local prototype's declared default) rather than
    ///     the raw input, so a caller can never accidentally use the untrusted original.
    /// </summary>
    public static bool TryValidateReceived(
        SolreignFxCueV1? received,
        ISolreignFxCuePrototypeSource prototypeSource,
        ISolreignFxAnchorResolver anchorResolver,
        out SolreignFxCueV1? validated,
        out SolreignFxCueValidateFailureReason failureReason)
    {
        validated = null;

        if (received is null)
        {
            failureReason = SolreignFxCueValidateFailureReason.EffectIdRejected;
            return false;
        }

        // 1. Exact-equality schema version check (spec §1.3b.1, cdx #17 — a one-sided "<= known max" lets a forged version 0 slip through).
        if (received.SchemaVersion != CurrentSchemaVersion)
        {
            failureReason = SolreignFxCueValidateFailureReason.SchemaVersionMismatch;
            return false;
        }

        // 2. EffectId must resolve locally AND be allowlist-compiled-in (spec §1.3b.2).
        var effectIdString = received.EffectId.Id;
        if (!SolreignFxWireAllowlist.IsAllowlisted(effectIdString)
            || !prototypeSource.TryResolve(effectIdString, out var prototype)
            || prototype is null)
        {
            failureReason = SolreignFxCueValidateFailureReason.EffectIdRejected;
            return false;
        }

        // 3. Full finiteness + bounds re-validation against the LOCAL prototype — never trusting
        //    the server clamped (spec §1.3b.3). Each field's hardcoded wire ceiling (spec §1.5) is
        //    dual-enforced here too (grk review finding M3), never trusting the local prototype's
        //    own bounds are well-formed.
        if (!IsWithinLocalBounds(received.Intensity, prototype.MinIntensity, prototype.MaxIntensity,
                SolreignFxCuePrototypeValidation.WireIntensityMin, SolreignFxCuePrototypeValidation.WireIntensityMax,
                out var numericFailure)
            || !IsWithinLocalBounds(received.Scale, prototype.MinScale, prototype.MaxScale,
                SolreignFxCuePrototypeValidation.WireScaleMin, SolreignFxCuePrototypeValidation.WireScaleMax,
                out numericFailure)
            || !IsWithinLocalBounds(received.Duration, prototype.MinDuration, prototype.MaxDuration,
                SolreignFxCuePrototypeValidation.WireDurationMin, SolreignFxCuePrototypeValidation.WireDurationMax,
                out numericFailure))
        {
            failureReason = numericFailure;
            return false;
        }

        // 4. Anchor re-resolution via the local resolver — never an unconditional lookup (spec §1.3b.4).
        if (!TryValidateAnchorForReceive(received.Coordinates, received.EntityAnchor, anchorResolver, out var anchorFailure))
        {
            failureReason = anchorFailure;
            return false;
        }

        // 5. Palette/Phase validated against the LOCAL prototype's actual counts; out-of-range falls back to the declared default, never rejects (spec §1.3b.5).
        var resolvedPalette = ResolvePaletteOrPhase(received.PaletteIndex, prototype.PaletteCount, prototype.DefaultPaletteIndex);
        var resolvedPhase = ResolvePaletteOrPhase(received.Phase, prototype.PhaseCount, prototype.DefaultPhaseIndex);

        validated = new SolreignFxCueV1(
            received.EffectId,
            received.Coordinates,
            received.EntityAnchor,
            received.Seed,
            received.Intensity,
            received.Scale,
            received.Duration,
            resolvedPalette,
            resolvedPhase,
            received.CorrelationId);
        failureReason = SolreignFxCueValidateFailureReason.None;
        return true;
    }

    private static bool TryValidateAnchor(
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        ISolreignFxAnchorResolver anchorResolver,
        out SolreignFxCueCreateFailureReason failureReason)
    {
        if (coordinates.HasValue == entityAnchor.HasValue)
        {
            // Both set, or both unset — exactly one is required (spec §1.3a.2).
            failureReason = SolreignFxCueCreateFailureReason.AnchorMissingOrBothSet;
            return false;
        }

        if (entityAnchor.HasValue)
        {
            // Same finiteness + world-ceiling scrutiny as the coordinate path below (grk review
            // finding H2) — confirming the entity merely EXISTS is not enough; a hostile server
            // could anchor to an entity whose transform resolves to a non-finite or absurd world
            // position and skip the exact ceiling coordinates enforce.
            if (!anchorResolver.TryResolveEntityWorldPosition(entityAnchor.Value, out var entityWorld)
                || !float.IsFinite(entityWorld.X) || !float.IsFinite(entityWorld.Y))
            {
                failureReason = SolreignFxCueCreateFailureReason.AnchorEntityInvalid;
                return false;
            }

            if (!IsWithinWorldCeiling(entityWorld))
            {
                failureReason = SolreignFxCueCreateFailureReason.AnchorCoordinatesOutOfCeiling;
                return false;
            }

            failureReason = SolreignFxCueCreateFailureReason.None;
            return true;
        }

        // coordinates.HasValue is guaranteed true here.
        var coords = coordinates!.Value;
        if (!float.IsFinite(coords.Position.X) || !float.IsFinite(coords.Position.Y))
        {
            failureReason = SolreignFxCueCreateFailureReason.AnchorCoordinatesInvalid;
            return false;
        }

        if (!anchorResolver.TryResolveCoordinatesWorldPosition(coords, out var world)
            || !float.IsFinite(world.X) || !float.IsFinite(world.Y))
        {
            failureReason = SolreignFxCueCreateFailureReason.AnchorCoordinatesInvalid;
            return false;
        }

        if (!IsWithinWorldCeiling(world))
        {
            failureReason = SolreignFxCueCreateFailureReason.AnchorCoordinatesOutOfCeiling;
            return false;
        }

        failureReason = SolreignFxCueCreateFailureReason.None;
        return true;
    }

    private static bool TryValidateAnchorForReceive(
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        ISolreignFxAnchorResolver anchorResolver,
        out SolreignFxCueValidateFailureReason failureReason)
    {
        if (coordinates.HasValue == entityAnchor.HasValue)
        {
            failureReason = SolreignFxCueValidateFailureReason.AnchorMissingOrBothSet;
            return false;
        }

        if (entityAnchor.HasValue)
        {
            // Same finiteness + world-ceiling scrutiny as the coordinate path below (grk review
            // finding H2) — a deleted/never-existed entity AND an entity whose transform resolves
            // to a non-finite or out-of-ceiling position both fail here; per-frame re-`TryGet`
            // for the cue's lifetime (spec §1.3b.4) is the caller's job once W2's lease manager
            // exists, same as any other anchor re-resolution.
            if (!anchorResolver.TryResolveEntityWorldPosition(entityAnchor.Value, out var entityWorld)
                || !float.IsFinite(entityWorld.X) || !float.IsFinite(entityWorld.Y))
            {
                failureReason = SolreignFxCueValidateFailureReason.AnchorEntityUnresolvable;
                return false;
            }

            if (!IsWithinWorldCeiling(entityWorld))
            {
                failureReason = SolreignFxCueValidateFailureReason.AnchorNonFiniteOrOutOfCeiling;
                return false;
            }

            failureReason = SolreignFxCueValidateFailureReason.None;
            return true;
        }

        var coords = coordinates!.Value;
        if (!float.IsFinite(coords.Position.X) || !float.IsFinite(coords.Position.Y))
        {
            failureReason = SolreignFxCueValidateFailureReason.AnchorNonFiniteOrOutOfCeiling;
            return false;
        }

        if (!anchorResolver.TryResolveCoordinatesWorldPosition(coords, out var world)
            || !float.IsFinite(world.X) || !float.IsFinite(world.Y))
        {
            failureReason = SolreignFxCueValidateFailureReason.AnchorCoordinatesUnresolvable;
            return false;
        }

        if (!IsWithinWorldCeiling(world))
        {
            failureReason = SolreignFxCueValidateFailureReason.AnchorNonFiniteOrOutOfCeiling;
            return false;
        }

        failureReason = SolreignFxCueValidateFailureReason.None;
        return true;
    }

    /// <summary>Hardcoded world-space ceiling (spec §1.3a.2): far beyond any real station, cheap to check.</summary>
    public const float WorldCoordinateCeiling = 16384f;

    private static bool IsWithinWorldCeiling(Vector2 world)
    {
        return world.X is >= -WorldCoordinateCeiling and <= WorldCoordinateCeiling
               && world.Y is >= -WorldCoordinateCeiling and <= WorldCoordinateCeiling;
    }

    /// <summary>
    ///     Server-side finiteness-then-bounds-clamp for one numeric field (spec §1.3a.3). NaN/Inf
    ///     hard-reject (cdx #2/#13 — comparison-based clamps let NaN through since both
    ///     comparisons are false). Negative values hard-reject (all three fields are non-negative
    ///     by contract). Values &gt;10x the prototype's Max hard-reject (cdx #13 — a
    ///     milliseconds-vs-seconds caller bug must fail loudly, not silently ship at max cost).
    ///     Only small drift within 10x clamps into [min, max].
    ///
    ///     Also defends against a MALFORMED resolved prototype (grk review finding M3): if the
    ///     prototype's own [min,max] is non-finite, inverted, or outside the hardcoded
    ///     [<paramref name="wireMin"/>, <paramref name="wireMax"/>] ceiling, this hard-rejects
    ///     BEFORE ever comparing <paramref name="value"/> against it — a NaN bound would otherwise
    ///     make `value &lt; min || value &gt; max`-style comparisons silently false for every
    ///     input, accepting anything. §1.5's load-time validation is the intended first line of
    ///     defense (disables a prototype this broken before it's ever resolved here), but that
    ///     hook isn't wired until a later worktree — this is the second, independent line.
    /// </summary>
    private static bool TryClampNumericField(float value, float min, float max, float wireMin, float wireMax, out float clamped, out SolreignFxCueCreateFailureReason failureReason)
    {
        clamped = 0f;

        if (!float.IsFinite(min) || !float.IsFinite(max) || min > max || min < wireMin || max > wireMax)
        {
            failureReason = SolreignFxCueCreateFailureReason.MalformedPrototypeBounds;
            return false;
        }

        if (!float.IsFinite(value))
        {
            failureReason = SolreignFxCueCreateFailureReason.NonFiniteNumericField;
            return false;
        }

        if (value < 0f)
        {
            failureReason = SolreignFxCueCreateFailureReason.NegativeNumericField;
            return false;
        }

        // Guard against a zero/negative Max (should be impossible post-§1.5 load validation, but
        // TryCreate never trusts that as its only line of defense).
        var grossCeiling = max > 0f ? max * 10f : max;
        if (value > grossCeiling)
        {
            failureReason = SolreignFxCueCreateFailureReason.GrosslyOutOfRangeNumericField;
            return false;
        }

        clamped = Math.Clamp(value, min, max);
        failureReason = SolreignFxCueCreateFailureReason.None;
        return true;
    }

    /// <summary>
    ///     Client-side re-validation for one numeric field (spec §1.3b.3): finite AND within the
    ///     LOCAL prototype's bounds, or reject outright. Unlike the server's clamp-small-drift
    ///     behavior, the client never repairs a numeric field — TryValidateReceived's contract for
    ///     these three fields is binary accept/reject, never silent repair (repair is reserved for
    ///     palette/phase, which the spec explicitly calls out as a fallback case).
    ///
    ///     Also defends against a malformed LOCAL prototype (grk review finding M3, same rationale
    ///     as the server-side twin in <see cref="TryClampNumericField"/>): a resolved prototype
    ///     whose own bounds are non-finite, inverted, or outside the hardcoded
    ///     [<paramref name="wireMin"/>, <paramref name="wireMax"/>] ceiling is rejected before the
    ///     value is ever compared against it — never silently "everything passes" via a NaN bound.
    /// </summary>
    private static bool IsWithinLocalBounds(float value, float min, float max, float wireMin, float wireMax, out SolreignFxCueValidateFailureReason failureReason)
    {
        if (!float.IsFinite(min) || !float.IsFinite(max) || min > max || min < wireMin || max > wireMax)
        {
            failureReason = SolreignFxCueValidateFailureReason.MalformedPrototypeBounds;
            return false;
        }

        if (!float.IsFinite(value))
        {
            failureReason = SolreignFxCueValidateFailureReason.NonFiniteNumericField;
            return false;
        }

        if (value < min || value > max)
        {
            failureReason = SolreignFxCueValidateFailureReason.OutOfRangeNumericField;
            return false;
        }

        failureReason = SolreignFxCueValidateFailureReason.None;
        return true;
    }

    /// <summary>
    ///     Out-of-range (or absent) falls back to the prototype's declared default entry — never a
    ///     bare index 0 (spec §1.3a.4/§1.3b.5, cdx #11). Shared by both TryCreate and
    ///     TryValidateReceived since the fallback rule is identical on both sides.
    /// </summary>
    private static byte ResolvePaletteOrPhase(byte? index, int declaredCount, byte declaredDefault)
    {
        if (index.HasValue && declaredCount > 0 && index.Value < declaredCount)
            return index.Value;

        return declaredDefault;
    }
}
