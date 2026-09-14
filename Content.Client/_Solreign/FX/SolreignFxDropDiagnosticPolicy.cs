using Content.Shared._Solreign.FX;

namespace Content.Client._Solreign.FX;

/// <summary>
///     Keeps ordinary lossy cosmetic delivery below warning severity without weakening the FX
///     receive trust boundary. Every ambiguous, confidential, or malformed case fails closed.
/// </summary>
internal static class SolreignFxDropDiagnosticPolicy
{
    internal const string ExpectedMissingBroadcastAnchorReason =
        "ValidateExpectedLossy:AnchorEntityUnresolvable";

    internal static bool IsExpectedMissingAnchorLoss(
        SolreignFxCueValidateFailureReason failure,
        bool classificationKnown,
        SolreignFxAudienceClassification classification,
        bool entityAnchorMissing)
    {
        return failure == SolreignFxCueValidateFailureReason.AnchorEntityUnresolvable
            && classificationKnown
            && classification == SolreignFxAudienceClassification.Broadcast
            && entityAnchorMissing;
    }

    internal static bool RequiresWarning(string? reason)
    {
        return reason != ExpectedMissingBroadcastAnchorReason;
    }
}
