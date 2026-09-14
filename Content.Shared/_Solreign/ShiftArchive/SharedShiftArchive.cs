using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared._Solreign.ShiftArchive;

/// <summary>
///     Wire state for the Shift Archive board. Entries arrive fully rendered — the server is the
///     only place that calls <c>Loc.GetString</c> for archive copy (the Records-terminal
///     precedent), so the wire carries plain strings and the client window is a dumb list.
///     <c>Enabled</c> false means the feature CVar is off and the window shows its offline state.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShiftArchiveState : BoundUserInterfaceState
{
    public readonly bool Enabled;
    public readonly List<ShiftArchiveEntry> Entries;

    public ShiftArchiveState(bool enabled, List<ShiftArchiveEntry> entries)
    {
        Enabled = enabled;
        Entries = entries;
    }
}

/// <summary>One archived shift: a rendered header line plus rendered detail lines.</summary>
[Serializable, NetSerializable]
public sealed class ShiftArchiveEntry
{
    public string Header;
    public List<string> Lines;

    public ShiftArchiveEntry(string header, List<string> lines)
    {
        Header = header;
        Lines = lines;
    }
}

[Serializable, NetSerializable]
public enum ShiftArchiveUiKey : byte
{
    Key
}
