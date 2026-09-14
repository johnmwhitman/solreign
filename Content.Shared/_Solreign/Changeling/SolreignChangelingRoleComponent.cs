using Content.Shared.Roles.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._Solreign.Changeling;

/// <summary>
///     Added to the mind-role entity when a player becomes the Talent Acquisition Specialist antag
///     (round integration, spec §6: docs/specs/2026-07-11-changeling-spec.md). The
///     <c>RoleRequirement</c> objective gate (Resources/Prototypes/_Solreign/Objectives/changeling.yml)
///     checks for this component by name to restrict the "collect N identities" objective to our own
///     antag's minds — same tagging idiom upstream's own <c>ChangelingRoleComponent</c>/
///     <c>TraitorRoleComponent</c> use for their antags (Content.Shared/Roles/Components).
///
///     Deliberately its own Solreign-namespaced type, NOT a reuse of upstream's
///     <c>ChangelingRoleComponent</c> — that one tags a completely different, unrelated antag
///     (upstream's own devour/sting Changeling merged into space-wizards/space-station-14 itself;
///     see the clean-room provenance note in docs/specs/2026-07-11-changeling-spec.md).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SolreignChangelingRoleComponent : BaseMindRoleComponent;
