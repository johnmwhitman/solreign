using Prometheus;

namespace Content.Server._Solreign.FX.Consumers;

internal enum SolreignWorldFeedbackObservation : byte
{
    KineticLightShadow = 0,
    KineticLightRaiserAcceptedOrCoalesced,
    KineticLightRaiserRejected,
    KineticHeavyShadow,
    KineticHeavyRaiserAcceptedOrCoalesced,
    KineticHeavyRaiserRejected,
    ElectrocutionShadow,
    ElectrocutionRaiserAcceptedOrCoalesced,
    ElectrocutionRaiserRejected,
    NonKineticFiltered,
}

/// <summary>
///     Process-lifetime aggregate observations for preactivation semantic validation. The ten
///     cached children are the entire label space; callers cannot supply labels or arbitrary cue
///     identifiers.
/// </summary>
internal static class SolreignWorldFeedbackMetrics
{
    private static readonly Counter WorldFeedbackTotal = Metrics.CreateCounter(
        "solreign_fx_world_feedback_total",
        "Aggregate preactivation world-feedback candidates by closed semantic class and outcome.",
        new[] { "class", "outcome" });

    private static readonly Counter.Child KineticLightShadow =
        WorldFeedbackTotal.WithLabels("kinetic_light", "shadow");
    private static readonly Counter.Child KineticLightRaiserAcceptedOrCoalesced =
        WorldFeedbackTotal.WithLabels("kinetic_light", "raiser_accepted_or_coalesced");
    private static readonly Counter.Child KineticLightRaiserRejected =
        WorldFeedbackTotal.WithLabels("kinetic_light", "raiser_rejected");
    private static readonly Counter.Child KineticHeavyShadow =
        WorldFeedbackTotal.WithLabels("kinetic_heavy", "shadow");
    private static readonly Counter.Child KineticHeavyRaiserAcceptedOrCoalesced =
        WorldFeedbackTotal.WithLabels("kinetic_heavy", "raiser_accepted_or_coalesced");
    private static readonly Counter.Child KineticHeavyRaiserRejected =
        WorldFeedbackTotal.WithLabels("kinetic_heavy", "raiser_rejected");
    private static readonly Counter.Child ElectrocutionShadow =
        WorldFeedbackTotal.WithLabels("electrocution", "shadow");
    private static readonly Counter.Child ElectrocutionRaiserAcceptedOrCoalesced =
        WorldFeedbackTotal.WithLabels("electrocution", "raiser_accepted_or_coalesced");
    private static readonly Counter.Child ElectrocutionRaiserRejected =
        WorldFeedbackTotal.WithLabels("electrocution", "raiser_rejected");
    private static readonly Counter.Child NonKineticFiltered =
        WorldFeedbackTotal.WithLabels("non_kinetic", "filtered");

    internal static void Record(SolreignWorldFeedbackObservation observation)
    {
        var child = observation switch
        {
            SolreignWorldFeedbackObservation.KineticLightShadow => KineticLightShadow,
            SolreignWorldFeedbackObservation.KineticLightRaiserAcceptedOrCoalesced =>
                KineticLightRaiserAcceptedOrCoalesced,
            SolreignWorldFeedbackObservation.KineticLightRaiserRejected => KineticLightRaiserRejected,
            SolreignWorldFeedbackObservation.KineticHeavyShadow => KineticHeavyShadow,
            SolreignWorldFeedbackObservation.KineticHeavyRaiserAcceptedOrCoalesced =>
                KineticHeavyRaiserAcceptedOrCoalesced,
            SolreignWorldFeedbackObservation.KineticHeavyRaiserRejected => KineticHeavyRaiserRejected,
            SolreignWorldFeedbackObservation.ElectrocutionShadow => ElectrocutionShadow,
            SolreignWorldFeedbackObservation.ElectrocutionRaiserAcceptedOrCoalesced =>
                ElectrocutionRaiserAcceptedOrCoalesced,
            SolreignWorldFeedbackObservation.ElectrocutionRaiserRejected => ElectrocutionRaiserRejected,
            SolreignWorldFeedbackObservation.NonKineticFiltered => NonKineticFiltered,
            _ => throw new ArgumentOutOfRangeException(nameof(observation), observation, null),
        };

        child.Inc();
    }
}
