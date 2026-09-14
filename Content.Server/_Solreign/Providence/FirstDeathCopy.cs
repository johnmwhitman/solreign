using System;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure copy-selection tables for the authored first death — the loc-key side of the copy pack
///     (Resources/Locale/en-US/_solreign/first-death.ftl, spec §8), unit-tested in
///     Content.Tests/_Solreign/FirstDeathCopyTests.cs. No ECS, no I/O, no Loc — this class only picks
///     KEYS; the system renders them.
///
///     Closed vocabulary (spec §3.3/§7): the only Fluent variables the whole pack may use are
///     <c>$name</c>, <c>$tours</c>, <c>$title</c>, <c>$fee</c>. There is deliberately no
///     <c>$attacker</c> variable anywhere — the redaction is enforced by the template set itself.
/// </summary>
public static class FirstDeathCopy
{
    /// <summary>Sender label loc key for the eulogy PA announcement ("PROVIDENCE").</summary>
    public const string SenderKey = "solreign-first-death-sender";

    /// <summary>The death-moment private ghost line (spec §4.2) — variables: $name.</summary>
    public const string PrivateLineKey = "solreign-first-death-private-line";

    /// <summary>The literal fee string ("45cr") — the fee is fiction, by law (spec §4.4).</summary>
    public const string FeeKey = "solreign-first-death-fee";

    /// <summary>
    ///     Generic eulogies E1/E2/E8 (spec §8A). Staged in the .ftl per the pack, but not selected by
    ///     <see cref="EulogyKeyFor"/> in v1: the spec's rule is "cause-matched template if one exists,
    ///     else deterministic pick among the generics", and every cause in the closed
    ///     <see cref="FirstDeathCause"/> set has a matched template (E3-E7) — so the generics are
    ///     defensive fallback + staged variety for a later pass. Flagged in the build receipt.
    /// </summary>
    public static readonly string[] GenericEulogyKeys =
    {
        "solreign-first-death-eulogy-generic-1",
        "solreign-first-death-eulogy-generic-2",
        "solreign-first-death-eulogy-generic-3",
    };

    /// <summary>Rehire lines R1-R6 (spec §8C) — variables: $fee/$title/$tours (line-dependent).</summary>
    public static readonly string[] RehireKeys =
    {
        "solreign-first-death-rehire-1",
        "solreign-first-death-rehire-2",
        "solreign-first-death-rehire-3",
        "solreign-first-death-rehire-4",
        "solreign-first-death-rehire-5",
        "solreign-first-death-rehire-6",
    };

    /// <summary>Index into <see cref="RehireKeys"/> of R2, the waiver line.</summary>
    public const int WaiverRehireIndex = 1;

    /// <summary>
    ///     The eulogy template for a classified cause — always the cause-matched template (E3-E7);
    ///     every eulogy takes exactly one variable, $name.
    /// </summary>
    public static string EulogyKeyFor(FirstDeathCause cause)
    {
        return cause switch
        {
            FirstDeathCause.Violence => "solreign-first-death-eulogy-violence",
            FirstDeathCause.Vacuum => "solreign-first-death-eulogy-vacuum",
            FirstDeathCause.Burn => "solreign-first-death-eulogy-burn",
            FirstDeathCause.Misadventure => "solreign-first-death-eulogy-misadventure",
            _ => "solreign-first-death-eulogy-unknown",
        };
    }

    /// <summary>
    ///     The public display label for a classified cause (spec §8D's closed, display-only map) —
    ///     shipped to the Director daemon's crypt as <c>cause_label</c> (FD-W3, spec §5.1) and reused
    ///     by the Discord obituary embed (FD-W4). Closed vocabulary: never a forensic fact, never an
    ///     attacker reference — safe to publish for every classification, including a wrong one.
    /// </summary>
    public static string CauseLabelFor(FirstDeathCause cause)
    {
        return cause switch
        {
            FirstDeathCause.Violence => "Third-party liability event",
            FirstDeathCause.Vacuum => "Environmental / atmospheric event",
            FirstDeathCause.Burn => "Thermal compliance event",
            FirstDeathCause.Misadventure => "Misadventure",
            _ => "Pending classification",
        };
    }

    /// <summary>
    ///     The rehire line for a claimed first death — deterministic pick over R1-R6 keyed on the same
    ///     (tours + title.Length + causeOrdinal) hash the epitaph picker uses, EXCEPT R2 (the waiver)
    ///     is ALWAYS used when <paramref name="toursAtDeath"/> == 0: a day-one death earns the warmest
    ///     beat (spec §3.5/§8C — the first failure must become a keepsake, not a bill).
    /// </summary>
    public static string RehireKeyFor(int toursAtDeath, string title, FirstDeathCause cause)
    {
        if (toursAtDeath <= 0)
            return RehireKeys[WaiverRehireIndex];

        title ??= string.Empty;
        var count = RehireKeys.Length;
        var index = ((toursAtDeath + title.Length + (int) cause) % count + count) % count;
        return RehireKeys[index];
    }
}
