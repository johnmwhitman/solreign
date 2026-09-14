namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Pure cooldown/formatting math for <see cref="SolreignWristOrganizerSystem"/>. Kept free of
///     IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/SolreignWristOrganizerRulesTests.cs), same reasoning as
///     <c>CarpComboRules</c>/<c>JudoComboRules</c>.
///
///     ACCESS LAW NOTE: an earlier draft of this feature wanted a radiation-dose line too (the
///     egg15 flavor text mentions "radiation levels"). <c>RadiationReceiverComponent</c> is
///     <c>[Access(typeof(RadiationSystem))]</c> with no <c>Other</c> grant and
///     <c>RadiationSystem</c> exposes no public getter for a mob's current dose (only
///     <c>IrradiateEntity</c>/<c>SetSourceEnabled</c>/<c>SetCanReceive</c>, all mutators) — so there
///     is no owning-system API to read it through. Per the ANALYZER LAW ("[Access] via owning
///     system API"), that line was dropped rather than reached around via a direct TryComp on a
///     component this system doesn't own. What ships instead is a literal reading of "tells you
///     exactly how doomed you are": a percentage of the way to the incapacitation threshold, via
///     <c>MobThresholdSystem.TryGetIncapPercentage</c> — a real, public, owning-system API.
/// </summary>
public static class SolreignWristOrganizerRules
{
    /// <summary>Whether the readout is off cooldown at <paramref name="curTime"/>. Same shape as CarpComboRules.ComboReady.</summary>
    public static bool ReadoutReady(TimeSpan curTime, TimeSpan nextReadoutTime)
    {
        return curTime >= nextReadoutTime;
    }

    /// <summary>Computes when the next readout may fire. A negative cooldown (YAML misconfiguration) clamps to zero.</summary>
    public static TimeSpan NextReadoutTime(TimeSpan curTime, TimeSpan cooldown)
    {
        return curTime + (cooldown > TimeSpan.Zero ? cooldown : TimeSpan.Zero);
    }

    /// <summary>
    ///     Classifies total damage against the wearer's own crit/dead thresholds (either may be
    ///     absent — not every mob has a MobThresholdsComponent). A non-positive threshold is treated
    ///     as "unknown" (YAML/config edge case) rather than tripping instantly at zero damage.
    /// </summary>
    public static string VitalsStatus(float totalDamage, float? deadThreshold, float? critThreshold)
    {
        if (deadThreshold is { } dead && dead > 0f && totalDamage >= dead)
            return "FLATLINE";

        if (critThreshold is { } crit && crit > 0f && totalDamage >= crit)
            return "CRITICAL";

        if (totalDamage > 0f)
            return "DEGRADED";

        return "NOMINAL";
    }

    /// <summary>
    ///     Formats an incapacitation percentage (0..1, or null if it couldn't be computed) as a
    ///     whole-number "doom index" 0-100. Clamps out-of-range input rather than trusting the
    ///     caller, since this is the number actually shown to a player.
    /// </summary>
    public static int DoomPercent(float? incapPercentage)
    {
        if (incapPercentage is not { } pct)
            return 0;

        if (pct < 0f)
            pct = 0f;
        else if (pct > 1f)
            pct = 1f;

        return (int) MathF.Round(pct * 100f);
    }

    /// <summary>
    ///     Delight-eggs batch (feat/delight-eggs): which drift tier applies given how many successful
    ///     readouts this specific unit has already given out (BEFORE this one is counted — call with
    ///     the pre-increment value). -1 means "no drift, show the plain readout only" (the first use);
    ///     0 is the first drift tier (the corporate-surveillance aside), 1 is the second and final tier
    ///     (the cold personal observation). A non-positive <paramref name="priorUses"/> always means -1.
    /// </summary>
    public static int DriftTierForPriorUses(int priorUses)
    {
        if (priorUses <= 0)
            return -1;

        if (priorUses == 1)
            return 0;

        return 1;
    }
}
