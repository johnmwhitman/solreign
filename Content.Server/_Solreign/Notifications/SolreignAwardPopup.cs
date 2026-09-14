using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Server._Solreign.Notifications;

/// <summary>
///     UX-SIMPLE FIX 4: a reusable "+N Standing — [reason]" (or "-N Standing — [reason]" for a
///     spend) confirmation popup, so every Standing award/spend across the Solreign feature set
///     renders in one consistent voice instead of each system inventing its own wording. Wraps
///     <see cref="SharedPopupSystem.PopupEntity(string?, EntityUid, EntityUid?, PopupType)"/> — the
///     exact self-notification call convention already used everywhere in
///     <c>Content.Server._Solreign</c> (Contracts, Market, Bounties) — so callers do not need to
///     remember the <c>(uid, uid, PopupType)</c> shape themselves.
///
///     Not an <see cref="EntitySystem"/>: award sites live in several different systems
///     (<c>ContractsSystem</c>, <c>SolreignMarketSystem</c>, ...), each already injecting their own
///     <see cref="SharedPopupSystem"/>, so this stays a stateless static helper they can all call
///     with the popup system they already have — resolving <see cref="ILocalizationManager"/>
///     directly via <see cref="IoCManager"/> is the same pattern several non-EntitySystem UI
///     classes elsewhere in this codebase already use (e.g. <c>Content.Client.FlavorText.FlavorText</c>).
///
///     No dedup mechanism exists here, and none is needed: every call site fires this exactly once
///     per completed, one-shot award/spend event (a contract payout, a verified market-buy ack,
///     etc.) — never on a retried or polled path. The closest existing analog in this codebase,
///     <c>SeasonLedgerStore</c>'s title-grant "announced" gate, exists specifically because titles
///     are re-evaluated on every <c>LoadTitle</c> call and would otherwise re-fire; nothing here has
///     an equivalent re-evaluation loop to guard against.
/// </summary>
public static class SolreignAwardPopup
{
    /// <summary>
    ///     Shows "+N Standing — [reason]" (or "-N Standing — [reason]" for a spend) to
    ///     <paramref name="recipient"/>, visually anchored at <paramref name="origin"/> (pass the
    ///     recipient's own entity for a pure self-notification — the idiom Market/Bounty/the raid-
    ///     payout ping already use — or a fixture like a dropbox/console for a "delivered here"
    ///     flavor, matching the existing Contracts-deposit idiom). <paramref name="delta"/> is
    ///     SIGNED: positive for an award, negative for a spend — the popup renders the absolute
    ///     value with the matching sign and wording. <paramref name="reasonText"/> must already be
    ///     bounded/escaped by the caller (contract id, item name, etc — never raw daemon or player
    ///     text) before it reaches here, same discipline as every other popup in this file tree.
    /// </summary>
    public static void Show(SharedPopupSystem popup, EntityUid origin, EntityUid recipient, int delta,
        string reasonText, PopupType type = PopupType.Medium)
    {
        var loc = IoCManager.Resolve<ILocalizationManager>();
        var locKey = delta >= 0 ? "solreign-award-standing-popup" : "solreign-award-standing-popup-spend";
        var text = loc.GetString(locKey, ("amount", System.Math.Abs(delta)), ("reason", reasonText));
        popup.PopupEntity(text, origin, recipient, type);
    }

    /// <summary>Convenience overload for the common self-notification case (origin == recipient).</summary>
    public static void Show(SharedPopupSystem popup, EntityUid recipient, int delta, string reasonText,
        PopupType type = PopupType.Medium) =>
        Show(popup, recipient, recipient, delta, reasonText, type);

    /// <summary>
    ///     Shows "MILESTONE LOGGED — [reason]" to <paramref name="recipient"/> — the no-Standing
    ///     sibling of <see cref="Show(SharedPopupSystem, EntityUid, int, string, PopupType)"/> for
    ///     the social-first celebrations (council 2026-07-16 item 5), kept here so every award-ish
    ///     toast across the Solreign feature set stays in one consistent voice. Deliberately carries
    ///     NO number: social firsts award nothing and deduct nothing (the ledger's ALWAYS-CUMULATIVE
    ///     covenant is untouched) — fabricating a "+N" here would be dishonest, the same law as the
    ///     salary-stipend popup. <paramref name="reasonText"/> must already be bounded/escaped by
    ///     the caller (a loc-resolved milestone name — never raw daemon or player text).
    /// </summary>
    public static void ShowMilestone(SharedPopupSystem popup, EntityUid recipient, string reasonText,
        PopupType type = PopupType.Medium)
    {
        var loc = IoCManager.Resolve<ILocalizationManager>();
        var text = loc.GetString("solreign-award-milestone-popup", ("reason", reasonText));
        popup.PopupEntity(text, recipient, recipient, type);
    }
}
