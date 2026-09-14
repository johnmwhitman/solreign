using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Per-station Solreign contract pool — the <c>StationCargoBountyDatabaseComponent</c> shape (spec
///     §2.1). Ensured onto every station at post-init by <c>ContractsSystem</c> (the additive-fork idiom:
///     no upstream station prototype is touched). Caps are anti-grief rule 7: they prevent
///     contract-hoarding denial.
/// </summary>
[RegisterComponent]
public sealed partial class StationSolreignContractsComponent : Component
{
    /// <summary>Maximum personal contracts active on the board at once (anti-grief rule 7).</summary>
    [DataField]
    public int MaxPersonal = 6;

    /// <summary>Maximum department contracts active at once (anti-grief rule 7; Milestone 2 scope).</summary>
    [DataField]
    public int MaxDepartment = 4;

    /// <summary>Maximum salvage raids active at once (rule 7 extended to the new category).</summary>
    [DataField]
    public int MaxSalvage = 2;

    /// <summary>The live contracts on this station's boards. Round-scoped, never persisted (spec §3.3).</summary>
    [ViewVariables]
    public readonly List<SolreignActiveContract> Contracts = new();

    /// <summary>Running counter of work orders ever issued this round (diagnostics).</summary>
    [ViewVariables]
    public int TotalIssued;

    /// <summary>Guards the once-per-station diagnostic for an empty issuable EasyTier pool.</summary>
    [ViewVariables]
    public bool LowPopEasyContentGapLogged;

    /// <summary>The time at which players will be able to skip the next contract (station-wide, bounty idiom).</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextSkipTime = TimeSpan.Zero;

    /// <summary>The cooldown between skips — anti-grief rule 6: boards can't be spun for the juiciest contract.</summary>
    [DataField]
    public TimeSpan SkipDelay = TimeSpan.FromMinutes(15);
}
