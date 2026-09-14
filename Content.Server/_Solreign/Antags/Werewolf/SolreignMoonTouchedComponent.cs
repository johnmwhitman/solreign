namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Infection-lite mark left by a werewolf maul: glitter fur, the occasional involuntary "awoo",
///     a mild clumsiness — and nothing else. It never converts, never damages, never stacks, and
///     expires on its own. While present it also serves as the re-maul immunity token (anti-grief
///     rule 2, spec §3.5), the same opt-out-marker idiom as upstream <c>ZombieImmuneComponent</c>.
///
///     Cure: expires at <see cref="ExpiresAt"/>, or instantly via the Follicle Stabilizer Draught.
/// </summary>
[RegisterComponent, Access(typeof(SolreignWerewolfSystem))]
public sealed partial class SolreignMoonTouchedComponent : Component
{
    /// <summary>Game time at which the fur politely excuses itself.</summary>
    [ViewVariables]
    public TimeSpan ExpiresAt;

    /// <summary>
    ///     Movement multiplier while Moon-Touched (build pass wires this through the standard
    ///     movement-modifier refresh). Kept close to 1: this is comedy, not a debuff arms race.
    /// </summary>
    [DataField]
    public float SlowMultiplier = 0.92f;
}
