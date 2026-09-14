using System.Linq;
using Content.Server.Antag;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     GameRule wiring for the Talent Acquisition Specialist antag (spec §6 round integration — the
///     follow-up the ability-skeleton pass explicitly flagged and deferred, see
///     docs/specs/2026-07-11-changeling-spec.md §6). Selection itself is entirely delegated to the
///     standard upstream <see cref="AntagSelectionSystem"/>/<c>AntagSpecifierPrototype</c> framework
///     via <c>Resources/Prototypes/_Solreign/Roles/changeling_antag.yml</c> — this system's only job
///     is the round-end summary, same "override <c>AppendRoundEndText</c>" idiom
///     <c>ZombieRuleSystem</c> and <c>SolreignCorporateRuleSystem</c> already use.
///
///     No <c>Started</c>/<c>ActiveTick</c> work is needed: everything mechanical already lives on
///     <see cref="SolreignChangelingComponent"/>, attached at selection time via the antagSpecifier's
///     own <c>components:</c> list, and its own <c>ComponentStartup</c> handler
///     (<see cref="SolreignChangelingSystem"/>'s <c>OnStartup</c>) already grants the abilities and
///     snapshots the true form — no <c>AfterAntagEntitySelectedEvent</c> hook required here, unlike
///     <c>TraitorRuleSystem.MakeTraitor</c>.
/// </summary>
public sealed partial class SolreignChangelingRuleSystem : GameRuleSystem<SolreignChangelingRuleComponent>
{
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private SolreignChangelingSystem _changeling = default!;

    protected override void AppendRoundEndText(EntityUid uid,
        SolreignChangelingRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        // GetAntagIdentifiers is scoped to THIS rule entity's own AntagSelectionComponent — it will
        // never include changelings spawned by some other rule instance.
        var identifiers = _antag.GetAntagIdentifiers(uid).ToList();
        if (identifiers.Count == 0)
            return;

        args.AddLine("");
        args.AddLine(Loc.GetString("solreign-changeling-round-end-header"));

        foreach (var (mind, data, name) in identifiers)
        {
            args.AddLine(Loc.GetString("solreign-changeling-round-end-was", ("name", name), ("username", data.UserName)));
            args.AddLine(FormatAliasLine(mind));
        }
    }

    /// <summary>
    ///     <paramref name="mind"/> is the MIND entity (per <see cref="AntagSelectionSystem.GetAntagIdentifiers"/>'s
    ///     own doc comment) — <see cref="SolreignChangelingComponent"/> lives on the mob it currently
    ///     owns, not the mind itself, and stays [Access]-locked to <see cref="SolreignChangelingSystem"/>
    ///     (ANALYZER LAW), so this reads through that system's public accessor rather than touching the
    ///     component directly.
    /// </summary>
    private string FormatAliasLine(EntityUid mind)
    {
        if (TryComp<MindComponent>(mind, out var mindComp) &&
            mindComp.OwnedEntity is { } mob &&
            _changeling.TryGetKnownAliases(mob, out var aliases) &&
            aliases.Count > 0)
        {
            return Loc.GetString("solreign-changeling-round-end-aliases", ("aliases", string.Join(", ", aliases)));
        }

        return Loc.GetString("solreign-changeling-round-end-aliases-none");
    }
}
