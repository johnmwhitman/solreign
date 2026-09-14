#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Behavioral companion to the static playable-species organ gate. Every round-start species
///     is instantiated from its real mob prototype in breathable atmosphere and soaked through
///     the real atmosphere, lung, respirator, damage, and mob-state systems.
///
///     This gate is intentionally about breathing viability, not zero total damage: Vox can
///     legitimately react to oxygen in the mixed-gas test room. A selectable species must remain
///     alive and accumulate no asphyxiation there. Other organ/content invariants belong in
///     separate calibrated gates.
/// </summary>
[TestFixture]
public sealed class SolreignPlayableSpeciesBehavioralViabilityTest : GameTest
{
    // Respirator damage begins only after several missed cycles. Thirty simulated seconds keeps
    // the negative control deterministic while remaining cheap because every species soaks at once.
    private const int SoakTicks = 900;
    private static readonly ResPath BreathingMap = new("Maps/Test/Breathing/3by3-20oxy-80nit.yml");

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    public async Task EveryRoundStartSpecies_RemainsAliveWithoutAsphyxiationAfterBreathingSoak()
    {
        var species = new List<(string Label, EntProtoId MobPrototype)>();

        await Server.WaitAssertion(() =>
        {
            species.AddRange(Server.ProtoMan.EnumeratePrototypes<SpeciesPrototype>()
                .Where(proto => proto.RoundStart)
                .OrderBy(proto => proto.ID)
                .Select(proto => (proto.ID, proto.Prototype)));
        });

        Assert.That(species, Has.Count.GreaterThanOrEqualTo(8),
            "Too few round-start species were discovered; the behavioral gate proved nothing.");

        var observations = await ObserveAfterBreathingSoak(species);

        Assert.Multiple(() =>
        {
            foreach (var observation in observations)
            {
                Assert.That(observation.Alive, Is.True,
                    $"Round-start species '{observation.Label}' ({observation.MobPrototype}) was not alive " +
                    $"after a {SoakTicks}-tick soak in breathable atmosphere.");
                Assert.That(observation.Asphyxiation, Is.Zero,
                    $"Round-start species '{observation.Label}' ({observation.MobPrototype}) accumulated " +
                    $"{observation.Asphyxiation} asphyxiation damage during a {SoakTicks}-tick soak in breathable atmosphere.");
            }
        });
    }

    [Test]
    public async Task FaultInjection_VacuumCausesAsphyxiationThroughTheRealRespirationPath()
    {
        var observations = await ObserveAfterBreathingSoak(
            [("vacuum-control", new EntProtoId("MobHuman"))],
            (grid, _) =>
            {
                var gridAtmosphere = Server.EntMan.GetComponent<GridAtmosphereComponent>(grid);
                Assert.That(gridAtmosphere.Tiles, Is.Not.Empty,
                    "The negative-control grid has no atmosphere tiles; the fixture proved nothing.");
                foreach (var tile in gridAtmosphere.Tiles.Values)
                    tile.Air?.Clear();
            });

        Assert.That(observations, Has.Count.EqualTo(1));
        Assert.That(observations[0].Asphyxiation, Is.GreaterThan(0),
            "A real human in the evacuated room accumulated no asphyxiation; " +
            "the behavioral gate would not catch a broken breathing path.");
    }

    private async Task<List<SpeciesObservation>> ObserveAfterBreathingSoak(
        IReadOnlyList<(string Label, EntProtoId MobPrototype)> targets,
        Action<EntityUid, EntityUid>? afterSpawn = null)
    {
        var entMan = Server.EntMan;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var damageSystem = entMan.System<DamageableSystem>();
        var grids = new List<EntityUid>(targets.Count);
        var mobs = new List<(string Label, EntProtoId MobPrototype, EntityUid Mob)>();

        try
        {
            await Server.WaitPost(() =>
            {
                foreach (var (label, mobPrototype) in targets)
                {
                    mapSystem.CreateMap(out var mapId);
                    Assert.That(mapLoader.TryLoadGrid(mapId, BreathingMap, out var loadedGrid));
                    var grid = loadedGrid!.Value.Owner;
                    grids.Add(grid);

                    var mob = entMan.SpawnEntity(mobPrototype, new EntityCoordinates(grid, 0.5f, 0.5f));
                    mobs.Add((label, mobPrototype, mob));
                    afterSpawn?.Invoke(grid, mob);
                }
            });

            await Server.WaitRunTicks(SoakTicks);

            var observations = new List<SpeciesObservation>(mobs.Count);
            await Server.WaitAssertion(() =>
            {
                foreach (var (label, mobPrototype, mob) in mobs)
                {
                    Assert.That(entMan.EntityExists(mob), Is.True,
                        $"Mob prototype '{mobPrototype}' was deleted during the breathing soak.");
                    Assert.That(entMan.TryGetComponent(mob, out DamageableComponent? damageable), Is.True,
                        $"Mob prototype '{mobPrototype}' has no DamageableComponent.");
                    Assert.That(entMan.TryGetComponent(mob, out MobStateComponent? mobState), Is.True,
                        $"Mob prototype '{mobPrototype}' has no MobStateComponent.");

                    var damage = damageSystem.GetAllDamage((mob, damageable));
                    var asphyxiation = damage.DamageDict.TryGetValue("Asphyxiation", out var amount)
                        ? (double) amount
                        : 0;

                    observations.Add(new SpeciesObservation(
                        label,
                        mobPrototype,
                        mobState!.CurrentState == MobState.Alive,
                        asphyxiation));
                }
            });

            return observations;
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                foreach (var grid in grids)
                {
                    if (entMan.EntityExists(grid))
                        entMan.DeleteEntity(grid);
                }
            });
        }
    }

    private readonly record struct SpeciesObservation(
        string Label,
        EntProtoId MobPrototype,
        bool Alive,
        double Asphyxiation);
}
