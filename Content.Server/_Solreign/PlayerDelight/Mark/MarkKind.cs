using System;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     The closed set of Mark object kinds (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §4.1). Three
///     kinds, fixed forever in v1 — sapling (growth unit: cm), lamp (lumens), name-plate (sheen).
///     The closed set is the anti-grief rail: no player-supplied text, no free-form building — a
///     mark can only ever be one of these. (River-stone is banked in the spec as the natural fourth
///     kind if a later wave widens the menu; it is deliberately NOT here.)
/// </summary>
public enum MarkKind : byte
{
    Sapling,
    Lamp,
    NamePlate,
}

/// <summary>
///     Ledger string forms for <see cref="MarkKind"/> — the exact TEXT values the <c>mark</c> table's
///     <c>kind</c> column stores (SAPLING | LAMP | NAMEPLATE, spec §3.1). Pure, engine-free;
///     round-trip is unit-tested so a ledger row written today parses forever.
/// </summary>
public static class MarkKinds
{
    public const string SaplingLedger = "SAPLING";
    public const string LampLedger = "LAMP";
    public const string NamePlateLedger = "NAMEPLATE";

    /// <summary>Every kind, in enum order — the closed menu, for iteration in tests and verbs.</summary>
    public static readonly MarkKind[] All =
    {
        MarkKind.Sapling,
        MarkKind.Lamp,
        MarkKind.NamePlate,
    };

    /// <summary>The ledger TEXT for a kind — what <c>TryClaimMarkAsync</c> is handed.</summary>
    public static string ToLedgerString(MarkKind kind)
    {
        return kind switch
        {
            MarkKind.Sapling => SaplingLedger,
            MarkKind.Lamp => LampLedger,
            MarkKind.NamePlate => NamePlateLedger,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown mark kind"),
        };
    }

    /// <summary>
    ///     Parses a ledger TEXT back to its kind. Returns <c>false</c> for anything outside the
    ///     closed set — callers treat an unparseable row as corrupt and skip it rather than guess.
    /// </summary>
    public static bool TryParse(string? ledger, out MarkKind kind)
    {
        switch (ledger)
        {
            case SaplingLedger:
                kind = MarkKind.Sapling;
                return true;
            case LampLedger:
                kind = MarkKind.Lamp;
                return true;
            case NamePlateLedger:
                kind = MarkKind.NamePlate;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
