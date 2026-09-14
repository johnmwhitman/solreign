using System.Collections.Generic;

namespace Content.Server._Solreign.Onboarding;

/// <summary>
///     Pure, testable line-selection logic for <see cref="SolreignOnboardingSystem"/>'s round-start PA
///     beat. Kept separate from the ECS glue so the "pick a rotating variant" rule can be unit-tested
///     without spinning up a game instance — same thin-split idiom as
///     <c>Content.Server._Solreign.Providence.ProvidenceWelcomeGate</c>
///     (see Content.Tests/_Solreign/SolreignOnboardingLinesTests.cs).
/// </summary>
public static class SolreignOnboardingLines
{
    /// <summary>Loc keys for the 4 rotating shift-start PA variants (onboarding.ftl), no parameters.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "solreign-onboarding-beat-1",
        "solreign-onboarding-beat-2",
        "solreign-onboarding-beat-3",
        "solreign-onboarding-beat-4",
    };

    /// <summary>
    ///     Selects one of <see cref="Keys"/> by index, wrapping via modulo so any roll — including a
    ///     negative one, which <c>IRobustRandom.Next()</c> should never produce but this function makes
    ///     no assumption about — is always safe to index with.
    /// </summary>
    public static string Pick(int roll)
    {
        var index = roll % Keys.Count;
        if (index < 0)
            index += Keys.Count;

        return Keys[index];
    }
}
