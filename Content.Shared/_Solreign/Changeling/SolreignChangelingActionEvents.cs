using Content.Shared.Actions;

namespace Content.Shared._Solreign.Changeling;

/// <summary>
///     Raised by <c>ActionSolreignChangelingTransform</c> — assume the most recently absorbed identity
///     (spec: docs/specs/2026-07-11-changeling-spec.md §2.2). Same shape as upstream's parameterless
///     instant action events (<c>EggLayInstantActionEvent</c>).
/// </summary>
public sealed partial class SolreignChangelingTransformActionEvent : InstantActionEvent
{
}

/// <summary>
///     Raised by <c>ActionSolreignChangelingRevert</c> — return to the changeling's own true form
///     (spec §2.2). Mirrors upstream's <c>RevertPolymorphActionEvent</c> pairing without depending on
///     <c>PolymorphSystem</c> — see the spec's §2.1 note on why Transform doesn't use Polymorph.
/// </summary>
public sealed partial class SolreignChangelingRevertActionEvent : InstantActionEvent
{
}

/// <summary>
///     Raised by <c>ActionSolreignChangelingArmBlade</c> — toggles the augmented arm blade (spec §2.3).
/// </summary>
public sealed partial class SolreignChangelingArmBladeToggleActionEvent : InstantActionEvent
{
}
