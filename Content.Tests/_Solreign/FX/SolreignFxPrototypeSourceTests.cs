#nullable enable
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     M5 regression coverage — grk adversarial review finding M5 from the W1 receipt ("Palette/phase
///     fallback under a count &lt;= 0 prototype can mint a meaningless index... the correct fix is
///     keeping a malformed prototype out of the allowlist at load time... W2/W3's job to wire").
///     This worktree's closure: <see cref="SolreignFxPrototypeSource"/> wraps a raw loader with
///     <see cref="SolreignFxCuePrototypeValidation"/> so a prototype failing ANY §1.5 rule is treated
///     as fully UNRESOLVABLE — never reaching <c>SolreignFxCueV1.TryCreate</c>/<c>TryValidateReceived</c>'s
///     palette/phase fallback step at all.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxPrototypeSource))]
public sealed class SolreignFxPrototypeSourceTests
{
    private sealed class FakeLoader : ISolreignFxRawPrototypeLoader
    {
        private readonly SolreignFxCuePrototype? _prototype;
        public int CallCount;

        public FakeLoader(SolreignFxCuePrototype? prototype)
        {
            _prototype = prototype;
        }

        public bool TryGetRaw(string effectId, out SolreignFxCuePrototype? prototype)
        {
            CallCount++;
            prototype = _prototype;
            return _prototype is not null;
        }
    }

#pragma warning disable RA0039 // Pure logic-boundary test; no prototype manager is running here.
    private static SolreignFxCuePrototype MakeValidPrototype() => new()
    {
        MinIntensity = 0f,
        MaxIntensity = 1f,
        MinScale = 0.1f,
        MaxScale = 1f,
        MinDuration = 0.1f,
        MaxDuration = 1f,
        PaletteCount = 3,
        DefaultPaletteIndex = 0,
        PhaseCount = 2,
        DefaultPhaseIndex = 0,
        DefaultIntensity = 0.5f,
        DefaultScale = 1f,
        DefaultDuration = 0.5f,
    };
#pragma warning restore RA0039

    [Test]
    public void TryResolve_ValidPrototype_Resolves()
    {
        var source = new SolreignFxPrototypeSource(new FakeLoader(MakeValidPrototype()));

        var ok = source.TryResolve("impact_light", out var prototype);

        Assert.That(ok, Is.True);
        Assert.That(prototype, Is.Not.Null);
    }

    [Test]
    public void TryResolve_ZeroPaletteCount_NeverResolves()
    {
        // The exact M5 scenario: a prototype whose PaletteCount is 0 (or negative) would otherwise
        // let ResolvePaletteOrPhase mint a meaningless index. It must never even reach that far —
        // TryResolve treats it as if the id didn't exist at all.
        var broken = MakeValidPrototype();
        broken.PaletteCount = 0;
        var source = new SolreignFxPrototypeSource(new FakeLoader(broken));

        var ok = source.TryResolve("impact_light", out var prototype);

        Assert.That(ok, Is.False);
        Assert.That(prototype, Is.Null);
    }

    [Test]
    public void TryResolve_NegativePaletteCount_NeverResolves()
    {
        var broken = MakeValidPrototype();
        broken.PaletteCount = -1;
        var source = new SolreignFxPrototypeSource(new FakeLoader(broken));

        Assert.That(source.TryResolve("impact_light", out _), Is.False);
    }

    [Test]
    public void TryResolve_InvertedBounds_NeverResolves()
    {
        var broken = MakeValidPrototype();
        broken.MinIntensity = 0.9f;
        broken.MaxIntensity = 0.1f;
        var source = new SolreignFxPrototypeSource(new FakeLoader(broken));

        Assert.That(source.TryResolve("impact_light", out _), Is.False);
    }

    [Test]
    public void TryResolve_NonFiniteBounds_NeverResolves()
    {
        var broken = MakeValidPrototype();
        broken.MaxDuration = float.NaN;
        var source = new SolreignFxPrototypeSource(new FakeLoader(broken));

        Assert.That(source.TryResolve("impact_light", out _), Is.False);
    }

    [Test]
    public void TryResolve_DefaultIndexOutOfRange_NeverResolves()
    {
        var broken = MakeValidPrototype();
        broken.PaletteCount = 2;
        broken.DefaultPaletteIndex = 5;
        var source = new SolreignFxPrototypeSource(new FakeLoader(broken));

        Assert.That(source.TryResolve("impact_light", out _), Is.False);
    }

    [Test]
    public void TryResolve_UnknownId_ReturnsFalseWithoutThrowing()
    {
        var source = new SolreignFxPrototypeSource(new FakeLoader(null));

        Assert.That(source.TryResolve("not_a_real_id", out var prototype), Is.False);
        Assert.That(prototype, Is.Null);
    }

    [Test]
    public void TryResolve_EmptyOrNullId_ReturnsFalseWithoutCallingTheLoader()
    {
        var loader = new FakeLoader(MakeValidPrototype());
        var source = new SolreignFxPrototypeSource(loader);

        Assert.That(source.TryResolve("", out _), Is.False);
        Assert.That(source.TryResolve(null!, out _), Is.False);
        Assert.That(loader.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void TryResolve_ValidationResultIsCached_LoaderNotReValidatedEveryCall()
    {
        var loader = new FakeLoader(MakeValidPrototype());
        var source = new SolreignFxPrototypeSource(loader);

        source.TryResolve("impact_light", out _);
        source.TryResolve("impact_light", out _);
        source.TryResolve("impact_light", out _);

        // The RAW loader is still called every time (cheap dictionary/prototype-manager lookup);
        // it's the VALIDATION pass that's cached — this assertion just documents the loader is
        // still consulted (never bypassed) rather than the source silently going stale.
        Assert.That(loader.CallCount, Is.EqualTo(3));
    }

    [Test]
    public void Invalidate_ClearsCache_ReValidatesOnNextResolve()
    {
        var mutable = MakeValidPrototype();
        var loader = new FakeLoader(mutable);
        var source = new SolreignFxPrototypeSource(loader);

        Assert.That(source.TryResolve("impact_light", out _), Is.True);

        mutable.PaletteCount = 0; // now malformed, but the loader always returns the SAME reference
        source.Invalidate();

        Assert.That(source.TryResolve("impact_light", out _), Is.False, "after Invalidate(), a prototype that has since become malformed must re-validate and be excluded");
    }
}
