using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Detection + presentation layer for the thirst meter (spec §4.2-4.3, build-pass estimate table:
///     "Coffin entry/exit detection + regen (container events) ~80" and "Garlic ward proximity +
///     chapel-area detection ~100"). The meter arithmetic itself stays in the pure
///     <see cref="VampireThirstMath"/>; this partial only supplies the situational flags it's fed and
///     reacts to band changes.
/// </summary>
public sealed partial class SolreignVampireSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>
    ///     Generous first-pass scan radius for garlic/chapel lookups, BEFORE the per-marker Radius gate
    ///     narrows it down. Coarse on purpose (spec §4.2 house doctrine: "thirst is a slow dial, not
    ///     physics") — this only needs to run once per <see cref="SolreignVampireComponent.ThirstTickSeconds"/>.
    /// </summary>
    private const float EnvironmentScanRadius = 12f;

    private void InitializeEnvironment()
    {
        // Directed at the entity being inserted/removed (upstream doc comment on both messages), which
        // is the vampire itself here — exactly the idiom HealthAnalyzerSystem uses for its own
        // "turn off when boxed up" container hook.
        SubscribeLocalEvent<SolreignVampireComponent, EntGotInsertedIntoContainerMessage>(OnInsertedIntoContainer);
        SubscribeLocalEvent<SolreignVampireComponent, EntGotRemovedFromContainerMessage>(OnRemovedFromContainer);

        SubscribeLocalEvent<SolreignVampireComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    /// <summary>Entering ANY container sets InCoffin only if that container's owner is an Executive Recharge Pod.</summary>
    private void OnInsertedIntoContainer(Entity<SolreignVampireComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        ent.Comp.InCoffin = HasComp<SolreignCoffinComponent>(args.Container.Owner);
    }

    /// <summary>Leaving the pod (specifically) clears InCoffin — leaving some OTHER container leaves it alone.</summary>
    private void OnRemovedFromContainer(Entity<SolreignVampireComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (HasComp<SolreignCoffinComponent>(args.Container.Owner))
            ent.Comp.InCoffin = false;
    }

    /// <summary>
    ///     Refreshes <see cref="SolreignVampireComponent.GarlicNearby"/> and
    ///     <see cref="SolreignVampireComponent.InChapel"/> from a proximity scan against
    ///     <see cref="SolreignGarlicWardComponent"/>/<see cref="SolreignChapelGroundComponent"/> markers.
    ///     Called once per thirst tick, right before <see cref="VampireThirstMath.Accumulate"/> reads
    ///     them — coarse and cheap on purpose (spec doctrine: thirst is a slow dial).
    /// </summary>
    private void RefreshEnvironment(EntityUid uid, SolreignVampireComponent vampire)
    {
        var coords = Transform(uid).Coordinates;

        vampire.GarlicNearby = false;
        var wards = _lookup.GetEntitiesInRange<SolreignGarlicWardComponent>(coords, EnvironmentScanRadius);
        foreach (var ward in wards)
        {
            if (_transform.InRange(coords, Transform(ward.Owner).Coordinates, ward.Comp.Radius))
            {
                vampire.GarlicNearby = true;
                break;
            }
        }

        vampire.InChapel = false;
        var chapelMarkers = _lookup.GetEntitiesInRange<SolreignChapelGroundComponent>(coords, EnvironmentScanRadius);
        foreach (var marker in chapelMarkers)
        {
            if (_transform.InRange(coords, Transform(marker.Owner).Coordinates, marker.Comp.Radius))
            {
                vampire.InChapel = true;
                break;
            }
        }
    }

    /// <summary>Sated gets a minor boost, Ravenous is slowed — hunger only ever weakens (spec §4.5 rule 2).</summary>
    private void OnRefreshSpeed(Entity<SolreignVampireComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var mult = (float) VampireThirstMath.SpeedMultiplier(VampireThirstMath.Band(ent.Comp.Thirst));
        args.ModifySpeed(mult, mult);
    }

    /// <summary>
    ///     Presentation for a band change: refresh the speed modifier immediately (don't wait for some
    ///     unrelated trigger to notice), and — Ravenous only — the involuntary stomach-growl popup that
    ///     broadcasts position (spec §4.2: "loud involuntary stomach-growl popups broadcast position").
    ///     <c>PopupEntity</c> with no filter defaults to PVS-range broadcast, which is exactly "broadcasts
    ///     position" — nobody needs line of sight, just proximity.
    /// </summary>
    private void OnBandChangedEnvironment(EntityUid uid, SolreignVampireComponent vampire, ThirstBand next)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);

        if (next == ThirstBand.Ravenous)
            _popup.PopupEntity(Loc.GetString("solreign-vampire-ravenous-growl"), uid, PopupType.MediumCaution);
    }
}
