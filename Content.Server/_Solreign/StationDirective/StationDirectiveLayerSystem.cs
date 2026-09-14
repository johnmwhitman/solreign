using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.StationDirective;

/// <summary>
///     Additive, zero-core-edit layering for "Station Directive". Subscribes <see cref="RoundStartingEvent"/>
///     and starts the <c>SolreignStationDirective</c> game rule every round, so the cosmetic directive
///     layer rides on top of whatever preset is playing without editing any core preset or game-rule list.
///     Mirrors <c>Content.Server._Solreign.Corporate.SolreignCorporateLayerSystem</c>.
///
///     Honors <see cref="CCVars.SolreignStationDirectiveEnabled"/> — off skips starting the rule entirely
///     for the round (checked once at round start, same as the game-ticker's own ignore-list check below).
/// </summary>
public sealed partial class StationDirectiveLayerSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly EntProtoId DirectiveRule = "SolreignStationDirective";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.SolreignStationDirectiveEnabled))
            return;

        // Preset rules have already been started at this point; add + start ours on top. Ignored if the
        // server has explicitly ignored this rule id.
        if (_gameTicker.IsIgnored(DirectiveRule))
            return;

        _gameTicker.StartGameRule(DirectiveRule);
    }
}
