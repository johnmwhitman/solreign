using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.PlayerDelight.Wingmates;

/// <summary>
/// Monotonic, beacon-scoped cache for private snapshots. Equal generations are replays and are
/// rejected along with older snapshots.
/// </summary>
public sealed class WingmateSnapshotIndex
{
    private readonly Dictionary<NetEntity, Entry> _entries = new();
    private readonly Dictionary<NetEntity, uint> _consumedTransitionSequences = new();

    public bool TryUpdate(NetEntity beacon, ulong generation, WingmateUiState state)
    {
        if (_entries.TryGetValue(beacon, out var current) && generation <= current.Generation)
            return false;

        _entries[beacon] = new Entry(generation, state);
        return true;
    }

    public bool TryGet(NetEntity beacon, out Entry entry)
    {
        return _entries.TryGetValue(beacon, out entry);
    }

    public bool TryConsumeTransition(NetEntity beacon, uint sequence)
    {
        if (sequence == 0)
            return false;

        if (_consumedTransitionSequences.TryGetValue(beacon, out var current) && sequence <= current)
            return false;

        _consumedTransitionSequences[beacon] = sequence;
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _consumedTransitionSequences.Clear();
    }

    public readonly record struct Entry(ulong Generation, WingmateUiState State);
}
