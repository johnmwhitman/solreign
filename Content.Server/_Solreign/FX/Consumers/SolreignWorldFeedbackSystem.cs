using Content.Shared._Solreign.FX;
using Content.Shared._Solreign.FX.Consumers;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Electrocution;
using Robust.Shared.Configuration;

#pragma warning disable CS0618 // DamageChangedEvent is the current event carrying the actual applied delta.

namespace Content.Server._Solreign.FX.Consumers;

/// <summary>
///     Dormant adapter from ordinary content events to the existing bounded FX Language v1 raise
///     path. It changes presentation only and never mutates combat or electrocution state.
/// </summary>
public sealed partial class SolreignWorldFeedbackSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ISolreignFxCueRaiser _fxRaiser = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<ElectrocutedEvent>(OnElectrocuted);
    }

    private void OnDamageChanged(Entity<DamageableComponent> target, ref DamageChangedEvent args)
    {
        var deliveryEnabled = _cfg.GetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled);
        var observeEnabled = _cfg.GetCVar(CCVars.SolreignFxWorldFeedbackObserveEnabled);
        if (!SolreignWorldFeedbackDispatchRules.ShouldHandle(deliveryEnabled, observeEnabled))
            return;

        if (TerminatingOrDeleted(target.Owner) || args.DamageDelta is not { } actualDelta)
            return;

        var classification = SolreignWorldFeedbackRules.ClassifyDamage(actualDelta);
        if (classification == SolreignDamageFeedbackCue.None)
            return;

        if (classification == SolreignDamageFeedbackCue.NonKinetic)
        {
            if (observeEnabled)
                SolreignWorldFeedbackMetrics.Record(SolreignWorldFeedbackObservation.NonKineticFiltered);
            return;
        }

        if (!deliveryEnabled)
        {
            if (observeEnabled)
            {
                SolreignWorldFeedbackMetrics.Record(
                    classification == SolreignDamageFeedbackCue.KineticHeavy
                        ? SolreignWorldFeedbackObservation.KineticHeavyShadow
                        : SolreignWorldFeedbackObservation.KineticLightShadow);
            }

            return;
        }

        if (!SolreignWorldFeedbackDispatchRules.TryGetImpactCandidate(classification, out var candidate))
            return;

        var anchor = target.Owner;

        var acceptedOrCoalesced = _fxRaiser.RaiseCue(
            candidate.EffectId,
            coordinates: null,
            entityAnchor: GetNetEntity(anchor),
            candidate.Intensity,
            scale: 1f,
            candidate.Duration,
            paletteIndex: null,
            phase: null,
            pvsSource: anchor);

        if (observeEnabled)
        {
            SolreignWorldFeedbackMetrics.Record(
                (classification, acceptedOrCoalesced) switch
                {
                    (SolreignDamageFeedbackCue.KineticLight, true) =>
                        SolreignWorldFeedbackObservation.KineticLightRaiserAcceptedOrCoalesced,
                    (SolreignDamageFeedbackCue.KineticLight, false) =>
                        SolreignWorldFeedbackObservation.KineticLightRaiserRejected,
                    (SolreignDamageFeedbackCue.KineticHeavy, true) =>
                        SolreignWorldFeedbackObservation.KineticHeavyRaiserAcceptedOrCoalesced,
                    (SolreignDamageFeedbackCue.KineticHeavy, false) =>
                        SolreignWorldFeedbackObservation.KineticHeavyRaiserRejected,
                    _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, null),
                });
        }
    }

#pragma warning restore CS0618

    private void OnElectrocuted(ElectrocutedEvent args)
    {
        var deliveryEnabled = _cfg.GetCVar(CCVars.SolreignFxWorldFeedbackV1Enabled);
        var observeEnabled = _cfg.GetCVar(CCVars.SolreignFxWorldFeedbackObserveEnabled);
        if (!SolreignWorldFeedbackDispatchRules.ShouldHandle(deliveryEnabled, observeEnabled))
            return;

        if (TerminatingOrDeleted(args.TargetUid))
            return;

        if (!deliveryEnabled)
        {
            if (observeEnabled)
                SolreignWorldFeedbackMetrics.Record(SolreignWorldFeedbackObservation.ElectrocutionShadow);
            return;
        }

        var acceptedOrCoalesced = _fxRaiser.RaiseCue(
            SolreignWorldFeedbackRules.BodyShockEffectId,
            coordinates: null,
            entityAnchor: GetNetEntity(args.TargetUid),
            intensity: 0.5f,
            scale: 1f,
            duration: 0.6f,
            paletteIndex: null,
            phase: null,
            pvsSource: args.TargetUid);

        if (!observeEnabled)
            return;

        SolreignWorldFeedbackMetrics.Record(
            acceptedOrCoalesced
                ? SolreignWorldFeedbackObservation.ElectrocutionRaiserAcceptedOrCoalesced
                : SolreignWorldFeedbackObservation.ElectrocutionRaiserRejected);
    }
}
