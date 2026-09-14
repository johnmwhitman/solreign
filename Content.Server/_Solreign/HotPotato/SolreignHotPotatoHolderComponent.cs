namespace Content.Server._Solreign.HotPotato;

/// <summary>
///     Server-side marker placed on whoever is currently holding an armed Mandatory Team-Building
///     Exercise. Exists so <see cref="SolreignHotPotatoSystem"/> can subscribe to the HOLDER's
///     StartCollideEvent — a held item sits in a container and produces no collisions of its own,
///     so the bump that passes the exercise along is always the holder's bump.
/// </summary>
[RegisterComponent]
[Access(typeof(SolreignHotPotatoSystem))]
public sealed partial class SolreignHotPotatoHolderComponent : Component
{
    /// <summary>
    /// The exercise this entity is currently responsible for.
    /// </summary>
    [ViewVariables]
    public EntityUid Potato;
}
