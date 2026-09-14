using Content.Server.Administration.Logs;
using Content.Server.Hands.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
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
///     MARTIAL ARTS TOY server logic (roadmap: ONE style, three hardcoded combos, nonlethal): the
///     Way of the Ornamental Carp, taught one-time by an ancient scroll
///     (<see cref="SolreignCarpScrollComponent"/>, learn on use, consumed).
///
///     Combos are sequences of EXISTING interactions — no new input bindings:
///       Strike = a landed UNARMED melee hit (fists raise MeleeHitEvent on the artist themself,
///                so subscribing on the artist's component is inherently unarmed-only);
///       Shove  = a successful disarm (DisarmedEvent, observed AFTER upstream HandsSystem /
///                SharedStaminaSystem apply the normal shove).
///     Finishers reuse existing systems rather than inventing state, exactly like the Wave 3
///     training baton: SharedStaminaSystem (Carp Rush jolt), SharedStunSystem.TryKnockdown
///     (Rising Tide), SharedHandsSystem + ThrowingSystem (Gentle Current item toss). Every
///     finisher gets a loud popup, a distinct sound, and an admin log line.
///
///     Chain/window math is pure and lives in <see cref="CarpComboRules"/>
///     (Content.Tests/_Solreign/CarpComboRulesTests.cs).
/// </summary>
public sealed partial class SolreignMartialArtsSystem : EntitySystem
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

        SubscribeLocalEvent<SolreignCarpScrollComponent, UseInHandEvent>(OnScrollUse);
        SubscribeLocalEvent<SolreignMartialArtistComponent, MeleeHitEvent>(OnArtistMeleeHit);

        // DisarmedEvent is raised directed on the shove TARGET (see SharedMeleeWeaponSystem), so we
        // can't hook it via the artist's own component. MobStateComponent is the ubiquitous hook:
        // every shovable target is a mob. Ordered AFTER upstream handlers so we observe a shove
        // that actually happened (args.Handled) and never race HandsSystem's own item throw.
        SubscribeLocalEvent<MobStateComponent, DisarmedEvent>(OnAnyDisarmed,
            after: new[] { typeof(HandsSystem), typeof(SharedStaminaSystem) });
    }

    // --- The scroll ---

    private void OnScrollUse(Entity<SolreignCarpScrollComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (HasComp<SolreignMartialArtistComponent>(args.User))
        {
            // Refresher courses are not offered; the scroll is NOT consumed on a refusal.
            _popup.PopupEntity(
                Loc.GetString("solreign-carp-scroll-already"),
                args.User,
                args.User,
                PopupType.Medium);
            return;
        }

        AddComp<SolreignMartialArtistComponent>(args.User);

        _popup.PopupEntity(
            Loc.GetString("solreign-carp-scroll-learned", ("user", Identity.Entity(args.User, EntityManager))),
            args.User,
            PopupType.Large);
        _audio.PlayPvs(ent.Comp.LearnSound, args.User);

        _adminLogger.Add(LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(args.User):actor} learned the Way of the Ornamental Carp from {ToPrettyString(ent.Owner):tool}");

        if (ent.Comp.Consumed)
            QueueDel(ent);
    }

    // --- Combo inputs ---

    private void OnArtistMeleeHit(Entity<SolreignMartialArtistComponent> ent, ref MeleeHitEvent args)
    {
        // Examining a melee weapon raises this event with IsHit = false.
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        // Unarmed only: fists raise MeleeHitEvent on the artist entity itself, so Weapon == artist.
        // A held weapon raises it on the weapon entity and never reaches this handler anyway;
        // this check is belt and braces.
        if (args.Weapon != ent.Owner)
            return;

        foreach (var target in args.HitEntities)
        {
            // Carp style is a people problem: crates, walls and pets neither advance nor break
            // the chain (same humanoid scope as the training baton).
            if (target == args.User || !HasComp<HumanoidProfileComponent>(target) || !_mobState.IsAlive(target))
                continue;

            RegisterStep(ent, args.User, target, CarpStep.Strike);

            // One chain step per swing — a wide swing is still one strike.
            return;
        }
    }

    private void OnAnyDisarmed(Entity<MobStateComponent> target, ref DisarmedEvent args)
    {
        // Unhandled means upstream aborted the shove entirely — nothing happened, nothing chains.
        if (!args.Handled)
            return;

        if (!TryComp<SolreignMartialArtistComponent>(args.Source, out var artist))
            return;

        if (!HasComp<HumanoidProfileComponent>(target.Owner) || !_mobState.IsAlive(target.Owner))
            return;

        RegisterStep((args.Source, artist), args.Source, target.Owner, CarpStep.Shove);
    }

    // --- Chain bookkeeping ---

    private void RegisterStep(
        Entity<SolreignMartialArtistComponent> ent,
        EntityUid user,
        EntityUid target,
        CarpStep step)
    {
        var comp = ent.Comp;
        var curTime = _timing.CurTime;
        var sameTarget = comp.LastTarget == target;

        // Finisher on cooldown: the step still opens a fresh chain, it just can't complete one.
        if (!CarpComboRules.ComboReady(curTime, comp.NextComboTime))
        {
            comp.LastStep = step;
            comp.LastStepTime = curTime;
            comp.LastTarget = target;
            return;
        }

        var combo = CarpComboRules.Advance(
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

        if (combo == CarpCombo.None)
            return;

        comp.NextComboTime = CarpComboRules.NextComboTime(curTime, comp.ComboCooldown);
        ApplyCombo(ent, user, target, combo);
    }

    // --- Finishers (all nonlethal: stamina, knockdown, item toss — never health damage) ---

    private void ApplyCombo(
        Entity<SolreignMartialArtistComponent> ent,
        EntityUid user,
        EntityUid target,
        CarpCombo combo)
    {
        var targetIdentity = Identity.Entity(target, EntityManager);

        switch (combo)
        {
            case CarpCombo.CarpRush:
                // Two quick hits -> stamina jolt. Stamina only; the target tires, nothing bleeds.
                _stamina.TakeStaminaDamage(target, ent.Comp.CarpRushStaminaJolt, source: user, with: user);
                _popup.PopupEntity(
                    Loc.GetString("solreign-carp-rush-cry", ("user", Identity.Entity(user, EntityManager))),
                    user,
                    PopupType.LargeCaution);
                _popup.PopupEntity(
                    Loc.GetString("solreign-carp-rush-target", ("target", targetIdentity)),
                    target,
                    PopupType.MediumCaution);
                _audio.PlayPvs(ent.Comp.CarpRushSound, target);
                break;

            case CarpCombo.RisingTide:
                // Hit then shove -> knockdown. drop: false — item removal is Gentle Current's job.
                _stun.TryKnockdown(target, ent.Comp.RisingTideKnockdownDuration, refresh: true, drop: false);
                _popup.PopupEntity(
                    Loc.GetString("solreign-rising-tide", ("target", targetIdentity)),
                    target,
                    PopupType.LargeCaution);
                _audio.PlayPvs(ent.Comp.RisingTideSound, target);
                break;

            case CarpCombo.GentleCurrent:
                // Shove-shove -> disarm throw. Upstream HandsSystem already tossed the active item
                // a tile on each shove; the finisher takes whatever the target STILL holds and
                // sends it properly flying.
                if (_hands.TryGetActiveItem(target, out var item)
                    && _hands.TryDrop(target, item.Value, checkActionBlocker: false))
                {
                    // Random compass direction, upstream RevenantSystem.Abilities throw pattern.
                    var direction = _random.NextAngle().ToWorldVec() * ent.Comp.GentleCurrentThrowDistance;
                    _throwing.TryThrow(item.Value, direction, ent.Comp.GentleCurrentThrowSpeed, user: user);
                    _popup.PopupEntity(
                        Loc.GetString("solreign-gentle-current",
                            ("target", targetIdentity),
                            ("item", item.Value)),
                        target,
                        PopupType.LargeCaution);
                }
                else
                {
                    _popup.PopupEntity(
                        Loc.GetString("solreign-gentle-current-empty", ("target", targetIdentity)),
                        target,
                        PopupType.MediumCaution);
                }

                _audio.PlayPvs(ent.Comp.GentleCurrentSound, target);
                break;
        }

        _adminLogger.Add(LogType.MeleeHit,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} landed the Ornamental Carp combo {combo} on {ToPrettyString(target):subject}");
    }
}
