using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.PlayerDelight.FirstShift;

/// <summary>
/// Beacon-scoped cache for targeted First Shift state. Equal generations are replays.
/// </summary>
public sealed class FirstShiftSnapshotIndex
{
    private readonly Dictionary<NetEntity, Entry> _entries = new();

    public bool TryUpdate(NetEntity beacon, ulong generation, FirstShiftUiState state)
    {
        if (_entries.TryGetValue(beacon, out var current) && generation <= current.Generation)
            return false;

        _entries[beacon] = new Entry(generation, state);
        return true;
    }

    public bool TryGet(NetEntity beacon, out Entry entry) => _entries.TryGetValue(beacon, out entry);

    public bool Remove(NetEntity beacon) => _entries.Remove(beacon);

    public void Clear() => _entries.Clear();

    public readonly record struct Entry(ulong Generation, FirstShiftUiState State);
}
