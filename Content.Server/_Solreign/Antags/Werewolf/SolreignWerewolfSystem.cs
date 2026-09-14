using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Ticks the pure <see cref="WerewolfStateMachine"/> for every Lunar-Reactive employee, expires
///     Moon-Touched marks, and (in the <c>SolreignWerewolfSystem.Transform.cs</c> partial) drives
///     the presentation layer: polymorph body swap, the maul, and Moon-Touched cosmetics. Spec:
///     docs/specs/2026-07-11-werewolf-vampire-spec.md §3.
///
///     Update-loop shape follows the house pattern (<c>SolreignPeriodicEffectSystem</c>): CurTime
///     comparisons, no per-tick allocation. ONE deliberate deviation from that house pattern: the main
///     tick uses <see cref="AllEntityQuery{T}"/>, NOT <c>EntityQueryEnumerator</c> — see the comment on
///     <see cref="Update"/> for why paused entities must NOT be skipped here.
/// </summary>
public sealed partial class SolreignWerewolfSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    ///     Whether a Full Moon Window is currently declared. Driven by
    ///     <see cref="SolreignFullMoonWindowRuleSystem"/> (a real event-night <c>GameRuleSystem</c>,
    ///     Resources/Prototypes/_Solreign/GameRules/full_moon_window.yml) in production, and directly
    ///     by tests/admin commands otherwise — the flag itself doesn't care who sets it.
    /// </summary>
    [ViewVariables]
    public bool MoonWindowActive;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignWerewolfComponent, ComponentStartup>(OnWerewolfStartup);

        // Maul path lives in the Transform partial (SolreignWerewolfSystem.Transform.cs) alongside the
        // polymorph wiring it depends on (SolreignWolfFormComponent back-reference).
        SubscribeLocalEvent<SolreignWolfFormComponent, MeleeHitEvent>(OnMeleeHit);

        // Moon-Touched cosmetic slow. Server-only component (spec §3 doc comment), so this refresh is
        // server-authoritative only — same tradeoff the component's own doc already commits to.
        SubscribeLocalEvent<SolreignMoonTouchedComponent, RefreshMovementSpeedModifiersEvent>(OnMoonTouchedRefreshSpeed);

        // Cure reagent effect ("Follicle Stabilizer Draught") and the chapel Sunrise Clause ritual both
        // resolve through the SAME entry point: ApplyCure(uid). Neither is wired from here:
        //   - the reagent is chemistry-registry YAML + an EntityEffect system
        //     (Content.Server/EntityEffects/Effects/…) — a task #12 follow-up, not this pass.
        //   - the Sunrise Clause is the shared chaplain ritual in Antags/SunriseClauseSystem.cs (spec
        //     §3.4/§4.4: "shared with vampire"), which calls this system's ApplyCure directly.
        // No subscription is needed on this end for either.
    }

    private void OnWerewolfStartup(Entity<SolreignWerewolfComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.State = WerewolfState.Dormant;
        ent.Comp.StateEnteredAt = _timing.CurTime;
    }

    /// <summary>
    ///     Administers a cure. The state machine resolves it at the next safe boundary: instantly if
    ///     Dormant, at the end of the visible Waning window otherwise (invariant, spec §3.4).
    /// </summary>
    public void ApplyCure(Entity<SolreignWerewolfComponent> ent)
    {
        ent.Comp.CureApplied = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // Deliberately AllEntityQuery, NOT EntityQueryEnumerator: PolymorphSystem banishes the ORIGINAL
        // (human) entity — the one carrying SolreignWerewolfComponent — to a paused holding map for the
        // whole Transformed/Waning episode (PolymorphSystem.EnsurePausedMap sets the map paused, which
        // cascades EntityPaused=true to everything parented under it). EntityQueryEnumerator silently
        // skips paused entities, so using it here would freeze the state machine the instant a wolf
        // transforms — Transformed would never elapse its cap, Waning would never resolve, the episode
        // would never end. AllEntityQuery does not filter on pause, so the clock keeps moving regardless
        // of where PolymorphSystem has parked the body.
        var werewolves = AllEntityQuery<SolreignWerewolfComponent>();
        while (werewolves.MoveNext(out var uid, out var wolf))
        {
            var timings = new WerewolfTimings(
                TimeSpan.FromSeconds(wolf.StirringSeconds),
                TimeSpan.FromSeconds(wolf.TransformedSeconds),
                TimeSpan.FromSeconds(wolf.WaningSeconds));

            var next = WerewolfStateMachine.Next(
                wolf.State, now, wolf.StateEnteredAt, MoonWindowActive, wolf.CureApplied, in timings);

            if (next == wolf.State)
                continue;

            var previous = wolf.State;
            wolf.State = next;
            wolf.StateEnteredAt = now;
            OnStateChanged(uid, wolf, previous, next);
        }

        // Moon-Touched marks politely excuse themselves. (Ordinary crew members, never banished to a
        // paused map, so EntityQueryEnumerator's pause-skip is fine here — unlike the loop above.)
        var touched = EntityQueryEnumerator<SolreignMoonTouchedComponent>();
        while (touched.MoveNext(out var uid, out var mark))
        {
            if (now < mark.ExpiresAt)
                continue;

            RemCompDeferred<SolreignMoonTouchedComponent>(uid);
            _movementSpeed.RefreshMovementSpeedModifiers(uid); // clear the cosmetic slow immediately
        }
    }

    /// <summary>
    ///     Presentation hook for phase changes. Body-swap side effects (polymorph in/out, maul-capable
    ///     marker, popups) live in the Transform partial (ZombieSystem.Transform.cs idiom) so this file
    ///     stays pure ticking + wiring. See <see cref="OnEnterStirring"/>, <see cref="OnEnterTransformed"/>,
    ///     <see cref="OnEnterWaning"/>, <see cref="OnEnterDormant"/> in SolreignWerewolfSystem.Transform.cs.
    /// </summary>
    private void OnStateChanged(EntityUid uid, SolreignWerewolfComponent wolf, WerewolfState previous, WerewolfState next)
    {
        switch (next)
        {
            case WerewolfState.Stirring:
                OnEnterStirring(uid, wolf);
                break;
            case WerewolfState.Transformed:
                OnEnterTransformed(uid, wolf);
                break;
            case WerewolfState.Waning:
                OnEnterWaning(uid, wolf);
                break;
            case WerewolfState.Dormant:
                OnEnterDormant(uid, wolf);
                break;
            case WerewolfState.Cured:
                OnEnterCured(uid, wolf);
                break;
        }
    }

    /// <summary>
    ///     TODO(ledger, spec §6): on a clean cure/revert, this is the werewolf's Season Ledger contact
    ///     point. Per spec: "completing an episode without a single Medbay visit *caused* by you
    ///     (trivially true — mauls do no damage) and reverting/curing cleanly counts as an antag win
    ///     (antag_wins + 1); each colleague who administered a cure earns standing_total + 1 ('Wellness
    ///     Response Commendation')." The actual submission is NOT wired here: SeasonLedgerSystem is
    ///     off-limits for this pass (do not edit SeasonLedger files), and the real hookup — mirroring
    ///     SolreignCorporateRuleSystem's `_ledger.SubmitRoundStandings(...)` call at
    ///     AppendRoundEndText — belongs to whichever gamerule ultimately owns this antag's round-end
    ///     accounting (SolreignFullMoonWindowRuleSystem is the current candidate: it already knows which
    ///     entity it drafted and can snapshot a clean-episode flag from here without touching the ledger
    ///     store itself).
    /// </summary>
    private void OnEnterCured(EntityUid uid, SolreignWerewolfComponent wolf)
    {
        // The state machine only reaches Cured after any in-flight polymorph has already been reverted
        // by OnEnterDormant/OnEnterWaning's revert-on-exit path (Waning is the visible revert window;
        // Cured is reached either directly from Dormant, or from Waning once it elapses). Nothing left
        // to un-transform here — just the ledger comment above and component teardown.
        RemCompDeferred<SolreignWerewolfComponent>(uid);
    }
}
