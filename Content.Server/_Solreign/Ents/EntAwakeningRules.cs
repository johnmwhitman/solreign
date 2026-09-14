namespace Content.Server._Solreign.Ents;

/// <summary>
///     Pure roll-vs-chance math for <see cref="SolreignDormantEntSystem"/>'s rare ambient
///     awakening. Kept free of IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/EntAwakeningRulesTests.cs), mirroring how
///     <c>Content.Server._Solreign.Effects.PeriodicEffectTiming</c> and
///     <c>Content.Server._Solreign.Pets.TamingRules</c> isolate pure logic elsewhere in _Solreign.
/// </summary>
public static class EntAwakeningRules
{
    /// <summary>
    ///     Whether a single awaken-chance roll succeeds. <paramref name="roll"/> is expected to be
    ///     a uniform sample in [0, 1) (e.g. <c>IRobustRandom.NextDouble()</c>). A roll strictly less
    ///     than <paramref name="chance"/> succeeds; an exact tie does not. Misconfigured chance
    ///     values coming from YAML are clamped into [0, 1] rather than thrown, matching
    ///     <c>PeriodicEffectTiming</c>'s sanitize-don't-throw approach to YAML-sourced numbers.
    /// </summary>
    public static bool ShouldAwaken(double roll, float chance)
    {
        var clamped = Math.Clamp(chance, 0f, 1f);

        // Compare in float precision, not double: widening `clamped` (float) up to double instead
        // would make e.g. 0.05f compare as ~0.050000000745 (larger than the double literal 0.05),
        // silently breaking the "exact tie doesn't awaken" contract for any roll that was meant to
        // land exactly on chance. Narrowing `roll` down to float keeps both sides in the same
        // precision domain chance is declared in.
        return (float) roll < clamped;
    }
}
