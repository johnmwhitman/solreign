using Content.Server._Solreign.Providence;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Solreign.HotPotato;
using Content.Shared.Flash;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Server._Solreign.HotPotato;

/// <summary>
///     Server half of the Mandatory Team-Building Exercise: runs the fuse clock and the
///     accelerating beep, hands the exercise off when its holder bumps into a living humanoid
///     colleague (1s anti-ping-pong cooldown, fuse never resets), and concludes the exercise with
///     a small, mostly-flash-and-knockdown detonation.
///
///     Collision transfer works via <see cref="SolreignHotPotatoHolderComponent"/>: a held item is
///     inside a container and never collides itself, so we tag the holder and listen for THEIR
///     StartCollideEvent (same shape as upstream StunOnCollideSystem's collision handling).
/// </summary>
public sealed partial class SolreignHotPotatoSystem : SharedSolreignHotPotatoSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private SharedFlashSystem _flash = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignHotPotatoComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<SolreignHotPotatoHolderComponent, StartCollideEvent>(OnHolderCollide);
    }

    /// <summary>
    /// Track the current holder so their collisions can pass the exercise along.
    /// (Virtual hook from the shared equip subscription — the equip event itself is subscribed
    /// exactly once, in the shared half.)
    /// </summary>
    protected override void OnPotatoEquippedHand(Entity<SolreignHotPotatoComponent> ent, EntityUid user)
    {
        var holder = EnsureComp<SolreignHotPotatoHolderComponent>(user);
        holder.Potato = ent.Owner;
    }

    /// <summary>
    /// Arm-time only (never on hand-offs, see the shared half's guard): Providence's hot_potato
    /// line, once, station-wide — distinct from the per-tick accelerating beep on the item itself.
    /// </summary>
    protected override void OnPotatoArmed(EntityUid potato)
    {
        _providence.PlayLine(ProvidenceLineCategory.HotPotato);
    }

    private void OnGotUnequippedHand(Entity<SolreignHotPotatoComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (TryComp<SolreignHotPotatoHolderComponent>(args.User, out var holder) && holder.Potato == ent.Owner)
            RemComp<SolreignHotPotatoHolderComponent>(args.User);
    }

    /// <summary>
    /// The holder bumped into something. If it's a living humanoid colleague with hands and the
    /// transfer cooldown has elapsed, the exercise is theirs now. The fuse does not reset.
    /// </summary>
    private void OnHolderCollide(Entity<SolreignHotPotatoHolderComponent> ent, ref StartCollideEvent args)
    {
        if (!TryComp<SolreignHotPotatoComponent>(ent.Comp.Potato, out var potato) || !potato.Armed)
            return;

        if (!HotPotatoFuseMath.CanTransfer(Timing.CurTime, potato.NextTransferAllowed))
            return;

        var target = args.OtherEntity;
        if (target == ent.Owner)
            return;

        // Only living humanoid players get voluntold. Corpses, crits, mice and vending machines
        // are exempt from this quarter's programming.
        if (!HasComp<HumanoidProfileComponent>(target))
            return;

        if (!_mobState.IsAlive(target))
            return;

        if (!TryComp<HandsComponent>(target, out var hands))
            return;

        if (_hands.IsHolding((target, hands), ent.Comp.Potato, out _))
            return;

        // Briefly open the no-drop gate so the forced pickup can pull the exercise out of the
        // old holder's hands (mirrors upstream SharedHotPotatoSystem.OnMeleeHit).
        var potatoUid = ent.Comp.Potato;
        potato.CanTransfer = true;

        if (_hands.TryForcePickupAnyHand(target, potatoUid, checkActionBlocker: false, handsComp: hands))
        {
            potato.NextTransferAllowed = HotPotatoFuseMath.NextTransferTime(Timing.CurTime, potato.TransferCooldown);

            _popup.PopupEntity(
                Loc.GetString("solreign-hot-potato-passed",
                    ("from", Identity.Entity(ent.Owner, EntityManager)),
                    ("to", Identity.Entity(target, EntityManager))),
                potatoUid,
                PopupType.MediumCaution);
        }

        potato.CanTransfer = false;
        Dirty(potatoUid, potato);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = Timing.CurTime;
        var query = EntityQueryEnumerator<SolreignHotPotatoComponent>();
        while (query.MoveNext(out var uid, out var potato))
        {
            if (!potato.Armed)
                continue;

            // Accelerating beep: interval shrinks linearly as the deadline approaches
            // (scheduled PlayPvs, same shape as TriggerSystem.UpdateTimer).
            if (potato.BeepSound != null && potato.NextBeep <= curTime)
            {
                _audio.PlayPvs(potato.BeepSound, uid);
                var remaining = HotPotatoFuseMath.Remaining(curTime, potato.DetonateAt);
                potato.NextBeep = curTime + HotPotatoFuseMath.BeepInterval(
                    remaining, potato.FuseDuration, potato.MinBeepInterval, potato.MaxBeepInterval);
            }

            if (potato.DetonateAt <= curTime)
                Detonate((uid, potato));
        }
    }

    /// <summary>
    /// The exercise concludes. Small PG bang: a flash, a shove, and a permanent record entry.
    /// </summary>
    private void Detonate(Entity<SolreignHotPotatoComponent> ent)
    {
        // SOLREIGN LEDGER INTEGRATION POINT (comment only — SeasonLedgerSystem is owned by the
        // Ledger team; do NOT wire from this file without their sign-off):
        //   SeasonLedgerSystem (Content.Server/_Solreign/SeasonLedger) should record here who was
        //   holding the exercise at maturity — e.g. a "Team Player of the Quarter" stat / title
        //   credit for the final holder, keyed the same way as round-end results.

        _flash.FlashArea(ent.Owner, null, ent.Comp.FlashRange, ent.Comp.FlashDuration);

        _explosion.QueueExplosion(
            ent.Owner,
            ent.Comp.ExplosionType,
            ent.Comp.TotalIntensity,
            ent.Comp.IntensitySlope,
            ent.Comp.MaxTileIntensity,
            canCreateVacuum: false);

        // Disarm before the queued delete lands so a stray tick can't detonate twice,
        // and use QueueDel: we're inside an EntityQueryEnumerator over this component.
        ent.Comp.Armed = false;
        Dirty(ent);
        QueueDel(ent.Owner);
    }
}
