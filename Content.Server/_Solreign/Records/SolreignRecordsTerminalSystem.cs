using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;
using Content.Shared._Solreign.Records;
using Content.Shared.CCVar;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Solreign.Records;

/// <summary>
///     ECS glue for the Personnel Records Terminal (wave-2 item einstein-016 — mine rank #8): a
///     player-reachable console rendering THEIR OWN Season Ledger record, clean-room built from
///     Einstein Engines' public description of a records computer (no source read — see
///     <c>docs/receipts/RECORDS-HUD-2026-07-17.md</c>).
///
///     Privacy is structural, not a checked gate: every read this system performs is keyed off
///     <c>args.Actor</c>'s OWN session — there is no message type anywhere in
///     <c>RecordsTerminalUiMessages.cs</c> that carries a target account, so "browse another
///     player's file" is not a disableable code path, it simply does not exist. The response is a
///     private, session-targeted network event (never a shared <c>BoundUserInterfaceState</c>) so a
///     second player opening the same console can never receive — or even have networked to their
///     client — the first player's data. See <c>RecordsTerminalSnapshotEvent</c>'s doc comment.
///
///     SHIPS FALSE (<see cref="CCVars.SolreignRecordsTerminalEnabled"/>): disabled, this system closes
///     any opened terminal UI immediately and never enqueues a read — zero behavior change, same as
///     every other dormant Solreign feature flag.
/// </summary>
public sealed partial class SolreignRecordsTerminalSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignRecordsTerminalEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<SolreignRecordsTerminalComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SolreignRecordsTerminalComponent, RecordsTerminalRefreshMessage>(OnRefresh);
    }

    private void OnOpened(EntityUid uid, SolreignRecordsTerminalComponent component, BoundUIOpenedEvent args)
    {
        RequestRead(uid, args.Actor);
    }

    private void OnRefresh(EntityUid uid, SolreignRecordsTerminalComponent component, RecordsTerminalRefreshMessage args)
    {
        RequestRead(uid, args.Actor);
    }

    /// <summary>
    ///     Feature-flag gate + actor resolution. Off: the window (if somehow opened — the entity isn't
    ///     placed on any map this wave) is force-closed for that one actor and nothing is read from the
    ///     ledger. On: kicks off the async ledger read for the requesting actor's OWN account only.
    /// </summary>
    private void RequestRead(EntityUid uid, EntityUid actor)
    {
        if (!_enabled)
        {
            _ui.CloseUi(uid, RecordsTerminalUiKey.Key, actor);
            return;
        }

        if (!_players.TryGetSessionByEntity(actor, out var session))
            return;

        LoadAndPush(GetNetEntity(uid), session.UserId.UserId, session);
    }

    private async void LoadAndPush(NetEntity console, Guid account, ICommonSession session)
    {
        try
        {
            var data = await _ledger.GetRecordsTerminalDataAsync(account);
            // Wall-clock, not game-tick time — the Mark's aging clock is deliberately wall-clock
            // (MarkAgeRules' own doc comment), matching SolreignMarkGardenSystem's own DateTime.UtcNow use.
            var plan = RecordsTerminalRenderer.BuildPlan(data, DateTime.UtcNow);
            var snapshot = Render(plan);

            // Session may have disconnected while the read was in flight; RaiseNetworkEvent to a
            // detached session is a silent no-op on the engine side, but skip explicitly for clarity.
            if (!session.Channel.IsConnected)
                return;

            RaiseNetworkEvent(new RecordsTerminalSnapshotEvent(console, snapshot), session);
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading Personnel Records Terminal data for an account:\n{e}");
        }
    }

    /// <summary>
    ///     Turns a pure <see cref="RecordsTerminalPlan"/> into the fully-rendered, closed-template
    ///     <see cref="RecordsTerminalSnapshot"/> the wire carries. This is the ONLY place in the feature
    ///     that calls <c>Loc.GetString</c> — every string that reaches the client came from a fixed
    ///     Fluent key, several of them reused verbatim from already-shipped copy packs
    ///     (<c>PersonnelFileRules</c>'s standing word, the social-first "reason" keys, <c>MarkCopy</c>'s
    ///     kind word and stage examine line) rather than duplicated.
    /// </summary>
    private RecordsTerminalSnapshot Render(RecordsTerminalPlan plan)
    {
        var standing = PersonnelFileRules.DescribeCareerStanding(plan.RankIndex);

        var socialFirsts = new List<string>();
        foreach (var flag in plan.CelebratedSocialFirstFlags)
            socialFirsts.Add(Loc.GetString(SocialFirstReasonKey(flag)));

        var firstDeathStatus = Loc.GetString(plan.HasFirstDeath
            ? "solreign-records-terminal-first-death-on-file"
            : "solreign-records-terminal-first-death-none");

        string? markLine = null;
        if (plan.HasMark)
        {
            var kindWord = Loc.GetString(MarkCopy.KindWordKeyFor(plan.MarkKind));
            var stageLine = Loc.GetString(MarkCopy.StageKeyFor(plan.MarkKind, plan.MarkStage));
            markLine = Loc.GetString("solreign-records-terminal-mark-line",
                ("kind", kindWord), ("stage", stageLine));
        }

        return new RecordsTerminalSnapshot(
            plan.DisplayTitle,
            plan.Tours,
            standing,
            socialFirsts,
            firstDeathStatus,
            markLine);
    }

    /// <summary>
    ///     Maps a celebrated social-first flag id to its already-shipped milestone-name loc key
    ///     (<c>Resources/Locale/en-US/_solreign/social-cheap-adds.ftl</c>). Exhaustive over
    ///     <see cref="RecordsTerminalRenderer.CelebratedFlagOrder"/>; the default arm should be
    ///     unreachable (the renderer's plan only ever contains flags from that closed order) but stays
    ///     a safe, generic fallback rather than a throw — an examine-style surface must never crash a
    ///     read over an unexpected flag id.
    /// </summary>
    private static string SocialFirstReasonKey(string flag)
    {
        return flag switch
        {
            SolreignSocialFirstFlags.ChirpAnswered => "solreign-social-first-chirp-answered-reason",
            SolreignSocialFirstFlags.HealedByAnother => "solreign-social-first-healed-reason",
            SolreignSocialFirstFlags.ItemReceived => "solreign-social-first-item-received-reason",
            _ => "solreign-records-terminal-social-first-unknown",
        };
    }
}
