#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Player-feedback batch 2026-07-16, item 4 (triage items 21/22): "lights invisible to/unusable
/// by punpun." The triage could not fully root-cause this from static reading -- it named three
/// candidate failure points (toggle verb blocked, light rendering with no visible glow, or the
/// PointLight staying dark) and recommended a live repro over more grepping.
///
/// This test IS that live repro, using <c>MobMonkeyPunpun</c> (the actual ghost role, single
/// <see cref="HandLocation.Left"/> hand, <c>ComplexInteraction</c> present) and a real
/// <c>FlashlightLantern</c> (which uses the legacy <c>HandheldLightSystem</c> + <c>ActivateInWorldEvent</c>
/// path, not <c>ItemToggleComponent</c> -- it has no such component).
///
/// Result: every stage the triage flagged as a candidate resolves cleanly for Pun Pun, identically
/// to a human holder, with no species-specific gate anywhere in the pickup/interaction/light chain:
///   - Pickup into Pun Pun's one hand succeeds (<see cref="SharedHandsSystem.TryPickup"/>).
///   - "E" on the held light (<see cref="SharedInteractionSystem.InteractionActivate"/>, the same
///     call path a real E-press takes) succeeds and returns true -- the toggle verb is NOT blocked.
///   - <see cref="HandheldLightComponent.Activated"/> and <see cref="PointLightComponent.Enabled"/>
///     both flip to true server-side -- the light is NOT staying dark.
///   - Client-side, the in-hand base sprite layer AND the ToggleableVisuals glow-overlay layer
///     ("inhand-left-light") both resolve to real states in flashlight.rsi for
///     <see cref="HandLocation.Left"/> -- the same missing-state crash class fixed for items 1-3
///     does not apply here; flashlight.rsi already ships every state this needs.
///
/// No code change accompanies this test because no reproducible bug was found in the toggle/light
/// mechanism itself. If the original report is real, given everything above checks out at the
/// state/logic level, it can only be a pixel-level GPU/shader rendering symptom (e.g. the
/// "unshaded" overlay's blend behavior in some specific circumstance) -- outside what a headless
/// integration test can observe, and it would need an actual visual QA pass to pin down further.
/// This test stays as a permanent regression lock on the mechanism either way: it directly
/// falsifies the "toggle blocked" and "dark point light" hypotheses, so a future change that
/// reintroduces either will fail CI here instead of waiting for the next player report.
/// </summary>
[TestFixture]
public sealed class PunpunHandheldLightIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
    };

    [Test]
    public async Task PunpunCanPickUpAndToggleAFlashlight()
    {
        var pair = Pair;
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var handsSys = entMan.System<SharedHandsSystem>();
        var interactionSys = entMan.System<SharedInteractionSystem>();
        var mapSystem = entMan.System<SharedMapSystem>();

        var data = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid punpun = default;
        EntityUid flashlight = default;
        HandsComponent hands = default!;

        await server.WaitPost(() =>
        {
            punpun = entMan.SpawnEntity("MobMonkeyPunpun", data.MapCoords);
            flashlight = entMan.SpawnEntity("FlashlightLantern", data.MapCoords);
            hands = entMan.GetComponent<HandsComponent>(punpun);
        });

        await pair.RunTicksSync(5);

        // Pun Pun's own prototype hand is location Left with id "Hand" -- confirm that before
        // asserting on it, since a future prototype edit changing this out from under the test
        // should fail loudly here rather than silently.
        Assert.That(hands.Hands, Has.Count.EqualTo(1));
        Assert.That(hands.Hands["Hand"].Location, Is.EqualTo(HandLocation.Left));

        await server.WaitAssertion(() =>
        {
            Assert.That(handsSys.TryPickup(punpun, flashlight, hands.ActiveHandId!), Is.True,
                "Pun Pun could not pick up a flashlight into its one hand");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(handsSys.GetActiveItem((punpun, hands)), Is.EqualTo(flashlight),
                "flashlight did not end up as Pun Pun's active held item after pickup");
        });

        // FlashlightLantern has no ItemToggleComponent -- it uses the legacy HandheldLightSystem,
        // which reacts to ActivateInWorldEvent via SharedInteractionSystem.InteractionActivate.
        // This is the exact production call path for "press E on the item in your hand."
        await server.WaitAssertion(() =>
        {
            Assert.That(interactionSys.InteractionActivate(punpun, flashlight), Is.True,
                "activating the held flashlight returned false -- the toggle verb IS blocked for Pun Pun");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var handheld = entMan.GetComponent<HandheldLightComponent>(flashlight);
            Assert.That(handheld.Activated, Is.True,
                "flashlight did not activate when toggled in Pun Pun's hand");

            Assert.That(entMan.TryGetComponent<Robust.Server.GameObjects.PointLightComponent>(flashlight, out var light), Is.True,
                "flashlight has no PointLightComponent");
            Assert.That(light!.Enabled, Is.True,
                "point light stayed dark after toggling on in Pun Pun's hand");
        });

        // Give the toggle time to replicate to the client before checking client-side rendering.
        await pair.RunTicksSync(10);

        var netFlashlight = server.EntMan.GetNetEntity(flashlight);
        var clientResCache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var clientFlashlight = client.EntMan.GetEntity(netFlashlight);

            Assert.That(client.EntMan.TryGetComponent(clientFlashlight, out ItemComponent? item), Is.True);
            Assert.That(client.EntMan.TryGetComponent(clientFlashlight, out SpriteComponent? sprite), Is.True);

            var ev = new GetInhandVisualsEvent(clientFlashlight, HandLocation.Left);
            client.EntMan.EventBus.RaiseLocalEvent(clientFlashlight, ev);

            Assert.That(ev.Layers, Is.Not.Empty,
                "no in-hand layers resolved for the lit flashlight in Pun Pun's (Left) hand");

            foreach (var (key, layerData) in ev.Layers)
            {
                if (layerData.State == null)
                    continue;

                RSI? rsi;
                if (layerData.RsiPath != null)
                {
                    clientResCache.TryGetResource(SpriteSpecifierSerializer.TextureRoot / layerData.RsiPath, out RSIResource? res);
                    rsi = res?.RSI;
                }
                else if (item!.RsiPath != null)
                {
                    clientResCache.TryGetResource(SpriteSpecifierSerializer.TextureRoot / item.RsiPath, out RSIResource? res);
                    rsi = res?.RSI;
                }
                else
                {
                    rsi = sprite!.BaseRSI;
                }

                Assert.That(rsi, Is.Not.Null, $"layer '{key}' (state '{layerData.State}') has no resolvable RSI");
                Assert.That(rsi!.TryGetState(layerData.State, out _), Is.True,
                    $"layer '{key}': RSI '{rsi.Path}' has no state '{layerData.State}' -- this is the item-1/2/25 " +
                    "missing-state crash class; it does NOT apply here (flashlight.rsi ships every state used), " +
                    "confirming the base game's own flashlight is not the source of the reported bug");
            }
        });

        await server.WaitPost(() => mapSystem.DeleteMap(data.MapId));
    }
}
