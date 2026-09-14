namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Delight-eggs batch (feat/delight-eggs): pays off SolreignEgg19's existing flavor text
///     ("crackles with static whenever bad vibes ... are nearby") — until this system, that promise
///     had no wiring at all. Use-in-hand triggers a one-shot proximity scan, same idiom as
///     <c>SolreignWristOrganizerComponent</c>'s self-scan: no DoAfter, no target, no
///     BoundUserInterface, cooldown-gated, holder-only popup.
///
///     REDESIGNED per the orchestrator's round-integrity review (2026-07-16): the signal is now
///     ambiguous (several supernatural-anchor source types, not just antags), probabilistic (misses
///     near real sources, false-positives with nothing around), and seeded per scan-window so rapid
///     re-use can't re-roll it — see <see cref="SolreignStaticReceiverRules"/>'s doc comment for the
///     full model. Atmosphere, not radar.
///
///     Server-only, same reasoning as <c>SolreignWristOrganizerComponent</c>: the whole effect is a
///     popup through existing shared systems, so nothing here needs its own networking.
/// </summary>
[RegisterComponent, Access(typeof(SolreignStaticReceiverSystem))]
public sealed partial class SolreignStaticReceiverComponent : Component
{
    /// <summary>
    ///     Seconds between scans. Review floor is 60s — long enough that a player can't sweep a
    ///     room by spamming use-in-hand, and it doubles as the deterministic roll's time-bucket
    ///     length (scans within one cooldown window share their roll).
    /// </summary>
    [DataField]
    public TimeSpan ScanCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Radius (in tiles) the proximity scan checks for supernatural-anchor sources.</summary>
    [DataField]
    public float ScanRadius = 5f;

    /// <summary>Chance (0-1) a scan crackles when at least one source is actually in range.</summary>
    [DataField]
    public float NearbyCrackleChance = 0.6f;

    /// <summary>Chance (0-1) a scan false-crackles with no source in range at all.</summary>
    [DataField]
    public float FalseCrackleChance = 0.12f;

    /// <summary>Chance (0-1) that a crackle uses the rare "almost-words" line instead of a common variant.</summary>
    [DataField]
    public float RareVariantChance = 0.15f;

    /// <summary>Game time at which the next scan may fire. Server-side scheduling state only — not saved, not networked.</summary>
    [ViewVariables]
    public TimeSpan NextScanTime;
}
