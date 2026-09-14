using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Solreign.Noticeboards;

/// <summary>
///     Marks an entity as a crew Noticeboard. Pure marker plus one identity field — all note
///     content lives in the Season Ledger (<c>noticeboard_notes</c>), not on this component, the
///     same idiom as <c>SolreignBountyBoardComponent</c>.
///
///     <see cref="BoardId"/> is the cross-round persistence key (spec: notes "persist in the
///     Season Ledger"). Entity uids are NOT stable across rounds and this wave ships
///     admin-spawnable only (no map placement), so a fresh spawn every round would otherwise
///     start every board's history over. Every board instance defaults to the shared
///     <c>"main"</c> id — one station-wide noticeboard, however many physical copies an admin
///     spawns of it — so its content is genuinely cross-round. A future map-placement wave (out
///     of THIS lane's scope) can assign distinct <c>BoardId</c>s per placed location (e.g.
///     "bar", "dorms") by overriding this field on a prototype variant; nothing here assumes a
///     single board.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignNoticeboardComponent : Component
{
    [DataField]
    public string BoardId = "main";
}
