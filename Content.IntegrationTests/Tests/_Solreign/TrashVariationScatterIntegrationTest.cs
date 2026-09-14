#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Tag;
using NUnit.Framework;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Regression coverage for the live "trash spawns in one central pile" bug report. Root cause
///     (found by re-investigating after commit 74f551661b's Terminus map-marker fix didn't hold):
///     the upstream rewrite of <c>GameRuleSystem&lt;T&gt;.TryFindRandomTileOnStation</c>
///     (Content.Server/GameTicking/Rules/GameRuleSystem.Utility.cs, commit c0f35ade3e
///     "Rewrite TryFindRandomTileOnStation (#44382)") introduced a weighted-by-filled-tile-count
///     tile selection (an improvement over the old AABB-random-point approach) but never copied the
///     actually-selected <c>tileRef.GridIndices</c> into the <c>tile</c> out-parameter -- it stayed
///     at its <c>default</c> initializer (grid-local (0,0)) for the entire call. Every caller that
///     relies on the returned <c>targetCoords</c> (computed from that stuck-at-default <c>tile</c>)
///     therefore collapses onto the same single tile: the round-start "BasicTrashVariationPass"
///     rule (Resources/Prototypes/GameRules/variation.yml, wired into every stock game preset via
///     BasicRoundstartVariation) that scatters ambient litter station-wide, the equivalent puddle-
///     mess pass, and every _Solreign rule sharing this helper (spore drift, acid storm, compliance
///     hunter, auditor prime, specimen zero).
///
///     74f551661b addressed a *different*, narrower bug (three RandomSpawner100 map markers
///     literally stacked on the same Terminus tile in the authored YAML) and didn't touch this
///     shared utility, which is why the live pile kept recurring after that fix landed.
///
///     This test drives the exact production round-start path
///     (<see cref="GameTicker.StartGameRule(string, out EntityUid)"/> for "BasicRoundstartVariation"
///     then <see cref="GameTicker.LoadGameMap"/>) against SolreignNocturne, a live Solreign map with
///     no baked RandomSpawner/RandomSpawner100 trash markers of its own (verified separately by
///     grepping Resources/Maps/_Solreign/solreign_nocturne.yml), so any "Trash"-tagged entity found
///     after load can only have come from the variation pass -- isolating the exact code path that
///     was broken. Pre-fix this test fails (all trash collapses onto one tile); post-fix it scatters.
/// </summary>
[TestFixture]
public sealed class TrashVariationScatterIntegrationTest : GameTest
{
    private const string TrashTag = "Trash";
    private const string NocturneMap = "SolreignNocturne";
    private const string VariationRule = "BasicRoundstartVariation";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        Dirty = true,
    };

    [Test]
    public async Task BasicTrashVariationPass_ScattersTrashAcrossStation_NotSinglePile()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var ticker = entManager.System<GameTicker>();
        var tagSystem = entManager.System<TagSystem>();

        await server.WaitPost(() =>
        {
            Assert.That(ticker.StartGameRule(VariationRule, out _), Is.True,
                $"Failed to start {VariationRule} -- can't exercise the round-start trash pass.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(protoManager.Index<GameMapPrototype>(NocturneMap), out _, opts);
        });

        await server.WaitAssertion(() =>
        {
            var positions = new HashSet<(EntityUid Grid, int X, int Y)>();
            var trashCount = 0;

            var query = entManager.AllEntityQueryEnumerator<TransformComponent, TagComponent>();
            while (query.MoveNext(out var uid, out var xform, out _))
            {
                if (!tagSystem.HasTag(uid, TrashTag))
                    continue;

                trashCount++;
                var local = xform.LocalPosition;
                positions.Add((xform.GridUid ?? EntityUid.Invalid, (int) MathF.Floor(local.X), (int) MathF.Floor(local.Y)));
            }

            // Too few samples to say anything meaningful about distribution -- if this trips, the
            // variation pass itself isn't running (check the rule/preset wiring, not this test).
            Assert.That(trashCount, Is.GreaterThan(10),
                $"Only {trashCount} trash-tagged entities were spawned on {NocturneMap} -- too few to " +
                "assert scatter. Expected BasicTrashVariationPass to have placed dozens.");

            // The bug collapsed every variation-pass spawn onto a single grid-local tile (0,0) --
            // measured directly (by temporarily reverting the fix) as 3 distinct positions for 137
            // trash entities on this map (one per station grid, since each collapses to that grid's
            // own local origin): a ~2% distinct-position ratio that stays flat regardless of map
            // size. A working scatter lands on a meaningfully larger, size-scaling subset of the
            // station's eligible (non-space, non-air-blocked) tiles -- measured post-fix at ~15% on
            // this map and ~19% on the much larger Terminus map. `/10` sits with a wide margin above
            // the bug's ~2% and below the fix's observed ~15-19%, so it flags the collapse without
            // being brittle to the natural birthday-paradox clustering of a real random scatter.
            Assert.That(positions.Count, Is.GreaterThan(trashCount / 10),
                $"Trash is piling up instead of scattering: {trashCount} trash entities but only " +
                $"{positions.Count} distinct tile positions on {NocturneMap}.");
        });
    }
}
