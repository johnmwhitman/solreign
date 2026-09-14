#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Unit coverage for <see cref="SolreignFxCueV1.TryCreate"/>'s reject/clamp rules (spec §1.3a)
///     and the <see cref="SolreignFxWireAllowlist"/> manifest's own internal consistency (spec
///     §1.3a.1/§2.2, cdx #3/#19) — the two "Unit — schema validation" / "Unit — allowlist manifest
///     consistency" rows of §7's test plan. <c>TryValidateReceived</c>'s total-exception-free-fuzz
///     coverage lives in <c>SolreignFxWireValidatorFuzzTests.cs</c>, a separate file per the test
///     plan's own split.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxCueV1))]
public sealed class SolreignFxCueV1Tests
{
    private const string KnownEffectId = "impact_light";
    private const string UnknownEffectId = "fx_stress_test_not_a_real_id";

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

    private sealed class FakeCorrelationSource : ISolreignFxCorrelationSource
    {
        private uint _seed;
        private uint _broadcast;
        private uint _targeted;

        public uint NextSeed() => _seed++;
        public uint NextBroadcastCorrelationId() => _broadcast++;
        public uint NextTargetedCorrelationId() => _targeted++;
    }

    private static readonly NetEntity SomeEntity = new(7);
    private static readonly NetCoordinates SomeCoordinates = new(new NetEntity(1), 10f, 10f);

    private static bool Create(
        FakePrototypeSource prototypeSource,
        FakeAnchorResolver anchorResolver,
        FakeCorrelationSource correlationSource,
        out SolreignFxCueV1? cue,
        out SolreignFxCueCreateFailureReason reason,
        string effectId = KnownEffectId,
        NetCoordinates? coordinates = null,
        NetEntity? entityAnchor = null,
        float intensity = 0.5f,
        float scale = 1f,
        float duration = 0.5f,
        byte? paletteIndex = null,
        byte? phase = null,
        SolreignFxCueStream stream = SolreignFxCueStream.Broadcast)
    {
        // Default to a coordinate anchor when the caller supplied neither, so most tests don't
        // need to think about anchor plumbing unless that's specifically what they're exercising.
        if (coordinates is null && entityAnchor is null)
            coordinates = SomeCoordinates;

        return SolreignFxCueV1.TryCreate(
            effectId,
            coordinates,
            entityAnchor,
            intensity,
            scale,
            duration,
            paletteIndex,
            phase,
            prototypeSource,
            anchorResolver,
            correlationSource,
            stream,
            out cue,
            out reason);
    }

    // --- EffectId resolution + allowlist membership (spec §1.3a.1, cdx #3) ---

