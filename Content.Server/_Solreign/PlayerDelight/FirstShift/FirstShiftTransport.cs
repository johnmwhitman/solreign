using Content.Shared._Solreign.PlayerDelight.FirstShift;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._Solreign.PlayerDelight.FirstShift;

internal delegate bool TryResolveFirstShiftSession<TSession>(NetUserId user, out TSession session);

internal sealed class FirstShiftSnapshotAdapter<TSession>
{
    private readonly TryResolveFirstShiftSession<TSession> _resolve;
    private readonly Func<EntityUid, bool> _available;
    private readonly Func<EntityUid, NetEntity> _netEntity;
    private readonly Func<NetUserId, FirstShiftUiState> _build;
    private readonly Action<FirstShiftPrivateSnapshotEvent, TSession> _send;
    private readonly Dictionary<NetUserId, EntityUid> _open = new();
    private readonly Dictionary<NetUserId, ulong> _generations = new();

    public IEnumerable<NetUserId> OpenUsers => _open.Keys;

    public FirstShiftSnapshotAdapter(TryResolveFirstShiftSession<TSession> resolve,
        Func<EntityUid, bool> available, Func<EntityUid, NetEntity> netEntity,
        Func<NetUserId, FirstShiftUiState> build, Action<FirstShiftPrivateSnapshotEvent, TSession> send)
    { _resolve = resolve; _available = available; _netEntity = netEntity; _build = build; _send = send; }

    public void RememberOpen(NetUserId user, EntityUid beacon) => _open[user] = beacon;
    public void Close(NetUserId user, EntityUid beacon)
    { if (_open.GetValueOrDefault(user) == beacon) _open.Remove(user); }
    public void CloseUser(NetUserId user) => _open.Remove(user);
    public void RemoveBeacon(EntityUid beacon)
    { foreach (var user in _open.Where(x => x.Value == beacon).Select(x => x.Key).ToArray()) _open.Remove(user); }
    public void Clear() { _open.Clear(); _generations.Clear(); }
    public bool HasOpenMapping(NetUserId user) => _open.ContainsKey(user);
    public EntityUid? GetOpenBeacon(NetUserId user) => _open.TryGetValue(user, out var beacon) ? beacon : null;
    public ulong CurrentGeneration(NetUserId user) => _generations.GetValueOrDefault(user);
    public bool IsCurrent(NetUserId user, EntityUid beacon, ulong generation) =>
        generation != 0 && _open.GetValueOrDefault(user) == beacon &&
        _generations.GetValueOrDefault(user) == generation;

    public void Publish(IEnumerable<NetUserId> users)
    {
        foreach (var user in users.Distinct())
        {
            if (!_open.TryGetValue(user, out var beacon)) continue;
            if (!_available(beacon)) { _open.Remove(user); continue; }
            if (!_resolve(user, out var session)) continue;
            var generation = _generations.GetValueOrDefault(user) + 1;
            _send(new FirstShiftPrivateSnapshotEvent(_netEntity(beacon), generation, _build(user)), session);
            _generations[user] = generation;
        }
    }
}

internal readonly record struct FirstShiftAnchorCandidate(
    int EntityId, string PrototypeId, int StationId, bool Enabled, bool Anchored);

internal static class FirstShiftAnchorResolver
{
    public static FirstShiftAnchorCandidate? Select(int stationId, IReadOnlyList<string> orderedAnchors,
        IEnumerable<FirstShiftAnchorCandidate> candidates)
    {
        foreach (var anchor in orderedAnchors)
        {
            var selected = candidates.Where(c => c.Enabled && c.Anchored && c.StationId == stationId && c.PrototypeId == anchor)
                .OrderBy(c => c.EntityId).Cast<FirstShiftAnchorCandidate?>().FirstOrDefault();
            if (selected != null) return selected;
        }
        return null;
    }
}
