namespace Content.Server._Solreign.Season1;

/// <summary>
///     Pure classification for the storage API's two distinct successful
///     postconditions: contained, or dropped beside an open container.
/// </summary>
public static class SolreignGhostCabinetsDeliveryRules
{
    public static SolreignGhostCabinetsDeliveryOutcome ClassifyInsertion(
        bool insertReturned,
        bool contained)
    {
        if (!insertReturned)
            return SolreignGhostCabinetsDeliveryOutcome.InsertFailed;

        return contained
            ? SolreignGhostCabinetsDeliveryOutcome.Inserted
            : SolreignGhostCabinetsDeliveryOutcome.DroppedAdjacent;
    }
}
