using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Library;

/// <summary>
///     Marks a round-start-materialized library book — the physical projection of one
///     <c>library_works</c> row (the <c>SolreignMarkComponent</c> idiom). Carries only the ledger
///     row id, so the report verb (<see cref="SolreignLibrarySystem"/>) knows which row to hide;
///     everything a reader sees (title, author, body) is the entity's own <c>MetaData</c>/<c>Paper</c>
///     state, stamped once at spawn time — no store round-trip on read or examine.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignLibraryBookComponent : Component
{
    [DataField]
    public int WorkId;
}
