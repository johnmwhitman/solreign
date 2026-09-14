using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Shared._Solreign.Corporate.Projects;

namespace Content.Server._Solreign.SeasonLedger;

public sealed partial class SeasonLedgerSystem
{
    public Task<CorporateProjectRecord?> GetCorporateProjectAsync(string projectId)
    {
        return _store.GetCorporateProjectRecordAsync(projectId);
    }

    public Task<Dictionary<string, CorporateProjectRecord>> GetAllCorporateProjectsAsync()
    {
        return _store.GetAllCorporateProjectRecordsAsync();
    }

    public Task<int> GetAccountProjectContributionAsync(string projectId, Guid user)
    {
        return _store.GetAccountContributionAsync(projectId, user);
    }

    public Task<List<CorporateProjectContributionRecord>> GetProjectContributorsAsync(string projectId)
    {
        return _store.GetProjectContributorsAsync(projectId);
    }

    public Task<CorporateProjectContributionResult> TryContributeToProjectAsync(
        string projectId,
        Guid user,
        int amount,
        SolreignCorporateProjectPrototype prototype)
    {
        var nowUtc = DateTime.UtcNow.ToString("o");
        return _store.TryContributeToProjectAsync(projectId, user, amount, nowUtc, prototype);
    }
}
