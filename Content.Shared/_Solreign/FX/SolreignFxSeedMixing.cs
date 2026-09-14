namespace Content.Shared._Solreign.FX;

/// <summary>
///     The frozen seed-mixing function for <see cref="SolreignFxCueV1.Seed"/> (spec §1.2, cdx #18).
///     The wire field is a raw <c>uint</c> — renderer code is PROHIBITED from
///     <c>Math.Abs(seed) % n</c> or signed-modulo indexing (both are corruption hazards: unsigned
///     bits reinterpreted as a negative <c>int</c> make <c>Math.Abs</c> undefined at
///     <c>int.MinValue</c>, and signed modulo can return a negative result). <see cref="Index"/> is
///     the ONLY sanctioned way to turn a seed into a bounded index.
///
///     This is a lowbias32-style finalizer (Chris Wellons' public-domain integer hash), chosen and
///     frozen per the spec's instruction to "pick one and freeze it" — never re-derive a different
///     mix later, since that would silently change every already-shipped cue's cosmetic variation.
///     Kept engine-free and directly unit-testable (determinism + distribution sanity), same split
///     as <c>Content.Server._Solreign.EasterEggs.SolreignStaticReceiverRules</c>'s splitmix64
///     finalizer.
/// </summary>
public static class SolreignFxSeedMixing
{
    /// <summary>
    ///     Mixes a raw seed into a well-distributed <c>uint</c>. Deterministic: the same input
    ///     always yields the same output, in-process and across processes (no randomized hashing).
    /// </summary>
    public static uint Mix(uint seed)
    {
        unchecked
        {
            var z = seed;
            z ^= z >> 16;
            z *= 0x21F0AAADu;
            z ^= z >> 15;
            z *= 0x735A2D97u;
            z ^= z >> 15;
            return z;
        }
    }

    /// <summary>
    ///     Derives a bounded index in <c>[0, n)</c> from a seed via <see cref="Mix"/>. The one
    ///     sanctioned replacement for <c>Math.Abs(seed) % n</c>/signed-modulo indexing. Returns 0
    ///     for <paramref name="n"/> == 0 rather than dividing by zero.
    /// </summary>
    public static uint Index(uint seed, uint n)
    {
        return n == 0 ? 0u : Mix(seed) % n;
    }
}
