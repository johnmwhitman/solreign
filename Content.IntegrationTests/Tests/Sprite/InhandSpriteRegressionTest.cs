#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.IntegrationTests.Tests.Sprite;

/// <summary>
/// Player-feedback batch 2026-07-16, items 1/2/3 (triage items 1, 10, 25): three Solreign
/// easter-egg prototypes either errored on pickup or silently rendered the wrong sprite once
/// held, all via the same mechanism -- <see cref="Content.Client.Hands.Systems.HandsSystem"/>'s
/// in-hand layer resolution falls back to (in order) an explicit layer RSI, then
/// <see cref="ItemComponent.RsiPath"/>, then the entity's own <see cref="SpriteComponent.BaseRSI"/>
/// -- and calls <c>LayerSetData</c> with whatever state name the layer specifies, with NO
/// existence check on that final path (unlike the *default*-visuals path in
/// <c>Content.Client.Items.Systems.ItemSystem.TryGetDefaultVisuals</c>, which does check and is
/// what <c>ItemSpriteTest.cs</c>'s <see cref="PrototypeSaveTest"/> covers -- that test's own doc
/// comment says "this has nothing to do with in-hand sprites").
///
/// This test closes that specific gap for the three fixed prototypes by reproducing the exact
/// same resolution HandsSystem performs and asserting the resolved RSI actually has the state.
/// It is intentionally scoped to these three prototypes, not a repo-wide sweep -- the
/// player-feedback triage's item 8 audit already found 16 *other* Solreign items with a related
/// but distinct worn-sprite gap (Clothing sprite, not Item/in-hand), tracked separately for the
/// sprite-factory wave.
///
/// Note the two fixed prototypes don't hit the identical failure mode: SolreignEgg20 inherits an
/// *explicit* (if RSI-less) <c>Item.InhandVisuals</c> entry from its parent, which skips the
/// existence check and would hard-error on pickup (item 1's actual crash). SolreignLiquidFlameThermos
/// has no explicit InhandVisuals anywhere in its ancestor chain, so it instead takes the *checked*
/// default-visuals path in <c>ItemSystem.TryGetDefaultVisuals</c> -- missing states there would
/// silently resolve to zero held layers (no crash, just nothing visible in hand), not a crash. Both
/// are still real bugs worth this same fix (real inhand states), just via different code paths.
/// </summary>
[TestFixture]
public sealed class InhandSpriteRegressionTest : GameTest
{
    private static readonly string[] TargetPrototypes =
    {
        "SolreignEgg20", // item 1: omni gauntlet, pickup error (explicit InhandVisuals from its
                          // parent skips the existence check -> hard error on pickup)
        "SolreignLiquidFlameThermos", // item 2: no explicit InhandVisuals, so missing inhand
                                      // states would silently resolve to nothing shown in hand
                                      // (not a crash -- see class doc); icon_open (the actual
                                      // open-error) is checked separately by
                                      // LiquidFlameThermosHasOpenState below
        "SolreignEgg12", // item 3/25: transit orb, wrong sprite in hand (BeachBall sets
                         // Item.sprite explicitly, so this needed its own Item.sprite override)
    };

