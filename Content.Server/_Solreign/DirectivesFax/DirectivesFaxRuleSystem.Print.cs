using System;
using System.Collections.Generic;
using System.Text;
using Content.Server._Solreign.DirectivesFax.Components;
using Content.Server._Solreign.Notifications;
using Content.Server._Solreign.StationDirective;
using Content.Shared.Fax.Components;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Paper;
using Robust.Server.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     Fax printing + the round-end streak persistence/notification hop for
///     <see cref="DirectivesFaxRuleSystem"/>.
///
///     PRINT-POINT DECISION (see receipt for the full write-up): every SOLREIGN station map already
///     places multiple department-labeled <c>FaxMachineBase</c> entities (fax machines are vanilla SS14
///     content SOLREIGN's own maps use extensively — no new machine/prototype was needed). Priority
///     order, cheapest-to-most-defensive:
///       1. A fax machine named "Bridge" (present on most maps) — PROVIDENCE issuing an HR directive
///          reads most naturally as arriving at the command deck, chain-of-command framing matching
///          the Corporate Directive's own HR voice.
///       2. A fax machine whose name contains "HoP" (present on every map that lacks a dedicated Bridge
///          fax, e.g. Perihelion/Verdant) — still command/HR-tier, same framing.
///       3. Any fax machine belonging to the station at all (deterministic: first one the entity query
///          finds) — guarantees a fax prints on every current SOLREIGN map, including the small
///          Nocturne map which has only one fax ("Reach") and no Bridge/HoP fax.
///       4. Defensive-only fallback: no fax machine resolves at all (a hypothetical future map with
///          zero fax coverage, or a content-integrity test map) — spawn a loose <c>Paper</c> entity at
///          the station's own transform origin rather than silently doing nothing. This path is never
///          expected to trigger against any current SOLREIGN map.
/// </summary>
public sealed partial class DirectivesFaxRuleSystem
{
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;

    private void PrintDirectiveFax(DirectivesFaxRuleComponent component, StationDirectiveDefinition directive)
    {
        if (component.FaxPrinted)
            return;

        var clauses = DirectivesFaxClauseCatalog.GetClauses(directive.Id);
        var body = BuildFaxBody(directive, clauses);
        var name = Loc.GetString("solreign-directives-fax-paper-name");

        if (!TryGetPrimaryStation(out var station))
            return; // No station resolves at all (e.g. a bare content-integrity test map) — nothing to print to.

        if (FindFaxPrintTarget(station) is { } faxUid)
        {
            var printout = new FaxPrintout(body, name, senderFaxName: Loc.GetString("solreign-directives-fax-sender"));
            _fax.Receive(faxUid, printout);
            component.FaxPrinted = true;
            return;
        }

        // Defensive fallback — see class doc, path 4.
        var loose = Spawn("Paper", Transform(station).Coordinates);
        if (TryComp<PaperComponent>(loose, out var paper))
            _paper.SetContent((loose, paper), body);
        _metaDataSystem.SetEntityName(loose, name);
        component.FaxPrinted = true;
    }

    private EntityUid? FindFaxPrintTarget(EntityUid station)
    {
        EntityUid? bridge = null;
        EntityUid? hop = null;
        EntityUid? any = null;

        var query = EntityQueryEnumerator<FaxMachineComponent>();
        while (query.MoveNext(out var uid, out var fax))
        {
            if (_station.GetOwningStation(uid) != station)
                continue;

            any ??= uid;

            if (bridge is null && string.Equals(fax.FaxName, "Bridge", StringComparison.OrdinalIgnoreCase))
                bridge = uid;

            if (hop is null && fax.FaxName.Contains("HoP", StringComparison.OrdinalIgnoreCase))
                hop = uid;
        }

        return bridge ?? hop ?? any;
    }

    private string BuildFaxBody(StationDirectiveDefinition directive, IReadOnlyList<DirectivesFaxClauseSpec> clauses)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("solreign-directives-fax-header", ("directive", DirectivesFaxClauseCatalog.GetDisplayName(directive.Id))));
        sb.AppendLine();
        sb.AppendLine(Loc.GetString(directive.AnnouncementLocKey));
        sb.AppendLine();
        sb.AppendLine(Loc.GetString("solreign-directives-fax-clauses-header"));

        foreach (var clause in clauses)
        {
            var (locKey, args) = DirectivesFaxClauseFormatting.Describe(clause);
            sb.AppendLine(Loc.GetString("solreign-directives-fax-clause-line", ("clause", Loc.GetString(locKey, args))));
        }

        return sb.ToString();
    }

    // --- Round-end streak persistence + milestone notification --------------------------------------

    /// <summary>
    ///     The one async hop for this round's streak update (house threading contract: async void +
    ///     try/catch, continuations marshal back to the game thread — the
    ///     <c>SolreignSocialFirstsSystem.ClaimAndDeliver</c> idiom). Eligible accounts are snapshotted
    ///     synchronously BEFORE the first await (main-thread-only dictionaries). Write-before-dispatch:
    ///     each account's ledger write is awaited and its result read before any milestone
    ///     popup/chat fires for that account.
    /// </summary>
    private async void UpdateStreaksAndNotify(string directiveId, bool met, int roundId)
    {
        var eligible = SnapshotPresentEligibleAccounts();
        if (eligible.Count == 0)
            return;

        foreach (var account in eligible)
        {
            try
            {
                var (currentStreak, _, _) = await _ledger.RecordDirectivesFaxOutcomeAsync(account, met, roundId);

                if (met && DirectivesFaxStreakMilestones.IsMilestone(currentStreak))
                    NotifyMilestone(account, currentStreak);
            }
            catch (Exception e)
            {
                Log.Error($"Error recording Directives Fax streak outcome for {account} (directive '{directiveId}', round {roundId}):\n{e}");
            }
        }
    }

    /// <summary>Fires (or silently drops) one streak-milestone toast — main thread, full
    /// re-resolution guard chain (never trust anything captured before the await), same idiom as
    /// <c>SolreignSocialFirstsSystem.Deliver</c>. The ledger row is already written: a dropped delivery
    /// is swallowed forever, never duplicated.</summary>
    private void NotifyMilestone(Guid account, int streak)
    {
        if (!DirectivesFaxStreakMilestones.ReasonLocKeyByStreak.TryGetValue(streak, out var reasonKey))
            return;

        if (!_players.TryGetSessionById(new NetUserId(account), out var session))
            return;

        var reasonText = Loc.GetString(reasonKey);

        if (session.AttachedEntity is { } target
            && !Deleted(target)
            && !HasComp<GhostComponent>(target)
            && (!TryComp<MobStateComponent>(target, out var mobState) || mobState.CurrentState != MobState.Dead))
        {
            SolreignAwardPopup.ShowMilestone(_popup, target, reasonText);
        }

        // Chat mirror always sends while the session lives — survives popup-blindness, screenshot-
        // surviving (the Social Firsts rehire-beat rationale, same dual-delivery idiom).
        _chatManager.DispatchServerMessage(session, Loc.GetString("solreign-directives-fax-streak-milestone-chat", ("reason", reasonText)));
    }
}
