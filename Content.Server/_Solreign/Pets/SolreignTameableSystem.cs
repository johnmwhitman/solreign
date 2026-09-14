using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.Pets;

/// <summary>
///     Drives Solreign Pets v1 (SR-W-047): feeding a whitelisted food item to a <see cref="SolreignTameableComponent"/>
///     critter registers the feeder as its owner and points its existing follow-AI at them. Also supports follow/stay toggling,
///     pet release, grooming/cleaning interactions, and cosmetic Season Ledger stamps without blocking core gameplay loops.
/// </summary>
public sealed partial class SolreignTameableSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    ///     How often the stale-owner sweep runs.
    /// </summary>
    private static readonly TimeSpan OwnerSweepInterval = TimeSpan.FromSeconds(2);

    private TimeSpan _nextOwnerSweep;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignTameableComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SolreignTameableComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<SolreignTameableComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInteractUsing(EntityUid uid, SolreignTameableComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Don't let corpses get "tamed" — a dead critter can't follow anyone anyway.
        if (_mobState.IsDead(uid))
            return;

        // Check if the item used is a cleaning/grooming tool first.
        if (component.CleanerWhitelist != null && _whitelist.IsWhitelistPass(component.CleanerWhitelist, args.Used))
        {
            HandleCleanup(uid, component, args.User, args.Used);
            args.Handled = true;
            return;
        }

        // A null whitelist never counts as food — prototypes must opt a critter in explicitly
        // (e.g. `whitelist: { components: [Edible] }`) rather than accepting taming from any item.
        var isValidFood = _whitelist.IsWhitelistPass(component.FoodWhitelist, args.Used);

        var wasTamed = component.TamedBy != null;
        var sameOwner = wasTamed && component.TamedBy == args.User;
        var outcome = TamingRules.Evaluate(isValidFood, wasTamed, sameOwner);

        if (outcome == TamingOutcome.Rejected)
            return;

        args.Handled = true;

        if (TamingRules.ShouldAssignOwner(outcome))
            AssignOwner(uid, component, args.User);

        var popupKey = outcome switch
        {
            TamingOutcome.NewlyTamed => component.TameSuccessPopup,
            TamingOutcome.Retamed => component.RetamedPopup,
            _ => component.AlreadyBondedPopup,
        };

        _popup.PopupEntity(
            Loc.GetString(popupKey,
                ("target", Identity.Entity(uid, EntityManager)),
                ("owner", Identity.Entity(args.User, EntityManager))),
            uid,
            args.User);

        RecordCosmeticStamp(uid, component, args.User, "tame_bond");

        if (TamingRules.ShouldConsumeFood(outcome) && component.ConsumeFood)
            QueueDel(args.Used);
    }

    private void OnGetInteractionVerbs(EntityUid uid, SolreignTameableComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var isOwner = component.TamedBy == user;

        if (isOwner && component.TamedBy != null)
        {
            // Release verb
            args.Verbs.Add(new InteractionVerb
            {
                Act = () => ReleasePet(uid, component, user),
                Text = Loc.GetString("solreign-pet-verb-release"),
                Message = Loc.GetString("solreign-pet-verb-release-tooltip"),
            });

            // Toggle follow / stay verb
            var followText = component.IsFollowing
                ? Loc.GetString("solreign-pet-verb-stay")
                : Loc.GetString("solreign-pet-verb-follow");

            args.Verbs.Add(new InteractionVerb
            {
                Act = () => ToggleFollow(uid, component, user),
                Text = followText,
                Message = Loc.GetString("solreign-pet-verb-toggle-tooltip"),
            });
        }
    }

    public void AssignOwner(EntityUid uid, SolreignTameableComponent component, EntityUid owner)
    {
        component.TamedBy = owner;
        component.IsFollowing = true;

        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(owner, Vector2.Zero));

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _htn.Replan(htn);
    }

    public void ReleasePet(EntityUid uid, SolreignTameableComponent component, EntityUid user)
    {
        if (!TamingRules.EvaluateRelease(component.TamedBy != null))
            return;

        component.TamedBy = null;
        component.IsFollowing = false;

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);

            if (htn.Plan != null)
                _htn.ShutdownPlan(htn);

            _htn.Replan(htn);
        }

        _popup.PopupEntity(
            Loc.GetString(component.ReleaseSuccessPopup,
                ("target", Identity.Entity(uid, EntityManager)),
                ("user", Identity.Entity(user, EntityManager))),
            uid,
            user);
    }

    public void ToggleFollow(EntityUid uid, SolreignTameableComponent component, EntityUid user)
    {
        if (component.TamedBy == null)
            return;

        var outcome = TamingRules.EvaluateFollowToggle(true, component.IsFollowing);
        if (outcome == FollowToggleOutcome.Untamed)
            return;

        component.IsFollowing = outcome == FollowToggleOutcome.NowFollowing;

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            if (component.IsFollowing)
            {
                _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(user, Vector2.Zero));
            }
            else
            {
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
            }

            if (htn.Plan != null)
                _htn.ShutdownPlan(htn);

            _htn.Replan(htn);
        }

        var popupKey = component.IsFollowing ? component.ToggleFollowPopup : component.ToggleStayPopup;
        _popup.PopupEntity(
            Loc.GetString(popupKey,
                ("target", Identity.Entity(uid, EntityManager)),
                ("user", Identity.Entity(user, EntityManager))),
            uid,
            user);
    }

    public void HandleCleanup(EntityUid uid, SolreignTameableComponent component, EntityUid user, EntityUid used)
    {
        var isValidCleaner = component.CleanerWhitelist != null && _whitelist.IsWhitelistPass(component.CleanerWhitelist, used);
        var outcome = TamingRules.EvaluateCleanup(isValidCleaner, component.IsDirty);

        if (outcome == CleanupOutcome.RejectedItem)
            return;

        if (outcome == CleanupOutcome.AlreadyClean)
        {
            _popup.PopupEntity(
                Loc.GetString(component.AlreadyCleanPopup,
                    ("target", Identity.Entity(uid, EntityManager))),
                uid,
                user);
            return;
        }

        component.IsDirty = false;

        _popup.PopupEntity(
            Loc.GetString(component.CleanupSuccessPopup,
                ("target", Identity.Entity(uid, EntityManager)),
                ("user", Identity.Entity(user, EntityManager))),
            uid,
            user);

        RecordCosmeticStamp(uid, component, user, "grooming");
    }

    public void RecordCosmeticStamp(EntityUid uid, SolreignTameableComponent component, EntityUid user, string stampType)
    {
        var stampId = $"{Name(uid).ToLowerInvariant()}_{stampType}";
        var outcome = TamingRules.EvaluateCosmeticStamp(component.TamedBy != null, component.LedgerStamps.Contains(stampId));

        if (outcome == CosmeticStampOutcome.NoBond || outcome == CosmeticStampOutcome.AlreadyStamped)
            return;

        component.LedgerStamps.Add(stampId);

        _popup.PopupEntity(
            Loc.GetString(component.LedgerStampPopup,
                ("target", Identity.Entity(uid, EntityManager)),
                ("stamp", stampId)),
            uid,
            user);
    }

    private void OnExamined(EntityUid uid, SolreignTameableComponent component, ExaminedEvent args)
    {
        args.PushMarkup(component.TamedBy is { } owner && !Deleted(owner)
            ? Loc.GetString(component.ExamineTamed, ("owner", Identity.Entity(owner, EntityManager)))
            : Loc.GetString(component.ExamineUntamed));

        if (component.TamedBy != null)
        {
            args.PushMarkup(component.IsFollowing
                ? Loc.GetString("solreign-pet-examine-following")
                : Loc.GetString("solreign-pet-examine-staying"));
        }

        if (component.IsDirty)
            args.PushMarkup(Loc.GetString("solreign-pet-examine-dirty"));

        if (component.LedgerStamps.Count > 0)
            args.PushMarkup(Loc.GetString("solreign-pet-examine-stamps", ("count", component.LedgerStamps.Count)));

        args.PushMarkup(Loc.GetString(component.ExamineLedgerFlavor));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextOwnerSweep)
            return;

        _nextOwnerSweep = _timing.CurTime + OwnerSweepInterval;

        var query = EntityQueryEnumerator<SolreignTameableComponent>();
        while (query.MoveNext(out var uid, out var tameable))
        {
            if (tameable.TamedBy is not { } owner || !Deleted(owner))
                continue;

            tameable.TamedBy = null;
            tameable.IsFollowing = false;

            if (TryComp<HTNComponent>(uid, out var htn))
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
        }
    }
}
