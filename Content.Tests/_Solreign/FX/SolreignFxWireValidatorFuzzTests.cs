#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Property-based, exception-free-total-function proof for
///     <see cref="SolreignFxCueV1.TryValidateReceived"/> (spec §1.3b, the actual client-side trust
///     boundary) — the "Unit — wire validator fuzz" row of §7's test plan. Every constructed cue in
///     this file uses the TEST-ONLY internal constructor overload (gated by
///     <c>[InternalsVisibleTo("Content.Tests")]</c>, see <c>Content.Shared/AssemblyInfo.cs</c>) so
///     fields a hostile/buggy server's raw wire bytes could produce — including a forged
///     <see cref="SolreignFxCueV1.SchemaVersion"/>, which no other constructor path in this
///     codebase can ever set — are directly reachable. The enforceable goal under test: the stock
///     client safely handles ANY payload a hostile server can serialize (spec §1.3, cdx #1).
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxCueV1))]
public sealed class SolreignFxWireValidatorFuzzTests
{
    private const string KnownEffectId = "impact_light";
    private const string UnknownEffectId = "fx_stress_test_not_a_real_id";

    private sealed class FakePrototypeSource : ISolreignFxCuePrototypeSource
    {
        private readonly Dictionary<string, SolreignFxCuePrototype> _prototypes;

        public FakePrototypeSource(Dictionary<string, SolreignFxCuePrototype> prototypes)
        {
            _prototypes = prototypes;
        }

        public bool TryResolve(string effectId, out SolreignFxCuePrototype? prototype)
        {
            return _prototypes.TryGetValue(effectId, out prototype);
        }
    }

    private sealed class FakeAnchorResolver : ISolreignFxAnchorResolver
    {
        public bool EntityExists = true;
        public bool CoordinatesResolve = true;
        public Vector2 WorldPosition = Vector2.Zero;
        public Vector2 EntityWorldPosition = Vector2.Zero;

        public bool TryResolveCoordinatesWorldPosition(NetCoordinates coordinates, out Vector2 worldPosition)
        {
            worldPosition = WorldPosition;
            return CoordinatesResolve;
        }

        public bool TryResolveEntityWorldPosition(NetEntity entity, out Vector2 worldPosition)
        {
            worldPosition = EntityWorldPosition;
            return EntityExists;
        }
    }

    private static SolreignFxCuePrototype MakePrototype(
        float minIntensity = 0f, float maxIntensity = 1f,
        float minScale = 0.1f, float maxScale = 1f,
        float minDuration = 0.1f, float maxDuration = 1f,
        int paletteCount = 3, byte defaultPaletteIndex = 1,
        int phaseCount = 2, byte defaultPhaseIndex = 0)
    {
#pragma warning disable RA0039 // Pure logic-boundary test; no prototype manager is running here.
        return new SolreignFxCuePrototype
        {
            MinIntensity = minIntensity,
            MaxIntensity = maxIntensity,
            MinScale = minScale,
            MaxScale = maxScale,
            MinDuration = minDuration,
            MaxDuration = maxDuration,
            PaletteCount = paletteCount,
            DefaultPaletteIndex = defaultPaletteIndex,
            PhaseCount = phaseCount,
            DefaultPhaseIndex = defaultPhaseIndex,
        };
#pragma warning restore RA0039
    }

