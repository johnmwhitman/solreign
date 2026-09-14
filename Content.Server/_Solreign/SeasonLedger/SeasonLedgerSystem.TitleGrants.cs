using System;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Network;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Admin-granted titles (community rewards program — the website /rewards page hands out real
///     in-game titles). This partial owns the grant/revoke orchestration used by the
///     <c>solreign_grant_title</c> / <c>solreign_revoke_title</c> console commands: persist via the
///     store, write the paper trail (admin log + server log), and — when the target is online with a
///     mob — refresh their examine title immediately through the existing LoadTitle enqueue path, so
///     no entity is ever mutated off the main thread.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    /// <summary>
    ///     Grants <paramref name="title"/> (already validated by <see cref="TitleGrantRules.TryNormalize"/>)
    ///     to the account, replacing any prior grant this season. Persists across rounds like earned titles
    ///     and surfaces on examine at next spawn — or immediately if the target is online.
    /// </summary>
    public async Task GrantAdminTitleAsync(Guid user, string targetName, string title, string grantedBy)
    {
        // Normalize HERE too (the store re-validates as defense in depth): the paper trail below must
        // record exactly the form that gets persisted, even for future non-console callers that skip the
        // command's pre-validation.
        if (!TitleGrantRules.TryNormalize(title, out var normalized, out var problem))
            throw new ArgumentException($"Granted title rejected: {problem}", nameof(title));

        await _store.SetAdminTitleAsync(user, normalized, grantedBy);

        // The grant is durably committed from here on: post-commit steps (paper trail, live refresh) are
        // best-effort and must never bubble an exception that would make the console report "nothing was
        // recorded" about a grant that IS on disk. The row itself carries granted_by/granted_utc, so the
        // audit survives even if both log sinks fail.
        try
        {
            var audit = TitleGrantRules.FormatGrantAudit(grantedBy, targetName, user, normalized);
            Log.Info(audit);
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"{audit}");
        }
        catch (Exception e)
        {
            Log.Error($"Paper-trail write failed for title grant (grant IS persisted): {e}");
        }

        RefreshTitleIfOnline(user);
    }

    /// <summary>
    ///     Revokes the account's admin-granted title for the current season. Returns false when there was
    ///     nothing to revoke (no paper trail is written in that case — nothing happened).
    /// </summary>
    public async Task<bool> RevokeAdminTitleAsync(Guid user, string targetName, string revokedBy)
    {
        if (!await _store.RevokeAdminTitleAsync(user))
            return false;

        // Same post-commit contract as GrantAdminTitleAsync: the delete is durable, everything after is
        // best-effort and must not make the console misreport a revocation that DID happen.
        try
        {
            var audit = TitleGrantRules.FormatRevokeAudit(revokedBy, targetName, user);
            Log.Info(audit);
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"{audit}");
        }
        catch (Exception e)
        {
            Log.Error($"Paper-trail write failed for title revoke (revoke IS persisted): {e}");
        }

        RefreshTitleIfOnline(user);
        return true;
    }

    /// <summary>
    ///     If the account is connected with a spawned mob, re-run the async title load DISPLAY-ONLY
    ///     (allowCeremony: false) so the grant/revocation shows on examine without waiting for a respawn.
    ///     Display-only matters: a full reload racing a concurrent spawn load could double-fire the
    ///     earned-title ceremony and its one-time HR bonus, since both read the announced-title record
    ///     before either writes it. LoadTitle only ever enqueues — Update applies the component change on
    ///     the main thread, same as the spawn path. Best-effort: never throws past this method, so a
    ///     refresh failure can't make the console misreport a committed grant/revoke.
    /// </summary>
    private void RefreshTitleIfOnline(Guid user)
    {
        try
        {
            if (_playerManager.TryGetSessionById(new NetUserId(user), out var session) &&
                session.AttachedEntity is { } mob)
            {
                LoadTitle(mob, user, allowCeremony: false);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Live title refresh failed (the grant/revoke IS persisted; shows on next spawn): {e}");
        }
    }
}
