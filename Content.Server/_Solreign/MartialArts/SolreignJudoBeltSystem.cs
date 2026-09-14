using Content.Server.Administration.Logs;
using Content.Server.Hands.Systems;
using Content.Shared.Clothing;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     SECURITY JUDO BELT server logic (roadmap: the combo core's judo moveset, grant-while-worn,
///     nonlethal): a certified de-escalation instrument that teaches Push -&gt; Shove -&gt; Grab for as
///     long as it stays buckled on (see <see cref="SolreignJudoBeltComponent"/> /
///     <see cref="SolreignJudoBeltWearerComponent"/>).
///
///     Combos are sequences of EXISTING interactions — no new input bindings, same idiom as
///     <see cref="SolreignMartialArtsSystem"/>:
///       Push  = a landed UNARMED melee hit (fists raise MeleeHitEvent on the wearer themself, so
///               subscribing on the wearer's granted component is inherently unarmed-only);
///       Shove = a successful disarm (DisarmedEvent, observed AFTER upstream HandsSystem /
///               SharedStaminaSystem apply the normal shove) — same quirk Carp documents: the event
///               is raised directed at the TARGET, so this hooks HumanoidProfileComponent (Carp
///               already owns the (MobStateComponent, DisarmedEvent) pair repo-wide, so this system
///               uses a different component to avoid a duplicate subscription pair);
///       Grab  = successfully STARTING A PULL on the target (PullStartedMessage, raised directed at
///               both puller and pulled — this hooks the wearer's own component and filters to the
///               puller side).
///
///     The throw is nonlethal and works WITH the stamina system rather than around it: it feeds a
///     stamina jolt (SharedStaminaSystem, respects resistance) and THEN applies the guaranteed stun
///     through the same paralyze status effect a stamina crit uses (SharedStunSystem
///     .TryUpdateParalyzeDuration) — a real Stunned status, not just a knockdown. A cooldown between
///     throws (mirroring the Carp finishers) keeps it from stunlocking one target.
///
///     Chain/window math is pure and lives in <see cref="JudoComboRules"/>
///     (Content.Tests/_Solreign/JudoComboRulesTests.cs).
/// </summary>
public sealed partial class SolreignJudoBeltSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Grant-while-worn: this belt teaches the moveset only as long as it's buckled on, unlike
        // the Carp Scroll's permanent one-time lesson.
        SubscribeLocalEvent<SolreignJudoBeltComponent, ClothingGotEquippedEvent>(OnBeltEquipped);
        SubscribeLocalEvent<SolreignJudoBeltComponent, ClothingGotUnequippedEvent>(OnBeltUnequipped);

        SubscribeLocalEvent<SolreignJudoBeltWearerComponent, MeleeHitEvent>(OnWearerMeleeHit);
        SubscribeLocalEvent<SolreignJudoBeltWearerComponent, PullStartedMessage>(OnWearerPullStarted);

        // See the class doc: DisarmedEvent is raised directed at the shove TARGET, and Carp already
        // owns the (MobStateComponent, DisarmedEvent) subscription pair repo-wide, so this system
        // hooks HumanoidProfileComponent instead — a distinct (component, event) pair, still ordered
        // AFTER upstream handlers so we only observe a shove that actually happened.
        SubscribeLocalEvent<HumanoidProfileComponent, DisarmedEvent>(OnAnyDisarmed,
            after: new[] { typeof(HandsSystem), typeof(SharedStaminaSystem) });
    }

    // --- Grant while worn ---

    private void OnBeltEquipped(Entity<SolreignJudoBeltComponent> ent, ref ClothingGotEquippedEvent args)
    {
        EnsureComp<SolreignJudoBeltWearerComponent>(args.Wearer);
    }

    private void OnBeltUnequipped(Entity<SolreignJudoBeltComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        // Take the belt off and the moveset (and any mid-chain progress) goes with it.
        RemComp<SolreignJudoBeltWearerComponent>(args.Wearer);
    }

    // --- Combo inputs ---

    private void OnWearerMeleeHit(Entity<SolreignJudoBeltWearerComponent> ent, ref MeleeHitEvent args)
    {
        // Examining a melee weapon raises this event with IsHit = false.
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        // Unarmed only: fists raise MeleeHitEvent on the wearer entity itself, so Weapon == wearer.
        // A held weapon raises it on the weapon entity and never reaches this handler anyway; this
        // check is belt and braces (pun very much intended).
        if (args.Weapon != ent.Owner)
            return;

        foreach (var target in args.HitEntities)
        {
            // Judo is a people problem: crates, walls and pets neither advance nor break the chain
            // (same humanoid scope as the Carp style).
            if (target == args.User || !HasComp<HumanoidProfileComponent>(target) || !_mobState.IsAlive(target))
                continue;

            RegisterStep(ent, args.User, target, JudoStep.Push);

            // One chain step per swing — a wide swing is still one push.
            return;
        }
    }

    private void OnAnyDisarmed(Entity<HumanoidProfileComponent> target, ref DisarmedEvent args)
    {
        // Unhandled means upstream aborted the shove entirely — nothing happened, nothing chains.
        if (!args.Handled)
            return;

        if (!TryComp<SolreignJudoBeltWearerComponent>(args.Source, out var wearer))
            return;

        if (!_mobState.IsAlive(target.Owner))
            return;

        RegisterStep((args.Source, wearer), args.Source, target.Owner, JudoStep.Shove);
    }

    private void OnWearerPullStarted(Entity<SolreignJudoBeltWearerComponent> ent, ref PullStartedMessage args)
    {
        // Raised directed at BOTH the puller and the pulled entity; only the puller side is a Grab
        // the wearer performed.
        if (args.PullerUid != ent.Owner)
            return;

        var target = args.PulledUid;

        if (target == ent.Owner || !HasComp<HumanoidProfileComponent>(target) || !_mobState.IsAlive(target))
            return;

        RegisterStep(ent, ent.Owner, target, JudoStep.Grab);
    }

    // --- Chain bookkeeping ---

    private void RegisterStep(
        Entity<SolreignJudoBeltWearerComponent> ent,
        EntityUid user,
        EntityUid target,
        JudoStep step)
    {
        var comp = ent.Comp;
        var curTime = _timing.CurTime;
        var sameTarget = comp.LastTarget == target;

        // Throw on cooldown: the step still updates the chain (a fresh Push still opens one), it
        // just can't complete one early.
        if (!JudoComboRules.ComboReady(curTime, comp.NextComboTime))
        {
            comp.ChainState = step == JudoStep.Push ? JudoChainState.PushLanded : JudoChainState.Empty;
            comp.LastStepTime = curTime;
            comp.LastTarget = target;
            return;
        }

        var thrown = JudoComboRules.Advance(
            comp.ChainState,
            comp.LastStepTime,
            step,
            curTime,
            comp.ComboWindow,
            sameTarget,
            out var nextState);

        comp.ChainState = nextState;
        comp.LastStepTime = curTime;
        comp.LastTarget = target;

        if (!thrown)
            return;

        comp.NextComboTime = JudoComboRules.NextComboTime(curTime, comp.ComboCooldown);
        ApplyThrow(ent, user, target);
    }

    // --- Finisher (nonlethal: stamina jolt + guaranteed stun — never health damage) ---

    private void ApplyThrow(Entity<SolreignJudoBeltWearerComponent> ent, EntityUid user, EntityUid target)
    {
        var targetIdentity = Identity.Entity(target, EntityManager);

        // Works WITH the stamina system: the jolt goes through the normal stamina pipeline (respects
        // resistance, visualizes, logs) exactly like Carp Rush, THEN the belt guarantees the stun
        // outright through the same paralyze status effect a stamina crit applies.
        _stamina.TakeStaminaDamage(target, ent.Comp.GrabStaminaJolt, source: user, with: user);
        _stun.TryUpdateParalyzeDuration(target, ent.Comp.StunDuration, visualized: true);

        _popup.PopupEntity(
            Loc.GetString("solreign-judo-throw-user", ("target", targetIdentity)),
            user,
            PopupType.LargeCaution);
        _popup.PopupEntity(
            Loc.GetString("solreign-judo-throw-target"),
            target,
            PopupType.LargeCaution);
        _audio.PlayPvs(ent.Comp.ThrowSound, target);

        _adminLogger.Add(LogType.MeleeHit,
            LogImpact.Medium,
            $"{ToPrettyString(user):actor} landed the Security Judo Belt takedown (Push-Shove-Grab) on {ToPrettyString(target):subject}, stunning them for {ent.Comp.StunDuration.TotalSeconds}s");
    }
}
