using Content.Server.Antag;
using Content.Server.Chat.Systems;
using Content.Server._Solreign.Antags.Werewolf;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Content.Shared.Objectives.Systems;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Drafts exactly one eligible crew member as a Nocturnal Acquisitions Specialist at round start
///     (spec §4, §8 explicit non-goal: "no ghost-role spawns of either" — this is a roundstart draft,
///     not a ghost role). Same v1 selection gap as the werewolf's
///     <see cref="SolreignFullMoonWindowRuleSystem"/> — see its doc comment for why a uniform-random
///     pick among eligible candidates stands in for the full antag-preference pipeline this pass.
/// </summary>
public sealed partial class SolreignVampireNightRuleSystem : GameRuleSystem<SolreignVampireNightRuleComponent>
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AliveHumanoidTargetSystem _target = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;

    /// <summary>Loc-string sweep, Phase2 A4: was a bare hardcoded literal.</summary>
    private string HrSender => Loc.GetString("solreign-vampire-night-sender");
    private static readonly Color VampireAnnouncementColor = Color.FromHex("#7a1f3d");

    protected override void Started(EntityUid uid, SolreignVampireNightRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Drafted)
            return;

        var candidates = new List<EntityUid>();
        foreach (var mind in _target.GetMinds())
        {
            if (mind.Comp.OwnedEntity is not { } ent)
                continue;

            if (HasComp<SolreignVampireComponent>(ent) || HasComp<SolreignWerewolfComponent>(ent))
                continue;

            candidates.Add(ent);
        }

        if (candidates.Count == 0)
            return;

        var chosen = _random.Pick(candidates);
        EnsureComp<SolreignVampireComponent>(chosen);
        component.Drafted = true;

        _antag.SendBriefing(chosen, Loc.GetString("solreign-vampire-briefing"), VampireAnnouncementColor, null);

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-vampire-night-start"),
            HrSender,
            playSound: true,
            colorOverride: VampireAnnouncementColor);
    }
}
