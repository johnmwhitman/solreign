using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Library;

/// <summary>
///     Marks an entity as the Station Archive ("PROVIDENCE Records Annex") — a bookshelf-class
///     structure where a player alt-clicks to submit a written work, and where the round's most
///     recent non-hidden works materialize as readable book items. Pure marker plus one identity
///     field — all work content lives in the Season Ledger (<c>library_works</c>), not on this
///     component, the same idiom as <c>SolreignNoticeboardComponent</c>.
///
///     <see cref="ArchiveId"/> is the cross-round persistence key. Entity uids are NOT stable
///     across rounds and this wave ships admin-spawnable only (no map placement — the Noticeboard
///     precedent), so a fresh spawn every round would otherwise start every archive's history
///     over. Every Annex instance defaults to the shared <c>"main"</c> id — one station-wide
///     archive, however many physical copies an admin spawns — so its content is genuinely
///     cross-round. A future map-placement wave can assign distinct <c>ArchiveId</c>s per placed
///     location by overriding this field on a prototype variant.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignLibraryAnnexComponent : Component
{
    [DataField]
    public string ArchiveId = "main";
}
