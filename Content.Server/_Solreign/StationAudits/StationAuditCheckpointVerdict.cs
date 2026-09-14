using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     Pure, ECS-free reduction of a set of graded criterion verdicts into the one overall
///     <see cref="StationAuditConsequenceKind"/> PROVIDENCE reacts with — the mandatory same-session
///     consequence the council's Pope-test gate requires. No I/O, no loc; unit-tested directly.
/// </summary>
public static class StationAuditCheckpointVerdict
{
    /// <summary>
    ///     any assigned Fail -&gt; Escalation; else all assigned Pass (no NotApplicable, no Fail) -&gt;
    ///     Commendation; else (no Fail, but at least one NotApplicable) -&gt; QuietlyCorrectedError; an
    ///     empty verdict set (nothing was assigned/gradeable this shift) -&gt; None, the layer's own
    ///     no-op case.
    /// </summary>
    public static StationAuditConsequenceKind Determine(IReadOnlyList<StationAuditCriterionVerdict> verdicts)
    {
        if (verdicts.Count == 0)
            return StationAuditConsequenceKind.None;

        if (verdicts.Any(v => v == StationAuditCriterionVerdict.Fail))
            return StationAuditConsequenceKind.Escalation;

        return verdicts.All(v => v == StationAuditCriterionVerdict.Pass)
            ? StationAuditConsequenceKind.Commendation
            : StationAuditConsequenceKind.QuietlyCorrectedError;
    }
}
