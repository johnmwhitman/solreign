using System.Diagnostics.CodeAnalysis;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Solreign.Director;

/// <summary>
///     Shared death-attribution helpers for Director-channel telemetry (Rivalry, Crypt). The
///     Director daemon keys standing, feuds, and obituaries by PLAYER GUIDs
///     (<see cref="ICommonSession.UserId"/>) — the same identity the Oracle webhook and the
///     Season Ledger report — never by EntityUids, which are round-local and recycled. Sending
///     an EntityUid where the daemon expects a player GUID silently breaks every cross-round
///     join (standing lookups, feud persistence, obituary attribution).
/// </summary>
public static class DeathAttribution
{
    /// <summary>
    ///     Resolves the GUID of the player currently controlling <paramref name="entity"/>, if any.
    /// </summary>
    public static bool TryGetPlayerGuid(IEntityManager entMan, EntityUid entity, [NotNullWhen(true)] out string? guid)
    {
        guid = null;

        if (!entMan.TryGetComponent<ActorComponent>(entity, out var actor))
            return false;

        guid = actor.PlayerSession.UserId.UserId.ToString();
        return true;
    }

    /// <summary>
    ///     Resolves the attacking PLAYER's GUID from a <c>MobStateChangedEvent</c> origin
    ///     (Codex MEDIUM #7 lineage: direct melee/interaction origins carry
    ///     <see cref="ActorComponent"/> directly; projectile kills carry the projectile entity as
    ///     origin and resolve one hop through <see cref="ProjectileComponent.Shooter"/>). Anything
    ///     else (turret/gun entities with no recorded player shooter, environmental damage, a
    ///     disconnected/deleted origin) intentionally fails to resolve — there is no attacker to
    ///     attribute, so report none rather than guessing.
    /// </summary>
    public static bool TryResolveAttackerGuid(IEntityManager entMan, EntityUid? origin, [NotNullWhen(true)] out string? guid)
    {
        guid = null;

        if (origin is not { } originEnt || !entMan.EntityExists(originEnt))
            return false;

        if (TryGetPlayerGuid(entMan, originEnt, out guid))
            return true;

        if (entMan.TryGetComponent<ProjectileComponent>(originEnt, out var projectile)
            && projectile.Shooter is { } shooter
            && entMan.EntityExists(shooter))
        {
            return TryGetPlayerGuid(entMan, shooter, out guid);
        }

        return false;
    }
}
