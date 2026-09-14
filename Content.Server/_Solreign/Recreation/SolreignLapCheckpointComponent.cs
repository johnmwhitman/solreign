namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Placed on a go-kart course checkpoint prop (<c>SolreignGoKartCheckpoint</c>,
/// Resources/Prototypes/_Solreign/Entities/recreation.yml) alongside the same generic
/// <c>TriggerOnCollide</c> primitive that builds the golf hole and floor traps — gap #2 in
/// docs/specs/2026-07-11-recreation-spec.md. <see cref="SolreignLapTrackerSystem"/> reads
/// <see cref="CourseId"/> + <see cref="Ordinal"/> off the checkpoint that got hit and validates it
/// against the kart's own <see cref="SolreignLapTrackerComponent"/> via
/// <see cref="SolreignLapTrackerMath.HitCheckpoint"/>.
///
/// Defaults are a spawnable template, not a real course — a mapper placing checkpoints for an
/// actual track overrides <see cref="CourseId"/>/<see cref="Ordinal"/> per instance (standard SS14
/// per-entity component override on the map), the same way one prototype covers every numbered
/// checkpoint on every course.
/// </summary>
[RegisterComponent]
[Access(typeof(SolreignLapTrackerSystem))]
public sealed partial class SolreignLapCheckpointComponent : Component
{
    /// <summary>
    /// Which course this checkpoint belongs to. Karts only accrue progress against a checkpoint
    /// whose course matches their own tracker's <see cref="SolreignLapTrackerComponent.CourseId"/> —
    /// stops a kart that wandered onto a different track's checkpoint from getting bogus credit.
    /// </summary>
    [DataField]
    public string CourseId = "default";

    /// <summary>
    /// This checkpoint's position in course order, starting at 0 (the start/finish line).
    /// </summary>
    [DataField]
    public int Ordinal;
}
