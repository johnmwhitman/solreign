using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Solreign.FX;
using Content.Shared.Camera;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Spawners;
using Robust.Shared.Utility;

namespace Content.Client._Solreign.FX;

/// <summary>
///     W3's engine-wired rendering half of <see cref="SolreignFxCueSystem"/> (spec §8 W3: "the
///     `effects.yml`-class prototypes + per-primitive client render recipes composing EXISTING
///     mechanisms"). Kept as a SEPARATE partial-class file rather than folded into W2's own
///     <c>SolreignFxCueSystem.cs</c> — the only edits made to that shipped file are the small,
///     documented <c>partial void</c> hook declarations/call-sites this file implements (see that
///     file's own W3-hook comments).
///
///     Per-activation DECISION logic lives in the engine-free
///     <see cref="Content.Shared._Solreign.FX.SolreignFxRenderRecipe"/> — this file's job is
///     strictly "take a <see cref="SolreignFxRenderPlan"/> and make the engine draw/play it,"
///     never primitive-specific branching of its own beyond dispatching category → recipe → plan.
///
///     Round-1 grk adversarial review (docs/receipts/fx-w3/FX-W3-2026-07-16.md §8) found several
///     real gaps in this file's first draft — every fix below is annotated with the finding it
///     closes.
/// </summary>
public sealed partial class SolreignFxCueSystem
{
    [Dependency] private SpriteSystem _fxSprite = default!;
    [Dependency] private SharedPointLightSystem _fxLight = default!;
    [Dependency] private SharedAudioSystem _fxAudio = default!;
    [Dependency] private IOverlayManager _fxOverlayMan = default!;
    [Dependency] private SharedCameraRecoilSystem _fxCameraRecoil = default!;

    private SolreignFxRenderPool? _renderPool;

    /// <summary>
    ///     Capacities awaiting application on the next frame, or null when the pool is in sync.
    /// </summary>
    /// <remarks>
    ///     The pool SPAWNS entities to size itself, and RebuildPool() runs from
    ///     SolreignFxCueSystem.Initialize(). Spawning during EntitySystem.Initialize() is not
    ///     valid — EntityManager.AllocEntity throws NullReferenceException — so sizing is staged
    ///     here and applied from FrameUpdate, by which point spawning is legal.
    ///
    ///     This was live for exactly as long as the feature was dormant. The disabled path sizes
    ///     the pool to ZERO, which spawns nothing, so the illegal spawn never happened and the bug
    ///     stayed invisible. Enabling solreign.fx.cue_v1 on 2026-07-25 aborted prototype loading
    ///     outright (Content.YAMLLinter exit 134) the first time real capacities were requested.
    ///
    ///     Deferring is safe by the pool's own contract: GetEntity documents that an unsized pool
    ///     returns null and that callers must treat that as "nothing to render this frame, never
    ///     throw". One frame of no effects at startup is exactly that case.
    /// </remarks>
    private IReadOnlyDictionary<SolreignFxCategory, int>? _pendingPoolCapacities;
    private readonly SolreignFxArcOverlay _arcOverlay = new();
    private readonly SolreignFxCastRingOverlay _castRingOverlay = new();
    private readonly SolreignFxCastRingCountdownOverlay _castRingCountdownOverlay = new();
    private readonly SolreignFxTransformationStingOverlay _transformationStingOverlay = new();
    private bool _worldOverlaysAdded;
    private bool _stingOverlayAdded;

    /// <summary>Per-(category, slot) render bookkeeping the base <c>ActiveEffect</c> record (W2) doesn't carry — anchor identity, start time, and the resolved plan, so per-frame reposition/overlay-refresh has what it needs without re-deriving the plan every frame.</summary>
    private sealed class RenderState
    {
        public SolreignFxCategory Category;
        public int SlotIndex;
        public NetCoordinates? Coordinates;
        public NetEntity? EntityAnchor;
        public double StartedAtSeconds;
        public double DurationSeconds;
        public SolreignFxRenderPlan Plan;
        public Color Color;
        public float BaseLightEnergy = 1f;
        public uint Seed;
        public float Intensity = 1f;

