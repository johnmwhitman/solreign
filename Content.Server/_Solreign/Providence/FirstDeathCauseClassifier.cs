using System.Collections.Generic;
using Content.Shared.FixedPoint;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     The five flavor families the first-death copy pack selects on
///     (docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §3.3). Ordinal values are FIXED by the copy
///     pack's deterministic index rule (§8B: VIOLENCE:0, VACUUM:1, BURN:2, MISADVENTURE:3, UNKNOWN:4)
///     — never reorder.
/// </summary>
public enum FirstDeathCause : byte
{
    Violence = 0,
    Vacuum = 1,
    Burn = 2,
    Misadventure = 3,
    Unknown = 4,
}

/// <summary>
///     Pure, table-driven death-cause classifier for the authored first-death scene — zero I/O, zero
///     engine dependencies, directly unit-tested (Content.Tests/_Solreign/FirstDeathCauseClassifierTests.cs).
///
///     Rules, first match wins (spec §3.3):
///       1. an attacker was present → <see cref="FirstDeathCause.Violence"/>
///       2. empty / all-zero damage snapshot → <see cref="FirstDeathCause.Unknown"/>
///       3. dominant damage type ∈ {Asphyxiation, Cold} → <see cref="FirstDeathCause.Vacuum"/>
///       4. dominant damage type is Blunt AND Asphyxiation is present → <see cref="FirstDeathCause.Vacuum"/>
///          (the spec's [needs verification] tag, RESOLVED at build: crew barotrauma deals BLUNT —
///          Resources/Prototypes/Body/species_base.yml's Barotrauma block is `Blunt: 0.50`, scaled by
///          Atmospherics.LowPressureDamage = 4 → 2 Blunt/s in hard vacuum, which routinely out-accumulates
///          the respirator's Asphyxiation. A genuine spacing ALWAYS also accrues Asphyxiation (no air to
///          breathe), while a no-attacker crushing/fall in a pressurized hall does not — so
///          Blunt-dominant + Asphyxiation-present + no attacker is the vacuum signature, and
///          Blunt-dominant alone stays MISADVENTURE.)
///       5. dominant damage type ∈ {Heat, Shock} → <see cref="FirstDeathCause.Burn"/>
///       6. anything else (Poison, Radiation, Bloodloss, Blunt-no-attacker, Caustic, …) →
///          <see cref="FirstDeathCause.Misadventure"/>
///
///     "Dominant" = highest snapshot value; on an exact tie the bucket preference is
///     VACUUM &gt; BURN &gt; MISADVENTURE (checked in rule order above), which keeps the result
///     deterministic without caring which tied key a dictionary happens to enumerate first.
///
///     The classifier can be wrong in edge cases (a poisoned player who then suffocates). Acceptable
///     by design: the cause only selects a flavor family, and every template survives
///     misclassification without being false — the copy never asserts forensic facts (spec §8).
///
///     Confidentiality rule: no output of this classifier ever names or describes the attacker; the
///     template set has no <c>{attacker}</c> variable at all (closed vocabulary, spec §3.3).
/// </summary>
public static class FirstDeathCauseClassifier
{
    // Damage type prototype ids, verified against Resources/Prototypes/Damage/types.yml.
    private const string Asphyxiation = "Asphyxiation";
    private const string Cold = "Cold";
    private const string Heat = "Heat";
    private const string Shock = "Shock";
    private const string Blunt = "Blunt";

    public static FirstDeathCause Classify(bool attackerPresent, IReadOnlyDictionary<string, FixedPoint2>? damage)
    {
        if (attackerPresent)
            return FirstDeathCause.Violence;

        if (damage is null || damage.Count == 0)
            return FirstDeathCause.Unknown;

        // Find the dominant (highest) damage value across the snapshot.
        var max = FixedPoint2.Zero;
        foreach (var value in damage.Values)
        {
            if (value > max)
                max = value;
        }

        // All-zero (or negative-only, defensively) snapshot carries no signal.
        if (max <= FixedPoint2.Zero)
            return FirstDeathCause.Unknown;

        // Bucket preference on ties: VACUUM > BURN > MISADVENTURE, checked in rule order.
        if (IsDominant(damage, max, Asphyxiation) || IsDominant(damage, max, Cold))
            return FirstDeathCause.Vacuum;

        // Barotrauma resolution (see class doc): Blunt-dominant with Asphyxiation present and no
        // attacker is a spacing, not a beating.
        if (IsDominant(damage, max, Blunt)
            && damage.TryGetValue(Asphyxiation, out var asphyx)
            && asphyx > FixedPoint2.Zero)
        {
            return FirstDeathCause.Vacuum;
        }

        if (IsDominant(damage, max, Heat) || IsDominant(damage, max, Shock))
            return FirstDeathCause.Burn;

        return FirstDeathCause.Misadventure;
    }

    private static bool IsDominant(IReadOnlyDictionary<string, FixedPoint2> damage, FixedPoint2 max, string type)
    {
        return damage.TryGetValue(type, out var value) && value == max;
    }
}
