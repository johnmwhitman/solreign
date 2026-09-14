namespace Content.Server._Solreign.DirectivesFax.Components;

/// <summary>
///     Data bag for the "Directives Fax" layerable game rule (v14 wave-1 item #1, council C1
///     einstein-001 "ship first"). Same idiom as
///     <c>Content.Server._Solreign.StationDirective.Components.StationDirectiveRuleComponent</c>: one
///     round's worth of state, set once in <c>DirectivesFaxRuleSystem.Started</c> and read back at
///     round end.
/// </summary>
[RegisterComponent, Access(typeof(DirectivesFaxRuleSystem))]
public sealed partial class DirectivesFaxRuleComponent : Component
{
    /// <summary>
    ///     The Station Directive id this shift's fax is for (see
    ///     <c>StationDirectiveSelection.SelectDirectiveIndex</c> — independently replayed here, not
    ///     read off the separate StationDirective rule's component; see class docs on
    ///     <see cref="DirectivesFaxRuleSystem"/> for why). Null until <c>Started</c> runs.
    /// </summary>
    [DataField]
    public string? DirectiveId;

    /// <summary>Whether the round-start fax has already been printed (guards a defensive double-call).</summary>
    [DataField]
    public bool FaxPrinted;

    /// <summary>Station Cargo-account balance snapshotted at <c>Started</c>, for the revenue-delta clause.</summary>
    [DataField]
    public int RoundStartCargoBalance;

    /// <summary>Station <c>NumOrdersCreated</c> snapshotted at <c>Started</c>, for the orders-delta clause.</summary>
    [DataField]
    public int RoundStartSupplyOrders;

    /// <summary>
    ///     Whether this round's directive outcome has already been computed and cached in
    ///     <see cref="OutcomeMet"/>. Set the first time EITHER <c>AppendRoundEndText</c> OR
    ///     <c>DirectivesFaxRuleSystem.Report.cs</c>'s <c>SolreignDirectiveOutcomeQueryEvent</c> handler
    ///     needs the outcome — whichever runs first (RobustToolbox gives no ordering guarantee between
    ///     independent <c>RoundEndTextAppendEvent</c> subscribers, and Station Audits' own subscriber
    ///     raises that query event from inside its own handler). Guards against recomputing
    ///     <c>DirectivesFaxClauseEvaluation.EvaluateAll</c> twice, never against the round-end
    ///     text/report itself being produced twice — see <see cref="RoundEndReportPrinted"/> for that.
    /// </summary>
    [DataField]
    public bool OutcomeComputed;

    /// <summary>The computed outcome: were every one of this shift's clauses met? Only meaningful once
    /// <see cref="OutcomeComputed"/> is true.</summary>
    [DataField]
    public bool OutcomeMet;

    /// <summary>Whether <c>AppendRoundEndText</c> has already produced this round's compliance text and
    /// spawned the stamped report paper (guards a defensive double-call — <c>RoundEndTextAppendEvent</c>
    /// is expected to fire exactly once for this rule). Deliberately a SEPARATE flag from
    /// <see cref="OutcomeComputed"/>: the outcome may already be cached by the time
    /// <c>AppendRoundEndText</c> first runs (if the query-event handler computed it first), but the
    /// text/paper still need to be produced exactly once regardless of which path got there first.</summary>
    [DataField]
    public bool RoundEndReportPrinted;
}
