#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Content.Client._Solreign.FX;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.FX;
using Content.Server.Electrocution;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Electrocution;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Prometheus;

namespace Content.IntegrationTests.Tests._Solreign.FX.Consumers;

/// <summary>
///     End-to-end coverage for the activated world-feedback adapter. These tests use the real
///     server event bus, FX raiser, network transport, and client receive path; they intentionally
///     classify the actual applied damage delta rather than an attempted melee contact.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SolreignWorldFeedbackIntegrationTest : GameTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntDamageTypeId = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> StructuralDamageTypeId = "Structural";
    private static readonly Regex WorldFeedbackMetricLine = new(
        "^solreign_fx_world_feedback_total\\{class=\"([^\"]+)\",outcome=\"([^\"]+)\"\\}\\s+([0-9.eE+-]+)$",
        RegexOptions.Compiled);

    private static readonly HashSet<MetricKey> LegalMetricKeys =
    [
        new("kinetic_light", "shadow"),
        new("kinetic_light", "raiser_accepted_or_coalesced"),
        new("kinetic_light", "raiser_rejected"),
        new("kinetic_heavy", "shadow"),
        new("kinetic_heavy", "raiser_accepted_or_coalesced"),
        new("kinetic_heavy", "raiser_rejected"),
        new("electrocution", "shadow"),
        new("electrocution", "raiser_accepted_or_coalesced"),
        new("electrocution", "raiser_rejected"),
        new("non_kinetic", "filtered"),
    ];

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        Fresh = true,
        DummyTicker = false,
    };

    [Test]
    public void WorldFeedbackCVars_HaveExactNamesFlagsAndActivatedDefaults()
    {
        // All three gates ship enabled per the activation passes. Earlier assertions for a dormant
        // observer predated 78de8fc0953 and must not redefine the deployed contract.
        Assert.Multiple(() =>
        {
            Assert.That(CCVars.SolreignFxCueV1Enabled.Name, Is.EqualTo("solreign.fx.cue_v1"));
            Assert.That(CCVars.SolreignFxCueV1Enabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignFxCueV1Enabled.Flags, Is.EqualTo(CVar.REPLICATED | CVar.SERVER));

            Assert.That(CCVars.SolreignFxWorldFeedbackV1Enabled.Name,
                Is.EqualTo("solreign.fx.world_feedback_v1"));
            Assert.That(CCVars.SolreignFxWorldFeedbackV1Enabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignFxWorldFeedbackV1Enabled.Flags, Is.EqualTo(CVar.SERVERONLY));

            Assert.That(CCVars.SolreignFxWorldFeedbackObserveEnabled.Name,
                Is.EqualTo("solreign.fx.world_feedback_observe"));
            Assert.That(CCVars.SolreignFxWorldFeedbackObserveEnabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignFxWorldFeedbackObserveEnabled.Flags, Is.EqualTo(CVar.SERVERONLY));
        });
    }

    [Test]
    public async Task WorldFeedback_ThreeGateTruthTable_BoundsDeliveryAndObservation()
    {
        EntityUid observer = default;
        EntityUid target = default;

        try
        {
            await Server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(Server.CfgMan.GetCVar(CCVars.SolreignFxCueV1Enabled), Is.True,
                        "the activation pass intentionally ships the FX Language master gate enabled");
                    Assert.That(Server.CfgMan.GetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled), Is.True,
                        "the activation pass intentionally ships the world-feedback consumer enabled");
                    Assert.That(Server.CfgMan.GetCVar(CCVars.SolreignFxWorldFeedbackObserveEnabled), Is.True,
                        "the activation pass intentionally ships aggregate observation enabled");
                });

                (observer, target) = PrepareObserverWorld();
            });
            await Pair.RunTicksSync(10);

            foreach (var master in new[] { false, true })
            {
                foreach (var delivery in new[] { false, true })
                {
                    foreach (var observe in new[] { false, true })
                    {
                        var scenario = $"master={master}, delivery={delivery}, observe={observe}";
                        await SetGates(master, delivery, observe);
                        var before = await CaptureWorldFeedbackMetrics();

                        await DealDamageAndTick(observer, target, 1);

                        var after = await CaptureWorldFeedbackMetrics();
                        await Server.WaitAssertion(() =>
                        {
                            Assert.Multiple(() =>
                            {
                                Assert.That(
                                    Server.CfgMan.GetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled),
                                    Is.EqualTo(delivery),
                                    $"{scenario}: delivery must remain enabled through post-event ticks");
                                Assert.That(
                                    Server.CfgMan.GetCVar(CCVars.SolreignFxWorldFeedbackObserveEnabled),
                                    Is.EqualTo(observe),
                                    $"{scenario}: observation must remain enabled through post-event ticks");
                            });
                        });
                        var cueExpected = master && delivery;
                        await Client.WaitAssertion(() =>
                        {
                            var system = Client.System<SolreignFxCueSystem>();
                            Assert.Multiple(() =>
                            {
                                Assert.That(system.RawReceivedEffectIdsForTests.Count(id => id == "impact_light"),
                                    Is.EqualTo(cueExpected ? 1 : 0), $"{scenario}: raw cue count");
                                Assert.That(system.AcceptedCuesForTests.Count(c => c.EffectId == "impact_light"),
                                    Is.EqualTo(cueExpected ? 1 : 0), $"{scenario}: accepted cue count");
                            });
                        });

                        Assert.That(TotalDelta(before, after), Is.EqualTo(observe ? 1d : 0d), scenario);
                        if (!observe)
                            continue;

                        var expectedOutcome = !delivery
                            ? "shadow"
                            : master
                                ? "raiser_accepted_or_coalesced"
                                : "raiser_rejected";
                        Assert.That(
                            Delta(before, after, new MetricKey("kinetic_light", expectedOutcome)),
                            Is.EqualTo(1d),
                            scenario);
                    }
                }
            }

            var finalMetrics = await CaptureWorldFeedbackMetrics();
            Assert.That(finalMetrics.Keys, Is.EquivalentTo(LegalMetricKeys),
                "the process-global metric may expose only the ten pre-created legal class/outcome pairs");
        }
        finally
        {
            await ResetGates();
        }
    }

    [Test]
    public async Task FinalDamageEvents_MapLightAndHeavyImpacts_AndIgnoreHealing()
    {
        EntityUid observer = default;
        EntityUid target = default;

        try
        {
            await Server.WaitAssertion(() => (observer, target) = PrepareObserverWorld());
            await SetGates(master: true, delivery: true, observe: true);
            await Pair.RunTicksSync(8);

            var beforeLight = await CaptureWorldFeedbackMetrics();
            await DealDamageAndTick(observer, target, 10);
            var afterLight = await CaptureWorldFeedbackMetrics();
            await Client.WaitAssertion(() =>
            {
                var cue = Client.System<SolreignFxCueSystem>().AcceptedCuesForTests.Single();
                Assert.Multiple(() =>
                {
                    Assert.That(cue.EffectId, Is.EqualTo("impact_light"));
                    Assert.That(cue.Intensity, Is.EqualTo(0.6f).Within(0.001f));
                    Assert.That(cue.Duration, Is.EqualTo(0.4f).Within(0.001f));
                });
            });
            Assert.That(
                Delta(beforeLight, afterLight, new MetricKey("kinetic_light", "raiser_accepted_or_coalesced")),
                Is.EqualTo(1d));
            Assert.That(TotalDelta(beforeLight, afterLight), Is.EqualTo(1d));

            var beforeHeavy = await CaptureWorldFeedbackMetrics();
            await DealDamageAndTick(observer, target, 20);
            var afterHeavy = await CaptureWorldFeedbackMetrics();
            await Client.WaitAssertion(() =>
            {
                var cue = Client.System<SolreignFxCueSystem>().AcceptedCuesForTests.Single();
                Assert.Multiple(() =>
                {
                    Assert.That(cue.EffectId, Is.EqualTo("impact_heavy"));
                    Assert.That(cue.Intensity, Is.EqualTo(0.8f).Within(0.001f));
                    Assert.That(cue.Duration, Is.EqualTo(0.8f).Within(0.001f));
                });
            });
            Assert.That(
                Delta(beforeHeavy, afterHeavy, new MetricKey("kinetic_heavy", "raiser_accepted_or_coalesced")),
                Is.EqualTo(1d));
            Assert.That(TotalDelta(beforeHeavy, afterHeavy), Is.EqualTo(1d));

            await ClearClientVisibility();
            var beforeHealing = await CaptureWorldFeedbackMetrics();
            await Server.WaitPost(() =>
            {
                var healing = new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), FixedPoint2.New(-5));
                Server.EntMan.System<DamageableSystem>().ChangeDamage(target, healing, origin: observer);
            });
            await Pair.RunTicksSync(5);
            var afterHealing = await CaptureWorldFeedbackMetrics();
            await AssertClientHasNoCue("healing-only damage event");
            Assert.That(TotalDelta(beforeHealing, afterHealing), Is.EqualTo(0d),
                "healing must produce no observation delta");

            var beforeUnsupported = await CaptureWorldFeedbackMetrics();
            await Server.WaitPost(() =>
            {
                var unsupported =
                    new DamageSpecifier(SProtoMan.Index(StructuralDamageTypeId), FixedPoint2.New(50));
                Server.EntMan.System<DamageableSystem>().ChangeDamage(target, unsupported, origin: observer);
            });
            await Pair.RunTicksSync(5);
            var afterUnsupported = await CaptureWorldFeedbackMetrics();
            await AssertClientHasNoCue("unsupported damage type that changes no target state");
            Assert.That(TotalDelta(beforeUnsupported, afterUnsupported), Is.EqualTo(0d),
                "an unapplied request must produce no observation delta");
        }
        finally
        {
            await ResetGates();
        }
    }

    [TestCase("Poison", TestName = "ToxinDamage_RecordsFilteredAndNeverRaisesCue")]
    [TestCase("Radiation")]
    [TestCase("Asphyxiation")]
    [TestCase("Heat")]
    public async Task NonKineticDamage_RecordsFilteredAndNeverRaisesCue(string damageType)
    {
        EntityUid observer = default;
        EntityUid target = default;

        try
        {
            await Server.WaitAssertion(() => (observer, target) = PrepareObserverWorld());
            await SetGates(master: true, delivery: true, observe: true);
            var before = await CaptureWorldFeedbackMetrics();

            await DealDamageAndTick(observer, target, damageType, 5);

            var after = await CaptureWorldFeedbackMetrics();
            await AssertClientHasNoCue($"{damageType} damage");
            Assert.That(
                Delta(before, after, new MetricKey("non_kinetic", "filtered")),
                Is.EqualTo(1d));
            Assert.That(TotalDelta(before, after), Is.EqualTo(1d));
        }
        finally
        {
            await ResetGates();
        }
    }

    [Test]
    public async Task EmptyZeroAndHealingDamage_ProduceNoObservationDelta()
    {
        EntityUid observer = default;
        EntityUid target = default;

        try
        {
            await Server.WaitAssertion(() => (observer, target) = PrepareObserverWorld());
            await SetGates(master: true, delivery: false, observe: false);
            await DealDamageAndTick(observer, target, 5);
            await ClearClientVisibility();
            await SetGates(master: true, delivery: false, observe: true);
            var before = await CaptureWorldFeedbackMetrics();

            await Server.WaitPost(() =>
            {
                var damageable = Server.EntMan.System<DamageableSystem>();
                damageable.ChangeDamage(target, new DamageSpecifier(), origin: observer);
                damageable.ChangeDamage(
                    target,
                    new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), FixedPoint2.Zero),
                    origin: observer);
                damageable.ChangeDamage(
                    target,
                    new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), FixedPoint2.New(-5)),
                    origin: observer);
            });
            await Pair.RunTicksSync(5);

            var after = await CaptureWorldFeedbackMetrics();
            await AssertClientHasNoCue("empty, zero, and healing damage");
            Assert.That(TotalDelta(before, after), Is.EqualTo(0d));
        }
        finally
        {
            await ResetGates();
        }
    }

    [Test]
    public async Task TryDoElectrocution_ObservesOneCandidateAndEmitsOnlyAfterSuccess()
    {
        EntityUid target = default;
        EntityUid insulatedTarget = default;

        try
        {
            await Server.WaitAssertion(() =>
            {
                var mapSystem = Server.EntMan.System<SharedMapSystem>();
                mapSystem.CreateMap(out var mapId);
                target = Server.EntMan.SpawnEntity("MobSkeletonPerson", new MapCoordinates(0f, 0f, mapId));
                insulatedTarget = Server.EntMan.SpawnEntity("MobSkeletonPerson", new MapCoordinates(1f, 0f, mapId));

                AttachObserverAndDeletePreviousBody(target);
            });
            await SetGates(master: true, delivery: true, observe: true);
            await Pair.RunTicksSync(10);
            await ClearClientVisibility();
            var beforeSuccess = await CaptureWorldFeedbackMetrics();
            (uint Broadcast, uint Targeted) raiserAttemptsBefore = default;
            await Server.WaitAssertion(() =>
            {
                raiserAttemptsBefore =
                    Server.EntMan.System<SolreignFxDiagnosticsSystem>().CountersForTests;
            });

            var succeeded = false;
            await Server.WaitAssertion(() =>
            {
                succeeded = Server.EntMan.System<ElectrocutionSystem>().TryDoElectrocution(
                    target,
                    sourceUid: null,
                    shockDamage: 5,
                    time: System.TimeSpan.FromSeconds(1),
                    refresh: true,
                    ignoreInsulation: true);
                Assert.That(succeeded, Is.True,
                    "the real electrocution path must succeed before an FX cue is expected");
            });
            await Pair.RunTicksSync(15);
            var afterSuccess = await CaptureWorldFeedbackMetrics();
            (uint Broadcast, uint Targeted) raiserAttemptsAfter = default;
            await Server.WaitAssertion(() =>
            {
                raiserAttemptsAfter =
                    Server.EntMan.System<SolreignFxDiagnosticsSystem>().CountersForTests;
            });
            await Client.WaitAssertion(() =>
            {
                var system = Client.System<SolreignFxCueSystem>();
                Assert.Multiple(() =>
                {
                    Assert.That(system.RawReceivedEffectIdsForTests.Count(id => id == "body_shock_generic"),
                        Is.EqualTo(1), "one successful source event may produce at most one raw shock cue");
                    Assert.That(system.AcceptedCuesForTests.Count(c => c.EffectId == "body_shock_generic"),
                        Is.EqualTo(1));
                    Assert.That(system.RawReceivedEffectIdsForTests.Count(id =>
                        id is "impact_light" or "impact_heavy"), Is.Zero);
                    Assert.That(system.AcceptedCuesForTests.Count(c =>
                        c.EffectId is "impact_light" or "impact_heavy"), Is.Zero);
                });
            });
            Assert.Multiple(() =>
            {
                Assert.That(
                    unchecked(raiserAttemptsAfter.Broadcast - raiserAttemptsBefore.Broadcast),
                    Is.EqualTo(1u),
                    "one successful electrocution must make exactly one broadcast raiser attempt; " +
                    "the correlation counter increments before egress coalescing");
                Assert.That(
                    unchecked(raiserAttemptsAfter.Targeted - raiserAttemptsBefore.Targeted),
                    Is.Zero,
                    "world feedback must not mint a targeted correlation ID");
            });
            Assert.That(
                Delta(beforeSuccess, afterSuccess,
                    new MetricKey("electrocution", "raiser_accepted_or_coalesced")),
                Is.EqualTo(1d));
            Assert.That(KineticDelta(beforeSuccess, afterSuccess), Is.EqualTo(0d),
                "successful electrocution creates no kinetic-impact candidate");
            var shockDamageDelta = Delta(
                beforeSuccess,
                afterSuccess,
                new MetricKey("non_kinetic", "filtered"));
            Assert.That(shockDamageDelta, Is.AnyOf(0d, 1d),
                "the upstream Shock DamageChangedEvent may independently contribute one filtered observation");
            Assert.That(TotalDelta(beforeSuccess, afterSuccess), Is.EqualTo(1d + shockDamageDelta));

            await ClearClientVisibility();
            var beforeFailure = await CaptureWorldFeedbackMetrics();
            await Server.WaitAssertion(() =>
            {
                var insulated = Server.EntMan.EnsureComponent<InsulatedComponent>(insulatedTarget);
                Server.EntMan.System<SharedElectrocutionSystem>()
                    .SetInsulatedSiemensCoefficient(insulatedTarget, 0f, insulated);
                succeeded = Server.EntMan.System<ElectrocutionSystem>().TryDoElectrocution(
                    insulatedTarget,
                    sourceUid: null,
                    shockDamage: 5,
                    time: System.TimeSpan.FromSeconds(1),
                    refresh: true,
                    ignoreInsulation: false);
                Assert.That(succeeded, Is.False,
                    "a fully insulated target must fail the real electrocution attempt");
            });
            await Pair.RunTicksSync(5);
            var afterFailure = await CaptureWorldFeedbackMetrics();
            await AssertClientHasNoCue("failed insulated electrocution");
            Assert.That(TotalDelta(beforeFailure, afterFailure), Is.EqualTo(0d),
                "a failed upstream electrocution creates no observation candidate");
        }
        finally
        {
            await ResetGates();
        }
    }

    private (EntityUid Observer, EntityUid Target) PrepareObserverWorld()
    {
        var mapSystem = Server.EntMan.System<SharedMapSystem>();
        mapSystem.CreateMap(out var mapId);

        var observer = Server.EntMan.SpawnEntity("MobSkeletonPerson", new MapCoordinates(0f, 0f, mapId));
        var target = Server.EntMan.SpawnEntity("MobSkeletonPerson", new MapCoordinates(1f, 0f, mapId));

        AttachObserverAndDeletePreviousBody(observer);
        return (observer, target);
    }

    private void AttachObserverAndDeletePreviousBody(EntityUid observer)
    {
        var players = Server.ResolveDependency<IPlayerManager>();
        Assert.That(ServerSession, Is.Not.Null);
        var previousBody = ServerSession!.AttachedEntity;
        Assert.That(players.SetAttachedEntity(ServerSession, observer), Is.True);

        if (previousBody is { } previous &&
            previous != observer &&
            Server.EntMan.EntityExists(previous))
        {
            Server.EntMan.QueueDeleteEntity(previous);
        }
    }

    private async Task DealDamageAndTick(
        EntityUid origin,
        EntityUid target,
        int damageAmount)
    {
        await ClearClientVisibility();
        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), FixedPoint2.New(damageAmount));
            Server.EntMan.System<DamageableSystem>().ChangeDamage(
                target,
                damage,
                ignoreResistances: true,
                origin: origin);
        });
        await Pair.RunTicksSync(5);
    }

    private async Task DealDamageAndTick(
        EntityUid origin,
        EntityUid target,
        string damageType,
        int damageAmount)
    {
        await ClearClientVisibility();
        await Server.WaitPost(() =>
        {
            var type = SProtoMan.Index<DamageTypePrototype>(damageType);
            var damage = new DamageSpecifier(type, FixedPoint2.New(damageAmount));
            Server.EntMan.System<DamageableSystem>().ChangeDamage(
                target,
                damage,
                ignoreResistances: true,
                origin: origin);
        });
        await Pair.RunTicksSync(5);
    }

    private async Task SetGates(bool master, bool delivery, bool observe)
    {
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, master);
            Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled, delivery);
            Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackObserveEnabled, observe);
        });
        await Pair.RunTicksSync(5);
    }

    private async Task ResetGates()
    {
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackObserveEnabled, true);
            Server.CfgMan.SetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled, true);
            Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, true);
        });
        await Pair.RunTicksSync(2);
    }

    private async Task ClearClientVisibility()
    {
        await Client.WaitAssertion(() => Client.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());
    }

    private async Task AssertClientHasNoCue(string scenario)
    {
        await Client.WaitAssertion(() =>
        {
            var system = Client.System<SolreignFxCueSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(system.RawReceivedEffectIdsForTests, Is.Empty, $"{scenario}: no FX cue should reach the client wire path");
                Assert.That(system.AcceptedCuesForTests, Is.Empty, $"{scenario}: no FX cue should be accepted");
            });
        });
    }

    private static async Task<Dictionary<MetricKey, double>> CaptureWorldFeedbackMetrics()
    {
        await using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream, CancellationToken.None);
        var exposition = Encoding.UTF8.GetString(stream.ToArray());
        var result = new Dictionary<MetricKey, double>();

        foreach (var line in exposition.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("solreign_fx_world_feedback_total{", StringComparison.Ordinal))
                continue;

            var match = WorldFeedbackMetricLine.Match(line);
            Assert.That(match.Success, Is.True,
                $"world-feedback metrics must have exactly class/outcome labels and no other dimensions: {line}");
            var key = new MetricKey(match.Groups[1].Value, match.Groups[2].Value);
            Assert.That(result.TryAdd(
                    key,
                    double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)),
                Is.True,
                $"duplicate world-feedback metric pair: {key}");
        }

        return result;
    }

    private static double Delta(
        IReadOnlyDictionary<MetricKey, double> before,
        IReadOnlyDictionary<MetricKey, double> after,
        MetricKey key)
    {
        return Value(after, key) - Value(before, key);
    }

    private static double TotalDelta(
        IReadOnlyDictionary<MetricKey, double> before,
        IReadOnlyDictionary<MetricKey, double> after)
    {
        return LegalMetricKeys.Sum(key => Delta(before, after, key));
    }

    private static double KineticDelta(
        IReadOnlyDictionary<MetricKey, double> before,
        IReadOnlyDictionary<MetricKey, double> after)
    {
        return LegalMetricKeys
            .Where(key => key.Class is "kinetic_light" or "kinetic_heavy")
            .Sum(key => Delta(before, after, key));
    }

    private static double Value(IReadOnlyDictionary<MetricKey, double> metrics, MetricKey key)
    {
        return metrics.TryGetValue(key, out var value) ? value : 0d;
    }

    private readonly record struct MetricKey(string Class, string Outcome);
}
