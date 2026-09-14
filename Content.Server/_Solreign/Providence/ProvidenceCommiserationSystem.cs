using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Providence's "death commiseration" beat (roadmap D2.2: "Providence death commiseration wired
///     with anti-fatigue rules"). On a player's death, Providence has a chance to privately offer one
///     of a handful of dark-corporate-comforting lines (see
///     Resources/Locale/en-US/_solreign/providence-commiseration.ftl) alongside its existing
///     <see cref="ProvidenceLineCategory.DeathCommiseration"/> voice-pack audio, which was already
///     staged in <c>providence_sounds.yml</c>/<c>ProvidenceVoiceMap</c> with no call site until now.
///     Cosmetic flavor only — never affects gameplay, revival, or scoring.
///
///     Anti-fatigue (required by the roadmap, not optional polish) lives entirely in the pure
///     <see cref="ProvidenceCommiserationGate"/>, unit-tested in isolation
///     (Content.Tests/_Solreign/ProvidenceCommiserationGateTests.cs). This system is thin ECS glue over
///     it, same split as <c>FeedbackRateLimiter</c>/<c>FeedbackSystem</c> and
///     <c>PeriodicEffectTiming</c>/<c>SolreignPeriodicEffectSystem</c>:
///       * Hard cap of one SUCCESSFUL commiseration per player per round, reset on every round
///         start/restart — same Dictionary/HashSet-then-Clear-on-round-boundary idiom as
///         <c>SeasonLedgerSystem.EarlyDeath.cs</c>.
///       * A per-death coin flip (<see cref="CCVars.SolreignProvidenceCommiserationChance"/>, default
///         50%) gates whether an eligible death actually fires — a miss does not consume the per-round
///         slot, so a player who dies again later in the round gets another roll.
///
///     Gated by <see cref="CCVars.SolreignProvidenceCommiserationEnabled"/> (default on) AND the base
///     station voice's own <see cref="ProvidenceVoiceSystem.Enabled"/> switch — either one being off
///     silences this feature entirely. There is deliberately no always-on text-only fallback when the
///     voice is disabled: unlike the title-ceremony bulletin (whose TEXT is the primary payload, with
///     audio as a bonus sting), here the private popup is a caption for the audio line, not a
///     freestanding announcement, so showing it with no possible voice behind it would read as a
///     non-sequitur rather than a graceful degradation.
///
///     Delight-eggs batch (feat/delight-eggs): right after a successful commiseration,
///     <see cref="OnMobStateChanged"/> offers <see cref="ProvidenceSecondCondolenceSystem"/> a chance
///     to schedule a rare, delayed follow-up for the SAME player — see that system's doc comment.
///     This system's own once-per-round cap is what guarantees a player can never be offered a
///     follow-up more than once a round.
/// </summary>
public sealed partial class ProvidenceCommiserationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;
    [Dependency] private ProvidenceSecondCondolenceSystem _secondCondolence = default!;

    /// <summary>
    ///     Loc keys for the 8 varied commiseration popup lines, in providence-commiseration.ftl. Chosen
    ///     independently of which of the 3 audio files <c>ProvidenceVoiceSystem.PlayLine</c> happens to
    ///     pick — there's no mechanism to sync a specific text line to a specific audio file, same
    ///     limitation every other multi-line Providence category already has. Fine for v1 flavor.
    /// </summary>
    private static readonly string[] LineKeys =
    {
        "solreign-providence-commiseration-1",
        "solreign-providence-commiseration-2",
        "solreign-providence-commiseration-3",
        "solreign-providence-commiseration-4",
        "solreign-providence-commiseration-5",
        "solreign-providence-commiseration-6",
        "solreign-providence-commiseration-7",
        "solreign-providence-commiseration-8",
    };

    /// <summary>The pure anti-fatigue gate — see its own doc comment for the full rule.</summary>
    private readonly ProvidenceCommiserationGate _gate = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceCommiserationEnabled"/>.</summary>
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignProvidenceCommiserationEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _gate.Reset();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _gate.Reset();
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (!_enabled || !_providence.Enabled)
            return;

        if (ev.NewMobState != MobState.Dead)
            return;

        // Resolve the mob back to the controlling account via its mind — same key the anti-fatigue
        // gate (and the Season Ledger's early-death tracker) is keyed on, so the cap survives body
        // transfer/cryo/reconnect within a round.
        if (!_mind.TryGetMind(ev.Target, out _, out var mind))
            return;

        if (mind.UserId is not { } user)
            return;

        // Hard cap first (cheap, no RNG spent) — if this player already got their one commiseration
        // this round, stop before even rolling.
        if (!_gate.CanFire(user))
            return;

        var chance = _cfg.GetCVar(CCVars.SolreignProvidenceCommiserationChance);
        if (!ProvidenceCommiserationGate.ShouldFire(_random.NextDouble(), chance))
            return;

        _gate.MarkFired(user);

        var line = LineKeys[_random.Next(LineKeys.Length)];
        _popup.PopupEntity(Loc.GetString(line), ev.Target, ev.Target, PopupType.Medium);
        _providence.PlayLine(ProvidenceLineCategory.DeathCommiseration);

        _secondCondolence.MaybeSchedule(user);
    }
}
