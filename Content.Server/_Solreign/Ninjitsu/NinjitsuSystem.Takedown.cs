using Content.Server._Solreign.MartialArts;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Carp-style nonlethal takedown: a Sleeping-Carp-flavored ninjitsu finisher granted innately
///     alongside <see cref="NinjitsuTrainingComponent"/>. Deliberately reuses the Way of the
///     Ornamental Carp's combo core math directly rather than re-deriving it —
///     <see cref="CarpComboRules.WithinWindow"/> (via <see cref="NinjitsuRules.StrikesChain"/>) for
///     the chain window and <see cref="CarpComboRules.ComboReady"/>/<see cref="CarpComboRules.NextComboTime"/>
///     for the finisher cooldown. Independent chain state from <c>SolreignMartialArtistComponent</c>
///     (own component, own subscription on <see cref="Content.Shared.Ninja.Components.SpaceNinjaComponent"/>
///     rather than <c>SolreignMartialArtistComponent</c>) — a ninja who also learns the Carp scroll
///     runs both chains independently, no interference.
///
///     Input detection mirrors <c>SolreignMartialArtsSystem.OnArtistMeleeHit</c> exactly: a landed
///     UNARMED melee hit (fists raise <see cref="MeleeHitEvent"/> on the striker's own entity, so
///     <c>Weapon == Owner</c> is the unarmed check) on a living humanoid. Two in a row on the SAME
///     target inside the window, off cooldown, drops them — nonlethal knockdown only, never damage.
/// </summary>
public sealed partial class NinjitsuSystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    private void InitializeTakedown()
    {
        SubscribeLocalEvent<NinjitsuTrainingComponent, MeleeHitEvent>(OnNinjaMeleeHit);
    }

    private void OnNinjaMeleeHit(Entity<NinjitsuTrainingComponent> ent, ref MeleeHitEvent args)
    {
        // Examining a melee weapon raises this event with IsHit = false.
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        // Unarmed only, belt and braces (a held weapon raises this on the weapon, never here).
        if (args.Weapon != ent.Owner)
            return;

        var comp = ent.Comp;
        var now = _timing.CurTime;

        foreach (var target in args.HitEntities)
        {
            if (target == args.User || !HasComp<HumanoidProfileComponent>(target) || !_mobState.IsAlive(target))
                continue;

            var sameTarget = comp.LastStrikeTarget == target;
            if (sameTarget
                && NinjitsuRules.StrikesChain(comp.LastStrikeTime, now, comp.TakedownWindow)
                && CarpComboRules.ComboReady(now, comp.NextTakedownAt))
            {
                // Finisher fires: the chain is consumed, same as Carp Rush consuming a 2-hit chain.
                comp.LastStrikeTarget = null;
                comp.NextTakedownAt = CarpComboRules.NextComboTime(now, comp.TakedownCooldown);
                ApplyTakedown(args.User, target, comp);
            }
            else
            {
                // No combo (first strike, different target, expired window, or finisher on
                // cooldown) — this strike opens (or re-opens) the chain, same as RegisterStep.
                comp.LastStrikeTarget = target;
                comp.LastStrikeTime = now;
            }

            // One chain step per swing — a wide swing still only counts once.
            return;
        }
    }

    private void ApplyTakedown(EntityUid user, EntityUid target, NinjitsuTrainingComponent comp)
    {
        _stun.TryKnockdown(target, comp.TakedownKnockdownDuration, refresh: true, autoStand: false, drop: false);

        _popup.PopupEntity(
            Loc.GetString("ninjitsu-takedown-user", ("target", Identity.Entity(target, EntityManager))),
            user,
            PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("ninjitsu-takedown-target"), target, PopupType.LargeCaution);
        _audio.PlayPvs(comp.TakedownSound, target);

        LedgerLog(LogType.MeleeHit, $"{ToPrettyString(user):actor} silently dropped {ToPrettyString(target):subject} — filed as an unexplained discrepancy (ninjitsu takedown)");
    }
}
