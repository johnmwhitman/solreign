using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Onboarding;

/// <summary>
///     Solreign's station-wide "first five minutes" onboarding beat: a single corporate PA
///     announcement, fired once at round start from "Solreign HR", that welcomes the shift,
///     reinforces the megacorp-dystopia tone immediately (so a newly-arrived Reddit player doesn't
///     read the station as a generic SS14 box before the theme lands), and gives a one-line newcomer
///     orientation pointing at the PDA / Conduct Charter / Feedback tool. Cosmetic flavor only — never
///     affects gameplay, jobs, or scoring.
///
///     Deliberately its own, self-contained system so it never duplicates two adjacent, already-shipped
///     Solreign beats:
///       * <see cref="Content.Server._Solreign.Corporate.SolreignCorporateRuleSystem"/>'s round-start
///         "keynote" (also from Solreign HR, see <c>solreign-corporate-keynote</c> in corporate.ftl)
///         sets the satirical corporate-metrics tone but never mentions the PDA, Conduct Charter, or
///         Feedback tool — this system is the practical-orientation companion to that tone-setter, not
///         a replacement for it. Both fire on <see cref="RoundStartingEvent"/> and can be toggled
///         independently.
///       * <see cref="Content.Server._Solreign.Providence.ProvidenceWelcomeSystem"/> already owns the
///         PER-PLAYER first-shift popup (private, ledger-driven, "this station remembers you", plus a
///         voice sting). This system is the STATION-WIDE beat instead: one broadcast, no per-player
///         gating, no touching Providence's files. The two are additive, not overlapping — this is why
///         there is no second "first-time-connecting player" tip here: Providence's first-shift
///         induction line already is that tip.
///
///     One broadcast, no repeats, no per-player spam. Gated by
///     <see cref="CCVars.SolreignOnboardingBeatEnabled"/> (default on).
/// </summary>
public sealed partial class SolreignOnboardingSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>PA sender label for this beat's broadcast (onboarding.ftl's own key — see that file's
    /// doc header for why it isn't shared with corporate.ftl's sender key).</summary>
    private string HrSender => Loc.GetString("solreign-onboarding-hr-sender");

    /// <summary>Same brand gold used for the Corporate Ladder's own PA broadcasts — one voice, one color.</summary>
    private static readonly Color BeatColor = Color.FromHex("#c0a062");

    /// <summary>Cached mirror of <see cref="CCVars.SolreignOnboardingBeatEnabled"/>.</summary>
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignOnboardingBeatEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!_enabled)
            return;

        var line = SolreignOnboardingLines.Pick(_random.Next(SolreignOnboardingLines.Keys.Count));

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(line),
            HrSender,
            playSound: true,
            colorOverride: BeatColor);
    }
}
