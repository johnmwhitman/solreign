using Robust.Shared.Timing;

namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Advances every Nocturnal Acquisitions Specialist's thirst meter through the pure
///     <see cref="VampireThirstMath"/>, and (in the Environment/Feeding partials) drives detection
///     (coffin occupancy, garlic proximity, chapel ground), feeding do-afters, band-driven effects, and
///     the Sunrise Clause hookup. Spec: docs/specs/2026-07-11-werewolf-vampire-spec.md §4.
///
///     Update-loop shape follows the house pattern (<c>SolreignPeriodicEffectSystem</c>): coarse
///     per-entity tick timestamps compared against CurTime, no per-tick randomness or allocation.
///     Unlike the werewolf (see <c>SolreignWerewolfSystem.Update</c>), a vampire is never banished to a
///     paused map — nothing here polymorphs — so plain <c>EntityQueryEnumerator</c> is correct.
/// </summary>
public sealed partial class SolreignVampireSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignVampireComponent, ComponentStartup>(OnVampireStartup);

        InitializeEnvironment(); // container events (coffin) + speed-modifier refresh (Environment partial)
        InitializeFeeding();     // blood pack + donor consent do-afters (Feeding partial)

        // Cure reagent effect ("Circadian Realignment Tonic" — halves thirst accrual, a management tool
        // per spec §4.4, not a direct cure) and the chapel Sunrise Clause ritual are NOT wired from here:
        //   - the tonic is chemistry-registry YAML + an EntityEffect system
        //     (Content.Server/EntityEffects/Effects/…) — a task #12 follow-up, not this pass.
        //   - the Sunrise Clause is the shared chaplain ritual in Antags/SunriseClauseSystem.cs (spec
        //     §3.4/§4.4: "shared with vampire" the OTHER direction), which calls this system's
        //     AdvanceCure directly.
    }

    private void OnVampireStartup(Entity<SolreignVampireComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.LastThirstTick = _timing.CurTime;
    }

    /// <summary>
    ///     Advances Sunrise Clause progress by one completed ritual. At 100 the specialist resolves to
    ///     cured: component removed, memories kept, "Reformed Night Auditor" flair granted, Season
    ///     Ledger contribution recorded (spec §6, TODO(build)).
    /// </summary>
    public void AdvanceCure(Entity<SolreignVampireComponent> ent)
    {
        ent.Comp.CureProgress = Math.Clamp(ent.Comp.CureProgress + ent.Comp.CurePerRitual, 0d, 100d);

        if (VampireThirstMath.IsCured(ent.Comp.CureProgress))
        {
            // TODO(ledger, spec §6): "ending the round cured ... counts as an antag win; ... the
            // officiating chaplain earn[s] standing_total + 1 ('Donor Program Participation')." Not
            // wired here — SeasonLedger files are off-limits for this pass; the real hookup mirrors
            // SolreignCorporateRuleSystem's `_ledger.SubmitRoundStandings(...)` call and belongs to
            // whichever gamerule ends up owning this antag's round-end accounting
            // (SolreignVampireNightRuleSystem is the current candidate).
            RemCompDeferred<SolreignVampireComponent>(ent.Owner);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var vampires = EntityQueryEnumerator<SolreignVampireComponent>();
        while (vampires.MoveNext(out var uid, out var vampire))
        {
            var elapsed = now - vampire.LastThirstTick;
            if (elapsed.TotalSeconds < vampire.ThirstTickSeconds)
                continue;

            vampire.LastThirstTick = now;

            // InCoffin is maintained live by the container event handlers (Environment partial);
            // GarlicNearby/InChapel are proximity scans, cheap enough to redo once per (coarse) tick.
            RefreshEnvironment(uid, vampire);

            var previousBand = VampireThirstMath.Band(vampire.Thirst);

            vampire.Thirst = VampireThirstMath.Accumulate(
                vampire.Thirst,
                elapsed,
                vampire.ThirstPerMinute,
                vampire.InCoffin,
                vampire.CoffinRecoveryPerMinute,
                vampire.GarlicNearby);

            var band = VampireThirstMath.Band(vampire.Thirst);
            if (band != previousBand)
                OnBandChanged(uid, vampire, previousBand, band);
        }
    }

    /// <summary>
    ///     Presentation hook for band changes: delegates to the Environment partial for the
    ///     movement-speed refresh + Ravenous growl popup (spec §4.2). TODO(build): pallor appearance
    ///     data + HUD thirst alert tier for the owning client — visuals only, no gameplay gate depends
    ///     on them.
    /// </summary>
    private void OnBandChanged(EntityUid uid, SolreignVampireComponent vampire, ThirstBand previous, ThirstBand next)
    {
        OnBandChangedEnvironment(uid, vampire, next);
    }
}