    [Test]
    public void TryCreate_UnknownEffectId_RejectsAsNotAllowlisted()
    {
        var prototypeSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [UnknownEffectId] = MakePrototype(), // resolves fine — allowlist membership must fail it anyway.
        });

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, effectId: UnknownEffectId);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.EffectIdNotAllowlisted));
    }

    [Test]
    public void TryCreate_AllowlistedButUnresolvedPrototype_RejectsAsNotResolved()
    {
        // KnownEffectId is allowlist-eligible but the fake prototype source has nothing loaded for
        // it — mirrors a forgotten/late-loaded effects.yml entry (cdx #3's "resolution AND
        // membership, both required" — this exercises the other half).
        var prototypeSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>());

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.EffectIdNotResolved));
    }

    // --- Anchor exclusivity and validity (spec §1.3a.2, cdx #6) ---

    [Test]
    public void TryCreate_BothAnchorsSet_RejectsAsMissingOrBothSet()
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, coordinates: SomeCoordinates, entityAnchor: SomeEntity);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorMissingOrBothSet));
    }

    [Test]
    public void TryCreate_NeitherAnchorSet_RejectsAsMissingOrBothSet()
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = SolreignFxCueV1.TryCreate(
            KnownEffectId, null, null, 0.5f, 1f, 0.5f, null, null,
            prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            SolreignFxCueStream.Broadcast, out var cue, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorMissingOrBothSet));
    }

    [Test]
    public void TryCreate_EntityAnchorDoesNotExist_RejectsAsAnchorEntityInvalid()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver { EntityExists = false };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, entityAnchor: SomeEntity);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorEntityInvalid));
    }

    [Test]
    public void TryCreate_EntityAnchorExists_Accepts()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver { EntityExists = true };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, entityAnchor: SomeEntity);

        Assert.That(ok, Is.True);
        Assert.That(cue, Is.Not.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.None));
    }

    [Test]
    public void TryCreate_CoordinatesUnresolvable_RejectsAsAnchorCoordinatesInvalid()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver { CoordinatesResolve = false };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, coordinates: SomeCoordinates);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorCoordinatesInvalid));
    }

    [Test]
    public void TryCreate_CoordinatesOutsideWorldCeiling_RejectsAsOutOfCeiling()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver
        {
            CoordinatesResolve = true,
            WorldPosition = new Vector2(SolreignFxCueV1.WorldCoordinateCeiling + 1f, 0f),
        };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, coordinates: SomeCoordinates);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorCoordinatesOutOfCeiling));
    }

    // --- Entity anchors get the SAME finiteness/ceiling scrutiny as coordinates (grk review finding H2) ---

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void TryCreate_EntityAnchorNonFiniteWorldPosition_RejectsAsAnchorEntityInvalid(float poison)
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver { EntityExists = true, EntityWorldPosition = new Vector2(poison, 0f) };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, entityAnchor: SomeEntity);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorEntityInvalid));
    }

    [Test]
    public void TryCreate_EntityAnchorOutsideWorldCeiling_RejectsAsOutOfCeiling()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver
        {
            EntityExists = true,
            EntityWorldPosition = new Vector2(SolreignFxCueV1.WorldCoordinateCeiling * 2f, 0f),
        };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, entityAnchor: SomeEntity);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorCoordinatesOutOfCeiling));
    }

    [Test]
    public void TryCreate_EntityAnchorWithinCeiling_Accepts()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver { EntityExists = true, EntityWorldPosition = new Vector2(5f, 5f) };

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, entityAnchor: SomeEntity);

        Assert.That(ok, Is.True);
        Assert.That(cue, Is.Not.Null);
    }

    // --- Malformed LOCAL prototype bounds are hard-rejected, never silently accept-everything (grk review finding M3) ---

    [Test]
    public void TryCreate_MalformedPrototypeBounds_Inverted_Rejects()
    {
        var prototypeSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(minIntensity: 0.9f, maxIntensity: 0.1f), // inverted
        });

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.MalformedPrototypeBounds));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void TryCreate_MalformedPrototypeBounds_NonFiniteMax_Rejects(float poison)
    {
        var prototypeSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(maxDuration: poison),
        });

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.MalformedPrototypeBounds));
    }

    [Test]
    public void TryCreate_MalformedPrototypeBounds_ExceedsWireCeiling_Rejects()
    {
        // Scale's hardcoded wire ceiling is (0, 8] (spec §1.5) — a prototype authored/loaded with
        // MaxScale=999 must not silently widen the accepted wire range.
        var prototypeSource = new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(maxScale: 999f),
        });

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.MalformedPrototypeBounds));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void TryCreate_NonFiniteRawCoordinatePosition_RejectsAsInvalid(float poison)
    {
        var prototypeSource = ProtoSourceWithDefault();
        var anchorResolver = new FakeAnchorResolver();
        var poisonedCoordinates = new NetCoordinates(new NetEntity(1), poison, 0f);

        var ok = Create(prototypeSource, anchorResolver, new FakeCorrelationSource(),
            out var cue, out var reason, coordinates: poisonedCoordinates);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.AnchorCoordinatesInvalid));
    }

    // --- Finiteness gate, then bounds clamp (spec §1.3a.3, cdx #2/#13) ---

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void TryCreate_NonFiniteIntensity_HardRejects(float poison)
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, intensity: poison);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.NonFiniteNumericField));
    }

    [Test]
    public void TryCreate_NegativeDuration_HardRejects()
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, duration: -1f);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.NegativeNumericField));
    }

    [Test]
    public void TryCreate_GrosslyOutOfRangeScale_HardRejects()
    {
        // MaxScale defaults to 1f in MakePrototype() -> 10x ceiling is 10f; 11f must hard-reject
        // rather than silently clamp to max (cdx #13's "ms-vs-s bug ships at max cost").
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, scale: 11f);

        Assert.That(ok, Is.False);
        Assert.That(cue, Is.Null);
        Assert.That(reason, Is.EqualTo(SolreignFxCueCreateFailureReason.GrosslyOutOfRangeNumericField));
    }

    [Test]
    public void TryCreate_SmallDriftAboveMax_ClampsToMax()
    {
        // MaxDuration defaults to 1f; 2f is within the 10x gross ceiling (10f) so it clamps rather
        // than rejects.
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, duration: 2f);

        Assert.That(ok, Is.True);
        Assert.That(cue, Is.Not.Null);
        Assert.That(cue!.Duration, Is.EqualTo(1f));
    }

    [Test]
    public void TryCreate_SmallDriftBelowMin_ClampsToMin()
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, scale: 0.01f);

        Assert.That(ok, Is.True);
        Assert.That(cue, Is.Not.Null);
        Assert.That(cue!.Scale, Is.EqualTo(0.1f));
    }

    [Test]
    public void TryCreate_ValidWithinRange_Unchanged()
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, intensity: 0.7f, scale: 0.5f, duration: 0.3f);

        Assert.That(ok, Is.True);
        Assert.That(cue, Is.Not.Null);
        Assert.That(cue!.Intensity, Is.EqualTo(0.7f));
        Assert.That(cue.Scale, Is.EqualTo(0.5f));
        Assert.That(cue.Duration, Is.EqualTo(0.3f));
    }

    // --- Palette/Phase fallback-to-declared-default (spec §1.3a.4, cdx #11) ---

    [Test]
    public void TryCreate_PaletteIndexOutOfRange_FallsBackToDeclaredDefault()
    {
        var prototypeSource = ProtoSourceWithDefault(); // PaletteCount=3, DefaultPaletteIndex=1

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, paletteIndex: 99);

        Assert.That(ok, Is.True);
        Assert.That(cue!.PaletteIndex, Is.EqualTo((byte) 1));
    }

    [Test]
    public void TryCreate_PaletteIndexAbsent_UsesDeclaredDefault()
    {
        var prototypeSource = ProtoSourceWithDefault();

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, paletteIndex: null);

        Assert.That(ok, Is.True);
        Assert.That(cue!.PaletteIndex, Is.EqualTo((byte) 1));
    }

    [Test]
    public void TryCreate_ValidPaletteIndex_Preserved()
    {
        var prototypeSource = ProtoSourceWithDefault(); // PaletteCount=3 -> valid indices 0,1,2

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, paletteIndex: 2);

        Assert.That(ok, Is.True);
        Assert.That(cue!.PaletteIndex, Is.EqualTo((byte) 2));
    }

    [Test]
    public void TryCreate_PhaseIndexOutOfRange_FallsBackToDeclaredDefault()
    {
        var prototypeSource = ProtoSourceWithDefault(); // PhaseCount=2, DefaultPhaseIndex=0

        var ok = Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(),
            out var cue, out var reason, phase: 200);

        Assert.That(ok, Is.True);
        Assert.That(cue!.Phase, Is.EqualTo((byte) 0));
    }

    // --- Seed/CorrelationId always server-assigned, from the correct stream namespace (spec §1.3a.5, grk #6) ---

    [Test]
    public void TryCreate_SchemaVersion_IsAlwaysCurrentOnConstructedCue()
    {
        var prototypeSource = ProtoSourceWithDefault();

        Create(prototypeSource, new FakeAnchorResolver(), new FakeCorrelationSource(), out var cue, out _);

        Assert.That(cue!.SchemaVersion, Is.EqualTo(SolreignFxCueV1.CurrentSchemaVersion));
    }

    [Test]
    public void TryCreate_Seed_ComesFromCorrelationSourceNotCaller()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var correlationSource = new FakeCorrelationSource();

        Create(prototypeSource, new FakeAnchorResolver(), correlationSource, out var first, out _);
        Create(prototypeSource, new FakeAnchorResolver(), correlationSource, out var second, out _);

        // The fake's NextSeed() is a simple increment — two calls must yield two different seeds,
        // proving TryCreate has no caller-supplied seed parameter to fall back on/collide with.
        Assert.That(first!.Seed, Is.Not.EqualTo(second!.Seed));
    }

    [Test]
    public void TryCreate_BroadcastStream_UsesBroadcastCounterNamespace()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var correlationSource = new FakeCorrelationSource();

        Create(prototypeSource, new FakeAnchorResolver(), correlationSource, out var broadcastCue, out _,
            stream: SolreignFxCueStream.Broadcast);
        Create(prototypeSource, new FakeAnchorResolver(), correlationSource, out var targetedCue, out _,
            stream: SolreignFxCueStream.Targeted);

        // Fresh FakeCorrelationSource: broadcast namespace starts at 0, targeted namespace also
        // starts at 0 independently — the first broadcast cue and the first targeted cue both get
        // CorrelationId 0 from their OWN counters, proving the namespaces are independent (grk #6).
        Assert.That(broadcastCue!.CorrelationId, Is.EqualTo(0u));
        Assert.That(targetedCue!.CorrelationId, Is.EqualTo(0u));
    }

    [Test]
    public void TryCreate_TwoBroadcastCallsInARow_CorrelationIdsAdvanceOnTheSameNamespace()
    {
        var prototypeSource = ProtoSourceWithDefault();
        var correlationSource = new FakeCorrelationSource();

        Create(prototypeSource, new FakeAnchorResolver(), correlationSource, out var first, out _,
            stream: SolreignFxCueStream.Broadcast);
        Create(prototypeSource, new FakeAnchorResolver(), correlationSource, out var second, out _,
            stream: SolreignFxCueStream.Broadcast);

        Assert.That(first!.CorrelationId, Is.EqualTo(0u));
        Assert.That(second!.CorrelationId, Is.EqualTo(1u));
    }

    private static FakePrototypeSource ProtoSourceWithDefault()
    {
        return new FakePrototypeSource(new Dictionary<string, SolreignFxCuePrototype>
        {
            [KnownEffectId] = MakePrototype(),
        });
    }

    // --- Allowlist manifest consistency (spec §2.2, cdx #3/#19) ---

    [Test]
    public void WireAllowlist_V1_HasExactlyTheSpecifiedTenIds()
    {
        var expected = new[]
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
        };

        Assert.That(SolreignFxWireAllowlist.V1.Count, Is.EqualTo(10));
        foreach (var id in expected)
            Assert.That(SolreignFxWireAllowlist.V1.Contains(id), Is.True, $"missing expected id '{id}'");
    }

    [Test]
    public void WireAllowlist_AudienceClassification_CoversExactlySameIdsAsV1()
    {
        Assert.That(SolreignFxWireAllowlist.AudienceClassificationV1.Count, Is.EqualTo(SolreignFxWireAllowlist.V1.Count));

        foreach (var id in SolreignFxWireAllowlist.V1)
        {
            Assert.That(SolreignFxWireAllowlist.AudienceClassificationV1.ContainsKey(id), Is.True,
                $"'{id}' is in V1 but has no audience classification entry");
        }

        foreach (var id in SolreignFxWireAllowlist.AudienceClassificationV1.Keys)
        {
            Assert.That(SolreignFxWireAllowlist.V1.Contains(id), Is.True,
                $"'{id}' has an audience classification but is not in V1 — allowlist/manifest disagreement");
        }
    }

    [Test]
    public void WireAllowlist_OnlyTransformationIsDetailOnly()
    {
        foreach (var (id, classification) in SolreignFxWireAllowlist.AudienceClassificationV1)
        {
            var expected = id == "transformation"
                ? SolreignFxAudienceClassification.DetailOnly
                : SolreignFxAudienceClassification.Broadcast;

            Assert.That(classification, Is.EqualTo(expected), $"unexpected classification for '{id}'");
        }
    }

    [Test]
    public void WireAllowlist_IsAllowlisted_NullOrEmpty_NeverThrowsAndReturnsFalse()
    {
        Assert.That(SolreignFxWireAllowlist.IsAllowlisted(null), Is.False);
        Assert.That(SolreignFxWireAllowlist.IsAllowlisted(string.Empty), Is.False);
    }
}
