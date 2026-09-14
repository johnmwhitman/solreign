using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Records;

/// <summary>
///     UI key for the Personnel Records Terminal's bound user interface (wave-2 item einstein-016).
/// </summary>
[Serializable, NetSerializable]
public enum RecordsTerminalUiKey : byte
{
    Key,
}

/// <summary>
///     One fully-rendered read of the requesting account's own Season Ledger record. Every field is
///     already-localized display TEXT chosen server-side from a closed set of Fluent templates — the
///     Contracts Board "dumb, cheerful terminal" idiom (<c>ContractsSystem.BoardUi.cs</c>): all rules
///     and all copy selection live server-side, the client only renders what it is handed. Nullable
///     fields (<see cref="MarkLine"/>) are honest empty states, never fabricated placeholders — a
///     fresh account with no mark gets <c>null</c>, not an invented "no mark yet" number.
/// </summary>
[Serializable, NetSerializable]
public sealed class RecordsTerminalSnapshot
{
    /// <summary>Current Ledger title (admin grant masks the earned one — same rule ID cards use).</summary>
    public readonly string Title;

    /// <summary>Career tour count, echoed straight from <c>TitleRules.Compute</c>.</summary>
    public readonly int Tours;

    /// <summary>Career standing flavor word (<c>PersonnelFileRules.DescribeCareerStanding</c>).</summary>
    public readonly string Standing;

    /// <summary>
    ///     Rendered milestone names for every celebratory social-first flag this account has claimed
    ///     (closed order — chirp answered, healed by another, item received). Empty, never null: an
    ///     empty list IS the honest "none yet" state, rendered by the client as one empty-state line.
    /// </summary>
    public readonly List<string> SocialFirsts;

    /// <summary>
    ///     First-death commemoration status line — "Statement of Record: on file." or "None. Keep it
    ///     that way." (spec voice, verbatim). Never the death's cause/character — this terminal is a
    ///     status read, not the crypt.
    /// </summary>
    public readonly string FirstDeathStatus;

    /// <summary>
    ///     The account's planted Mark (kind + freshly-computed growth stage), rendered as one line, or
    ///     <c>null</c> if the account has never planted — the honest empty state, distinct from a
    ///     rendered "you have no mark" sentence so the client can style the row differently.
    /// </summary>
    public readonly string? MarkLine;

    public RecordsTerminalSnapshot(
        string title,
        int tours,
        string standing,
        List<string> socialFirsts,
        string firstDeathStatus,
        string? markLine)
    {
        Title = title;
        Tours = tours;
        Standing = standing;
        SocialFirsts = socialFirsts;
        FirstDeathStatus = firstDeathStatus;
        MarkLine = markLine;
    }
}

/// <summary>
///     Server -&gt; ONE session: a private, session-targeted push of the requesting player's own
///     record. Deliberately NOT a shared <c>BoundUserInterfaceState</c> — <c>UserInterfaceComponent</c>
///     state is networked to every client observing the console entity, which would leak one player's
///     ledger data to anyone else who has the same public console in view. Copies the shipped
///     <c>FirstShiftPrivateSnapshotEvent</c> idiom (<c>Content.Shared._Solreign.PlayerDelight.FirstShift</c>):
///     a plain networked event, <c>RaiseNetworkEvent(event, session)</c>'d at exactly one recipient.
///     <see cref="Console"/> lets the client match the push to the terminal window that requested it.
/// </summary>
[Serializable, NetSerializable]
public sealed class RecordsTerminalSnapshotEvent : EntityEventArgs
{
    public readonly NetEntity Console;
    public readonly RecordsTerminalSnapshot Snapshot;

    public RecordsTerminalSnapshotEvent(NetEntity console, RecordsTerminalSnapshot snapshot)
    {
        Console = console;
        Snapshot = snapshot;
    }
}

/// <summary>
///     Client -&gt; server: re-fetch the sender's own record (the window's "Refresh" button). A fresh
///     BUI open already triggers a push; this lets a player re-poll without closing/reopening the
///     window if their record changed while it was open (e.g. a title ceremony fired mid-read).
/// </summary>
[Serializable, NetSerializable]
public sealed class RecordsTerminalRefreshMessage : BoundUserInterfaceMessage
{
}
