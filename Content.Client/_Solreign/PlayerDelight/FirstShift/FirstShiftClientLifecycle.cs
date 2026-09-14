using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.PlayerDelight.FirstShift;

/// <summary>Orders private-view teardown before cache invalidation on beacon removal.</summary>
public static class FirstShiftClientLifecycle
{
    public static void BeaconShutdown(NetEntity beacon, Action<NetEntity> clearOpenView,
        Action<NetEntity> invalidateCache)
    {
        clearOpenView(beacon);
        invalidateCache(beacon);
    }
}
