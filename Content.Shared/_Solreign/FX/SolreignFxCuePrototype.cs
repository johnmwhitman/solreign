using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     One entry in the FX Language v1 allowlist — the per-effect bounds, palette/phase shape, and
///     audience classification that <see cref="SolreignFxCueV1.TryCreate"/>/
///     <c>TryValidateReceived</c> resolve against (spec §1, §2). W1 ships the schema shape only;
///     W3's <c>Resources/Prototypes/_Solreign/FX/effects.yml</c> is what actually instantiates the
///     8 primitives + 2 redacted variants named in spec §2/§2.2.
///
///     Palette/phase are modeled here as counts + a validated default index rather than concrete
///     asset lists (texture/shader refs) — those concrete lists don't exist until W3 ships the real
///     sprite/shader resources per primitive. §1.5's "actual length == declared count" check is
///     therefore N/A in W1 (there is only one length, the count itself); W3's consumer wiring is
///     where a real asset list's length gets cross-checked against whatever count the loaded
///     prototype declares.
/// </summary>
[Prototype]
public sealed partial class SolreignFxCuePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Whether this id may be raised broadcast directly, or only as the targeted detail half of
    ///     a secret-role pair (spec §5.2). Mirrored in <see cref="SolreignFxWireAllowlist.AudienceClassificationV1"/>;
    ///     a consistency test in W1 pins the two to agree.
    /// </summary>
    [DataField]
    public SolreignFxAudienceClassification AudienceClassification = SolreignFxAudienceClassification.Broadcast;

    /// <summary>
    ///     Whether emitting this id requires the generic+detail broadcast split (spec §5, §5.2) —
    ///     <c>transformation</c> sets this in v1. Distinct from <see cref="AudienceClassification"/>:
    ///     a <see cref="SolreignFxAudienceClassification.Broadcast"/>-classified id can still be
    ///     used by a secret-role caller (keyed on caller secrecy, not the primitive), but this flag
    ///     marks ids that structurally can never be raised any other way.
    /// </summary>
    [DataField]
    public bool RequiresRedactedBroadcastVariant;

    /// <summary>The redacted broadcast id used when this id requires the split (e.g. <c>transformation</c> → <c>transformation_generic</c>).</summary>
    [DataField]
    public ProtoId<SolreignFxCuePrototype>? GenericVariant;

    /// <summary>Lower bound for <see cref="SolreignFxCueV1.Intensity"/>. Must be finite and &lt;= <see cref="MaxIntensity"/> (spec §1.5).</summary>
    [DataField(required: true)]
    public float MinIntensity = 0f;

    /// <summary>Upper bound for <see cref="SolreignFxCueV1.Intensity"/>. Hard-capped at the wire ceiling regardless of authored value (spec §1.5).</summary>
    [DataField(required: true)]
    public float MaxIntensity = 1f;

    /// <summary>Lower bound for <see cref="SolreignFxCueV1.Scale"/>.</summary>
    [DataField(required: true)]
    public float MinScale = 0.1f;

    /// <summary>Upper bound for <see cref="SolreignFxCueV1.Scale"/>. Hard-capped at the wire ceiling (spec §1.5: scale ceiling is (0, 8]).</summary>
    [DataField(required: true)]
    public float MaxScale = 1f;

    /// <summary>Lower bound for <see cref="SolreignFxCueV1.Duration"/>. Must be &gt;= <see cref="SolreignFxCuePrototypeValidation.MinDurationFloor"/> (one tick, spec §1.5).</summary>
    [DataField(required: true)]
    public float MinDuration = 0.1f;

    /// <summary>Upper bound for <see cref="SolreignFxCueV1.Duration"/>. Hard-capped at the wire ceiling (spec §1.5: duration ceiling is (0, 30s]).</summary>
    [DataField(required: true)]
    public float MaxDuration = 1f;

    /// <summary>
    ///     [W2 addition] The fixed value the server's <c>RaiseSecretRoleCue</c> raise helper scrubs a
    ///     GENERIC/redacted variant's <see cref="SolreignFxCueV1.Intensity"/> to (spec §5.2 item 1:
    ///     "Intensity/Scale/Duration at the generic prototype's fixed default values, never the
    ///     detail cue's values"). Not used for non-generic prototypes. Must lie within
    ///     [<see cref="MinIntensity"/>, <see cref="MaxIntensity"/>] (validated at load time, same
    ///     rationale as <see cref="DefaultPaletteIndex"/> — never a value that could itself be out
    ///     of the prototype's own declared bounds).
    /// </summary>
    [DataField]
    public float DefaultIntensity = 0.5f;

    /// <summary>Same rationale as <see cref="DefaultIntensity"/>, for <see cref="SolreignFxCueV1.Scale"/>. Must lie within [<see cref="MinScale"/>, <see cref="MaxScale"/>].</summary>
    [DataField]
    public float DefaultScale = 1f;

    /// <summary>Same rationale as <see cref="DefaultIntensity"/>, for <see cref="SolreignFxCueV1.Duration"/>. Must lie within [<see cref="MinDuration"/>, <see cref="MaxDuration"/>].</summary>
    [DataField]
    public float DefaultDuration = 1f;

    /// <summary>
    ///     Declared palette entry count (spec §1.5: non-empty, &lt;= 256, byte-addressable — an
    ///     <c>int</c> field so the value 256 itself is representable, since the byte INDEX that
    ///     addresses entries ranges 0-255). No concrete asset list exists in W1 (see class
    ///     remarks) — this count IS the "declared length" §1.5 validates.
    /// </summary>
    [DataField]
    public int PaletteCount = 1;

    /// <summary>The declared default palette entry used when a received/constructed index is absent or out of range. Must be &lt; <see cref="PaletteCount"/> (spec §1.5, cdx #11 — never a bare index 0).</summary>
    [DataField]
    public byte DefaultPaletteIndex;

    /// <summary>Declared phase entry count. Same shape and rules as <see cref="PaletteCount"/>; palette and phase are orthogonal axes by contract (spec §1.5, cdx #12) — never tuple-indexed.</summary>
    [DataField]
    public int PhaseCount = 1;

    /// <summary>The declared default phase entry. Must be &lt; <see cref="PhaseCount"/>.</summary>
    [DataField]
    public byte DefaultPhaseIndex;
}

