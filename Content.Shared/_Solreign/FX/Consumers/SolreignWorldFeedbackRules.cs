using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared._Solreign.FX.Consumers;

/// <summary>
///     Deterministic shared policy for the dormant world-feedback FX consumer.
///     This class describes feedback for post-modifier damage accepted by the damage pipeline.
/// </summary>
public static class SolreignWorldFeedbackRules
{
    public const float HeavyImpactThreshold = 20f;

    public const string LightImpactEffectId = "impact_light";
    public const string HeavyImpactEffectId = "impact_heavy";
    public const string BodyShockEffectId = "body_shock_generic";

    public static SolreignImpactCue ClassifyAppliedDamage(float appliedPositiveDamage)
    {
        if (!float.IsFinite(appliedPositiveDamage) || appliedPositiveDamage <= 0f)
            return SolreignImpactCue.None;

        return appliedPositiveDamage >= HeavyImpactThreshold
            ? SolreignImpactCue.Heavy
            : SolreignImpactCue.Light;
    }

    /// <summary>
    ///     Sums positive final physical-damage entries without allowing healing entries to cancel
    ///     visible impact. Callers supply the actual delta from DamageChangedEvent.
    /// </summary>
    public static float AppliedPositiveDamage(DamageSpecifier damage)
    {
        var total = FixedPoint2.Zero;
        foreach (var (damageType, value) in damage.DamageDict)
        {
            if (value <= FixedPoint2.Zero)
                continue;

            switch (damageType.Id)
            {
                case "Blunt":
                case "Slash":
                case "Piercing":
                case "Structural":
                    total += value;
                    break;
            }
        }

        return total.Float();
    }

    public static SolreignDamageFeedbackCue ClassifyDamage(DamageSpecifier damage)
    {
        var total = FixedPoint2.Zero;
        var hasPositive = false;
        foreach (var (damageType, value) in damage.DamageDict)
        {
            if (value <= FixedPoint2.Zero)
                continue;

            hasPositive = true;
            switch (damageType.Id)
            {
                case "Blunt":
                case "Slash":
                case "Piercing":
                case "Structural":
                    total += value;
                    break;
            }
        }

        if (total > FixedPoint2.Zero)
            return total.Float() >= HeavyImpactThreshold
                ? SolreignDamageFeedbackCue.KineticHeavy
                : SolreignDamageFeedbackCue.KineticLight;

        return hasPositive ? SolreignDamageFeedbackCue.NonKinetic : SolreignDamageFeedbackCue.None;
    }
}

public enum SolreignDamageFeedbackCue : byte
{
    None = 0,
    KineticLight,
    KineticHeavy,
    NonKinetic,
}

public enum SolreignImpactCue : byte
{
    None = 0,
    Light,
    Heavy,
}
