using Content.Shared._Solreign.Changeling;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Absorb Identity — "Take Biometric Sample" (spec §2.1). A verb offered on any humanoid, visible
///     only to a changeling, gated on the target being unconscious (never dead, never healthy). Same
///     "offer liberally, re-validate hard at completion" idiom as
///     <c>SolreignVampireSystem.Feeding</c>'s donation verb.
/// </summary>
public sealed partial class SolreignChangelingSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    private void InitializeAbsorb()
    {
        SubscribeLocalEvent<HumanoidProfileComponent, GetVerbsEvent<AlternativeVerb>>(AddAbsorbVerb);
        SubscribeLocalEvent<SolreignChangelingComponent, SolreignChangelingAbsorbDoAfterEvent>(OnAbsorbDoAfter);
    }

    /// <summary>
    ///     Offered on any humanoid target, only to a user with <see cref="SolreignChangelingComponent"/>
    ///     (spec §2.1). Deliberately permissive about the target's current state here — the hard gate
    ///     (<see cref="ChangelingIdentityRules.CanAbsorb"/>) re-runs at do-after completion in
    ///     <see cref="OnAbsorbDoAfter"/>, since Critical/Dead/cooldown can all change mid do-after.
    /// </summary>
    private void AddAbsorbVerb(Entity<HumanoidProfileComponent> target, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (args.User == target.Owner)
            return; // no self-absorb — spec's identity theft is always FROM someone else

        if (!TryComp<SolreignChangelingComponent>(args.User, out var changeling))
            return;

        var changelingUid = args.User;
        var targetUid = target.Owner;
        AlternativeVerb verb = new()
        {
            Text = Loc.GetString("solreign-changeling-absorb-verb"),
            Priority = -1, // low priority — this is a covert antag action, not a common interaction
            Act = () => StartAbsorbDoAfter(changelingUid, targetUid, changeling),
        };
        args.Verbs.Add(verb);
    }

    private void StartAbsorbDoAfter(EntityUid changelingUid, EntityUid targetUid, SolreignChangelingComponent changeling)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, changelingUid, changeling.AbsorbDoAfterSeconds,
            new SolreignChangelingAbsorbDoAfterEvent(), changelingUid, target: targetUid)
        {
            BreakOnMove = true,
            NeedHand = false, // organic contact, not tool use — same call Vampire's donation verb made
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnAbsorbDoAfter(Entity<SolreignChangelingComponent> ent, ref SolreignChangelingAbsorbDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } targetUid)
            return;

        var changeling = ent.Comp;
        var now = _timing.CurTime;

        var targetState = TryComp<MobStateComponent>(targetUid, out var mobState) ? mobState.CurrentState : MobState.Invalid;
        var alreadyAbsorbed = changeling.AbsorbedSources.Contains(targetUid);

        var denial = ChangelingIdentityRules.CanAbsorb(
            targetState,
            alreadyAbsorbed,
            now,
            changeling.NextAbsorbAllowed,
            changeling.KnownAliases.Count,
            changeling.MaxKnownAliases);

        if (denial != AbsorbDenialReason.None)
        {
            _popup.PopupEntity(Loc.GetString(DenialLocKey(denial)), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        var snapshot = CaptureSnapshot(targetUid);
        if (snapshot is null)
            return; // no HumanoidProfileComponent — nothing absorbable (shouldn't happen, verb is humanoid-gated)

        var alias = new SolreignChangelingAlias(snapshot.Profile.Name, snapshot, targetUid, now);
        changeling.KnownAliases.Add(alias);
        changeling.AbsorbedSources.Add(targetUid);
        changeling.NextAbsorbAllowed = ChangelingIdentityRules.NextAbsorbAllowedAt(now, changeling.AbsorbCooldownSeconds);

        // TODO(ledger, spec §4): "Known Aliases" Season Ledger note. Not wired this pass — SeasonLedger
        // files are out of this task's assigned paths. The data this needs (alias.DisplayName,
        // alias.AbsorbedAt, the acting account's Guid) is already sitting right here; the hookup is a
        // dedicated follow-up (additive migration + SeasonLedgerSystem.ChangelingAliases.cs partial,
        // mirroring how Contracts added contract_log) — same documented-but-deferred shape
        // SolreignWerewolfSystem.OnEnterCured already uses for ITS ledger hook.

        _popup.PopupEntity(Loc.GetString("solreign-changeling-absorb-success", ("name", alias.DisplayName)),
            ent.Owner, ent.Owner, PopupType.Medium);

        // Spec §1 PG rule: the victim is never harmed — only informed. No MobState change, no damage.
        _popup.PopupEntity(Loc.GetString("solreign-changeling-absorb-victim-popup"), targetUid, targetUid, PopupType.Medium);

        args.Handled = true;
    }

    private static string DenialLocKey(AbsorbDenialReason reason) => reason switch
    {
        AbsorbDenialReason.TargetNotCritical => "solreign-changeling-absorb-denied-not-critical",
        AbsorbDenialReason.TargetAlreadyAbsorbed => "solreign-changeling-absorb-denied-already-absorbed",
        AbsorbDenialReason.OnCooldown => "solreign-changeling-absorb-denied-cooldown",
        AbsorbDenialReason.AliasLimitReached => "solreign-changeling-absorb-denied-alias-limit",
        _ => "solreign-changeling-absorb-denied-not-critical",
    };
}
