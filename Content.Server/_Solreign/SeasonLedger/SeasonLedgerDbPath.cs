using System.IO;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Single source of truth for where the Season Ledger SQLite file lives. Every consumer that
///     opens the ledger (<see cref="SeasonLedgerSystem"/>, <c>WingmateSystem</c>) MUST resolve
///     through here so they always share one file — and so the
///     <see cref="CCVars.SolreignSeasonLedgerDbPath"/> test seam redirects all of them together
///     (integration tests give each pooled server instance a unique temp file to keep round-id
///     replay protection from colliding across instances/runs).
/// </summary>
public static class SeasonLedgerDbPath
{
    public static string Resolve(IConfigurationManager cfg, IResourceManager res)
    {
        var overridePath = cfg.GetCVar(CCVars.SolreignSeasonLedgerDbPath);
        if (!string.IsNullOrEmpty(overridePath))
            return overridePath;

        var dir = res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        return Path.Combine(dir, "solreign_season_ledger.db");
    }
}
