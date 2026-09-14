namespace Content.Shared._Solreign.VesselIdentity;

/// <summary>
///     Represents the moderation and approval state of a vessel's custom name (SR-W-043).
/// </summary>
public enum SolreignVesselModerationState : byte
{
    /// <summary>
    ///     The vessel name has passed automated screening and is fully approved.
    /// </summary>
    Approved = 0,

    /// <summary>
    ///     The vessel name is awaiting manual admin or automated system review.
    /// </summary>
    Pending = 1,

    /// <summary>
    ///     The vessel name was flagged by automated screening as inappropriate or invalid.
    /// </summary>
    Flagged = 2,

    /// <summary>
    ///     The vessel name was forcibly reset by an administrator due to a policy violation.
    /// </summary>
    ResetByAdmin = 3
}
