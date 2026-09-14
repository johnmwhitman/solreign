using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     Additive, zero-core-edit layering for "Directives Fax" — mirrors
///     <c>Content.Server._Solreign.StationDirective.StationDirectiveLayerSystem</c> exactly. Subscribes
///     <see cref="RoundStartingEvent"/> and starts the <c>SolreignDirectivesFax</c> game rule every
///     round, so long as <see cref="CCVars.SolreignDirectivesFaxEnabled"/> is true.
///
///     SHIPS OFF: the CVar defaults to false, so in the shipped-dormant state this class's
///     <see cref="OnRoundStarting"/> returns immediately every round and
///     <see cref="DirectivesFaxRuleSystem"/> never starts — zero behavior change until someone with
///     authority flips the CVar.
/// </summary>
public sealed partial class DirectivesFaxLayerSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly EntProtoId DirectivesFaxRule = "SolreignDirectivesFax";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.SolreignDirectivesFaxEnabled))
            return;

        if (_gameTicker.IsIgnored(DirectivesFaxRule))
            return;

        _gameTicker.StartGameRule(DirectivesFaxRule);
    }
}