/// <summary>
///     Load-time validation rules for <see cref="SolreignFxCuePrototype"/> (spec §1.5). Kept free
///     of <c>IPrototypeManager</c>/engine dependencies so it is directly unit-testable
///     (<c>Content.Tests._Solreign.FX.SolreignFxPrototypeValidationTests</c>), same split as
///     <c>SolreignScreenFxTiming</c>/<c>MovementBobMath</c>. Wiring this into an actual
///     prototype-reload hook that disables a failing prototype (spec §1.5: "the game still boots")
///     is W2/W3's job once a client/server system exists to own that reload subscription — W1 ships
///     the rule set + hardcoded wire ceilings that no prototype or CVar can ever exceed.
/// </summary>
public static class SolreignFxCuePrototypeValidation
{
    /// <summary>Hardcoded, non-configurable wire ceiling for <see cref="SolreignFxCueV1.Intensity"/> (spec §1.5): [0, 1].</summary>
    public const float WireIntensityMin = 0f;
    public const float WireIntensityMax = 1f;

    /// <summary>Hardcoded, non-configurable wire ceiling for <see cref="SolreignFxCueV1.Scale"/> (spec §1.5): (0, 8].</summary>
    public const float WireScaleMin = 0f;
    public const float WireScaleMax = 8f;

    /// <summary>Hardcoded, non-configurable wire ceiling for <see cref="SolreignFxCueV1.Duration"/> (spec §1.5): (0, 30s].</summary>
    public const float WireDurationMin = 0f;
    public const float WireDurationMax = 30f;

    /// <summary>One tick — the floor for any prototype's <see cref="SolreignFxCuePrototype.MinDuration"/> (spec §1.5), so no per-frame division hazard can ship.</summary>
    public const float MinDurationFloor = 0.05f;

    /// <summary>
    ///     The floor for any prototype's <see cref="SolreignFxCuePrototype.MinScale"/> (spec §1.5's
    ///     scale ceiling is the OPEN interval (0, 8] — <see cref="WireScaleMin"/> alone is
    ///     inclusive-zero and does not enforce that). A functionally-zero scale is a degenerate
    ///     render/div hazard for W2/W3 consumers, same rationale as <see cref="MinDurationFloor"/>
    ///     (grk adversarial review finding M4).
    /// </summary>
    public const float MinScaleFloor = 0.01f;

    /// <summary>Byte-addressable ceiling for palette/phase counts (spec §1.5).</summary>
    public const int MaxPaletteOrPhaseCount = 256;

