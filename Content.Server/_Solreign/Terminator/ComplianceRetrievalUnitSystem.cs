using Content.Server.Ghost.Roles.Components;
using Content.Shared.Emp;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Terminator;

/// <summary>
///     TERMINATOR SPIKE server logic for the Compliance Retrieval Unit. What this spike proves:
///     <list type="bullet">
///     <item>Target selection: while dormant, the unit periodically snapshots all player-controlled
///     humanoids into <see cref="FixationCandidate"/>s and picks ONE via the pure, unit-tested
///     <see cref="FixationRules.SelectTarget"/>.</item>
///     <item>Single-entity aggro: the fixation is enforced through upstream
///     <see cref="NpcFactionSystem"/> faction EXCEPTIONS (<c>AggroEntity</c>) — the unit sits in the
///     Passive faction and is hostile to exactly its target, never the crew at large. This is the
///     same lever upstream uses for per-entity grudges, and it feeds the stock hostile-targeting
///     utility queries without a custom HTN (full pursuit/drag compound = next wave).</item>
///     <item>EMP counter: an <see cref="EmpPulseEvent"/> staggers the unit (paralyze via
///     <see cref="SharedStunSystem"/>) and delays its next poll.</item>
///     <item>Retarget discipline: a dead, detained or departed target drops the fixation
///     (<see cref="FixationRules.ShouldRetarget"/>) and the unit re-polls.</item>
///     </list>
///
///     NEXT WAVE (out of spike scope, see the spec): pursuit/drag HTN compound + Compliance Desk
///     delivery, criminal-records/Corporate-Standing wiring for <c>WantedLevel</c>/<c>Demerits</c>,
///     cuffed/enforcement candidate flags, pilot briefing UI, Ledger entries on deliver/evade,
///     no-kill rails, and the periodic coarse "compliance ping" toward the target.
/// </summary>
public sealed partial class ComplianceRetrievalUnitSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    /// <summary>
    ///     When each player mob spawned into the shift, for the new-join grace window. Keyed by mob
    ///     (not session) so respawns restart the clock; cleared on round restart.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _shiftStart = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComplianceFixationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ComplianceFixationComponent, TakeGhostRoleEvent>(OnTakeGhostRole);
        SubscribeLocalEvent<ComplianceFixationComponent, EmpPulseEvent>(OnEmpPulse);

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnMapInit(Entity<ComplianceFixationComponent> ent, ref MapInitEvent args)
    {
        // Poll on the next Update tick; the unit may spawn before any eligible crew exists.
        ent.Comp.NextPoll = _timing.CurTime;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        _shiftStart[ev.Mob] = _timing.CurTime;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _shiftStart.Clear();
    }

    private void OnTakeGhostRole(Entity<ComplianceFixationComponent> ent, ref TakeGhostRoleEvent args)
    {
        // Pilot briefing popup/objective UI is next wave; for the spike the fixation is logged so
        // admins can audit who the unit was pointed at when a player took it over.
        var target = ent.Comp.Target is { } t ? ToPrettyString(t).ToString() : "no one yet (dormant)";
        Log.Info($"Compliance Retrieval Unit {ToPrettyString(ent)} taken by {args.Player.Name}; fixated on {target}.");
    }

    private void OnEmpPulse(Entity<ComplianceFixationComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true; // gets the stock EmpDisabled tag + visuals for the pulse duration

        ent.Comp.StaggeredUntil = FixationRules.StaggerUntil(_timing.CurTime, ent.Comp.EmpStagger);
        _stun.TryUpdateParalyzeDuration(ent, ent.Comp.EmpStagger);

        // A staggered unit also loses its polling cadence — EMP buys the crew real time.
        if (ent.Comp.NextPoll < ent.Comp.StaggeredUntil)
            ent.Comp.NextPoll = ent.Comp.StaggeredUntil;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ComplianceFixationComponent>();

        while (query.MoveNext(out var uid, out var fixation))
        {
            if (FixationRules.IsStaggered(curTime, fixation.StaggeredUntil))
                continue;

            // Drop a solved/lost fixation so the unit goes back to the pool.
            if (fixation.Target is { } current)
            {
                var alive = !TerminatingOrDeleted(current) && _mobState.IsAlive(current);
                // TODO(SOLREIGN-TERMINATOR next wave): real in-custody + off-station checks
                // (CuffableComponent state, station grid membership).
                if (FixationRules.ShouldRetarget(alive, targetInCustody: false, targetOnStation: !TerminatingOrDeleted(current)))
                {
                    if (!TerminatingOrDeleted(current))
                        _faction.DeAggroEntity(uid, current);

                    fixation.Target = null;
                    fixation.NextPoll = curTime + fixation.RetargetPoll;
                }

                continue;
            }

            if (curTime < fixation.NextPoll)
                continue;

            fixation.NextPoll = curTime + fixation.RetargetPoll;
            TrySelectTarget((uid, fixation), curTime);
        }
    }

    /// <summary>
    ///     Snapshots the crew into pure candidates and asks <see cref="FixationRules"/> for the one
    ///     target. On success, wires the fixation into upstream AI via a per-entity faction aggro
    ///     exception so stock hostile queries pick the target up.
    /// </summary>
    private void TrySelectTarget(Entity<ComplianceFixationComponent> unit, TimeSpan curTime)
    {
        var uids = new List<EntityUid>();
        var candidates = new List<FixationCandidate>();

        var crew = EntityQueryEnumerator<ActorComponent, HumanoidProfileComponent, MobStateComponent>();
        while (crew.MoveNext(out var mobUid, out _, out _, out var mobState))
        {
            if (mobUid == unit.Owner)
                continue;

            var minutes = _shiftStart.TryGetValue(mobUid, out var start)
                ? (float) (curTime - start).TotalMinutes
                : 0f;

            // TODO(SOLREIGN-TERMINATOR next wave): WantedLevel from criminal records,
            // Demerits from Corporate Standing, InCustody from cuffed state, Enforcement
            // from job department. Spike snapshots them neutral so selection reduces to
            // the (fully tested) shift-time + tie-roll path.
            uids.Add(mobUid);
            candidates.Add(new FixationCandidate(
                WantedLevel: 0,
                Demerits: 0,
                MinutesOnShift: minutes,
                Alive: _mobState.IsAlive(mobUid, mobState)));
        }

        var pick = FixationRules.SelectTarget(candidates, _random.NextDouble(), unit.Comp.GraceMinutes);
        if (pick < 0)
            return;

        var target = uids[pick];
        unit.Comp.Target = target;
        _faction.AggroEntity(unit.Owner, target);

        Log.Info($"Compliance Retrieval Unit {ToPrettyString(unit)} fixated on {ToPrettyString(target)}.");
    }
}
