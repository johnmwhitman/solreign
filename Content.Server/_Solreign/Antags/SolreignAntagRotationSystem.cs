using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Antags;

/// <summary>
///     SR-W-036: Antag Rotation Cooldown & Selection Rules System.
///     Enforces shift cooldowns and progressive non-antagonist streak weighting for fair antag distribution.
/// </summary>
public sealed class SolreignAntagRotationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
    }

    /// <summary>Increment non-antagonist shift streak and calculate updated weight.</summary>
    public void IncrementNonAntagStreak(SolreignAntagRotationComponent comp)
    {
        comp.RoundsSinceLastAntag++;
        comp.EligibleForAntag = comp.RoundsSinceLastAntag >= comp.MinimumCooldownRounds;

        if (comp.EligibleForAntag)
        {
            // Increase selection weight by 1 for each additional non-antag shift past cooldown
            comp.AntagWeight = 1 + (comp.RoundsSinceLastAntag - comp.MinimumCooldownRounds);
        }
        else
        {
            comp.AntagWeight = 0;
        }
    }

    /// <summary>Reset streak upon receiving an antagonist role.</summary>
    public void ResetAntagStreak(SolreignAntagRotationComponent comp)
    {
        comp.RoundsSinceLastAntag = 0;
        comp.EligibleForAntag = false;
        comp.AntagWeight = 0;
    }

    /// <summary>Check whether player satisfies antag selection eligibility.</summary>
    public bool CheckEligibility(SolreignAntagRotationComponent comp)
    {
        return comp.RoundsSinceLastAntag >= comp.MinimumCooldownRounds;
    }
}
