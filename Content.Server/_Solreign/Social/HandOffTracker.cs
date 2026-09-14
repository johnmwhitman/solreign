using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Social;

/// <summary>
///     Pure round-local "item handed to you" resolution (no ECS, no clock — unit-tested in
///     Content.Tests/_Solreign/HandOffTrackerTests.cs). This fork has no offer/give verb, so a
///     hand-off is detected as the composed gesture players actually use: an item leaves account
///     A's hands (drop, throw, or a strip-menu placement) and lands in account B's hands within
///     <see cref="_window"/>. All three transfer styles funnel through the same two hand events
///     (<c>DidUnequipHandEvent</c>/<c>DidEquipHandEvent</c>), so one tracker covers them all.
///
///     One release entry per item (a newer release overwrites — the LAST holder is the giver).
///     <see cref="TryTakeGiver"/> always consumes the entry for the equipped item: picking your own
///     item back up simply clears it (no milestone, and no stale giver left behind to mis-credit a
///     later pickup).
/// </summary>
public sealed class HandOffTracker
{
    private readonly TimeSpan _window;

    private readonly Dictionary<EntityUid, Release> _releases = new();

    private readonly record struct Release(Guid Giver, TimeSpan At);

    public HandOffTracker(TimeSpan window)
    {
        _window = window;
    }

    /// <summary>Tracked in-flight releases — test visibility only.</summary>
    public int PendingCount => _releases.Count;

    /// <summary>Records that <paramref name="giver"/>'s hands released <paramref name="item"/>.</summary>
    public void RecordRelease(EntityUid item, Guid giver, TimeSpan now)
    {
        _releases[item] = new Release(giver, now);
    }

    /// <summary>
    ///     Resolves (and consumes) the giver for an item that just landed in
    ///     <paramref name="receiver"/>'s hands. True only when a release exists, is inside the
    ///     window, and came from a DIFFERENT account than the receiver.
    /// </summary>
    public bool TryTakeGiver(EntityUid item, Guid receiver, TimeSpan now, out Guid giver)
    {
        giver = default;

        if (!_releases.Remove(item, out var release))
            return false;

        if (now - release.At > _window)
            return false;

        if (release.Giver == receiver)
            return false;

        giver = release.Giver;
        return true;
    }

    /// <summary>Drops every release older than the hand-off window (bounds memory).</summary>
    public void Prune(TimeSpan now)
    {
        List<EntityUid>? stale = null;
        foreach (var (item, release) in _releases)
        {
            if (now - release.At > _window)
            {
                stale ??= new List<EntityUid>();
                stale.Add(item);
            }
        }

        if (stale == null)
            return;

        foreach (var item in stale)
        {
            _releases.Remove(item);
        }
    }

    /// <summary>Round-boundary reset — entities do not survive the round, neither may releases.</summary>
    public void Clear()
    {
        _releases.Clear();
    }
}
