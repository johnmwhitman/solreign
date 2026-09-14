using Content.Shared._Solreign.FX.Consumers;

namespace Content.Server._Solreign.FX.Consumers;

/// <summary>
///     Allocation-free, deterministic routing decisions shared by the live adapter and its
///     performance proof. This contains no entity, session, or world state.
/// </summary>
internal static class SolreignWorldFeedbackDispatchRules
{
    internal static bool ShouldHandle(bool deliveryEnabled, bool observeEnabled)
    {
        return deliveryEnabled || observeEnabled;
    }

    internal static bool TryGetImpactCandidate(
        SolreignDamageFeedbackCue classification,
        out SolreignImpactFeedbackCandidate candidate)
    {
        switch (classification)
        {
            case SolreignDamageFeedbackCue.KineticLight:
                candidate = new SolreignImpactFeedbackCandidate(
                    SolreignWorldFeedbackRules.LightImpactEffectId,
                    Intensity: 0.6f,
                    Duration: 0.4f);
                return true;
            case SolreignDamageFeedbackCue.KineticHeavy:
                candidate = new SolreignImpactFeedbackCandidate(
                    SolreignWorldFeedbackRules.HeavyImpactEffectId,
                    Intensity: 0.8f,
                    Duration: 0.8f);
                return true;
            default:
                candidate = default;
                return false;
        }
    }
}

internal readonly record struct SolreignImpactFeedbackCandidate(
    string EffectId,
    float Intensity,
    float Duration);
