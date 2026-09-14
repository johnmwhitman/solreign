namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Marks the game rule entity that runs the Talent Acquisition Specialist antag round
///     integration (spec §6 follow-up: docs/specs/2026-07-11-changeling-spec.md), wired via
///     <c>Resources/Prototypes/_Solreign/GameRules/changeling.yml</c>. Pure marker — every
///     selection/count decision lives on the standard upstream <c>AntagSelectionComponent</c>
///     already attached to the same game rule entity (same idiom Traitor/Zombie/Nukeops use in
///     <c>Resources/Prototypes/GameRules/roundstart.yml</c>). This type exists only so
///     <see cref="SolreignChangelingRuleSystem"/> — a <c>GameRuleSystem&lt;T&gt;</c> — fires
///     exclusively for instances of THIS rule, not every game rule in play.
/// </summary>
[RegisterComponent, Access(typeof(SolreignChangelingRuleSystem))]
public sealed partial class SolreignChangelingRuleComponent : Component;
