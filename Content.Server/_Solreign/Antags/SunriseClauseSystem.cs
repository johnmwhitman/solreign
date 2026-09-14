using Content.Shared.Bible.Components;
using Content.Server._Solreign.Antags.Vampire;
using Content.Server._Solreign.Antags.Werewolf;
using Content.Shared.DoAfter;
using Content.Shared._Solreign.Antags;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Serialization;

namespace Content.Server._Solreign.Antags;

/// <summary>
///     The Sunrise Clause — the chaplain-led cure ritual shared by BOTH event-night antags (spec §3.4:
///     "the Sunrise Clause ritual (shared with vampire, §4.4) also sets the cure flag"; spec §4.4:
///     "chaplain-led ritual (bible-interaction shape, do-after, witnesses welcome)").
///
///     Deliberately does NOT hook into upstream <c>BibleComponent</c>/<c>BibleSystem</c> — that would
///     mean subscribing on a component this fork doesn't own and duplicating/interfering with its own
///     heal logic. Instead this is <c>BibleSystem.OnAfterInteract</c>'s "sacred item, cooldown-gated
///     touch effect" IDIOM (spec §2.3), reapplied as its own verb gated on the same ordination marker
///     BibleSystem already reads (<see cref="BibleUserComponent"/>) — same doctrine, zero coupling to
///     Bible's own component/event surface.
/// </summary>
public sealed partial class SunriseClauseSystem : EntitySystem
{
    [Dependency] private SolreignWerewolfSystem _werewolf = default!;
    [Dependency] private SolreignVampireSystem _vampire = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignWerewolfComponent, GetVerbsEvent<AlternativeVerb>>(AddRitualVerb);

        SubscribeLocalEvent<SolreignWerewolfComponent, SolreignSunriseRitualDoAfterEvent>(OnWerewolfRitualDoAfter);
        SubscribeLocalEvent<SolreignVampireComponent, SolreignSunriseRitualDoAfterEvent>(OnVampireRitualDoAfter);
    }

    private void AddRitualVerb(EntityUid uid, SolreignWerewolfComponent component, GetVerbsEvent<AlternativeVerb> args)
        => AddRitualVerb(uid, args, component.RitualDoAfterSeconds);


    /// <summary>Chaplain-gated (spec: "bible-interaction shape") — reuses the BibleSystem ordination marker.</summary>
    public void AddRitualVerb(EntityUid uid, GetVerbsEvent<AlternativeVerb> args, float ritualSeconds)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!HasComp<BibleUserComponent>(args.User))
            return;

        var chaplain = args.User;
        AlternativeVerb verb = new()
        {
            Text = Loc.GetString("solreign-sunrise-clause-verb"),
            Priority = 0,
            Act = () =>
            {
                var doAfterArgs = new DoAfterArgs(EntityManager, chaplain, ritualSeconds,
                    new SolreignSunriseRitualDoAfterEvent(), uid, target: uid)
                {
                    BreakOnMove = true,
                    NeedHand = true,
                };

                _doAfter.TryStartDoAfter(doAfterArgs);
            },
        };
        args.Verbs.Add(verb);
    }

    private void OnWerewolfRitualDoAfter(Entity<SolreignWerewolfComponent> ent, ref SolreignSunriseRitualDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        _werewolf.ApplyCure(ent);
        _popup.PopupEntity(Loc.GetString("solreign-sunrise-clause-success"), ent.Owner, PopupType.Medium);
        args.Handled = true;
    }

    private void OnVampireRitualDoAfter(Entity<SolreignVampireComponent> ent, ref SolreignSunriseRitualDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        _vampire.AdvanceCure(ent);
        _popup.PopupEntity(Loc.GetString("solreign-sunrise-clause-success"), ent.Owner, PopupType.Medium);
        args.Handled = true;
    }
}

/// <summary>
///     Raised when the chaplain finishes a Sunrise Clause do-after on an antag (spec §3.4/§4.4). Shared
///     event type — <see cref="SunriseClauseSystem"/> subscribes it once per antag component so each
///     handler independently no-ops if its own component isn't the one present.
/// </summary>
