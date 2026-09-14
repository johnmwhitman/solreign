using System.Numerics;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Follower.Components;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._Solreign.MovementBob;

/// <summary>
/// SR-W-083: procedural movement animation for humanoid mobs — a subtle sine bob while
/// walking, no new art required. Entirely client-side render polish: it listens to the
/// existing <see cref="SpriteMoveEvent"/> (raised for local prediction, networked movers
/// and NPC steering alike) and nudges the sprite offset each frame. Zero netcode.
///
/// Gated behind the replicated, server-owned <c>solreign.movement_bob.enabled</c> CVar
/// (ships false = dormant) and the client's own <c>accessibility.reduced_motion</c>.
/// Guards keep it off entities whose sprite offset/rotation channels are owned by other
/// systems (orbiting ghosts, buckled or downed mobs, weightless floaters).
///
/// Bookkeeping (<see cref="MovementBobComponent"/>) exists only while an entity is
/// actually moving with the feature live; every stop/gate/removal path retires it, so
/// the dormant and idle costs are an empty query.
/// </summary>
public sealed partial class MovementBobSystem : EntitySystem
{
    // RA0049/RA0051: [Dependency] fields must be non-readonly on a partial type. These are
    // WARNINGS in a Debug build but ERRORS in Release — which is what the packager builds,
    // so a Debug-only check will not catch a regression here.
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AnimationPlayerSystem _animationPlayer = default!;

    /// <summary>
    /// Mirrors of the animation keys other systems use to animate
    /// <see cref="SpriteComponent.Offset"/> — the bob suspends entirely while any runs.
    /// Sources: JitteringSystem ("jittering", private field), StaminaSystem ("stamina"
    /// fatigue breathing, private const), FloatingVisualsComponent.AnimationKey ("gravity").
    /// </summary>
    private const string JitterAnimationKey = "jittering";
    private const string StaminaAnimationKey = "stamina";
    private const string FloatAnimationKey = "gravity";
    private const string OrbitStopAnimationKey = "orbiting_stop";
    private const string MeleeLungeAnimationKey = "melee-lunge";

    private EntityQuery<SpriteComponent> _spriteQuery;
    private EntityQuery<StandingStateComponent> _standingQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<OrbitVisualsComponent> _orbitQuery;
    private EntityQuery<FloatingVisualsComponent> _floatingQuery;
    private EntityQuery<InputMoverComponent> _moverQuery;

    private bool _enabled;
    private bool _reducedMotion;
    private bool _wasActive;
    private float _amplitudePx;
    private float _hz;

    /// <summary>Scratch list so component removal never happens mid-enumeration.</summary>
    private readonly List<EntityUid> _toRemove = new();

    /// <summary>
    /// Whether the bob may do anything at all on this client. Amplitude is part of the
    /// definition: a zero/invalid amplitude is "off", and raising it back above zero is an
    /// inactive-to-active transition that must reseed (cdx r2).
    /// </summary>
    private bool FeatureActive => _enabled && !_reducedMotion && MovementBobMath.EffectiveAmplitude(_amplitudePx) > 0f;

    public override void Initialize()
    {
        base.Initialize();

        _spriteQuery = GetEntityQuery<SpriteComponent>();
        _standingQuery = GetEntityQuery<StandingStateComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _orbitQuery = GetEntityQuery<OrbitVisualsComponent>();
        _floatingQuery = GetEntityQuery<FloatingVisualsComponent>();
        _moverQuery = GetEntityQuery<InputMoverComponent>();

        Subs.CVar(_cfg, CCVars.SolreignMovementBobEnabled, v => { _enabled = v; RefreshActive(); }, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, v => { _reducedMotion = v; RefreshActive(); }, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignMovementBobAmplitudePx, v => { _amplitudePx = v; RefreshActive(); }, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignMovementBobHz, v => _hz = v, invokeImmediately: true);

        SubscribeLocalEvent<HumanoidProfileComponent, SpriteMoveEvent>(OnSpriteMove);
        SubscribeLocalEvent<HumanoidProfileComponent, ComponentShutdown>(OnHumanoidShutdown);
        SubscribeLocalEvent<MovementBobComponent, ComponentShutdown>(OnBobShutdown);
    }

