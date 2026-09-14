using System;
using System.Linq;
using Content.Server._Solreign.Notifications;
using Content.Shared._Solreign.Contracts;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Fulfillment Dropbox deposit matching (spec §3.2.2). A player hits the dropbox with a deliverable in
///     hand; if it matches an entry of one of THEIR contracts (personal claim or launched raid roster) the
///     item is consumed, progress ticks, a cheerful chime plays, and a full contract completes instantly.
///
///     Interaction choice: <see cref="InteractUsingEvent"/> rather than container-insert, because it
///     carries the depositor — attribution is the whole trick (who gets credit, whose contracts to match)
///     and it keeps anti-grief rule 3 trivially true: the only items that ever enter the flow are ones the
///     depositor is holding and offering, never anything "pulled from a player's person" by the system.
///
///     Matching is upstream <c>EntityWhitelistSystem</c> — the entire "did you bring the right thing?"
///     verifier, zero new matching code (spec §2.1). Progress arithmetic is pure (<see cref="ContractRules"/>).
/// </summary>
public sealed partial class ContractsSystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    private void InitializeDropbox()
    {
        SubscribeLocalEvent<SolreignDropboxComponent, InteractUsingEvent>(OnDropboxInteractUsing);
    }

    private void OnDropboxInteractUsing(EntityUid uid, SolreignDropboxComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetUser(args.User, out var guid, out _))
            return;

        if (_station.GetOwningStation(uid) is not { } station ||
            !TryComp<StationSolreignContractsComponent>(station, out var db))
            return;

        // Only THIS depositor's contracts are in play: their personal claims and the launched raids
        // they registered on. Nobody can tick (or grief) someone else's work order.
        var mine = db.Contracts
            .Where(c => c.Claimant == guid ||
                        (c.Scope == SolreignContractScope.SalvageRaid && c.Launched && c.Participants.ContainsKey(guid)))
            .ToList();

        if (mine.Count == 0)
            return; // no contracts — leave the interaction to whatever the entity normally does

        foreach (var contract in mine)
        {
            if (TryDeposit(uid, component, station, db, contract, args.Used, args.User, guid))
            {
                args.Handled = true;
                return;
            }
        }

        // Held item matches none of the depositor's open entries: polite corporate buzz.
        _audio.PlayPvs(component.RejectSound, uid);
        _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-deposit-rejected"), uid, args.User);
        args.Handled = true;
    }

    /// <summary>
    ///     Tries to book <paramref name="item"/> against one entry of <paramref name="contract"/>. Returns
    ///     true (and consumes the matched amount) on success. Stacks are consumed only up to what the entry
    ///     still needs — the remainder stays in the player's hand. <paramref name="depositorGuid"/> is
    ///     forwarded to <see cref="CompleteContract"/> so it can skip re-messaging the depositor (who
    ///     already gets the popup below) while still pinging any other raid teammates.
    /// </summary>
    private bool TryDeposit(EntityUid dropbox, SolreignDropboxComponent component, EntityUid station,
        StationSolreignContractsComponent db, SolreignActiveContract contract, EntityUid item, EntityUid user,
        Guid depositorGuid)
    {
        if (!_proto.TryIndex(contract.Prototype, out var proto))
            return false;

        for (var i = 0; i < proto.Entries.Count && i < contract.Required.Length; i++)
        {
            var entry = proto.Entries[i];

            if (contract.Progress[i] >= contract.Required[i])
                continue;

            // Whitelist in, blacklist out — the bounty IsValidBountyEntry idiom.
            if (!_whitelist.IsValid(entry.Whitelist, item))
                continue;
            if (entry.Blacklist != null && _whitelist.IsValid(entry.Blacklist, item))
                continue;

            var available = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
            var consumed = ContractRules.ApplyDeposit(contract.Progress, contract.Required, i, available);
            if (consumed <= 0)
                continue;

            // Consume exactly what was booked; hand back the rest of a stack.
            if (stack != null && consumed < stack.Count)
                _stack.SetCount(item, stack.Count - consumed, stack);
            else
                QueueDel(item);

            _audio.PlayPvs(component.AcceptSound, dropbox);

            if (ContractRules.IsComplete(contract.Progress, contract.Required))
            {
                CompleteContract(station, db, contract, depositorGuid);
                // UX-SIMPLE FIX 4: shared award-popup helper — same "+N Standing — [reason]" voice
                // as the raid-teammate ping in ContractsSystem.CompleteContract and every other
                // Standing confirmation across the Solreign feature set.
                SolreignAwardPopup.Show(_popup, dropbox, user, contract.StandingPerHead,
                    Loc.GetString("solreign-contracts-award-reason-complete", ("id", contract.Id)));
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-deposit-accepted",
                    ("done", contract.Progress[i]),
                    ("total", contract.Required[i]),
                    ("item", Loc.GetString(entry.Name))), dropbox, user);

                // Partial progress still moves the board windows (completion refreshes via CompleteContract).
                UpdateBoards(station, db);
            }

            return true;
        }

        return false;
    }
}
