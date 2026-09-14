using Content.Server.Administration.Logs;
using Content.Server.Speech.EntitySystems;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.TrainingCombat;

/// <summary>
///     COMBAT SPIKE server logic: on a landed melee hit against a living humanoid, roll a training
///     zone (<see cref="ZoneRules.PickZone"/>) and apply that zone's outcome — head = brief stutter +
///     jitter, legs = short accumulating slowdown, hands = drop the active held item. Each outcome
///     gets a popup, a distinct sound, and an admin log line (same shape as upstream
///     SharedMeleeWeaponSystem's MeleeHit logging).
///
///     Subscription shape follows upstream <c>UseDelayOnMeleeHitSystem</c> /
///     <c>WeaponRandomSystem</c>; outcomes reuse existing systems rather than inventing state:
///     StutteringSystem + SharedJitteringSystem (head), MovementModStatusSystem (legs),
///     SharedHandsSystem.TryDrop (hands).
/// </summary>
public sealed partial class SolreignTrainingBatonSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementModStatusSystem _movementMod = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private Content.Shared.Speech.EntitySystems.StutteringSystem _stuttering = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignTrainingBatonComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<SolreignTrainingBatonComponent> ent, ref MeleeHitEvent args)
    {
        // Examining a melee weapon raises this event with IsHit = false.
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (!ZoneRules.CooldownReady(_timing.CurTime, ent.Comp.NextEffectTime))
            return;

        foreach (var target in args.HitEntities)
        {
            // Training is a humanoid-HR program: crates, walls and pets are out of scope.
            if (!HasComp<HumanoidProfileComponent>(target) || !_mobState.IsAlive(target))
                continue;

            var zone = ZoneRules.PickZone(
                ent.Comp.HeadWeight,
                ent.Comp.LegsWeight,
                ent.Comp.HandsWeight,
                _random.NextDouble());

            if (zone == TrainingZone.None)
                return;

            ent.Comp.NextEffectTime = ZoneRules.NextEffectTime(_timing.CurTime, ent.Comp.EffectCooldown);
            ApplyZoneOutcome(ent, args.User, target, zone);

            // One trainee per swing — a wide swing shouldn't hand out a row of status effects.
            return;
        }
    }

    private void ApplyZoneOutcome(
        Entity<SolreignTrainingBatonComponent> ent,
        EntityUid user,
        EntityUid target,
        TrainingZone zone)
    {
        switch (zone)
        {
            case TrainingZone.Head:
                // Mildest available head consequence: a brief stutter (speech status effect) plus a
                // low-amplitude jitter "stumble". No stun, no knockdown — this is training.
                _stuttering.DoStutter(target, ent.Comp.HeadEffectDuration, refresh: true);
                _jitter.DoJitter(
                    target,
                    ent.Comp.HeadEffectDuration,
                    refresh: true,
                    ent.Comp.HeadJitterAmplitude,
                    ent.Comp.HeadJitterFrequency);
                _popup.PopupEntity(
                    Loc.GetString("solreign-training-baton-head", ("target", Identity.Entity(target, EntityManager))),
                    target,
                    PopupType.SmallCaution);
                _audio.PlayPvs(ent.Comp.HeadSound, target);
                break;

            case TrainingZone.Legs:
                // Repeat hits accumulate duration on the same effect prototype — the spike's "stack".
                _movementMod.TryAddMovementSpeedModDuration(
                    target,
                    ent.Comp.LegsSlowdownEffect,
                    ent.Comp.LegsSlowdownDuration,
                    ent.Comp.LegsSpeedModifier);
                _popup.PopupEntity(
                    Loc.GetString("solreign-training-baton-legs", ("target", Identity.Entity(target, EntityManager))),
                    target,
                    PopupType.SmallCaution);
                _audio.PlayPvs(ent.Comp.LegsSound, target);
                break;

            case TrainingZone.Hands:
                var dropped = _hands.TryDrop(target);
                _popup.PopupEntity(
                    Loc.GetString(
                        dropped ? "solreign-training-baton-hands" : "solreign-training-baton-hands-empty",
                        ("target", Identity.Entity(target, EntityManager))),
                    target,
                    PopupType.SmallCaution);
                _audio.PlayPvs(ent.Comp.HandsSound, target);
                break;
        }

        _adminLogger.Add(LogType.MeleeHit,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} landed a training baton {zone} hit on {ToPrettyString(target):subject} using {ToPrettyString(ent.Owner):tool}");
    }
}
