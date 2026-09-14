using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Corporate;

/// <summary>
///     Additive, zero-core-edit layering for "The Corporate Ladder". Subscribes <see cref="RoundStartingEvent"/>
///     and starts the <c>SolreignCorporate</c> game rule every round, so the cosmetic corporate layer rides on
///     top of whatever preset is playing without editing any core preset or game-rule list.
///
///     This mirrors how <c>SeasonLedgerSystem</c> hooks round lifecycle as a plain <see cref="EntitySystem"/>.
///     The rule itself (announcements, scoring, scoreboard) lives in <see cref="SolreignCorporateRuleSystem"/>.
/// </summary>
public sealed partial class SolreignCorporateLayerSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;

    private static readonly EntProtoId CorporateRule = "SolreignCorporate";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        // Preset rules have already been started at this point; add + start ours on top. Ignored if the
        // server has explicitly ignored this rule id.
        if (_gameTicker.IsIgnored(CorporateRule))
            return;

        // Some presets (SolreignAntagsOnSpawn, SolreignComplianceHunt) already list SolreignCorporate
        // in their own `rules:`, so StartGamePresetRules() has already added + started it by the time
        // RoundStartingEvent fires here. Without this guard we'd unconditionally spawn a SECOND,
        // independent SolreignCorporate rule entity on top of it every such round: a duplicate HR
        // "keynote" announcement at round start, a duplicate quarterly-earnings scoring tracker, and a
        // duplicate SubmitRoundStandings(...) call double-crediting the Season Ledger at round end with
        // the same standings. Confirmed via SolreignSecretRoundstartRegressionTest logs (two separate
        // SolreignCorporate rule entities both Added+Started in the same round under SolreignAntagsOnSpawn).
        if (_gameTicker.IsGameRuleAdded(CorporateRule))
            return;

        _gameTicker.StartGameRule(CorporateRule);
    }
}
