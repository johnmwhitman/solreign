using Content.Shared._Solreign.Ninjitsu;
using Content.Shared.Database;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;

namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Silent Step: an innate ninjitsu toggle (<see cref="NinjitsuTrainingComponent"/>, granted
///     directly to the ninja — no suit or boots required). Verified upstream already ships a
///     body-level footstep silencer as a component: <c>ClothingShoesSpaceNinja</c>
///     (Resources/Prototypes/Entities/Clothing/Shoes/specific.yml) carries
///     <c>FootstepModifierComponent</c> with <c>footstepSoundCollection: null</c>, and
///     <c>SharedMoverController.TryGetSound</c> checks that component directly on the mover's own
///     uid BEFORE it ever looks at the equipped shoes slot. Silent Step reuses that exact mechanism
///     on the ninja's BODY instead of their shoes, so the silence survives losing the boots — a
///     stripped ninja is still a ninja.
/// </summary>
public sealed partial class NinjitsuSystem
{
    private void InitializeSilentStep()
    {
        SubscribeLocalEvent<NinjitsuTrainingComponent, NinjitsuSilentStepEvent>(OnSilentStepToggle);
    }

    private void OnSilentStepToggle(Entity<NinjitsuTrainingComponent> ent, ref NinjitsuSilentStepEvent args)
    {
        var (uid, comp) = ent;
        var now = _timing.CurTime;

        if (!NinjitsuRules.CooldownReady(now, comp.NextSilentStepToggleAt))
            return;

        args.Handled = true;
        comp.NextSilentStepToggleAt = NinjitsuRules.NextReady(now, comp.SilentStepToggleDebounce);

        // Nothing else legitimately puts a FootstepModifierComponent directly on a mob's body
        // (only shoes/items carry one upstream), so owning add/remove of it here outright is safe —
        // its presence on uid IS the toggle state, no separate marker component needed.
        if (RemComp<FootstepModifierComponent>(uid))
        {
            _popup.PopupEntity(Loc.GetString("ninjitsu-silent-step-off"), uid, uid, PopupType.Small);
            LedgerLog(LogType.Action, $"{ToPrettyString(uid):actor} resumed a normal gait; the earlier silence in the sensor logs remains unexplained");
        }
        else
        {
            AddComp<FootstepModifierComponent>(uid);
            _popup.PopupEntity(Loc.GetString("ninjitsu-silent-step-on"), uid, uid, PopupType.Small);
            LedgerLog(LogType.Action, $"{ToPrettyString(uid):actor} generated zero footfall telemetry (Silent Step) — filed as unexplained");
        }

        _audio.PlayPvs(comp.SilentStepSound, uid);
    }
}