    /// <summary>
    ///     Runs every §1.5 rule against <paramref name="prototype"/>. Returns <c>true</c> (empty
    ///     <paramref name="reasons"/>) only if every rule passes; otherwise every violated rule is
    ///     reported (not just the first) so a bad prototype's error log names everything wrong with
    ///     it at once. Never throws — a null <paramref name="prototype"/> is reported as a reason,
    ///     not a <see cref="NullReferenceException"/>.
    /// </summary>
    public static bool Validate(SolreignFxCuePrototype? prototype, out IReadOnlyList<string> reasons)
    {
        var found = new List<string>();

        if (prototype is null)
        {
            reasons = new[] { "prototype is null" };
            return false;
        }

        ValidateBounds(prototype.MinIntensity, prototype.MaxIntensity, WireIntensityMin, WireIntensityMax, "Intensity", found);
        ValidateBounds(prototype.MinScale, prototype.MaxScale, WireScaleMin, WireScaleMax, "Scale", found);
        ValidateBounds(prototype.MinDuration, prototype.MaxDuration, WireDurationMin, WireDurationMax, "Duration", found);

        if (float.IsFinite(prototype.MinDuration) && prototype.MinDuration < MinDurationFloor)
            found.Add($"MinDuration ({prototype.MinDuration}) is below the one-tick floor ({MinDurationFloor})");

        if (float.IsFinite(prototype.MinScale) && prototype.MinScale < MinScaleFloor)
            found.Add($"MinScale ({prototype.MinScale}) is below the minimum scale floor ({MinScaleFloor}) — spec §1.5's scale ceiling is the open interval (0, 8]");

        // [W2 addition] The three "fixed default" fields RaiseSecretRoleCue's generic-cue scrub
        // reads (spec §5.2 item 1) must themselves lie within the prototype's own declared bounds —
        // same "never let the fallback itself be out of range" rule cdx #11 already established for
        // palette/phase defaults, applied to the three numeric defaults.
        ValidateDefaultWithinBounds(prototype.DefaultIntensity, prototype.MinIntensity, prototype.MaxIntensity, "Intensity", found);
        ValidateDefaultWithinBounds(prototype.DefaultScale, prototype.MinScale, prototype.MaxScale, "Scale", found);
        ValidateDefaultWithinBounds(prototype.DefaultDuration, prototype.MinDuration, prototype.MaxDuration, "Duration", found);

        ValidatePaletteOrPhase(prototype.PaletteCount, prototype.DefaultPaletteIndex, "Palette", found);
        ValidatePaletteOrPhase(prototype.PhaseCount, prototype.DefaultPhaseIndex, "Phase", found);

        reasons = found;
        return found.Count == 0;
    }

    private static void ValidateDefaultWithinBounds(float defaultValue, float min, float max, string fieldName, List<string> reasons)
    {
        if (!float.IsFinite(defaultValue))
        {
            reasons.Add($"Default{fieldName} is not finite ({defaultValue})");
            return;
        }

        // Bounds themselves may already be malformed (reported above); only compare when they're
        // sane, so one root problem doesn't cascade into a second misleading message.
        if (!float.IsFinite(min) || !float.IsFinite(max) || min > max)
            return;

        if (defaultValue < min || defaultValue > max)
            reasons.Add($"Default{fieldName} ({defaultValue}) is outside [Min{fieldName}, Max{fieldName}] ([{min}, {max}])");
    }

    private static void ValidateBounds(float min, float max, float wireMin, float wireMax, string fieldName, List<string> reasons)
    {
        if (!float.IsFinite(min))
        {
            reasons.Add($"Min{fieldName} is not finite ({min})");
            return;
        }

        if (!float.IsFinite(max))
        {
            reasons.Add($"Max{fieldName} is not finite ({max})");
            return;
        }

        if (min > max)
            reasons.Add($"Min{fieldName} ({min}) is greater than Max{fieldName} ({max}) — inverted bounds");

        // The wire ceiling is a floor+ceiling that NO prototype may exceed, independent of whether
        // Min<=Max holds — both ends are checked so an inverted-AND-out-of-ceiling prototype
        // reports both problems.
        if (max > wireMax)
            reasons.Add($"Max{fieldName} ({max}) exceeds the hardcoded wire ceiling ({wireMax})");

        if (min < wireMin)
            reasons.Add($"Min{fieldName} ({min}) is below the hardcoded wire floor ({wireMin})");
    }

    private static void ValidatePaletteOrPhase(int count, byte defaultIndex, string axisName, List<string> reasons)
    {
        if (count <= 0)
        {
            reasons.Add($"{axisName}Count is non-positive ({count}) — palette/phase lists must be non-empty");
            return;
        }

        if (count > MaxPaletteOrPhaseCount)
            reasons.Add($"{axisName}Count ({count}) exceeds the byte-addressable ceiling ({MaxPaletteOrPhaseCount})");

        if (defaultIndex >= count)
            reasons.Add($"Default{axisName}Index ({defaultIndex}) is not less than {axisName}Count ({count})");
    }
}