    private static FakePrototypeSource ProtoSourceWithDefault()
    {
        return new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(),
        });
    }

    private static readonly NetCoordinates ValidCoordinates = new(new NetEntity(1), 10f, 10f);
    private static readonly NetEntity ValidEntity = new(7);

    /// <summary>Builds a raw, TEST-ONLY cue via the internal escape-hatch constructor — bypasses every TryCreate rule, exactly modeling a hostile server's wire bytes.</summary>
    private static SolreignFxCueV1 MakeRawCue(
        byte schemaVersion = SolreignFxCueV1.CurrentSchemaVersion,
        string effectId = KnownEffectId,
        NetCoordinates? coordinates = null,
        NetEntity? entityAnchor = null,
        uint seed = 1,
        float intensity = 0.5f,
        float scale = 0.5f,
        float duration = 0.5f,
        byte? paletteIndex = 1,
        byte? phase = 0,
        uint correlationId = 1)
    {
        if (coordinates is null && entityAnchor is null)
            coordinates = ValidCoordinates;

        return new SolreignFxCueV1(
            schemaVersion,
            effectId,
            coordinates,
            entityAnchor,
            seed,
            intensity,
            scale,
            duration,
            paletteIndex,
            phase,
            correlationId);
    }

    // --- Never throws, period ---

    [Test]
    public void TryValidateReceived_NullInput_NeverThrowsAndRejects()
    {
        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(null, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(validated, Is.Null);
        });
        Assert.That(ok, Is.False);
    }

    // --- Step 1: exact-equality schema version (spec §1.3b.1, cdx #17) ---

    [TestCase((byte) 0)]
    [TestCase((byte) 2)]
    [TestCase((byte) 3)]
    [TestCase((byte) 255)]
    public void TryValidateReceived_ForgedSchemaVersion_RejectsWithoutThrowing(byte forgedVersion)
    {
        var raw = MakeRawCue(schemaVersion: forgedVersion);

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.SchemaVersionMismatch));
            Assert.That(validated, Is.Null);
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_CorrectSchemaVersionOtherwiseValid_Accepts()
    {
        var raw = MakeRawCue();

        var ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
            out var validated, out var reason);

        Assert.That(ok, Is.True);
        Assert.That(validated, Is.Not.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.None));
    }

    // --- Step 2: EffectId must resolve locally AND be allowlist-compiled-in (spec §1.3b.2) ---

    [Test]
    public void TryValidateReceived_UnknownEffectId_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue(effectId: UnknownEffectId);

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.EffectIdRejected));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_AllowlistedButLocallyUnresolvedEffectId_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue();
        var emptyPrototypeSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>());

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, emptyPrototypeSource, new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.EffectIdRejected));
        });
        Assert.That(ok, Is.False);
    }

    // --- Step 3: full finiteness + LOCAL bounds re-validation (spec §1.3b.3) — never repairs, only accept/reject ---

    private static readonly float[] NonFiniteValues = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };

    [Test]
    public void TryValidateReceived_NonFiniteIntensity_RejectsWithoutThrowing_ForEveryPoisonValue()
    {
        foreach (var poison in NonFiniteValues)
        {
            var raw = MakeRawCue(intensity: poison);

            bool ok = false;
            Assert.DoesNotThrow(() =>
            {
                ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                    out var validated, out var reason);
                Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.NonFiniteNumericField));
            }, $"threw for poison={poison}");
            Assert.That(ok, Is.False, $"accepted for poison={poison}");
        }
    }

    [Test]
    public void TryValidateReceived_NonFiniteScaleOrDuration_RejectsWithoutThrowing_ForEveryPoisonValue()
    {
        foreach (var poison in NonFiniteValues)
        {
            var rawScale = MakeRawCue(scale: poison);
            var rawDuration = MakeRawCue(duration: poison);

            Assert.DoesNotThrow(() =>
            {
                var ok = SolreignFxCueV1.TryValidateReceived(rawScale, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                    out _, out var reason);
                Assert.That(ok, Is.False);
                Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.NonFiniteNumericField));
            }, $"threw for scale poison={poison}");

            Assert.DoesNotThrow(() =>
            {
                var ok = SolreignFxCueV1.TryValidateReceived(rawDuration, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                    out _, out var reason);
                Assert.That(ok, Is.False);
                Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.NonFiniteNumericField));
            }, $"threw for duration poison={poison}");
        }
    }

    [Test]
    public void TryValidateReceived_FiniteButOutsideLocalBounds_RejectsWithoutRepairing()
    {
        // Prototype bounds are [0,1]/[0.1,1]/[0.1,1] (MakePrototype defaults) — 5f is finite but
        // grossly outside every one of them. Unlike TryCreate, the client NEVER clamps/repairs a
        // numeric field — it only accepts or rejects (spec §1.3b.3 remarks).
        var raw = MakeRawCue(intensity: 5f);

        var ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
            out var validated, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(validated, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.OutOfRangeNumericField));
    }

    // --- Step 4: anchor re-resolution via TryGetEntity/TryResolve (spec §1.3b.4) ---

    [Test]
    public void TryValidateReceived_BothAnchorsSet_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue(coordinates: ValidCoordinates, entityAnchor: ValidEntity);

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorMissingOrBothSet));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_NeitherAnchorSet_RejectsWithoutThrowing()
    {
        var raw = new SolreignFxCueV1(
            SolreignFxCueV1.CurrentSchemaVersion, KnownEffectId, null, null, 1, 0.5f, 0.5f, 0.5f, 1, 0, 1);

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorMissingOrBothSet));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_DanglingEntityAnchor_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue(entityAnchor: ValidEntity, coordinates: null);
        var anchorResolver = new FakeAnchorResolver { EntityExists = false };

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), anchorResolver,
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorEntityUnresolvable));
        });
        Assert.That(ok, Is.False);
    }

    // --- Entity anchors get the SAME finiteness/ceiling scrutiny as coordinates (grk review finding H2) ---

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void TryValidateReceived_EntityAnchorResolvesToNonFiniteWorldPosition_RejectsWithoutThrowing(float poison)
    {
        var raw = MakeRawCue(entityAnchor: ValidEntity, coordinates: null);
        var anchorResolver = new FakeAnchorResolver { EntityExists = true, EntityWorldPosition = new Vector2(poison, 0f) };

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), anchorResolver,
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorEntityUnresolvable));
        }, $"threw for poison={poison}");
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_EntityAnchorOutsideWorldCeiling_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue(entityAnchor: ValidEntity, coordinates: null);
        var anchorResolver = new FakeAnchorResolver
        {
            EntityExists = true,
            EntityWorldPosition = new Vector2(SolreignFxCueV1.WorldCoordinateCeiling * 3f, 0f),
        };

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), anchorResolver,
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorNonFiniteOrOutOfCeiling));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_EntityAnchorWithinCeiling_Accepts()
    {
        var raw = MakeRawCue(entityAnchor: ValidEntity, coordinates: null);
        var anchorResolver = new FakeAnchorResolver { EntityExists = true, EntityWorldPosition = new Vector2(5f, 5f) };

        var ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), anchorResolver,
            out var validated, out var reason);

        Assert.That(ok, Is.True);
        Assert.That(validated, Is.Not.Null);
    }

    // --- Malformed LOCAL prototype bounds are hard-rejected, never silently accept-everything (grk review finding M3) ---

    [Test]
    public void TryValidateReceived_MalformedPrototypeBounds_Inverted_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue();
        var malformedSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(minIntensity: 0.9f, maxIntensity: 0.1f),
        });

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, malformedSource, new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.MalformedPrototypeBounds));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_MalformedPrototypeBounds_ExceedsWireCeiling_RejectsWithoutThrowing_EvenWithAnOtherwiseValidValue()
    {
        // MaxScale=999 is finite and internally consistent (Min<=Max) but blows through the
        // hardcoded (0,8] wire ceiling — the received Scale value itself (0.5) is well within
        // BOTH bounds, proving this rejects on the PROTOTYPE'S malformation, not the wire value.
        var raw = MakeRawCue(scale: 0.5f);
        var malformedSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(maxScale: 999f),
        });

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, malformedSource, new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.MalformedPrototypeBounds));
        });
        Assert.That(ok, Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void TryValidateReceived_NonFiniteRawCoordinatePosition_RejectsWithoutThrowing(float poison)
    {
        var poisoned = new NetCoordinates(new NetEntity(1), poison, 0f);
        var raw = MakeRawCue(coordinates: poisoned, entityAnchor: null);

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorNonFiniteOrOutOfCeiling));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_CoordinatesResolveToNonFiniteWorldPosition_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue(coordinates: ValidCoordinates, entityAnchor: null);
        var anchorResolver = new FakeAnchorResolver { CoordinatesResolve = true, WorldPosition = new Vector2(float.NaN, 0f) };

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), anchorResolver,
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorCoordinatesUnresolvable));
        });
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryValidateReceived_CoordinatesOutsideWorldCeiling_RejectsWithoutThrowing()
    {
        var raw = MakeRawCue(coordinates: ValidCoordinates, entityAnchor: null);
        var anchorResolver = new FakeAnchorResolver
        {
            CoordinatesResolve = true,
            WorldPosition = new Vector2(SolreignFxCueV1.WorldCoordinateCeiling * 2f, 0f),
        };

        bool ok = false;
        Assert.DoesNotThrow(() =>
        {
            ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), anchorResolver,
                out var validated, out var reason);
            Assert.That(reason, Is.EqualTo(SolreignFxCueValidateFailureReason.AnchorNonFiniteOrOutOfCeiling));
        });
        Assert.That(ok, Is.False);
    }

    // --- Step 5: palette/phase validated against LOCAL actual counts, fallback never throws (spec §1.3b.5) ---

    [Test]
    public void TryValidateReceived_MalformedZeroPalettePrototype_NeverThrows_FallsBackSafely()
    {
        var raw = MakeRawCue(paletteIndex: 5);
        var malformedSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            // A prototype that skipped §1.5 load-time validation somehow (defense in depth: the
            // validator itself must never crash even handed a broken prototype).
            [KnownEffectId] = MakePrototype(paletteCount: 0, defaultPaletteIndex: 0),
        });

        Assert.DoesNotThrow(() =>
        {
            SolreignFxCueV1.TryValidateReceived(raw, malformedSource, new FakeAnchorResolver(),
                out var validated, out var reason);
        });
    }

    [Test]
    public void TryValidateReceived_PaletteIndexOutOfLocalRange_FallsBackToDeclaredDefault()
    {
        var raw = MakeRawCue(paletteIndex: 250); // prototype's PaletteCount defaults to 3

        var ok = SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
            out var validated, out var reason);

        Assert.That(ok, Is.True);
        Assert.That(validated!.PaletteIndex, Is.EqualTo((byte) 1)); // DefaultPaletteIndex from MakePrototype()
    }

    // --- Property-based sweep: total function proof across a Cartesian slice of hostile combinations ---

    [Test]
    public void TryValidateReceived_CartesianHostileSweep_NeverThrows()
    {
        byte[] versions = { 0, 1, 2, 255 };
        float[] numericPoisons = { 0.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 999f };
        byte?[] paletteIndices = { null, 0, 1, 2, 250 };

        var wellFormedSource = ProtoSourceWithDefault();
        var malformedSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(paletteCount: 0, phaseCount: 0),
        });

        var sawAtLeastOneAccept = false;

        foreach (var version in versions)
        foreach (var intensity in numericPoisons)
        foreach (var paletteIndex in paletteIndices)
        foreach (var prototypeSource in new[] { wellFormedSource, malformedSource })
        {
            var raw = MakeRawCue(schemaVersion: version, intensity: intensity, paletteIndex: paletteIndex);

            var reason = SolreignFxCueValidateFailureReason.None;
            var ok = false;
            Assert.DoesNotThrow(() =>
            {
                ok = SolreignFxCueV1.TryValidateReceived(raw, prototypeSource, new FakeAnchorResolver(),
                    out _, out reason);
            }, $"threw for version={version}, intensity={intensity}, paletteIndex={paletteIndex}, malformedProto={prototypeSource == malformedSource}");

            if (ok)
                sawAtLeastOneAccept = true;
        }

        // Mutation check: the sweep must not be vacuously "everything rejects" — at least one
        // combination (version=1, intensity=0.5, a valid palette index, well-formed prototype) is
        // expected to succeed, proving the total-function proof isn't hiding a validator that
        // rejects unconditionally.
        Assert.That(sawAtLeastOneAccept, Is.True, "sweep never accepted anything — suspiciously total rejection");
    }

    [Test]
    public void TryValidateReceived_AnchorCartesianSweep_NeverThrows()
    {
        var anchorConfigs = new (NetCoordinates? Coordinates, NetEntity? Entity)[]
        {
            (ValidCoordinates, null),
            (null, ValidEntity),
            (ValidCoordinates, ValidEntity), // both set — invalid
            (null, null), // neither set — invalid
            (new NetCoordinates(new NetEntity(1), float.NaN, float.NaN), null),
            (new NetCoordinates(new NetEntity(1), SolreignFxCueV1.WorldCoordinateCeiling * 4f, 0f), null),
        };

        foreach (var (coordinates, entity) in anchorConfigs)
        {
            var raw = new SolreignFxCueV1(
                SolreignFxCueV1.CurrentSchemaVersion, KnownEffectId, coordinates, entity, 1, 0.5f, 0.5f, 0.5f, 1, 0, 1);

            Assert.DoesNotThrow(() =>
            {
                SolreignFxCueV1.TryValidateReceived(raw, ProtoSourceWithDefault(), new FakeAnchorResolver(),
                    out _, out _);
            }, $"threw for anchor config coords={coordinates}, entity={entity}");
        }
    }
}