        /// <summary>
        ///     The lease-ANDed resource flags actually applied at acquire time (spec §3.1's atomic
        ///     lease grant, grk W3 round-1 review finding H3) — stored so <c>OnFrameTick</c>'s
        ///     per-frame refresh re-checks THESE, not the raw <see cref="Plan"/> flags, which would
        ///     silently bypass the lease-grant AND on every frame after the first.
        /// </summary>
        public bool ShowSprite;
        public bool ShowLight;
        public bool ShowOverlay;

        /// <summary>`cast_ring`'s <c>cosmetic_minimal</c> countdown-text fallback is the budget-wise SUBSTITUTE for the overlay slot (spec: "still one overlay activation") — gated by the SAME <c>lease.OverlayGranted</c> flag as <see cref="ShowOverlay"/>, even though the two are mutually exclusive per <see cref="Plan"/>.</summary>
        public bool ShowCountdownText;
    }

    private readonly List<RenderState> _renderStates = new();

    /// <summary>
    ///     grk W3 round-1 review finding #5: `transformation` cues bypass the lease manager entirely
    ///     (spec §3's all-zero budget row), so nothing bounded their transient burst-sprite entities.
    ///     Fixed client-side cap, oldest-recycled-first — same shape as spec §3's own
    ///     "recycle-not-drop" idiom for the lease-backed primitives, sized to match the server
    ///     egress budget's own already-accepted "nominal burst of 8" reasoning (W2 receipt finding
    ///     M-F) rather than inventing an unrelated number.
    /// </summary>
    private const int MaxLiveTransformationBursts = 8;
    private readonly Queue<EntityUid> _liveTransformationBursts = new();

    private void EnsureWorldOverlaysAdded()
    {
        if (_worldOverlaysAdded)
            return;

        _fxOverlayMan.AddOverlay(_arcOverlay);
        _fxOverlayMan.AddOverlay(_castRingOverlay);
        _fxOverlayMan.AddOverlay(_castRingCountdownOverlay);
        _worldOverlaysAdded = true;
    }

    private void RemoveWorldOverlaysIfAdded()
    {
        if (!_worldOverlaysAdded)
            return;

        _fxOverlayMan.RemoveOverlay(_arcOverlay);
        _fxOverlayMan.RemoveOverlay(_castRingOverlay);
        _fxOverlayMan.RemoveOverlay(_castRingCountdownOverlay);
        _worldOverlaysAdded = false;
    }

    /// <summary>
    ///     Applies any staged pool capacities. Called once per frame from
    ///     <see cref="SolreignFxCueSystem.FrameUpdate"/>, where spawning entities is legal.
    /// </summary>
    partial void ApplyPendingPoolCapacities()
    {
        if (_pendingPoolCapacities is not { } pending || _renderPool is null)
            return;

        _pendingPoolCapacities = null;
        _renderPool.EnsureCapacities(pending);
    }

    partial void OnPoolRebuilt(IReadOnlyDictionary<SolreignFxCategory, int> capacities)
    {
        _renderPool ??= new SolreignFxRenderPool(EntityManager, _fxSprite, _fxLight, _transform);

        // Genuine "ships dormant" (spec §9): SolreignFxCueSystem.RebuildPool() computes real
        // per-category capacities unconditionally (it runs from Initialize() before the master
        // CVar's own subscription callback has necessarily fired yet) — without this guard, the
        // pooled-sprite entities would exist for every connected client regardless of
        // solreign.fx.cue_v1, which is real per-client memory/entity footprint for a feature that
        // defaults OFF. Zero capacities (no entities spawned at all) while disabled; the real
        // computed capacities apply the moment the master switch flips on.
        if (!_profileGate.CueSystemEnabled)
        {
            _pendingPoolCapacities = ZeroCapacities(capacities);
            _renderStates.Clear();
            return;
        }

        _pendingPoolCapacities = capacities;
        _renderStates.Clear();

        // grk W3 round-1 review finding #10: world overlays used to be added unconditionally here
        // (even while disabled, above, this method used to still call this) — cheap but pointless
        // while nothing can ever activate a slot. Only add them once the feature is actually live;
        // OnKillSwitchDisabled removes them again on the way back to dormant.
        EnsureWorldOverlaysAdded();
    }

