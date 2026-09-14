using Content.Shared._Solreign.Changeling;
using Robust.Client.GameObjects;

namespace Content.Client._Solreign.Changeling;

/// <summary>
///     Client-side reaction to <see cref="SolreignChangelingVisuals.ArmBladeExtended"/> (spec
///     <c>docs/specs/FX-LANGUAGE-V1-SPEC-2026-07-16.md</c> §6.2 item 1 — the literal shipped-bug
///     fix). The server-side networking of this flag
///     (<c>Content.Server._Solreign.Changeling.SolreignChangelingSystem.ArmBlade.cs</c>'s
///     <c>_appearance.SetData(...)</c> calls) was ALREADY correct before this worktree — it is a
///     plain <see cref="AppearanceComponent"/> data write, which every humanoid species body already
///     networks regardless of prototype (per <c>BaseSpeciesAppearance</c>'s <c>- type: Appearance</c>).
///     The bug was always "nothing consumes it client-side."
///
///     <b>NOT implemented as a `GenericVisualizer` YAML component</b>, despite that being the spec's
///     literal §6.2 sample — a real changeling's body is whatever species/entity prototype the
///     player already had BEFORE the antag role was granted
///     (<c>Resources/Prototypes/Roles/Antags/changeling.yml</c>'s <c>antagSpecifier</c> applies its
///     <c>components:</c> grant list onto an ALREADY-SPAWNED entity at role-assignment time, via
///     <c>AntagSelectionSystem.AssignAntagComponents</c> → <c>EntityManager.AddComponents</c> — not
///     via entity-prototype composition at spawn). <c>GenericVisualizerComponent</c> is a
///     <c>Robust.Client</c>-only, non-<c>[NetworkedComponent]</c> type with ZERO server-side
///     representation: the server's own <see cref="Robust.Shared.GameObjects.IComponentFactory"/>
///     resolves it as <c>ComponentAvailability.Unknown</c>, so
///     <c>ComponentRegistrySerializer</c> drops it at YAML-parse time before
///     <c>AntagSpecifierPrototype.Components</c> is ever built — there is no component instance to
///     apply, and even if there were, nothing would ever network it to any client (contrast with
///     <c>Resources/Prototypes/Entities/foldable.yml</c>'s `BaseFoldable`, which works precisely
///     because it is an ENTITY PROTOTYPE: each side's own <see cref="Robust.Shared.Prototypes.IPrototypeManager"/>
///     independently parses the SAME shared YAML and builds its OWN local component set at spawn —
///     the client's copy has `GenericVisualizer` `Available`, entirely independent of what the
///     server did with its own). A first attempt at exactly this YAML-only fix was caught and
///     corrected by grk adversarial review during this worktree — see the W4 receipt
///     (<c>docs/receipts/fx-w4/FX-W4-2026-07-16.md</c>) for the full account.
///
///     This system instead listens directly for <see cref="AppearanceChangeEvent"/> on ANY entity
///     with an <see cref="AppearanceComponent"/> (universal — no marker component required, so it
///     works identically whether the entity is <c>MobLing</c> or an ordinary Human/Moth/whatever
///     body a real changeling role was granted onto) and manually reserves/toggles the
///     <c>"armBladeLayer"</c> sprite layer using the exact same
///     <see cref="SpriteComponent.LayerMapReserveBlank"/>/<see cref="SpriteComponent.LayerSetData(int, Robust.Shared.GameObjects.PrototypeLayerData)"/>
///     primitives <see cref="GenericVisualizerSystem"/> itself uses internally.
/// </summary>
public sealed class SolreignChangelingArmBladeVisualsSystem : EntitySystem
{
    private const string ArmBladeLayerKey = "armBladeLayer";
    // SOLREIGN's own blade art. This used to point at Objects/Weapons/Melee/armblade.rsi,
    // which is upstream tgstation art (CC-BY-SA-3.0) shared with the non-Solreign changeling
    // prototypes — so every Solreign blade drew somebody else's sprite, and repainting it
    // would have changed theirs too.
    private const string ArmBladeRsiPath = "_Solreign/solreign_changeling_armblade.rsi";
    private const string ArmBladeExtendedState = "inhand-right";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AppearanceComponent, AppearanceChangeEvent>(OnAppearanceChanged);
    }

    private void OnAppearanceChanged(EntityUid uid, AppearanceComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite)
            return;

        if (!args.AppearanceData.TryGetValue(SolreignChangelingVisuals.ArmBladeExtended, out var raw) || raw is not bool extended)
            return;

        var index = sprite.LayerMapReserveBlank(ArmBladeLayerKey);

        sprite.LayerSetData(index, extended
            ? new Robust.Shared.GameObjects.PrototypeLayerData
            {
                RsiPath = ArmBladeRsiPath,
                State = ArmBladeExtendedState,
                Visible = true,
            }
            : new Robust.Shared.GameObjects.PrototypeLayerData
            {
                Visible = false,
            });
    }
}
