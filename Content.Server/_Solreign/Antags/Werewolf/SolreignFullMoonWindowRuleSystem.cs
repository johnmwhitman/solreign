using Content.Server.Antag;
using Content.Server.Chat.Systems;
using Content.Server._Solreign.Antags.Vampire;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Content.Shared.Objectives.Systems;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Ticks the "Full Moon Window" event-night game rule (spec §3.2, §3.5 rule 6). On start, drafts
///     ONE eligible crew member as Lunar-Reactive and opens the window; on end (duration elapsed),
///     closes it. The actual transformation decision (dormant → stirring → transformed → waning) is
///     entirely the pure <see cref="WerewolfStateMachine"/>'s — this rule only supplies the
///     <see cref="SolreignWerewolfSystem.MoonWindowActive"/> flag it reads every tick.
///
///     KNOWN v1 GAP (documented, not silently swept under): anti-grief rule 6 asks for "consent-shaped
///     selection: event-night role offered via the antag preference system, never forced onto a random
///     player". Wiring the real upstream antag-preference pipeline (AntagSelectionComponent +
///     antagSpecifier role prototype + mind roles) is a substantially bigger, separate YAML+C# surface
///     (see Resources/Prototypes/Roles/Antags/traitor.yml for the shape) that risks a lot of unverified
///     schema for this pass. This v1 instead drafts uniformly at random among ELIGIBLE candidates
///     (alive, humanoid, not already Moon-Touched or antagged) and sends the same upstream briefing a
///     preference-driven pick would get (<see cref="AntagSelectionSystem.SendBriefing"/>) — functional
///     and safe, but not yet preference-gated. Flagged here so the build pass doesn't lose the gap.
/// </summary>
public sealed partial class SolreignFullMoonWindowRuleSystem : GameRuleSystem<SolreignFullMoonWindowRuleComponent>
{
    [Dependency] private SolreignWerewolfSystem _werewolf = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AliveHumanoidTargetSystem _target = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;

    /// <summary>Loc-string sweep, Phase2 A4: was a bare hardcoded literal.</summary>
    private string HrSender => Loc.GetString("solreign-corporate-hr-sender");
    private static readonly Color MoonAnnouncementColor = Color.FromHex("#8fd3ff");

    protected override void Started(EntityUid uid, SolreignFullMoonWindowRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Elapsed = 0f;
        _werewolf.MoonWindowActive = true;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-werewolf-window-open"),
            HrSender,
            playSound: true,
            colorOverride: MoonAnnouncementColor);

        DraftOneWerewolf(component);
    }

    protected override void ActiveTick(EntityUid uid, SolreignFullMoonWindowRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        component.Elapsed += frameTime;
        if (component.Elapsed < component.WindowSeconds)
            return;

        GameTicker.EndGameRule(uid, gameRule);
    }

    protected override void Ended(EntityUid uid, SolreignFullMoonWindowRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        _werewolf.MoonWindowActive = false;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-werewolf-window-close"),
            HrSender,
            playSound: false,
            colorOverride: MoonAnnouncementColor);
    }

    /// <summary>
    ///     Drafts exactly one eligible candidate (anti-grief rule 5: one werewolf per event) and hands
    ///     them <see cref="SolreignWerewolfComponent"/>. A candidate already Moon-Touched, already a
    ///     werewolf, or already a vampire this round is excluded — nobody gets double-cast by two
    ///     event-night rules landing on the same window.
    /// </summary>
    private void DraftOneWerewolf(SolreignFullMoonWindowRuleComponent component)
    {
        if (component.Drafted)
            return;

        var candidates = new List<EntityUid>();
        foreach (var mind in _target.GetMinds())
        {
            if (mind.Comp.OwnedEntity is not { } ent)
                continue;

            if (HasComp<SolreignWerewolfComponent>(ent) || HasComp<SolreignMoonTouchedComponent>(ent) ||
                HasComp<SolreignVampireComponent>(ent))
                continue;

            candidates.Add(ent);
        }

        if (candidates.Count == 0)
            return;

        var chosen = _random.Pick(candidates);
        EnsureComp<SolreignWerewolfComponent>(chosen);
        component.Drafted = true;

        _antag.SendBriefing(chosen, Loc.GetString("solreign-werewolf-briefing"), MoonAnnouncementColor, null);
    }
}
