using Content.Server._Solreign.Providence;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Player;

namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Behavior for "The Auditor Prime" — see <see cref="SolreignAuditorPrimeRuleComponent"/> for the
///     event summary and Resources/Prototypes/_Solreign/GameRules/minibosses.yml for the prototype.
///     Admin-invocable today (<c>addgamerule SolreignAuditorPrime</c>); not wired into any random event
///     table, same posture as the Season 1 beats and the werewolf/vampire event-night rules.
///
///     Systems touched (binding-rule count): <see cref="MobStateSystem"/> (alive-crew headcount for the
///     population gate) + <c>StationSystem</c>/<c>ChatSystem</c> (inherited from
///     <see cref="StationEventSystem{T}"/>: station scoping, spawn-tile selection, arrival
///     announcement) + <c>ProvidenceVoiceSystem</c> (the event_audit voice line, added alongside the
///     arrival announcement — Wave: Providence Playback Wiring).
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this
///     wave, same idiom as <c>SolreignLedgerDiscrepancyRule</c>): the actual per-kill trace lives on
///     <see cref="SolreignMiniBossComponent"/>/<see cref="SolreignMiniBossSystem"/>, fired when the
///     spawned boss dies, not here at spawn time.
/// </summary>
public sealed partial class SolreignAuditorPrimeRule : StationEventSystem<SolreignAuditorPrimeRuleComponent>
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;

    protected override void Started(EntityUid uid, SolreignAuditorPrimeRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Spawned)
            return;

        if (!TryGetRandomStation(out var station))
        {
            Sawmill.Info("SolreignAuditorPrime: no event-eligible station found; rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        var alive = CountAliveOnStation(station.Value);
        if (!MiniBossPopulationGate.InBounds(alive, component.MinPopulation, component.MaxPopulation))
        {
            Sawmill.Info($"SolreignAuditorPrime: population gate failed ({alive} alive; needs >= {component.MinPopulation}" +
                (component.MaxPopulation > 0 ? $" and <= {component.MaxPopulation}" : string.Empty) +
                "); rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        if (!TryComp<StationDataComponent>(station.Value, out var stationData) ||
            !TryFindRandomTileOnStation((station.Value, stationData), out _, out _, out var coords))
        {
            Sawmill.Warning("SolreignAuditorPrime: no valid spawn tile found on station; rule ending without spawn.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        Spawn(component.BossPrototype, coords);
        component.Spawned = true;

        ChatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-auditor-prime-arrival"),
            SolreignMiniBossAudit.Sender,
            playSound: true,
            colorOverride: SolreignMiniBossAudit.Color);

        // Providence's corporate-audit line, on top of the arrival stinger above (not a replacement —
        // the audit event keeps its own sting; Providence adds the voice on top of it).
        _providence.PlayLine(ProvidenceLineCategory.EventAudit);

        ForceEndSelf(uid, gameRule);
    }

    /// <summary>Alive, player-controlled crew count on <paramref name="station"/> — same idiom as
    /// <c>SolreignLedgerDiscrepancyRule.SpawnClue</c>'s candidate gathering.</summary>
    private int CountAliveOnStation(EntityUid station)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out _, out var mobState, out var xform))
        {
            if (StationSystem.GetOwningStation(candidate, xform) != station)
                continue;

            if (!_mobState.IsAlive(candidate, mobState))
                continue;

            count++;
        }

        return count;
    }
}
