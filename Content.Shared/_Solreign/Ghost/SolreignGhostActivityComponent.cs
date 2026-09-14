namespace Content.Shared._Solreign.Ghost;

/// <summary>
/// Solreign — Afterlife Activities (roadmap D2.2).
/// Marks a map entity as a safe, non-antag activity that dead/observer players can
/// discover through the ghost "Afterlife" menu and warp to.
/// Mappers add activities purely in YAML by attaching this component to an entity:
/// <code>
/// - type: SolreignGhostActivity
///   name: Arcade Cabinet (Recreation)
///   description: Watch the high-score chase, heckle politely.
/// </code>
/// No code changes are required to add, rename, or disable an activity.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignGhostActivityComponent : Component
{
    /// <summary>
    /// Display name shown in the Afterlife window. Falls back to the entity's name if empty.
    /// </summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// Short blurb shown under the activity name.
    /// </summary>
    [DataField]
    public string Description = string.Empty;

    /// <summary>
    /// Set false to hide the activity without removing the component (e.g. mid-event).
    /// </summary>
    [DataField]
    public bool Enabled = true;
}
