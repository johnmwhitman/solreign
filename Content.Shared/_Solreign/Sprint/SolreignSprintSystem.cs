using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Solreign.Sprint;

/// <summary>
///     Handles the hold-to-sprint keybind (default C): +<see cref="SprintComponent.SpeedModifier"/>
///     movement speed while held, paid for out of stamina via SharedStaminaSystem, with an
///     auto-drop and cooldown once stamina runs low. All decision math lives in
///     <see cref="SprintMath"/> so it can be unit tested without spinning up the ECS.
///
///     Shared (not split into Server/Client) because the drain/cooldown math is fully
///     deterministic from networked state, matching how SharedStaminaSystem itself runs its
///     Update() loop on both sides for prediction.
/// </summary>
public sealed partial class SolreignSprintSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SprintComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.Sprint, new SprintInputCmdHandler(this))
            .Register<SolreignSprintSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SolreignSprintSystem>();
    }

    private void OnRefreshMoveSpeed(Entity<SprintComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(SprintMath.EffectiveSpeedModifier(ent.Comp.Sprinting, ent.Comp.SpeedModifier));
    }

    /// <summary>
    ///     Called from the Sprint keybind's input handler whenever it's pressed or released.
    ///     Entities without both stamina and a movement speed modifier (i.e. anything that can't
    ///     get tired - ghosts, silicons without stamina, etc) silently ignore the press instead
    ///     of growing a SprintComponent for no reason.
    /// </summary>
    public void SetSprintKeyHeld(EntityUid uid, bool held)
    {
        if (!held)
        {
            if (TryComp<SprintComponent>(uid, out var releasing))
            {
                releasing.KeyHeld = false;
                Dirty(uid, releasing);
            }

            return;
        }

        if (!HasComp<StaminaComponent>(uid) || !HasComp<MovementSpeedModifierComponent>(uid))
            return;

        var sprint = EnsureComp<SprintComponent>(uid);
        sprint.KeyHeld = true;
        Dirty(uid, sprint);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SprintComponent, StaminaComponent>();

        while (query.MoveNext(out var uid, out var sprint, out var stamina))
        {
            Tick(uid, sprint, stamina);
        }
    }

    private void Tick(EntityUid uid, SprintComponent sprint, StaminaComponent stamina)
    {
        var onCooldown = SprintMath.IsOnCooldown(_timing.CurTime, sprint.CooldownEndTime);
        var damageFraction = SprintMath.DamageFraction(stamina.StaminaDamage, stamina.CritThreshold);

        var wantsToSprint = SprintMath.ShouldSprint(
            sprint.KeyHeld,
            onCooldown,
            stamina.Critical,
            damageFraction,
            sprint.AutoDropDamageFraction);

        if (!wantsToSprint)
        {
            // Only charge a cooldown when the key is still held and we're not already on one -
            // i.e. this is an actual "ran out of stamina mid-sprint" drop, not a normal release.
            if (sprint.Sprinting)
                StopSprinting(uid, sprint, enterCooldown: sprint.KeyHeld && !onCooldown);

            return;
        }

        if (!sprint.Sprinting)
            StartSprinting(uid, sprint);

        // Drain is charged in once-a-second lumps (see SprintComponent.NextDrainTime) rather
        // than every tick.
        if (sprint.NextDrainTime > _timing.CurTime)
            return;

        sprint.NextDrainTime += TimeSpan.FromSeconds(1f);

        // Predict (using the nominal, unmodified drain rate - stamina resistance/status effects
        // can still shift the real number slightly) whether the next second of drain would
        // cross the auto-drop line, and stop *before* applying it so sprinting alone can never
        // push a mob into stamina crit.
        var predictedDamage = SprintMath.StaminaDamageAfterDrain(stamina.StaminaDamage, sprint.StaminaDrainPerSecond, seconds: 1f);
        var predictedFraction = SprintMath.DamageFraction(predictedDamage, stamina.CritThreshold);

        if (SprintMath.IsAutoDropTriggered(predictedFraction, sprint.AutoDropDamageFraction))
        {
            StopSprinting(uid, sprint, enterCooldown: true);
            return;
        }

        _stamina.TakeStaminaDamage(uid, sprint.StaminaDrainPerSecond, stamina);
        Dirty(uid, sprint);

        if (stamina.Critical)
            StopSprinting(uid, sprint, enterCooldown: true);
    }

    private void StartSprinting(EntityUid uid, SprintComponent sprint)
    {
        sprint.Sprinting = true;
        sprint.NextDrainTime = _timing.CurTime + TimeSpan.FromSeconds(1f);
        Dirty(uid, sprint);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);

        // PlayPredicted (not PlayPvs): this fires from Tick(), which runs identically on both
        // client and server (same prediction discipline as the drain/cooldown math below), so the
        // sprinter hears the cue the instant they start moving instead of waiting on a round trip,
        // while everyone else in PVS range still hears it via the server's copy.
        _audio.PlayPredicted(sprint.SprintStartSound, uid, uid);
    }

    private void StopSprinting(EntityUid uid, SprintComponent sprint, bool enterCooldown)
    {
        sprint.Sprinting = false;

        if (enterCooldown)
            sprint.CooldownEndTime = SprintMath.CooldownEndTime(_timing.CurTime, sprint.Cooldown);

        Dirty(uid, sprint);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private sealed class SprintInputCmdHandler : InputCmdHandler
    {
        private readonly SolreignSprintSystem _system;

        public SprintInputCmdHandler(SolreignSprintSystem system)
        {
            _system = system;
        }

        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            if (session?.AttachedEntity is not { } uid)
                return false;

            _system.SetSprintKeyHeld(uid, message.State == BoundKeyState.Down);
            return false;
        }
    }
}
