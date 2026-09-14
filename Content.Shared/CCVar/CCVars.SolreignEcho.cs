using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Solreign "Echoes of the Departed" CVars (v14 wave, EOD-spec §10), split into their own
///     partial file rather than appending to <c>CCVars.Solreign.cs</c> — the same D0
///     collision-control guidance <c>CCVars.SolreignMark.cs</c> documents: a new, narrowly-scoped
///     CVar family gets its own file so a concurrent worktree editing the shared CCVars files
///     never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the Echo feature: round-start projection of already-claimed
    ///     <c>first_death</c> rows into the Continuity Garden as PROVIDENCE-authored grave
    ///     markers. ENABLED 2026-07-25 (activation pass) — activated 2026-07-25, and independent of
    ///     <see cref="SolreignMarkEnabled"/> (a server may run one feature without the other).
    ///     Off: <c>first_death</c> rows persist untouched (already true today, unconditionally),
    ///     nothing reads them for Echo purposes, zero behavior change from today.
    /// </summary>
    public static readonly CVarDef<bool> SolreignEchoEnabled =
        CVarDef.Create("solreign.echo.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Physical projection capacity: how many claimed first-death rows materialize in the
    ///     garden each round, most-recent-death-first (the opposite of Mark's seniority-first —
    ///     Echoes are ambient, rotating flavor, not a player-earned trophy shelf). Smaller than
    ///     <see cref="SolreignMarkSlots"/>'s 24 default by design: a Garden wall-to-wall with
    ///     grave markers stops reading as "haunted" and starts reading as "cluttered".
    ///     Clamped at read to the garden's <c>EchoSlotOffsets.Count</c> (default bed: 10 —
    ///     <c>MarkGardenLayout.EchoColumns * EchoRows</c>); an oversize value must never stack
    ///     multiple physics entities onto the same modulo-wrapped tile.
    /// </summary>
    public static readonly CVarDef<int> SolreignEchoSlots =
        CVarDef.Create("solreign.echo.slots", 10, CVar.SERVERONLY);
}
