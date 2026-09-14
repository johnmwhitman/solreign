using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.Corporate;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Chat.Systems;
using Content.Server.UserInterface;
using Content.Shared._Solreign.Corporate.Projects;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Server._Solreign.Corporate.Projects;

/// <summary>
///     Server system for player-run Corporate Projects (SR-W-030).
///     Handles project progress, bounded contributions, anti-monopoly validation, persistence across shifts, and corporate milestone triggers.
/// </summary>
public sealed partial class SolreignCorporateProjectSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SolreignCorporateRuleSystem _corporateRule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignCorporateProjectConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<SolreignCorporateProjectConsoleComponent, SolreignCorporateProjectContributeMessage>(OnContributeMessage);
        SubscribeLocalEvent<SolreignCorporateProjectConsoleComponent, SolreignCorporateProjectSelectMessage>(OnSelectMessage);
    }

    // These handlers are async void (the engine's event signature is void), so the try/catch in
    // each is load-bearing, not defensive: an exception escaping an async void has no caller to
    // receive it and reaches the runtime as UNHANDLED, taking the server down — the exact hazard
    // the Memorial console shipped and 3d339eec3a fixed. A player opening a wall console must
    // never be able to do that.
    private async void OnConsoleOpened(EntityUid uid, SolreignCorporateProjectConsoleComponent comp, BoundUIOpenedEvent args)
    {
        if (!_playerManager.TryGetSessionByEntity(args.Actor, out var player))
            return;

        try
        {
            await UpdateUiStateAsync(uid, player);
        }
        catch (Exception e)
        {
            Log.Error($"Corporate Projects console {ToPrettyString(uid)} failed to load its state: {e}");
        }
    }

    private async void OnSelectMessage(EntityUid uid, SolreignCorporateProjectConsoleComponent comp, SolreignCorporateProjectSelectMessage msg)
    {
        if (!_playerManager.TryGetSessionByEntity(msg.Actor, out var player))
            return;

        try
        {
            await UpdateUiStateAsync(uid, player, selectedProjectId: msg.ProjectId);
        }
        catch (Exception e)
        {
            Log.Error($"Corporate Projects console {ToPrettyString(uid)} failed to load its state: {e}");
        }
    }

    private async void OnContributeMessage(EntityUid uid, SolreignCorporateProjectConsoleComponent comp, SolreignCorporateProjectContributeMessage msg)
    {
        if (!_playerManager.TryGetSessionByEntity(msg.Actor, out var player))
            return;

        try
        {
            await ProcessContribution(uid, player, msg);
        }
        catch (Exception e)
        {
            Log.Error($"Corporate Projects console {ToPrettyString(uid)} failed to process a contribution: {e}");
        }
    }

    private async Task ProcessContribution(EntityUid uid, ICommonSession player, SolreignCorporateProjectContributeMessage msg)
    {
        var userId = player.UserId.UserId;
        var playerName = player.Name;

        if (string.IsNullOrWhiteSpace(msg.ProjectId) || !_prototype.TryIndex<SolreignCorporateProjectPrototype>(msg.ProjectId, out var proto))
        {
            await UpdateUiStateAsync(uid, player, selectedProjectId: msg.ProjectId, statusMessage: Loc.GetString("solreign-corporate-projects-rejection-not-found"));
            return;
        }

        var result = await _ledger.TryContributeToProjectAsync(msg.ProjectId, userId, msg.Amount, proto);

        string? statusMsg = null;

        switch (result.Kind)
        {
            case CorporateProjectContributionResultKind.Success:
                statusMsg = Loc.GetString("solreign-corporate-projects-amount-placeholder", ("max", proto.MaxContributionPerAction));

                // Process milestone unlocks
                foreach (var milestoneId in result.NewlyUnlockedMilestones)
                {
                    var milestone = proto.Milestones.FirstOrDefault(m => m.MilestoneId == milestoneId);
                    if (milestone == null)
                        continue;

                    var milestoneName = Loc.GetString(milestone.Name);
                    var projectName = Loc.GetString(proto.Name);

                    // Announce milestone unlock
                    _chat.DispatchGlobalAnnouncement(
                        Loc.GetString("solreign-corporate-project-milestone-unlocked-announcement",
                            ("projectName", projectName),
                            ("milestoneName", milestoneName),
                            ("standing", milestone.StandingReward)),
                        sender: Loc.GetString("solreign-corporate-hr-sender"),
                        playSound: true,
                        colorOverride: Color.FromHex("#c0a062"));

                    // Award corporate standing to contributors
                    var contributors = await _ledger.GetProjectContributorsAsync(msg.ProjectId);
                    foreach (var contributor in contributors)
                    {
                        _corporateRule.AwardStanding(new NetUserId(contributor.User), milestone.StandingReward, playerName);
                    }
                }

                if (result.ProjectNowCompleted)
                {
                    var projectName = Loc.GetString(proto.Name);
                    var sponsorName = Loc.GetString(proto.Sponsor);

                    _chat.DispatchGlobalAnnouncement(
                        Loc.GetString("solreign-corporate-project-completed-announcement",
                            ("projectName", projectName),
                            ("sponsor", sponsorName)),
                        sender: Loc.GetString("solreign-corporate-hr-sender"),
                        playSound: true,
                        colorOverride: Color.FromHex("#50c878"));
                }
                break;

            case CorporateProjectContributionResultKind.AlreadyCompleted:
                statusMsg = Loc.GetString("solreign-corporate-projects-rejection-already-completed");
                break;

            case CorporateProjectContributionResultKind.AccountCapReached:
                int cap = proto.GetMaxAccountContribution();
                statusMsg = Loc.GetString("solreign-corporate-projects-rejection-account-cap", ("cap", cap));
                break;

            case CorporateProjectContributionResultKind.InvalidAmount:
                statusMsg = Loc.GetString("solreign-corporate-projects-rejection-invalid-amount");
                break;

            case CorporateProjectContributionResultKind.ProjectNotFound:
                statusMsg = Loc.GetString("solreign-corporate-projects-rejection-not-found");
                break;
        }

        await UpdateUiStateAsync(uid, player, selectedProjectId: msg.ProjectId, statusMessage: statusMsg);
    }

    public async Task UpdateUiStateAsync(EntityUid consoleUid, ICommonSession player, string? selectedProjectId = null, string? statusMessage = null)
    {
        if (!_ui.HasUi(consoleUid, SolreignCorporateProjectUiKey.Key))
            return;

        var userId = player.UserId.UserId;
        var allPrototypes = _prototype.EnumeratePrototypes<SolreignCorporateProjectPrototype>().ToList();
        var records = await _ledger.GetAllCorporateProjectsAsync();

        var projectStates = new List<SolreignCorporateProjectState>();

        foreach (var proto in allPrototypes)
        {
            records.TryGetValue(proto.ID, out var record);

            int currentContrib = record?.TotalContribution ?? 0;
            bool completed = record?.Completed ?? false;
            var unlockedSet = new HashSet<string>(record?.UnlockedMilestones ?? new List<string>());

            int accountContrib = await _ledger.GetAccountProjectContributionAsync(proto.ID, userId);
            int maxAccountCap = proto.GetMaxAccountContribution();

            var milestones = new List<SolreignCorporateProjectMilestoneState>();
            foreach (var m in proto.Milestones)
            {
                bool unlocked = unlockedSet.Contains(m.MilestoneId) || currentContrib >= (int) Math.Floor(proto.TargetContribution * m.ThresholdFraction);
                milestones.Add(new SolreignCorporateProjectMilestoneState(
                    m.MilestoneId,
                    Loc.GetString(m.Name),
                    m.ThresholdFraction,
                    m.StandingReward,
                    unlocked));
            }

            projectStates.Add(new SolreignCorporateProjectState(
                proto.ID,
                Loc.GetString(proto.Name),
                Loc.GetString(proto.Description),
                Loc.GetString(proto.Sponsor),
                proto.TargetContribution,
                currentContrib,
                proto.MaxContributionPerAction,
                accountContrib,
                maxAccountCap,
                completed,
                milestones));
        }

        if (string.IsNullOrEmpty(selectedProjectId) && projectStates.Count > 0)
        {
            selectedProjectId = projectStates[0].ProjectId;
        }

        // The console may have been deleted (round restart, explosion, admin delete) while the
        // ledger reads above were in flight — awaiting yields to the game loop, and SetUiState
        // against a dead entity throws on a thread with no handler.
        if (Deleted(consoleUid))
            return;

        var state = new SolreignCorporateProjectUiState(projectStates, selectedProjectId, statusMessage);
        _ui.SetUiState(consoleUid, SolreignCorporateProjectUiKey.Key, state);
    }
}