    /// <summary>
    ///     grk W3 round-1 review finding H2: the master kill switch transitioning to disabled must
    ///     zero every render-side resource, not just the logical lease bookkeeping
    ///     <c>OnProfileChanged</c> already clears before calling this. Mirrors
    ///     <see cref="OnPoolRebuilt"/>'s own disabled-branch capacity-zeroing without needing a full
    ///     <c>RebuildPool()</c> round-trip (the logical pool must NOT be rebuilt here — W2's own
    ///     <c>_pool</c>/<c>_leaseManager</c> stay exactly as they were, only render-side state tears
    ///     down).
    /// </summary>
    partial void OnKillSwitchDisabled()
    {
        if (_renderPool is null)
            return;

        var zero = new Dictionary<SolreignFxCategory, int>();
        foreach (SolreignFxCategory category in Enum.GetValues<SolreignFxCategory>())
            zero[category] = 0;

        _renderPool.EnsureCapacities(zero);
        _renderStates.Clear();

        _arcOverlay.ClearAllActivations();
        _castRingOverlay.ClearAllActivations();
        _castRingCountdownOverlay.Clear();
        RemoveWorldOverlaysIfAdded();

        while (_liveTransformationBursts.Count > 0)
        {
            var uid = _liveTransformationBursts.Dequeue();
            if (Exists(uid))
                Del(uid);
        }

        if (_stingOverlayAdded)
        {
            _transformationStingOverlay.Clear();
            _fxOverlayMan.RemoveOverlay(_transformationStingOverlay);
            _stingOverlayAdded = false;
        }
    }

    private static IReadOnlyDictionary<SolreignFxCategory, int> ZeroCapacities(IReadOnlyDictionary<SolreignFxCategory, int> source)
    {
        var zeroed = new Dictionary<SolreignFxCategory, int>(source.Count);
        foreach (var category in source.Keys)
            zeroed[category] = 0;

        return zeroed;
    }

    /// <summary>
    ///     Mirrors <see cref="SolreignFxClientAnchorResolver"/>'s own resolution logic but returns
    ///     the full <see cref="MapCoordinates"/> the renderer needs to actually place a sprite/
    ///     overlay — the shared <c>ISolreignFxAnchorResolver</c> interface only returns a bare
    ///     <see cref="Vector2"/>, which is enough for trust-boundary validation (spec §1.3b) but not
    ///     for placement.
    /// </summary>
    private bool TryResolveRenderCoordinates(NetCoordinates? coordinates, NetEntity? entityAnchor, out MapCoordinates result)
    {
        result = default;

        if (entityAnchor is { } netEntity)
        {
            if (!TryGetEntity(netEntity, out var uid) || uid is not { } resolved)
                return false;

            if (!TryComp(resolved, out TransformComponent? xform) || xform.MapID == MapId.Nullspace)
                return false;

            var pos = _transform.GetWorldPosition(xform);
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y))
                return false;

