using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     ALIVENESS P1 #7 — PROVIDENCE low-pop cadence clamp. Own partial file per this codebase's
///     D0 collision-control convention (see <c>CCVars.SolreignLowPop.cs</c>'s doc comment for the
///     same reasoning).
///
///     When the connected player count is at or below this threshold, the idle-musings window
///     (<c>Content.Server._Solreign.Providence.ProvidenceVoiceSystem</c>) WIDENS to 30-60
///     minutes — the station voice holds back as the station empties. Above the threshold the
///     default 20-40 minute cadence applies unchanged. Same one-CVar-threshold idiom as
///     <see cref="CCVars.SolreignLowPopLobbyReminderThreshold"/>. Evaluated when each idle
///     firing is scheduled (round start and after every musing), so cadence returns to default
///     within one cycle of the population rising past the threshold.
///
///     DIRECTION REVERSED 2026-07-22. This clamp originally ran the other way: 8-15 minutes at
///     low pop, on the reasoning that a solo player's one ambient companion should not fall
///     silent for 40 minutes. The note this comment used to end on — "No new VO — the existing
///     idle-musings collection just plays more often" — turned out to be the whole problem. A
///     real player on a near-empty server reported the voice as irritating, repetitive and out
///     of place. With roughly four lines in the collection, playing it more often does not read
///     as company; it reads as a stuck loop. Widening the window is the cheap half of the fix.
///     The other half is more VO in the idle collection, which this CVar cannot substitute for.
/// </summary>
public sealed partial class CCVars
{
    public static readonly CVarDef<int> SolreignProvidenceLowPopCadenceThreshold =
        CVarDef.Create("solreign.providence.lowpop_cadence_threshold", 3, CVar.SERVERONLY);
}
