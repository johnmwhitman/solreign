using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Noticeboards;

/// <summary>UI key for the crew Noticeboard bound user interface.</summary>
[Serializable, NetSerializable]
public enum SolreignNoticeboardUiKey : byte
{
    Key = 0,
}

/// <summary>
///     One visible note, as pushed to the client. Deliberately carries NO account identifier —
///     spec rule: "Notes display the author's in-character name only. The underlying account
///     identity stays reachable solely through the normal restricted admin-log path... never
///     surfaced on the board itself." <see cref="Id"/> is the Season Ledger row id, needed only so
///     the client can target a report tap at a specific note.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignNoticeboardNoteView
{
    public int Id;

    public string AuthorDisplay = string.Empty;

    public string Text = string.Empty;

    /// <summary>True for a PROVIDENCE-authored notice — the client renders these with the
    /// mandatory system-labeled marker (spec rule 5), never blended in as a player's handwriting.</summary>
    public bool IsProvidence;
}

/// <summary>
///     Full board snapshot pushed to the Noticeboard BUI: the current active (unhidden,
///     unexpired) notes plus an optional status line shown above the post input (e.g. the last
///     submit outcome). <see cref="Offline"/> mirrors the Liability Board's ALIVENESS idiom: a
///     failed local read is never rendered as a true-empty board.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignNoticeboardUiState : BoundUserInterfaceState
{
    public List<SolreignNoticeboardNoteView> Notes;

    public string? Status;

    public bool Offline;

    public SolreignNoticeboardUiState(List<SolreignNoticeboardNoteView> notes, string? status = null, bool offline = false)
    {
        Notes = notes;
        Status = status;
        Offline = offline;
    }
}

/// <summary>Submit a new note to the board this BUI is open on.</summary>
[Serializable, NetSerializable]
public sealed class SolreignNoticeboardPostMessage : BoundUserInterfaceMessage
{
    public string Text;

    public SolreignNoticeboardPostMessage(string text)
    {
        Text = text;
    }
}

/// <summary>
///     One-tap report: any player may report any currently-visible note. This IS the containment
///     action (spec rule 3) — it takes effect immediately and reversibly, with no independent
///     review required, exactly like a moderator's own hide.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignNoticeboardReportMessage : BoundUserInterfaceMessage
{
    public int NoteId;

    public SolreignNoticeboardReportMessage(int noteId)
    {
        NoteId = noteId;
    }
}
