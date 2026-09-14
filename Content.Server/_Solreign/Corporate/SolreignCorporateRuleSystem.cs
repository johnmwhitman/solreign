using Content.Server._Solreign.Corporate.Components;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Events;
using Content.Shared._Solreign.Corporate;
using Content.Shared._Solreign.FX;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Corporate;

/// <summary>
///     "The Corporate Ladder" (Solreign, Milestone 1). A layerable, cosmetic-only game rule that runs every
///     round on top of whatever preset is playing. On start it broadcasts an evil-megacorp HR "keynote"; it
///     tracks a per-account Corporate Standing (see <see cref="OnMobStateChanged"/>); every fiscal quarter it
///     reads out the top performers over the PA as a "quarterly earnings call"; and at round end it appends an
///     FY final-standings block. Nothing here changes mechanics or compels play — it is pure flavor + scoreboard.
///
///     Layering is handled without touching core presets by <see cref="SolreignCorporateLayerSystem"/>, which
///     starts this rule on <c>RoundStartingEvent</c>.
/// </summary>
public sealed partial class SolreignCorporateRuleSystem : GameRuleSystem<SolreignCorporateRuleComponent>
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedCargoSystem _cargo = default!;

    // The cross-round Season Ledger. We hand our final per-account standings to it at round end so they
    // persist and compound into each account's Corporate Rank. Non-readonly: filled by dependency injection.
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    /// <summary>
    ///     Sender label the PA stamps on every Solreign HR broadcast (loc-string sweep, Phase2 A4:
    ///     was a bare hardcoded literal).
    /// </summary>
    private string HrSender => Loc.GetString("solreign-corporate-hr-sender");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);

        // Per-round standing tracker + kill attribution (partial: SolreignCorporateRuleSystem.Scoring.cs).
        InitializeScoring();
    }

    protected override void Started(EntityUid uid, SolreignCorporateRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        _active = true;
        component.SinceLastAudit = 0f;

        // Roll the corporate modifier for this round
        var modifiers = Enum.GetValues<CorporateModifierType>();
        component.ActiveModifier = modifiers[_random.Next(modifiers.Length)];

        var keynote = Loc.GetString("solreign-corporate-keynote");
        
        switch (component.ActiveModifier)
        {
            case CorporateModifierType.BudgetCuts:
                keynote += "\n\nNotice: Due to recent market fluctuations, all departmental budgets have been halved for this fiscal quarter.";
                break;
            case CorporateModifierType.BudgetSurplus:
                keynote += "\n\nNotice: Record profits! All departmental budgets have been doubled.";
                break;
            case CorporateModifierType.PizzaParty:
                keynote += "\n\nNotice: Budget cuts are in effect, but morale is high because we've ordered pizza for everyone.";
                break;
        }

        _chat.DispatchGlobalAnnouncement(
            keynote,
            HrSender,
            playSound: true,
            // ceremony_stinger.ogg, not track2_*.mp3. Two reasons, either sufficient:
            //   * the engine cannot load .mp3 at all ("Unknown file type: .mp3") — this reference
            //     alone crashed the client at startup and failed 379 of 385 integration tests.
            //   * even in a supported format it is the wrong asset. That track is a 4.4MB
            //     atmospheric drone; an announcement wants a short sting, which is exactly what
            //     ceremony_stinger.ogg already is.
            announcementSound: new Robust.Shared.Audio.SoundPathSpecifier("/Audio/_Solreign/ceremony_stinger.ogg"),
            colorOverride: Color.FromHex("#c0a062"));
    }

    protected override void ActiveTick(EntityUid uid, SolreignCorporateRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        component.SinceLastAudit += frameTime;
        if (component.SinceLastAudit < component.AuditIntervalSeconds)
            return;

        component.SinceLastAudit = 0f;
        BroadcastEarningsCall(component);
    }

    /// <summary>
    ///     The "quarterly earnings call": names the current top performers over the PA. Skipped entirely while
    ///     the board is empty (no attributable productivity yet) so the station isn't spammed with a hollow call.
    /// </summary>
    private void BroadcastEarningsCall(SolreignCorporateRuleComponent component)
    {
        var top = CorporateScoring.TopN(SnapshotStandings(), component.EarningsCallTopCount);
        if (top.Count == 0)
            return;

        var body = string.Join("\n", CorporateScoring.FormatBoard(top));
        var message = Loc.GetString("solreign-corporate-earnings-call", ("board", body));

        _chat.DispatchGlobalAnnouncement(message, HrSender, playSound: false, colorOverride: Color.FromHex("#c0a062"));

        // Signature shader moment: the quarterly audit gets the acid-green screen-border sting on every
        // client, for free, via the general-purpose Solreign FX hook (Content.Shared._Solreign.FX).
        RaiseNetworkEvent(new SolreignScreenFxEvent());
    }

    protected override void AppendRoundEndText(EntityUid uid, SolreignCorporateRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        // Close the loop: hand our final per-account standings to the Season Ledger. This fires before the
        // ledger's own RoundEndMessageEvent handler (verified ordering), so the ledger sees these standings
        // when it snapshots the round. Persistence + rank compounding happen there, not here.
        _ledger.SubmitRoundStandings(_standing);

        args.AddLine(Loc.GetString("solreign-corporate-fy-header"));

        var ranked = CorporateScoring.TopN(SnapshotStandings(), component.FinalStandingsCount);
        if (ranked.Count == 0)
        {
            args.AddLine(Loc.GetString("solreign-corporate-fy-empty"));
        }
        else
        {
            foreach (var line in CorporateScoring.FormatBoard(ranked))
                args.AddLine(line);
        }

        args.AddLine("");
    }

    protected override void Ended(EntityUid uid, SolreignCorporateRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        _active = false;
    }

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        var query = EntityQueryEnumerator<SolreignCorporateRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var corp, out var rule))
        {
            if (!_active)
                continue;

            if (corp.ActiveModifier == CorporateModifierType.None)
                continue;

            if (TryComp<StationBankAccountComponent>(ev.Station, out var bank))
            {
                var accounts = _cargo.GetAccounts((ev.Station.Owner, bank));
                var keys = new List<Robust.Shared.Prototypes.ProtoId<Content.Shared.Cargo.Prototypes.CargoAccountPrototype>>(accounts.Keys);
                foreach (var account in keys)
                {
                    var balance = accounts[account];
                    if (corp.ActiveModifier == CorporateModifierType.BudgetCuts || corp.ActiveModifier == CorporateModifierType.PizzaParty)
                        _cargo.TrySetBankAccount((ev.Station.Owner, bank), account, balance / 2);
                    else if (corp.ActiveModifier == CorporateModifierType.BudgetSurplus)
                        _cargo.TrySetBankAccount((ev.Station.Owner, bank), account, balance * 2);
                }
            }
        }
    }
}
