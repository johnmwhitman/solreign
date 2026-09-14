using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Server.Player;

namespace Content.Server._Solreign.Antags;

/// <summary>
///     Spreads antag turns across the roster so the same few players do not draw it every shift.
///
///     SR-W-036 shipped as arithmetic with no callers — the component/system pair computed streaks
///     that nothing ever read, so "the same three people always get antag" was never actually fixed.
///     This is the wiring: recency comes from the Season Ledger (persistent across rounds, which is
///     the only place it can meaningfully live), and <see cref="IsOnCooldown"/> is consulted by
///     AntagSelectionSystem.IsSessionValid.
///
///     LOW-POPULATION SAFETY IS THE WHOLE DESIGN. A naive "exclude everyone who was recently antag"
///     rule starves selection on a 3-player server: if two of three players drew antag last shift,
///     a hard exclusion can leave nobody eligible and the round ships with no antagonist at all.
///     So the exclusion set is CAPPED when it is built — we never exclude so many accounts that
///     fewer than <see cref="MinEligible"/> candidates remain, and the most-recent antags are
///     dropped first. The gate can therefore only ever *reorder* who draws antag, never prevent
///     antags from existing.
/// </summary>
public sealed partial class SolreignAntagRotationGate : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>Never shrink the candidate pool below this many accounts.</summary>
    private const int MinEligible = 2;

    /// <summary>Accounts on cooldown for the current round. Rebuilt at round start; empty = no gating.</summary>
    private readonly HashSet<Guid> _onCooldown = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _onCooldown.Clear();
    }

    private async void OnRoundStarting(RoundStartingEvent ev)
    {
        _onCooldown.Clear();

        if (!_cfg.GetCVar(CCVars.SolreignAntagRotationEnabled))
            return;

        var window = _cfg.GetCVar(CCVars.SolreignAntagRotationCooldownRounds);
        if (window <= 0)
            return;

        try
        {
            // Everyone whose last antag round falls inside the cooldown window.
            var recent = await _ledger.GetRecentAntagsAsync(ev.Id - window);
            // Cap against the CONNECTED PLAYER COUNT, never the round number: at round 107
            // a round-number budget would permit ~105 exclusions, i.e. no cap at all.
            ApplyCapped(recent, _players.PlayerCount);
        }
        catch (Exception e)
        {
            // Rotation is a fairness nicety. It must never be able to break round start, so a ledger
            // read failure leaves the set empty and selection behaves exactly as vanilla.
            Log.Warning($"antag rotation: ledger read failed ({e.GetType().Name}); gating disabled this round");
            _onCooldown.Clear();
        }
    }

    /// <summary>
    ///     Populate the cooldown set, capped so at least <see cref="MinEligible"/> accounts remain
    ///     selectable. Visible for testing.
    /// </summary>
    internal void ApplyCapped(IReadOnlySet<Guid> recentAntags, int knownCandidates)
    {
        _onCooldown.Clear();
        if (recentAntags.Count == 0)
            return;

        // How many we may exclude without starving selection.
        var budget = knownCandidates - MinEligible;
        if (budget <= 0)
            return;

        foreach (var guid in recentAntags.Take(budget))
            _onCooldown.Add(guid);
    }

    /// <summary>
    ///     True when this account drew antag recently enough to be skipped this round. Synchronous
    ///     and allocation-free by design: it is called from inside antag selection.
    /// </summary>
    public bool IsOnCooldown(ICommonSession? session)
    {
        return session is not null && _onCooldown.Contains(session.UserId.UserId);
    }

    /// <summary>Accounts currently gated. Visible for testing.</summary>
    internal IReadOnlySet<Guid> CooldownSet => _onCooldown;
}
