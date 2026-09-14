using Content.Server._Solreign.Antags.Vampire;
using Content.Server._Solreign.Antags.Werewolf;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Server logic for <see cref="SolreignStaticReceiverComponent"/> — see that component's doc
///     comment for the "paying off existing flavor text" motivation, and
///     <see cref="SolreignStaticReceiverRules"/> for the full round-integrity signal model
///     (orchestrator review, 2026-07-16: the first draft was a deterministic binary antag detector —
///     an accidental wallhack against hidden-identity rounds. This version is atmosphere, not radar).
///
///     Ambiguous sources — a scan counts "something nearby" if ANY of these is in range, so a
///     crackle never uniquely implies an antagonist:
///       * <see cref="SolreignVampireComponent"/> / <see cref="SolreignWerewolfComponent"/> /
///         <see cref="SolreignMoonTouchedComponent"/> — event-night entities;
///       * <see cref="XenoArtifactComponent"/> — ordinary science/salvage xenoartifacts;
///       * <see cref="SolreignChapelGroundComponent"/> — consecrated-ground markers;
///       * <see cref="SolreignCoffinComponent"/> — recharge-pod props.
///     All checks are presence-only <c>GetEntitiesInRange&lt;T&gt;</c> lookups (the same scan idiom
///     as <c>SolreignVampireSystem.Environment.cs</c>) — this system owns none of these components
///     and never reads their fields, which is why the <c>[Access]</c> boundaries are respected.
///
///     The crackle decision and text pick are deterministic per (item, round, cooldown-window) via
///     <see cref="SolreignStaticReceiverRules.Roll"/> — re-using the item inside one window re-yields
///     the same answer, so the noise can't be averaged away by spamming.
///
///     Gated by <see cref="CCVars.SolreignDelightStaticReceiverEnabled"/> (default TRUE): off makes
///     the item completely inert on use (no popup at all), same "no non-sequitur when the feature is
///     off" reasoning as <c>ProvidenceCommiserationSystem</c>'s doc comment, rather than always
///     reporting calm regardless of what's actually nearby.
/// </summary>
public sealed partial class SolreignStaticReceiverSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>Salt for the crackle-or-calm roll — independent of the text-variant roll below.</summary>
    private const uint CrackleSalt = 0x5EA51DE1u;

    /// <summary>Salt for the crackle text-variant roll.</summary>
    private const uint VariantSalt = 0xC0FFEE42u;

    /// <summary>
    ///     Crackle lines by <see cref="SolreignStaticReceiverRules.CrackleVariant"/> index: two
    ///     common variants plus the rare "almost-words" line. All deliberately read as atmosphere —
    ///     none of them names a direction, a distance, or a kind of source.
    /// </summary>
    private static readonly string[] CrackleKeys =
    {
        "solreign-static-receiver-crackle-faint",
        "solreign-static-receiver-crackle-hum",
        "solreign-static-receiver-crackle-words",
    };

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignDelightStaticReceiverEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<SolreignStaticReceiverComponent, UseInHandEvent>(OnUse);
    }

    private void OnUse(Entity<SolreignStaticReceiverComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !_enabled)
            return;

        var comp = ent.Comp;
        var curTime = _timing.CurTime;

        if (curTime < comp.NextScanTime)
        {
            _popup.PopupEntity(Loc.GetString("solreign-static-receiver-recalibrating"), args.User, args.User);
            return;
        }

        args.Handled = true;
        comp.NextScanTime = curTime + (comp.ScanCooldown > TimeSpan.Zero ? comp.ScanCooldown : TimeSpan.Zero);

        var coords = Transform(ent.Owner).Coordinates;
        var r = comp.ScanRadius;
        var sourceNearby =
            _lookup.GetEntitiesInRange<SolreignVampireComponent>(coords, r).Count > 0 ||
            _lookup.GetEntitiesInRange<SolreignWerewolfComponent>(coords, r).Count > 0 ||
            _lookup.GetEntitiesInRange<SolreignMoonTouchedComponent>(coords, r).Count > 0 ||
            _lookup.GetEntitiesInRange<XenoArtifactComponent>(coords, r).Count > 0 ||
            _lookup.GetEntitiesInRange<SolreignChapelGroundComponent>(coords, r).Count > 0 ||
            _lookup.GetEntitiesInRange<SolreignCoffinComponent>(coords, r).Count > 0;

        var bucket = SolreignStaticReceiverRules.TimeBucket(curTime, comp.ScanCooldown);
        var entityId = ent.Owner.Id;
        var roundId = _ticker.RoundId;

        var crackleRoll = SolreignStaticReceiverRules.Roll(entityId, roundId, bucket, CrackleSalt);
        if (SolreignStaticReceiverRules.ShouldCrackle(sourceNearby, crackleRoll, comp.NearbyCrackleChance, comp.FalseCrackleChance))
        {
            var variantRoll = SolreignStaticReceiverRules.Roll(entityId, roundId, bucket, VariantSalt);
            var variant = SolreignStaticReceiverRules.CrackleVariant(variantRoll, comp.RareVariantChance);
            _popup.PopupEntity(Loc.GetString(CrackleKeys[variant]), args.User, args.User, PopupType.Medium);
            return;
        }

        _popup.PopupEntity(Loc.GetString("solreign-static-receiver-calm"), args.User, args.User);
    }
}
