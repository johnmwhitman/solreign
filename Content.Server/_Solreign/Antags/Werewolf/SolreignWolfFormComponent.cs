namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Marks the POLYMORPHED wolf-form entity spawned by <see cref="PolymorphSystem"/> during
///     <see cref="WerewolfState.Transformed"/>. <see cref="SolreignWerewolfComponent"/> — the thing that
///     owns the state machine, the maul cooldown and the moon-touched roster — stays on the ORIGINAL
///     (human) entity, which upstream Polymorph banishes to a paused holding map for the transform's
///     duration (see <c>PolymorphSystem.PolymorphEntity</c>/<c>EnsurePausedMap</c>). This component is
///     the back-reference the maul handler needs to find that original entity's component from the
///     wolf body that's actually swinging.
///
///     Added programmatically by <see cref="SolreignWerewolfSystem"/> right after polymorphing, not via
///     YAML — the wolf-form entity prototype itself (sprite pipeline, task #9) is a separate, later
///     build-pass item; this marker doesn't require it to exist yet, only the resulting spawned entity
///     to carry it. Removed automatically when the wolf entity is deleted on revert
///     (<c>PolymorphSystem.Revert</c> queues the wolf body for deletion).
/// </summary>
[RegisterComponent, Access(typeof(SolreignWerewolfSystem))]
public sealed partial class SolreignWolfFormComponent : Component
{
    /// <summary>The original (human) entity whose <see cref="SolreignWerewolfComponent"/> drives this wolf.</summary>
    [ViewVariables]
    public EntityUid HumanForm;
}
