using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared._Solreign.Antags;
using Content.Server._Solreign.Antags;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Feeding (spec §4.2, anti-grief rule 1: "no forced biting... Blood comes from packs or the
///     consent verb. Period."). Two paths, both do-after gated, neither attack-shaped:
///     - A blood pack used on oneself (<see cref="UseInHandEvent"/>, no target needed).
///     - A willing donor's "offer a donation" verb, which the DONOR invokes on the vampire — the donor
///       is always <c>args.User</c>, never the vampire acting on someone else.
///     Both check <see cref="VampireThirstMath.CanFeed"/> at COMPLETION time (not just when offered),
///     since garlic/chapel state can change mid do-after.
/// </summary>
public sealed partial class SolreignVampireSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SunriseClauseSystem _sunrise = default!;

    private void InitializeFeeding()
    {
        SubscribeLocalEvent<SolreignBloodPackComponent, UseInHandEvent>(OnBloodPackUseInHand);
        SubscribeLocalEvent<SolreignBloodPackComponent, SolreignVampireBloodPackDoAfterEvent>(OnBloodPackDoAfter);

        SubscribeLocalEvent<SolreignVampireComponent, GetVerbsEvent<AlternativeVerb>>(AddDonationVerb);
        SubscribeLocalEvent<SolreignVampireComponent, SolreignVampireDonationDoAfterEvent>(OnDonationDoAfter);
    }

    /// <summary>A vampire drinks a Medbay blood pack on themselves.</summary>
    private void OnBloodPackUseInHand(Entity<SolreignBloodPackComponent> pack, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<SolreignVampireComponent>(args.User, out var vampire))
            return; // only a Nocturnal Acquisitions Specialist has any use for one of these

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, vampire.FeedDoAfterSeconds,
            new SolreignVampireBloodPackDoAfterEvent(), pack.Owner, target: args.User, used: pack.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            args.Handled = true;
    }

    private void OnBloodPackDoAfter(Entity<SolreignBloodPackComponent> pack, ref SolreignVampireBloodPackDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var vampireUid = args.User;
        if (!TryComp<SolreignVampireComponent>(vampireUid, out var vampire))
            return;

        var band = VampireThirstMath.Band(vampire.Thirst);
        if (!VampireThirstMath.CanFeed(band, vampire.GarlicNearby, vampire.InChapel))
        {
            _popup.PopupEntity(Loc.GetString("solreign-vampire-feed-blocked"), vampireUid, vampireUid, PopupType.SmallCaution);
            return;
        }

        var amount = pack.Comp.DrinkOverride ?? vampire.DrinkAmount;
        vampire.Thirst = VampireThirstMath.Drink(vampire.Thirst, amount);

        _popup.PopupEntity(Loc.GetString("solreign-vampire-feed-pack-success"), vampireUid, vampireUid, PopupType.Medium);
        QueueDel(pack.Owner);
        args.Handled = true;
    }

    /// <summary>
    ///     Offers the "offer a donation" verb on a vampire — always invoked BY the donor, never by the
    ///     vampire on someone else (anti-grief rule 1). Hidden entirely when the feeding gate is already
    ///     closed, so nobody is offered a verb that can only fail.
    /// </summary>
    private void AddDonationVerb(EntityUid uid, SolreignVampireComponent vampire, GetVerbsEvent<AlternativeVerb> args)
    {
        _sunrise.AddRitualVerb(uid, args, vampire.RitualDoAfterSeconds); // consolidated: one verb subscription per comp+event
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (args.User == uid)
            return; // a "willing donor" per spec §4.2 is someone else offering, not self-service

        var band = VampireThirstMath.Band(vampire.Thirst);
        if (!VampireThirstMath.CanFeed(band, vampire.GarlicNearby, vampire.InChapel))
            return;

        var donor = args.User;
        AlternativeVerb verb = new()
        {
            Text = Loc.GetString("solreign-vampire-donate-verb"),
            Priority = 1,
            Act = () => StartDonationDoAfter(donor, uid, vampire),
        };
        args.Verbs.Add(verb);
    }

    private void StartDonationDoAfter(EntityUid donor, EntityUid vampireUid, SolreignVampireComponent vampire)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, donor, vampire.FeedDoAfterSeconds,
            new SolreignVampireDonationDoAfterEvent(), vampireUid, target: vampireUid)
        {
            BreakOnMove = true,
            NeedHand = false, // offering an arm doesn't require a free hand
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDonationDoAfter(Entity<SolreignVampireComponent> ent, ref SolreignVampireDonationDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var donor = args.User;
        var vampire = ent.Comp;

        var band = VampireThirstMath.Band(vampire.Thirst);
        if (!VampireThirstMath.CanFeed(band, vampire.GarlicNearby, vampire.InChapel))
        {
            _popup.PopupEntity(Loc.GetString("solreign-vampire-feed-blocked"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        // Small, never-crits draw (spec §4.2) — TryModifyBloodLevel's own regulator caps it at the
        // donor's normal blood volume, so this can't overdraw even with a misconfigured amount.
        _bloodstream.TryModifyBloodLevel(donor, FixedPoint2.New(-vampire.DonorBloodDraw));
        vampire.Thirst = VampireThirstMath.Drink(vampire.Thirst, vampire.DrinkAmount);

        _popup.PopupEntity(Loc.GetString("solreign-vampire-donate-success-vampire"), ent.Owner, ent.Owner, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("solreign-vampire-donate-success-donor"), donor, donor, PopupType.Medium);

        args.Handled = true;
    }
}