            result = new MapCoordinates(pos, xform.MapID);
            return true;
        }

        if (coordinates is { } netCoordinates)
        {
            var entityCoordinates = GetCoordinates(netCoordinates);
            var map = _transform.ToMapCoordinates(entityCoordinates, logError: false);
            if (map.MapId == MapId.Nullspace || !float.IsFinite(map.Position.X) || !float.IsFinite(map.Position.Y))
                return false;

            result = map;
            return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="entityAnchor"/> resolves to THIS client's own locally-controlled entity — the actor-scoping check both the transformation screen sting and the impact-heavy camera impulse need (grk W3 round-1 review finding #9: neither is safe to fire off of profile/EffectId alone without also confirming the anchor really is the local player, defense in depth alongside W1/W2's own delivery-scope guarantees).</summary>
    private bool IsLocalPlayerAnchor(NetEntity? entityAnchor)
    {
        if (entityAnchor is not { } netEntity || !TryGetEntity(netEntity, out var uid))
            return false;

        return _player.LocalEntity == uid;
    }

    /// <summary>Plays a category's configured sound anchored to whichever anchor kind the cue carries — an <see cref="EntityUid"/>-relative play when entity-anchored (so it tracks the entity for the clip's own duration, same idiom stock content uses), an <see cref="EntityCoordinates"/> play otherwise.</summary>
    private void PlayCueSound(SoundSpecifier sound, NetCoordinates? coordinates, NetEntity? entityAnchor)
    {
        if (entityAnchor is { } netEntity && TryGetEntity(netEntity, out var uid) && uid is { } resolved)
        {
            _fxAudio.PlayPvs(sound, resolved);
            return;
        }

        if (coordinates is { } netCoordinates)
            _fxAudio.PlayPvs(sound, GetCoordinates(netCoordinates));
    }

    partial void OnLeaseAcquiredForRender(SolreignFxCueV1 cue, SolreignFxCategory category, SolreignFxLeaseManager.Lease lease)
    {
        if (_renderPool is null)
            return;

        if (!TryResolveRenderCoordinates(cue.Coordinates, cue.EntityAnchor, out var worldCoordinates))
        {
            // grk W3 round-1 review finding #8: the logical lease is already recorded (UpsertActiveEffect
            // ran before this hook fires) — failing to resolve a position must not leave a stale
            // MERGED slot's old visuals running under the new cue's lifetime, nor a fresh slot paid
            // for with nothing shown. Fail closed: park whatever this slot currently shows.
            ParkSlot(category, lease.SlotIndex);
            return;
        }

        var behavior = _profileGate.CurrentBehavior(category);
        var plan = SolreignFxRenderRecipe.BuildPlan(category, cue.EffectId.Id, _profileGate.CurrentProfile, behavior, _profileGate.NoFlash, cue.Seed);
        var color = SolreignFxPrimitiveAssets.GetPaletteColor(category, cue.PaletteIndex ?? 0);

        var state = FindOrCreateRenderState(lease.Category, lease.SlotIndex);
        state.Coordinates = cue.Coordinates;
        state.EntityAnchor = cue.EntityAnchor;
        state.StartedAtSeconds = _timing.CurTime.TotalSeconds;
        state.DurationSeconds = Math.Max(0.05f, cue.Duration);
        state.Plan = plan;
        state.Color = color;
        state.Seed = cue.Seed;
        state.Intensity = cue.Intensity;

        // grk W3 round-1 review finding H3: the lease's own granted-resource flags are the actual
        // budget grant (spec §3.1's "atomic lease... no lease → coalesce-or-drop, never partial
        // setup") — the render plan alone is not authoritative over whether a resource may be used
        // AT ALL; it only decides how to draw one that the lease already permits. AND-ing both
        // closes the gap where a future lease-manager change could grant fewer resources than a
        // category's static defaults without the renderer ever noticing.
        var showSprite = plan.SpriteEnabled && lease.EntitiesGranted > 0;
        var showLight = plan.LightEnabled && lease.LightsGranted > 0;
        var showOverlay = plan.OverlayEnabled && lease.OverlayGranted;
        state.ShowSprite = showSprite;
        state.ShowLight = showLight;
        state.ShowOverlay = showOverlay;
        state.ShowCountdownText = plan.CountdownTextEnabled && lease.OverlayGranted;

        ApplyEntityVisual(category, lease.SlotIndex, plan, showSprite, showLight, color, worldCoordinates, cue.Scale, state);

        if (showOverlay && category == SolreignFxCategory.Electrical)
            _arcOverlay.SetActivation(lease.SlotIndex, worldCoordinates, color, cue.Intensity, cue.Seed);
        else if (showOverlay && category == SolreignFxCategory.CastRing)
            _castRingOverlay.SetActivation(lease.SlotIndex, worldCoordinates, color, 0f, plan.OverlayAnimated);
        else
            ClearOverlayActivation(category, lease.SlotIndex);

        if (plan.AudioEnabled && SolreignFxPrimitiveAssets.GetSound(category) is { } sound)
            PlayCueSound(sound, cue.Coordinates, cue.EntityAnchor);

        // impact_heavy's camera impulse (spec §4's profile table: full profile only) is a per-VIEWER
        // effect — it must only fire for the client whose OWN controlled entity is the one that got
        // hit, never for a bystander merely in PVS range of the same broadcast cue (grk finding #9).
        if (plan.CameraImpulseEnabled && IsLocalPlayerAnchor(cue.EntityAnchor))
        {
            var kick = new Vector2(0f, 0.3f) * Math.Clamp(cue.Intensity, 0f, 1f);
            _fxCameraRecoil.KickCamera(_player.LocalEntity!.Value, kick);
        }
    }

    private void ParkSlot(SolreignFxCategory category, int slotIndex)
    {
        if (_renderPool?.GetEntity(category, slotIndex) is { } uid)
            _renderPool.Park(uid);

        ClearOverlayActivation(category, slotIndex);

        for (var i = _renderStates.Count - 1; i >= 0; i--)
        {
            if (_renderStates[i].Category == category && _renderStates[i].SlotIndex == slotIndex)
                _renderStates.RemoveAt(i);
        }
    }

    private RenderState FindOrCreateRenderState(SolreignFxCategory category, int slotIndex)
    {
        foreach (var existing in _renderStates)
        {
            if (existing.Category == category && existing.SlotIndex == slotIndex)
                return existing;
        }

        var created = new RenderState { Category = category, SlotIndex = slotIndex };
        _renderStates.Add(created);
        return created;
    }

    private void ApplyEntityVisual(
        SolreignFxCategory category,
        int slotIndex,
        SolreignFxRenderPlan plan,
        bool showSprite,
        bool showLight,
        Color color,
        MapCoordinates worldCoordinates,
        float cueScale,
        RenderState state)
    {
        if (_renderPool is null)
            return;

        var uid = _renderPool.GetEntity(category, slotIndex);
        if (uid is not { } entity || !TryComp<SpriteComponent>(entity, out var sprite))
            return;

        _renderPool.Reposition(entity, worldCoordinates);

        if (!showSprite)
        {
            _fxSprite.SetVisible((entity, sprite), false);
        }
        else
        {
            var assets = SolreignFxPrimitiveAssets.GetAssets(category);
            if (assets.RsiPath is not null && assets.SpriteState is not null)
                _fxSprite.LayerSetRsi((entity, sprite), 0, new ResPath(assets.RsiPath), assets.SpriteState);

            _fxSprite.SetVisible((entity, sprite), true);
            _fxSprite.SetColor((entity, sprite), color);
            var scale = cueScale * plan.SpriteScaleMultiplier;
            _fxSprite.SetScale((entity, sprite), new Vector2(scale, scale));

            // grk W3 round-1 review finding M4 (VariationIndex never consumed): a small deterministic
            // seed-derived rotation so repeated activations of the same primitive don't all look
            // pixel-identical — the ONE sanctioned way to turn a seed into visual variety per spec
            // §1.2 (SolreignFxRenderRecipe already derived VariationIndex via SolreignFxSeedMixing.Index).
            var rotationDegrees = plan.VariationIndex * (360f / SolreignFxRenderRecipe.VariationCount);
            _fxSprite.SetRotation((entity, sprite), Angle.FromDegrees(rotationDegrees));

            // grk W3 round-1 review finding M4 (icon-flash substitution never applied): cosmetic_minimal's
            // "single non-animated icon flash" (spec §4) is rendered here as the same sprite frozen
            // on its first frame — a real, distinct behavior from the animated version, not a no-op.
            _fxSprite.LayerSetAutoAnimated((entity, sprite), 0, !plan.SpriteIsIconFlash);
            if (plan.SpriteIsIconFlash)
                _fxSprite.LayerSetAnimationTime((entity, sprite), 0, 0f);
        }

        if (!showLight)
        {
            if (TryComp<PointLightComponent>(entity, out var lightOff))
                _fxLight.SetEnabled(entity, false, lightOff);
        }
        else if (TryComp<PointLightComponent>(entity, out var light))
        {
            _fxLight.SetEnabled(entity, true, light);
            _fxLight.SetColor(entity, color, light);
            _fxLight.SetCastShadows(entity, false, light);

            // grk W3 round-1 review finding M4 (LightFlickers/LightFlashRatePerSecond never applied):
            // a real per-frame energy modulation, not a steady light regardless of profile — see
            // OnFrameTick's flicker-envelope branch, which needs the light's BASE (unmodulated)
            // energy to compute its envelope against.
            state.BaseLightEnergy = light.Energy > 0f ? light.Energy : 1f;
            _fxLight.SetEnergy(entity, state.BaseLightEnergy, light);
        }
    }

    private void ClearOverlayActivation(SolreignFxCategory category, int slotIndex)
    {
        switch (category)
        {
            case SolreignFxCategory.Electrical:
                _arcOverlay.ClearActivation(slotIndex);
                break;
            case SolreignFxCategory.CastRing:
                _castRingOverlay.ClearActivation(slotIndex);
                _castRingCountdownOverlay.ClearActivation(slotIndex);
                break;
        }
    }

    partial void OnLeaseReleasedForRender(SolreignFxLeaseManager.Lease lease)
    {
        ParkSlot(lease.Category, lease.SlotIndex);
    }

    partial void OnFrameTick(float frameTime)
    {
        if (_renderPool is null)
            return;

        var now = _timing.CurTime.TotalSeconds;

        foreach (var state in _renderStates)
        {
            if (!TryResolveRenderCoordinates(state.Coordinates, state.EntityAnchor, out var worldCoordinates))
                continue; // FrameUpdate's own anchor-liveness check releases this next tick if it stays unresolvable.

            if (_renderPool.GetEntity(state.Category, state.SlotIndex) is { } entity)
                _renderPool.Reposition(entity, worldCoordinates);

            var elapsed = (float) Math.Max(0.0, now - state.StartedAtSeconds);
            var progress = state.DurationSeconds > 0 ? Math.Clamp(elapsed / (float) state.DurationSeconds, 0f, 1f) : 0f;

            // grk W3 round-1 review finding M4: the light-flicker/single-pulse envelope the recipe
            // computed (LightFlickers, LightFlashRatePerSecond) is applied here every frame — a
            // steady light regardless of profile was a vacuous a11y guarantee (no_flash/reduced_motion
            // "convert flicker to a single pulse" was true of nothing, since nothing ever flickered).
            // Gated on state.ShowLight (the lease-ANDed flag from acquire time), NOT the raw
            // state.Plan.LightEnabled — re-reading the raw plan flag here would silently bypass the
            // H3 fix's lease-grant AND on every frame after the first.
            if (state.ShowLight && _renderPool.GetEntity(state.Category, state.SlotIndex) is { } litEntity
                && TryComp<PointLightComponent>(litEntity, out var flickerLight))
            {
                var envelope = state.Plan.LightFlickers
                    ? 0.6f + 0.4f * MathF.Sin(elapsed * state.Plan.LightFlashRatePerSecond * (MathF.PI * 2f))
                    : MathF.Exp(-elapsed * 6f); // single decaying pulse, never repeats

                _fxLight.SetEnergy(litEntity, state.BaseLightEnergy * MathF.Max(0.05f, envelope), flickerLight);
            }

            switch (state.Category)
            {
                case SolreignFxCategory.Electrical:
                    if (state.ShowOverlay)
                        _arcOverlay.SetActivation(state.SlotIndex, worldCoordinates, state.Color, state.Intensity, state.Seed);
                    break;

                case SolreignFxCategory.CastRing:
                    if (state.ShowCountdownText)
                    {
                        _castRingOverlay.ClearActivation(state.SlotIndex);
                        var remaining = (float) Math.Max(0.0, state.DurationSeconds - elapsed);
                        _castRingCountdownOverlay.SetActivation(state.SlotIndex, worldCoordinates, state.Color, remaining);
                    }
                    else if (state.ShowOverlay)
                    {
                        _castRingCountdownOverlay.ClearActivation(state.SlotIndex);
                        var fill = state.Plan.OverlayAnimated ? progress : Math.Min(progress, 0.999f);
                        _castRingOverlay.SetActivation(state.SlotIndex, worldCoordinates, state.Color, fill, state.Plan.OverlayAnimated);
                    }

                    break;

                case SolreignFxCategory.Smoke:
                    if (_renderPool.GetEntity(state.Category, state.SlotIndex) is { } smokeEntity
                        && TryComp<SpriteComponent>(smokeEntity, out var smokeSprite))
                    {
                        // Slow ease-out alpha decay (smoke_fog.swsl's own math, mirrored here in
                        // plain sprite color so it applies without extra per-layer shader plumbing —
                        // see this worktree's receipt for the documented scope trim).
                        var decay = 1f - progress * progress;
                        var faded = new Color(state.Color.R, state.Color.G, state.Color.B, state.Color.A * decay);
                        _fxSprite.SetColor((smokeEntity, smokeSprite), faded);
                    }

                    break;
            }
        }

        if (_transformationStingOverlay.Tick(frameTime) && _stingOverlayAdded)
        {
            _fxOverlayMan.RemoveOverlay(_transformationStingOverlay);
            _stingOverlayAdded = false;
        }
    }

    partial void OnTransformationCue(SolreignFxCueV1 cue)
    {
        var behavior = _profileGate.CurrentBehavior(SolreignFxCategory.Transformation);
        var plan = SolreignFxRenderRecipe.BuildPlan(
            SolreignFxCategory.Transformation, cue.EffectId.Id, _profileGate.CurrentProfile, behavior, _profileGate.NoFlash, cue.Seed);

        if (!TryResolveRenderCoordinates(cue.Coordinates, cue.EntityAnchor, out var worldCoordinates))
            return;

        var color = SolreignFxPrimitiveAssets.GetPaletteColor(SolreignFxCategory.Transformation, cue.PaletteIndex ?? 0);
        var assets = SolreignFxPrimitiveAssets.GetAssets(SolreignFxCategory.Transformation);
        var duration = Math.Clamp(cue.Duration, 0.1f, 3f);

        if (plan.SpriteEnabled && assets.RsiPath is not null && assets.SpriteState is not null)
        {
            // A true one-shot spawn+self-despawn burst (same idiom as
            // Content.Client.Weapons.Melee.MeleeWeaponSystem.Effects.cs's own lunge-arc animation
            // entities) — deliberately NOT drawn from SolreignFxRenderPool's pooled arrays: spec §3
            // states Transformation's pooled-entity budget is 0 (it "rides GenericVisualizer, no
            // extra entity" for the gameplay-critical appearance swap), so this cosmetic bonus layer
            // gets its own bounded, self-cleaning lifetime instead of consuming category-pool
            // capacity that spec says doesn't exist for this primitive.
            //
            // grk W3 round-1 review finding #5: bounded here via MaxLiveTransformationBursts —
            // oldest-recycled-first once at capacity, since this path deliberately bypasses the
            // lease manager (per spec) and therefore has no OTHER concurrent-cap enforcement of its
            // own beyond the server egress budget and the client's raw-receive flood cap.
            if (_liveTransformationBursts.Count >= MaxLiveTransformationBursts)
            {
                var oldest = _liveTransformationBursts.Dequeue();
                if (Exists(oldest))
                    Del(oldest);
            }

            var burst = Spawn("SolreignFxCosmeticSprite", worldCoordinates);
            _liveTransformationBursts.Enqueue(burst);

            if (TryComp<SpriteComponent>(burst, out var burstSprite))
            {
                _fxSprite.LayerSetRsi((burst, burstSprite), 0, new ResPath(assets.RsiPath), assets.SpriteState);
                _fxSprite.SetVisible((burst, burstSprite), true);
                _fxSprite.SetColor((burst, burstSprite), color);
                var scale = cue.Scale * plan.SpriteScaleMultiplier;
                _fxSprite.SetScale((burst, burstSprite), new Vector2(scale, scale));
            }

            var despawn = EnsureComp<TimedDespawnComponent>(burst);
            despawn.Lifetime = duration;
        }

        if (plan.AudioEnabled && assets.SoundPath is not null)
            PlayCueSound(new SoundPathSpecifier(assets.SoundPath), cue.Coordinates, cue.EntityAnchor);

        // grk W3 round-1 review finding #9: the recipe already restricts ScreenStingEnabled to the
        // real `transformation` id (never the bystander-received `transformation_generic`), but that
        // alone assumes delivery scope is airtight upstream. Defense in depth: also require the
        // cue's own anchor to resolve to THIS client's locally-controlled entity before ever holding
        // the sting overlay — a mis-routed detail cue (should never happen given W1/W2's guarantees)
        // still can't sting the wrong client's screen.
        if (plan.ScreenStingEnabled && IsLocalPlayerAnchor(cue.EntityAnchor))
        {
            _transformationStingOverlay.Hold(duration);
            if (!_stingOverlayAdded)
            {
                _fxOverlayMan.AddOverlay(_transformationStingOverlay);
                _stingOverlayAdded = true;
            }
        }
    }

    partial void OnSystemShutdown()
    {
        _arcOverlay.ClearAllActivations();
        _castRingOverlay.ClearAllActivations();
        _castRingCountdownOverlay.Clear();
        _transformationStingOverlay.Clear();
        _transformationStingOverlay.DisposeShader();
        RemoveWorldOverlaysIfAdded();

        if (_stingOverlayAdded)
        {
            _fxOverlayMan.RemoveOverlay(_transformationStingOverlay);
            _stingOverlayAdded = false;
        }

        while (_liveTransformationBursts.Count > 0)
        {
            var uid = _liveTransformationBursts.Dequeue();
            if (Exists(uid))
                Del(uid);
        }

        _renderPool?.DeleteAll();
    }
}