    /// <summary>
    /// Single transition handler for both gates (feature CVar and reduced motion): on any
    /// inactive-to-active flip, seed bookkeeping for humanoids that are ALREADY holding a
    /// move key, so they start bobbing without needing a fresh input change. The frame
    /// sweep handles the active-to-inactive direction.
    /// </summary>
    private void RefreshActive()
    {
        var active = FeatureActive;

        if (active && !_wasActive)
            SeedMovingHumanoids();

        _wasActive = active;
    }

    private void SeedMovingHumanoids()
    {
        var query = EntityQueryEnumerator<HumanoidProfileComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out _, out var mover))
        {
            if (!mover.HasDirectionalMovement)
                continue;

            var bob = EnsureComp<MovementBobComponent>(uid);
            bob.IsMoving = true;
            bob.Phase = MovementBobMath.Phase(uid.GetHashCode());
        }
    }

    private void OnSpriteMove(Entity<HumanoidProfileComponent> ent, ref SpriteMoveEvent args)
    {
        // Existing bookkeeping just mirrors the new state; the frame sweep retires it.
        if (TryComp<MovementBobComponent>(ent, out var existing))
        {
            existing.IsMoving = args.IsMoving;
            return;
        }

        // Only a genuine start-moving while the feature is live creates bookkeeping —
        // stop events and dormant/reduced-motion clients allocate nothing.
        if (!FeatureActive || !args.IsMoving)
            return;

        var bob = AddComp<MovementBobComponent>(ent);
        bob.IsMoving = true;
        bob.Phase = MovementBobMath.Phase(ent.Owner.GetHashCode());
    }

    private void OnHumanoidShutdown(Entity<HumanoidProfileComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<MovementBobComponent>(ent, out var bob))
            return;

        // While an offset animation owns the channel, removing now would restore the
        // baseline mid-animation and let the animation's end re-poison it permanently —
        // defer to the first unoccupied frame instead. (cdx r4)
        if (IsOffsetAnimated(ent))
        {
            bob.Retiring = true;
            return;
        }

        // No qualifying component, no bob: restores the offset via OnBobShutdown.
        RemComp<MovementBobComponent>(ent);
    }

    /// <summary>Whether another system's animation is writing this sprite's offset right now.</summary>
    private bool IsOffsetAnimated(EntityUid uid)
    {
        return _animationPlayer.HasRunningAnimation(uid, JitterAnimationKey)
               || _animationPlayer.HasRunningAnimation(uid, StaminaAnimationKey)
               || _animationPlayer.HasRunningAnimation(uid, FloatAnimationKey)
               || _animationPlayer.HasRunningAnimation(uid, OrbitStopAnimationKey)
               || _animationPlayer.HasRunningAnimation(uid, MeleeLungeAnimationKey);
    }

    private void OnBobShutdown(Entity<MovementBobComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.HasBaseline
            && _spriteQuery.TryGetComponent(ent, out var sprite)
            && sprite.Offset != ent.Comp.BaseOffset)
        {
            _sprite.SetOffset((ent.Owner, sprite), ent.Comp.BaseOffset);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var amplitude = MovementBobMath.EffectiveAmplitude(_amplitudePx);
        var featureOn = FeatureActive;
        var time = _timing.CurTime.TotalSeconds;

        var query = EntityQueryEnumerator<MovementBobComponent>();
        while (query.MoveNext(out var uid, out var bob))
        {
            if (!_spriteQuery.TryGetComponent(uid, out var sprite))
            {
                // Sprite gone: nothing to restore, and stale BaseOffset must never be
                // written onto a future sprite. Drop the bookkeeping outright.
                bob.Applied = false;
                _toRemove.Add(uid);
                continue;
            }

            // Channel occupancy: another animation is writing this offset RIGHT NOW
            // (jitter, stamina fatigue, weightless float, orbit stop). Suspend completely —
            // no writes (no per-frame fighting), no retirement (the retained baseline is the
            // only clean path back once the animation ends and possibly leaves a poisoned
            // offset behind). Checked before every other branch so neither a feature-off
            // sweep nor a deferred retirement can discard the baseline mid-animation. (cdx r2-r4)
            if (IsOffsetAnimated(uid))
            {
                bob.Applied = false;
                continue;
            }

            // A disqualified entity (humanoid profile shut down mid-animation) retires on
            // the first unoccupied frame, restoring the retained baseline first. (cdx r4)
            if (bob.Retiring)
            {
                Reset(uid, bob, sprite);
                _toRemove.Add(uid);
                continue;
            }

            if (!featureOn)
            {
                Reset(uid, bob, sprite);
                _toRemove.Add(uid);
                continue;
            }

            if (!_moverQuery.HasComponent(uid))
            {
                // Lost its mover: no stop event will ever arrive, so retire instead of
                // bobbing forever.
                Reset(uid, bob, sprite);
                _toRemove.Add(uid);
                continue;
            }

            var standing = !_standingQuery.TryGetComponent(uid, out var standState) || standState.Standing;
            var buckled = _buckleQuery.TryGetComponent(uid, out var buckle) && buckle.Buckled;
            var orbiting = _orbitQuery.HasComponent(uid);
            // Weightless mobs own this offset channel via floating visuals even between
            // animation loops — never bob them.
            var floating = _floatingQuery.TryGetComponent(uid, out var floatComp) && floatComp.CanFloat;

            if (!MovementBobMath.ShouldBob(_enabled, _reducedMotion, bob.IsMoving, standing, buckled, orbiting, floating))
            {
                Reset(uid, bob, sprite);

                // Stopped entities retire (a fresh move event re-creates them); entities
                // still moving but gated (downed, buckled, floating...) keep their
                // bookkeeping — no input change will re-raise the event when the gate clears.
                if (!bob.IsMoving)
                    _toRemove.Add(uid);

                continue;
            }

            if (!bob.Applied)
            {
                // Reuse a retained baseline from before an animation suspension — the
                // current offset may be whatever that animation left behind (cdx r3).
                if (!bob.HasBaseline)
                {
                    bob.BaseOffset = sprite.Offset;
                    bob.HasBaseline = true;
                }

                bob.Applied = true;
            }

            var dy = MovementBobMath.OffsetUnits(time, _hz, amplitude, bob.Phase);
            _sprite.SetOffset((uid, sprite), bob.BaseOffset + new Vector2(0f, dy));
        }

        if (_toRemove.Count == 0)
            return;

        foreach (var uid in _toRemove)
        {
            RemComp<MovementBobComponent>(uid);
        }

        _toRemove.Clear();
    }

    private void Reset(EntityUid uid, MovementBobComponent bob, SpriteComponent sprite)
    {
        bob.Applied = false;

        if (!bob.HasBaseline)
            return;

        // Keyed on the offset, not on Applied: an offset animation that ended during a
        // suspension leaves its last (possibly bob-poisoned) keyframe behind — restore
        // whenever the sprite isn't sitting exactly on the retained baseline. (cdx r3)
        if (sprite.Offset != bob.BaseOffset)
            _sprite.SetOffset((uid, sprite), bob.BaseOffset);

        // One-shot: the baseline is only retained ACROSS an occupancy suspension (which
        // never reaches Reset). Keeping it here would make every moving-but-gated entity
        // a permanent baseline enforcer fighting any later legitimate offset writer,
        // per-frame and order-dependent. (cdx r4)
        bob.HasBaseline = false;
    }
}
