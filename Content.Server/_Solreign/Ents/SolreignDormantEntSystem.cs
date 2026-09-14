using Content.Server._Solreign.Effects;
using Content.Server.Popups;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Ents;

/// <summary>
///     Ticks every <see cref="SolreignDormantEntComponent"/> and, on its precomputed
///     <c>NextCheckTime</c>, rolls a low-probability chance (<see cref="EntAwakeningRules"/>). On a
///     hit the dormant tree plays a creak sound, shows a "the tree stirs" popup, is deleted, and a
///     takeover-available <see cref="SolreignDormantEntComponent.AwakenedPrototype"/> Ent is spawned
///     in its place. On a miss it just reschedules the next roll.
///
///     Update-loop shape and scheduling reuse <see cref="SolreignPeriodicEffectSystem"/> /
///     <see cref="PeriodicEffectTiming"/> wholesale (EntityQueryEnumerator + single CurTime
///     comparison per entity per tick, randomness only at (re)schedule time) — this is the same
///     primitive with an extra probability gate and a real state change instead of a cosmetic one.
/// </summary>
public sealed partial class SolreignDormantEntSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignDormantEntComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SolreignDormantEntComponent> entity, ref MapInitEvent args)
    {
        Reschedule(entity.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SolreignDormantEntComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Component added without a map-init (VV / admin AddComp): schedule instead of rolling instantly.
            if (comp.NextCheckTime == TimeSpan.Zero)
            {
                Reschedule(comp);
                continue;
            }

            if (_timing.CurTime < comp.NextCheckTime)
                continue;

            if (EntAwakeningRules.ShouldAwaken(_random.NextDouble(), comp.AwakenChance))
            {
                Awaken(uid, comp);
                continue; // uid is queued for deletion; nothing left to reschedule on it
            }

            Reschedule(comp);
        }
    }

    private void Awaken(EntityUid uid, SolreignDormantEntComponent comp)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var coordinates = Transform(uid).Coordinates;

        if (comp.CreakSound != null)
            _audio.PlayPvs(comp.CreakSound, uid);

        if (comp.AwakenPopup != null)
            // LargeCaution, not the Small default: this is meant to startle everyone nearby who
            // just watched a decorative tree wake up (Phase2 A4 game-feel sweep — the sound already
            // played above, but a bigger popup sells the scare the sound alone can't).
            _popup.PopupEntity(Loc.GetString(comp.AwakenPopup), uid, PopupType.LargeCaution);

        Spawn(comp.AwakenedPrototype, coordinates);
        QueueDel(uid);
    }

    private void Reschedule(SolreignDormantEntComponent comp)
    {
        comp.NextCheckTime = PeriodicEffectTiming.NextFireTime(
            _timing.CurTime,
            comp.MinCheckIntervalSeconds,
            comp.MaxCheckIntervalSeconds,
            _random.NextDouble());
    }
}
