namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Marker for a mini-golf hole (<c>SolreignGolfHole</c>,
/// Resources/Prototypes/_Solreign/Entities/recreation.yml). Lets <see cref="SolreignGolfSystem"/>
/// add a stroke-count-aware "sunk it in N strokes" popup on top of the hole's existing generic
/// <c>TriggerOnCollide</c> + <c>DeleteOnTrigger</c> + <c>EmitSoundOnTrigger</c> + <c>PopupOnTrigger</c>
/// primitives (all zero-C#-gap per the spec) without touching those shipped prototypes' behavior.
/// </summary>
[RegisterComponent]
[Access(typeof(SolreignGolfSystem))]
public sealed partial class SolreignGolfHoleFeedbackComponent : Component
{
}
