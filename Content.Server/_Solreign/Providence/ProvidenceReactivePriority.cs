namespace Content.Server._Solreign.Providence;

/// <summary>
///     Priority tier for a PROVIDENCE event-reactive line (feat/providence-event-reactive —
///     PROVIDENCE-VOICE-DESIGN.md). Used by <see cref="ProvidenceReactiveDispatchGate"/> to scale the
///     shared anti-spam cooldown: a rare, meaningful beat (Ledger memory, a round's death toll) is
///     allowed to break through sooner than routine chatter, and routine chatter waits the longest —
///     the design doc's "silence is what makes a line land" applied as an actual arithmetic rule
///     rather than a single flat cooldown for everything.
/// </summary>
public enum ProvidenceReactivePriority
{
    /// <summary>Routine, easily-repeated beats. Waits the longest after any other line.</summary>
    Low,

    /// <summary>The default tier — ordinary event-reactive commentary.</summary>
    Normal,

    /// <summary>Rare, meaningful beats (Ledger-sourced memory, round-end summary). Allowed to speak
    /// again soonest after a prior line.</summary>
    High,
}
