namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Server-side stroke counter for a mini-golf ball (<c>SolreignGolfBall</c>,
/// Resources/Prototypes/_Solreign/Entities/recreation.yml). Incremented by
/// <see cref="SolreignGolfSystem"/> whenever a golf club successfully whacks the ball — the
/// existing <c>MeleeWeapon</c> + <c>MeleeThrowOnHitComponent</c> combo already does the actual
/// throw (zero C# gap per docs/specs/2026-07-11-recreation-spec.md); this component just adds the
/// "how many swings so far" bookkeeping the spec's stroke-counter popup needs.
/// </summary>
[RegisterComponent]
[Access(typeof(SolreignGolfSystem))]
public sealed partial class SolreignGolfBallProgressComponent : Component
{
    /// <summary>
    /// Total number of times this ball has been struck since it was spawned (or last reset).
    /// </summary>
    [ViewVariables]
    public int Strokes;
}
