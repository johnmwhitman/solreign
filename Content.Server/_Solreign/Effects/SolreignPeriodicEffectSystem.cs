using Content.Server.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Effects;

/// <summary>
///     Ticks every <see cref="SolreignPeriodicEffectComponent"/> and, when its precomputed
///     <c>NextFireTime</c> elapses, spawns the configured effect entity at the owner, plays the configured
///     sound (PVS), and shows the configured popup — then rolls the next fire time.
///
///     Update-loop shape follows upstream <c>EmitSoundSystem</c> (Content.Server/Sound): an
///     <c>EntityQueryEnumerator</c> (which skips paused entities) plus a single CurTime comparison per
///     entity per tick. Randomness only happens when (re)scheduling, never per-tick.
/// </summary>
public sealed partial class SolreignPeriodicEffectSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignPeriodicEffectComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SolreignPeriodicEffectComponent> entity, ref MapInitEvent args)
    {
        Reschedule(entity.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SolreignPeriodicEffectComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Component added without a map-init (VV / admin AddComp): schedule instead of firing instantly.
            if (comp.NextFireTime == TimeSpan.Zero)
            {
                Reschedule(comp);
                continue;
            }

            if (_timing.CurTime < comp.NextFireTime)
                continue;

            Fire(uid, comp);
            Reschedule(comp);
        }
    }

    private void Fire(EntityUid uid, SolreignPeriodicEffectComponent comp)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (comp.EffectPrototype is { } effect)
            Spawn(effect, Transform(uid).Coordinates);

        if (comp.SoundCollection is { } sound)
            _audio.PlayPvs(sound, uid);

        if (comp.PopupText is { } popup)
            _popup.PopupEntity(Loc.GetString(popup), uid);
    }

    private void Reschedule(SolreignPeriodicEffectComponent comp)
    {
        comp.NextFireTime = PeriodicEffectTiming.NextFireTime(
            _timing.CurTime,
            comp.MinIntervalSeconds,
            comp.MaxIntervalSeconds,
            _random.NextDouble());
    }
}
