using Content.Server.Administration.Logs;
using Content.Server.Hands.Systems;
using Content.Shared.Clothing;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     CHEF CQC server logic (roadmap: the combo core's kitchen-brigade moveset, grant-while-worn,
///     nonlethal): a certified kitchen-discipline instrument that teaches Swat/Toss combos for as
///     long as the apron stays buckled on (see <see cref="SolreignChefApronComponent"/> /
///     <see cref="SolreignChefApronWearerComponent"/>).
///
///     Combos are sequences of EXISTING interactions — no new input bindings, same idiom as
///     <see cref="SolreignMartialArtsSystem"/> (the Way of the Ornamental Carp):
///       Swat = a landed UNARMED melee hit (fists/open hand raise MeleeHitEvent on the wearer
///              themself, so subscribing on the wearer's granted component is inherently
///              unarmed-only);
///       Toss = a successful disarm (DisarmedEvent, observed AFTER upstream HandsSystem /
///              SharedStaminaSystem apply the normal shove) — same quirk Carp/Judo document: the
///              event is raised directed at the TARGET. Carp already owns (MobStateComponent,
///              DisarmedEvent), Judo owns (HumanoidProfileComponent, DisarmedEvent), and the shove
///              knockback system owns (MovementSpeedModifierComponent, DisarmedEvent) — this system
///              hooks DamageableComponent instead, a fourth distinct pair, to avoid piling onto an
///              already-claimed (component, event) combination.
///
///     Finishers mirror the Way of the Ornamental Carp exactly (same two-input pairwise shape):
///     Heat Check (stamina jolt), 86'd (knockdown), Order Up (disarm throw) — all nonlethal, all
///     going through existing shared systems.
///
///     Chain/window math is pure and lives in <see cref="ChefComboRules"/>
///     (Content.Tests/_Solreign/ChefComboRulesTests.cs).
/// </summary>
public sealed partial class SolreignChefApronSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Grant-while-worn: the apron teaches the moveset only as long as it's buckled on, same
        // idiom as the Security Judo Belt (not the Carp Scroll's permanent one-time lesson).
        SubscribeLocalEvent<SolreignChefApronComponent, ClothingGotEquippedEvent>(OnApronEquipped);
        SubscribeLocalEvent<SolreignChefApronComponent, ClothingGotUnequippedEvent>(OnApronUnequipped);

        SubscribeLocalEvent<SolreignChefApronWearerComponent, MeleeHitEvent>(OnWearerMeleeHit);

        // See the class doc: DisarmedEvent is raised directed at the shove TARGET, and Carp/Judo/
        // the shove system already each own a distinct (component, event) pair, so this system
        // hooks DamageableComponent instead — still ordered AFTER upstream handlers so we only
        // observe a shove that actually happened.
        SubscribeLocalEvent<DamageableComponent, DisarmedEvent>(OnAnyDisarmed,
            after: new[] { typeof(HandsSystem), typeof(SharedStaminaSystem) });
    }

    // --- Grant while worn ---

    private void OnApronEquipped(Entity<SolreignChefApronComponent> ent, ref ClothingGotEquippedEvent args)
    {
        EnsureComp<SolreignChefApronWearerComponent>(args.Wearer);
    }

    private void OnApronUnequipped(Entity<SolreignChefApronComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        // Take the apron off and the moveset (and any mid-chain progress) goes with it.
        RemComp<SolreignChefApronWearerComponent>(args.Wearer);
    }

    // --- Combo inputs ---

    private void OnWearerMeleeHit(Entity<SolreignChefApronWearerComponent> ent, ref MeleeHitEvent args)
    {
        // Examining a melee weapon raises this event with IsHit = false.
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        // Unarmed only: fists raise MeleeHitEvent on the wearer entity itself, so Weapon == wearer.
        // A held weapon raises it on the weapon entity and never reaches this handler anyway; this
        // check is belt and braces.
        if (args.Weapon != ent.Owner)
            return;

        foreach (var target in args.HitEntities)
        {
            // Chef CQC is a people problem: crates, walls and pets neither advance nor break the
            // chain (same humanoid scope as Carp/Judo).
            if (target == args.User || !HasComp<HumanoidProfileComponent>(target) || !_mobState.IsAlive(target))
                continue;

            RegisterStep(ent, args.User, target, ChefStep.Swat);

            // One chain step per swing — a wide swing is still one swat.
            return;
        }
    }

    private void OnAnyDisarmed(Entity<DamageableComponent> target, ref DisarmedEvent args)
    {
        // Unhandled means upstream aborted the shove entirely — nothing happened, nothing chains.
        if (!args.Handled)
            return;

        if (!TryComp<SolreignChefApronWearerComponent>(args.Source, out var wearer))
            return;

        if (!HasComp<HumanoidProfileComponent>(target.Owner) || !_mobState.IsAlive(target.Owner))
            return;

        RegisterStep((args.Source, wearer), args.Source, target.Owner, ChefStep.Toss);
    }

    // --- Chain bookkeeping ---

    private void RegisterStep(
        Entity<SolreignChefApronWearerComponent> ent,
        EntityUid user,
        EntityUid target,
        ChefStep step)
    {
        var comp = ent.Comp;
        var curTime = _timing.CurTime;
        var sameTarget = comp.LastTarget == target;

        // Finisher on cooldown: the step still opens a fresh chain, it just can't complete one.
        if (!ChefComboRules.ComboReady(curTime, comp.NextComboTime))
        {
            comp.LastStep = step;
            comp.LastStepTime = curTime;
            comp.LastTarget = target;
            return;
        }

        var combo = ChefComboRules.Advance(
            comp.LastStep,
            comp.LastStepTime,
            step,
            curTime,
            comp.ComboWindow,
            sameTarget,
            out var nextPrevious);

        comp.LastStep = nextPrevious;
        comp.LastStepTime = curTime;
        comp.LastTarget = target;

        if (combo == ChefCombo.None)
            return;

        comp.NextComboTime = ChefComboRules.NextComboTime(curTime, comp.ComboCooldown);
        ApplyCombo(ent, user, target, combo);
    }

    // --- Finishers (all nonlethal: stamina, knockdown, item toss — never health damage) ---

    private void ApplyCombo(
        Entity<SolreignChefApronWearerComponent> ent,
        EntityUid user,
        EntityUid target,
        ChefCombo combo)
    {
        var targetIdentity = Identity.Entity(target, EntityManager);

        switch (combo)
        {
            case ChefCombo.HeatCheck:
                // Two quick swats -> stamina jolt. Stamina only; the target tires, nothing bleeds.
                _stamina.TakeStaminaDamage(target, ent.Comp.HeatCheckStaminaJolt, source: user, with: user);
                _popup.PopupEntity(
                    Loc.GetString("solreign-chef-heat-check-user", ("user", Identity.Entity(user, EntityManager))),
                    user,
                    PopupType.LargeCaution);
                _popup.PopupEntity(
                    Loc.GetString("solreign-chef-heat-check-target", ("target", targetIdentity)),
                    target,
                    PopupType.MediumCaution);
                _audio.PlayPvs(ent.Comp.HeatCheckSound, target);
                break;

            case ChefCombo.EightySixed:
                // Swat then toss -> knockdown. drop: false — item removal is Order Up's job.
                _stun.TryKnockdown(target, ent.Comp.EightySixedKnockdownDuration, refresh: true, drop: false);
                _popup.PopupEntity(
                    Loc.GetString("solreign-chef-eighty-sixed", ("target", targetIdentity)),
                    target,
                    PopupType.LargeCaution);
                _audio.PlayPvs(ent.Comp.EightySixedSound, target);
                break;

            case ChefCombo.OrderUp:
                // Toss-toss -> disarm throw. Upstream HandsSystem already tossed the active item a
                // tile on each shove; the finisher takes whatever the target STILL holds and sends
                // it properly flying.
                if (_hands.TryGetActiveItem(target, out var item)
                    && _hands.TryDrop(target, item.Value, checkActionBlocker: false))
                {
                    // Random compass direction, same idiom as Gentle Current.
                    var direction = _random.NextAngle().ToWorldVec() * ent.Comp.OrderUpThrowDistance;
                    _throwing.TryThrow(item.Value, direction, ent.Comp.OrderUpThrowSpeed, user: user);
                    _popup.PopupEntity(
                        Loc.GetString("solreign-chef-order-up",
                            ("target", targetIdentity),
                            ("item", item.Value)),
                        target,
                        PopupType.LargeCaution);
                }
                else
                {
                    _popup.PopupEntity(
                        Loc.GetString("solreign-chef-order-up-empty", ("target", targetIdentity)),
                        target,
                        PopupType.MediumCaution);
                }

                _audio.PlayPvs(ent.Comp.OrderUpSound, target);
                break;
        }

        _adminLogger.Add(LogType.MeleeHit,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} landed the Chef CQC combo {combo} on {ToPrettyString(target):subject}");
    }
}
