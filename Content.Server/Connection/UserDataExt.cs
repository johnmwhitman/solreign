using Content.Shared.Database;
using Robust.Shared.Network;

namespace Content.Server.Connection;

/// <summary>
/// Helper functions for working with <see cref="NetUserData"/>.
/// </summary>
public static class UserDataExt
{
    /// <summary>
    /// Get the preferred HWID that should be used for new records related to a player.
    /// </summary>
    /// <remarks>
    /// Players can have zero or more HWIDs, but for logging things like connection logs we generally
    /// only want a single one. This method returns a nullable method.
    /// </remarks>
    public static ImmutableTypedHwid? GetModernHwid(this NetUserData userData)
    {
        // ModernHWIds is an init-only ImmutableArray<...> with no default-value initializer, so any
        // NetUserData built without explicitly setting it (e.g. DummySession's -- see
        // Robust.Shared.Player.DummySession) leaves it in the "default" (null-backed) state, not
        // ImmutableArray<T>.Empty. Indexing .Length on a default ImmutableArray throws
        // NullReferenceException, so that has to be checked for separately from "genuinely empty".
        return userData.ModernHWIds.IsDefaultOrEmpty
            ? null
            : new ImmutableTypedHwid(userData.ModernHWIds[0], HwidType.Modern);
    }
}