    [Test]
    [TestCaseSource(nameof(TargetPrototypes))]
    public async Task InhandLayersResolveToExistingRsiStates(string protoId)
    {
        var pair = Pair;
        var client = pair.Client;
        var entMan = client.EntMan;
        var resCache = client.ResolveDependency<IResourceCache>();

        var failures = new List<string>();

        await client.WaitPost(() =>
        {
            var uid = entMan.Spawn(protoId);
            entMan.RunMapInit(uid, entMan.GetComponent<MetaDataComponent>(uid));

            if (!entMan.TryGetComponent(uid, out ItemComponent? item))
            {
                failures.Add($"{protoId}: no ItemComponent");
                entMan.DeleteEntity(uid);
                return;
            }

            if (!entMan.TryGetComponent(uid, out SpriteComponent? sprite))
            {
                failures.Add($"{protoId}: no SpriteComponent");
                entMan.DeleteEntity(uid);
                return;
            }

            foreach (var location in new[] { HandLocation.Left, HandLocation.Right })
            {
                var ev = new GetInhandVisualsEvent(uid, location);
                entMan.EventBus.RaiseLocalEvent(uid, ev);

                if (ev.Layers.Count == 0)
                {
                    failures.Add($"{protoId} ({location}): no in-hand layers resolved at all");
                    continue;
                }

                foreach (var (key, layerData) in ev.Layers)
                {
                    if (layerData.TexturePath != null)
                        continue; // raw texture layer, no RSI state involved

                    // Mirrors Content.Client/Hands/Systems/HandsSystem.cs's UpdateHandVisuals
                    // fallback order exactly: explicit layer RSI wins; else ItemComponent.RsiPath;
                    // else the entity's own Sprite.BaseRSI.
                    RSI? rsi;
                    if (layerData.RsiPath != null)
                    {
                        if (!resCache.TryGetResource(SpriteSpecifierSerializer.TextureRoot / layerData.RsiPath,
                                out RSIResource? rsiRes))
                        {
                            failures.Add($"{protoId} ({location}, layer '{key}'): layer RSI path '{layerData.RsiPath}' does not resolve to a resource");
                            continue;
                        }
                        rsi = rsiRes.RSI;
                    }
                    else if (item.RsiPath != null)
                    {
                        if (!resCache.TryGetResource(SpriteSpecifierSerializer.TextureRoot / item.RsiPath,
                                out RSIResource? rsiRes))
                        {
                            failures.Add($"{protoId} ({location}, layer '{key}'): Item.RsiPath '{item.RsiPath}' does not resolve to a resource");
                            continue;
                        }
                        rsi = rsiRes.RSI;
                    }
                    else
                    {
                        rsi = sprite.BaseRSI;
                    }

                    if (rsi == null)
                    {
                        failures.Add($"{protoId} ({location}, layer '{key}'): could not resolve any fallback RSI (no layer RSI, no Item.RsiPath, no Sprite.BaseRSI)");
                        continue;
                    }

                    if (layerData.State == null)
                        continue;

                    if (!rsi.TryGetState(layerData.State, out _))
                    {
                        failures.Add(
                            $"{protoId} ({location}, layer '{key}'): RSI '{rsi.Path}' has no state '{layerData.State}' -- " +
                            "this is exactly the item-1/2/25 crash/wrong-sprite class (HandsSystem resolves to this RSI at " +
                            "runtime with no existence check and errors, or silently shows the wrong sprite)");
                    }
                }
            }

            entMan.DeleteEntity(uid);
        });

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// Item 2 specifically: the thermos also needs an <c>icon_open</c> state for
    /// DrinkVisualsOpenable's GenericVisualizer (enum.OpenableVisuals.Layer -> "icon_open" when
    /// opened) -- a separate mechanism from the in-hand layers above, checked directly here.
    /// </summary>
    [Test]
    public async Task LiquidFlameThermosHasOpenState()
    {
        var pair = Pair;
        var client = pair.Client;
        var entMan = client.EntMan;

        await client.WaitPost(() =>
        {
            var uid = entMan.Spawn("SolreignLiquidFlameThermos");
            entMan.RunMapInit(uid, entMan.GetComponent<MetaDataComponent>(uid));

            Assert.That(entMan.TryGetComponent(uid, out SpriteComponent? sprite), Is.True,
                "SolreignLiquidFlameThermos has no SpriteComponent");

            var rsi = sprite!.BaseRSI;
            Assert.That(rsi, Is.Not.Null, "SolreignLiquidFlameThermos's Sprite has no BaseRSI");
            Assert.That(rsi!.TryGetState("icon_open", out _), Is.True,
                $"SolreignLiquidFlameThermos's RSI ('{rsi.Path}') is missing 'icon_open' -- " +
                "DrinkVisualsOpenable's GenericVisualizer switches to this state on open and errors without it");

            entMan.DeleteEntity(uid);
        });
    }
}
