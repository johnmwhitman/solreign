using Content.Server.Fluids.EntitySystems;
using Content.Shared._Solreign.Ninjitsu;
using Content.Shared.Chemistry.Components;
using Content.Shared.Database;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Smoke Vanish: a suit-tech gadget ability (<see cref="NinjitsuGearComponent"/>, attached to
///     the ninja suit in YAML) alongside the upstream EMP/throwing-star/recall-katana suit actions.
///     Pops a real <c>Smoke</c> cloud (visual only — empty <see cref="Solution"/>, no chemical
///     effects) at the wearer's feet and grants a brief personal stealth + speed window
///     (<see cref="NinjitsuVanishComponent"/>), same idiom as <c>SolreignMoonTouchedComponent</c>'s
///     <see cref="RefreshMovementSpeedModifiersEvent"/> hook. Getting hit ends the window early,
///     mirroring the suit's own phase cloak (<c>SharedSpaceNinjaSystem.TryRevealNinja</c>) — but
///     Smoke Vanish is a separate technique from the suit's phase cloak and doesn't touch it.
/// </summary>
public sealed partial class NinjitsuSystem
{
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;

    private void InitializeVanish()
    {
        SubscribeLocalEvent<NinjitsuGearComponent, NinjitsuSmokeVanishEvent>(OnSmokeVanish);
        SubscribeLocalEvent<NinjitsuVanishComponent, RefreshMovementSpeedModifiersEvent>(OnVanishRefreshSpeed);
        SubscribeLocalEvent<NinjitsuVanishComponent, AttackedEvent>(OnVanishAttacked);
    }

    private void OnSmokeVanish(Entity<NinjitsuGearComponent> ent, ref NinjitsuSmokeVanishEvent args)
    {
        var comp = ent.Comp;
        var user = args.Performer;
        var now = _timing.CurTime;

        if (!NinjitsuRules.CooldownReady(now, comp.NextVanishReadyAt))
        {
            _popup.PopupEntity(Loc.GetString("ninjitsu-vanish-cooldown"), user, user, PopupType.Small);
            return;
        }

        if (!_ninja.TryUseCharge(user, comp.SmokeVanishCharge))
        {
            _popup.PopupEntity(Loc.GetString("ninja-no-power"), user, user);
            return;
        }

        args.Handled = true;
        comp.NextVanishReadyAt = NinjitsuRules.NextReady(now, comp.SmokeVanishCooldown);

        var coords = Transform(user).Coordinates;
        var smoke = Spawn(comp.SmokePrototype, coords);
        if (TryComp<SmokeComponent>(smoke, out var smokeComp))
        {
            _smoke.StartSmoke(smoke, new Solution(), (float) comp.SmokeDuration.TotalSeconds, comp.SmokeSpreadAmount, smokeComp);
        }
        else
        {
            Log.Error($"Smoke Vanish prototype {comp.SmokePrototype} was missing SmokeComponent");
            Del(smoke);
        }

        _audio.PlayPvs(comp.VanishSound, user);

        var vanish = EnsureComp<NinjitsuVanishComponent>(user);
        vanish.ExpiresAt = now + comp.VanishDuration;
        vanish.SpeedMultiplier = comp.VanishSpeedMultiplier;

        // Don't clobber a phase cloak the wearer already had running under their own steam —
        // only remove the stealth on expiry if Smoke Vanish is the one that added it.
        if (!HasComp<StealthComponent>(user))
        {
            vanish.OwnsStealth = true;
            var stealth = EnsureComp<StealthComponent>(user);
            // StealthComponent's fields are [Access]-locked to SharedStealthSystem — go through its
            // public API rather than writing them directly (same reason NinjitsuRules doesn't poke
            // CarpComboRules internals: use the friend system's contract, don't bypass it).
            _stealth.SetEnabled(user, true, stealth);
            _stealth.SetVisibility(user, comp.VanishVisibility, stealth);
        }

        _movementSpeed.RefreshMovementSpeedModifiers(user);

        _popup.PopupEntity(Loc.GetString("ninjitsu-vanish-popup"), user, user, PopupType.Medium);

        LedgerLog(LogType.Action, $"{ToPrettyString(user):actor} logged an unexplained atmospherics smoke discharge (Smoke Vanish) near {coords}");
    }

    private void OnVanishRefreshSpeed(Entity<NinjitsuVanishComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    private void OnVanishAttacked(Entity<NinjitsuVanishComponent> ent, ref AttackedEvent args)
    {
        EndVanish(ent);
    }

    /// <summary>Ends an active Smoke Vanish window early (expiry or getting hit) and cleans up exactly what we added.</summary>
    private void EndVanish(Entity<NinjitsuVanishComponent> ent)
    {
        var (uid, vanish) = ent;

        if (vanish.OwnsStealth)
            RemCompDeferred<StealthComponent>(uid);

        RemCompDeferred<NinjitsuVanishComponent>(uid);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
}
