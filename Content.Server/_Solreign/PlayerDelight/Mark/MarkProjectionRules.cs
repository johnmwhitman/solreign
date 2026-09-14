using System;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     Pure (kind, stage) → prototype-id mapping for the closed 3x4 mark table
///     (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §4.1). The table is closed BY CONSTRUCTION: three
///     kinds, four stages, twelve prototype ids, nothing else — a ledger row outside the table
///     never spawns anything (callers parse the kind with <see cref="MarkKinds.TryParse"/> and skip
///     corrupt rows). Unit-tested against the YAML ids in
///     Content.Tests/_Solreign/MarkProjectionTests.cs.
/// </summary>
public static class MarkProjectionRules
{
    /// <summary>
    ///     The entity prototype id for a kind at a stage (<c>SolreignMarkSapling0</c> ..
    ///     <c>SolreignMarkPlate3</c> — the mark_garden.yml set).
    /// </summary>
    public static string PrototypeFor(MarkKind kind, int stage)
    {
        if (stage < 0 || stage > MarkAgeRules.MaxStage)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "Mark stage outside the closed 0..3 table");

        var slug = kind switch
        {
            MarkKind.Sapling => "Sapling",
            MarkKind.Lamp => "Lamp",
            MarkKind.NamePlate => "Plate",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown mark kind"),
        };

        return $"SolreignMark{slug}{stage}";
    }
}
