using System;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     Pure copy-selection tables for the Mark — the loc-key side of the copy pack
///     (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §8), unit-tested in
///     Content.Tests/_Solreign/MarkCopyTests.cs. No ECS, no I/O, no Loc — this class only picks
///     KEYS; the systems (MG-W2/W4) render them. The backing
///     <c>Resources/Locale/en-US/_solreign/mark.ftl</c> lands with MG-W2 (the garden wave — W1
///     ships zero player-facing text); the W2 copy test extends the key-existence and
///     closed-vocabulary assertions over the actual templates, the FirstDeathCopyTests idiom.
///
///     Closed vocabulary (spec §8): the only Fluent variables the whole pack may use are
///     <c>$name</c>, <c>$kind</c>, <c>$cm</c>, <c>$days</c>. <c>$stage</c>/<c>$tours</c> are
///     contract-reserved but deliberately UNUSED by any template. <c>$name</c> may appear ONLY in
///     owner-private lines (C1/C3) — never in a public stage or stranger string (rail 5, enforced
///     as a denylist test once the templates land).
/// </summary>
public static class MarkCopy
{
    /// <summary>The closed Fluent variable vocabulary for the whole pack (spec §8).</summary>
    public static readonly string[] AllowedVariables = { "name", "kind", "cm", "days" };

    /// <summary>C5 — the garden fixture's display-name loc key ("Continuity Garden").</summary>
    public const string GardenNameKey = "solreign-mark-garden-name";

    /// <summary>C5 — the garden fixture's examine/description loc key.</summary>
    public const string GardenDescriptionKey = "solreign-mark-garden-desc";

    /// <summary>C3 — owner-examine suffixes (variables: $name; owner-private surface).</summary>
    public static readonly string[] OwnerSuffixKeys =
    {
        "solreign-mark-examine-owner-1",
        "solreign-mark-examine-owner-2",
    };

    /// <summary>C4 — stranger-examine suffixes (NO variables, NO name — public surface).</summary>
    public static readonly string[] StrangerSuffixKeys =
    {
        "solreign-mark-examine-stranger-1",
        "solreign-mark-examine-stranger-2",
    };

    /// <summary>
    ///     The private refusal for a plant attempt by an account that already holds its one mark
    ///     (the spec §2 "already claimed" quiet no-op — a racing double-plant's loser lands here
    ///     too). W2 addition to the W1 tables, same closed-vocabulary contract (no variables).
    /// </summary>
    public const string AlreadyClaimedKey = "solreign-mark-plant-already";

    /// <summary>C1 — generic placement confirmations (variables: $kind; private).</summary>
    public static readonly string[] GenericPlantConfirmKeys =
    {
        "solreign-mark-plant-confirm-generic-1",
        "solreign-mark-plant-confirm-generic-2",
    };

    /// <summary>C2 — generic return-visit alternates (variables: $kind/$cm/$days; private).</summary>
    public static readonly string[] GenericReturnKeys =
    {
        "solreign-mark-return-generic-1",
        "solreign-mark-return-generic-2",
    };

    /// <summary>C6 — overflow lines (record exists, no physical slot this round; private).</summary>
    public static readonly string[] OverflowKeys =
    {
        "solreign-mark-overflow-1",
        "solreign-mark-overflow-2",
    };

    /// <summary>C7 — the once-ever first-return nudge lines (variables: $kind; private).</summary>
    public static readonly string[] NudgeKeys =
    {
        "solreign-mark-nudge-1",
        "solreign-mark-nudge-2",
    };

    /// <summary>
    ///     The examine stage line for a kind at a stage (spec §8A: 3 kinds x 4 stages, no variables —
    ///     a PUBLIC surface). Stage must be a valid closed-table index (0..<see cref="MarkAgeRules.MaxStage"/>).
    /// </summary>
    public static string StageKeyFor(MarkKind kind, int stage)
    {
        if (stage < 0 || stage > MarkAgeRules.MaxStage)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "Mark stage outside the closed 0..3 table");

        return $"solreign-mark-stage-{KindSlug(kind)}-{stage}";
    }

    /// <summary>C1 — the kind-true placement confirmation ("Your sapling is planted. ...").</summary>
    public static string PlantConfirmKeyFor(MarkKind kind)
    {
        return $"solreign-mark-plant-confirm-{KindSlug(kind)}";
    }

    /// <summary>
    ///     C2 — the kind-true return line, unit word baked into the template (cm / lumens / sheen
    ///     riding one shared scalar; variables: $cm). The canonical beat:
    ///     "Your sapling has grown {$cm} cm. I have been watering it. You are welcome."
    /// </summary>
    public static string ReturnKeyFor(MarkKind kind)
    {
        return $"solreign-mark-return-{KindSlug(kind)}";
    }

    /// <summary>The display word for a kind ("sapling" / "lamp" / "name-plate") — renders $kind.</summary>
    public static string KindWordKeyFor(MarkKind kind)
    {
        return $"solreign-mark-kind-{KindSlug(kind)}";
    }

    /// <summary>
    ///     The garden alt-verb label for a kind ("Plant a sapling" / "Light a lamp" / "Seat a
    ///     name-plate" — spec §2's three planting verbs). W2 addition, no variables.
    /// </summary>
    public static string VerbKeyFor(MarkKind kind)
    {
        return $"solreign-mark-verb-{KindSlug(kind)}";
    }

    /// <summary>
    ///     Deterministic variant pick over a key table — the RehireKeyFor idiom (non-negative mod, so
    ///     any int seed, including negatives, lands in range). Same seed, same key, forever.
    /// </summary>
    public static string Pick(string[] keys, int seed)
    {
        if (keys.Length == 0)
            throw new ArgumentException("Key table must be nonempty.", nameof(keys));

        var count = keys.Length;
        return keys[(seed % count + count) % count];
    }

    private static string KindSlug(MarkKind kind)
    {
        return kind switch
        {
            MarkKind.Sapling => "sapling",
            MarkKind.Lamp => "lamp",
            MarkKind.NamePlate => "plate",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown mark kind"),
        };
    }
}
