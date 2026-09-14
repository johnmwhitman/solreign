using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Solreign session-telemetry CVars (ASK-3, pre-registered retention criteria in
///     SUCCESSION/devbus-acquisition-strategy-2026-07-26/H-XYHUR-ROUND2-AND-TELEMETRY-2026-08-03.md),
///     in their own partial file per the CCVars.SolreignMark.cs collision-control guidance.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the per-session activity ledger (data/session_telemetry.jsonl).
    ///     DARK BY DEFAULT: even when true, nothing is written unless
    ///     <see cref="SolreignSessionTelemetryPepper"/> is also non-empty — the hash has no key
    ///     without it, and an unkeyed ledger must not exist at all (fail closed, not fail plain).
    /// </summary>
    public static readonly CVarDef<bool> SolreignSessionTelemetryEnabled =
        CVarDef.Create("solreign.session_telemetry.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     HMAC key for the account hash. Set only in box config; never logged, never written
    ///     beside the hashes it keys. Rotating it unlinks all prior sessions by design.
    /// </summary>
    public static readonly CVarDef<string> SolreignSessionTelemetryPepper =
        CVarDef.Create("solreign.session_telemetry.pepper", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Seconds between activity samples. Clamped to [10, 120] at read.</summary>
    public static readonly CVarDef<int> SolreignSessionTelemetryIntervalSeconds =
        CVarDef.Create("solreign.session_telemetry.interval_s", 30, CVar.SERVERONLY);

    /// <summary>Rotate session_telemetry.jsonl when it reaches this size (bytes).</summary>
    public static readonly CVarDef<int> SolreignSessionTelemetryMaxBytes =
        CVarDef.Create("solreign.session_telemetry.max_bytes", 32 * 1024 * 1024, CVar.SERVERONLY);
}
