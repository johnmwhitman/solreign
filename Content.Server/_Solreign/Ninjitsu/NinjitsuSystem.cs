using Content.Server.Actions;
using Content.Server.Administration.Logs;
using Content.Server.Ninja.Systems;
using Content.Shared._Solreign.Ninjitsu;
using Content.Shared.Database;
using Content.Shared.Movement.Systems;
using Content.Shared.Ninja.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Space Ninja ninjitsu flavor (roadmap wave 7+): three abilities layered on top of the
///     upstream Ninja/EnergyKatana systems (Content.Server/Ninja, Content.Shared/Ninja) and the
///     Way of the Ornamental Carp combo core (Content.Server._Solreign.MartialArts) without
///     modifying either — same "standalone component + system" idiom as upstream's own
///     <c>DashAbilityComponent</c> on the Energy Katana.
///
///     1. Smoke Vanish (partial: NinjitsuSystem.Vanish.cs) — suit-tech gadget, gated behind
///        <see cref="NinjitsuGearComponent"/> on the suit (like EMP/throwing star): pops a real
///        <c>Smoke</c> cloud plus a brief personal stealth + speed window.
///     2. Silent Step (partial: NinjitsuSystem.SilentStep.cs) — innate toggle, granted directly to
///        the ninja at <see cref="OnNinjaStartup"/> regardless of gear: suppresses the wearer's own
///        footstep sound by owning a body-level <c>FootstepModifierComponent</c>. Verified upstream
///        already puts one on the ninja's own boots (suits.yml <c>ClothingShoesSpaceNinja</c>,
///        <c>footstepSoundCollection: null</c>) — this generalizes that so silence survives losing
///        the boots (a ninja who gets stripped is still a ninja).
///     3. Carp-style nonlethal takedown (partial: NinjitsuSystem.Takedown.cs) — two unarmed strikes
///        on the same target inside a window drop them, reusing <c>CarpComboRules</c>'s window and
///        finisher-cooldown math directly (see <see cref="NinjitsuRules"/>'s doc comment).
///
///     Solreign twist: every ability use gets logged via <see cref="LedgerLog"/> as an "unexplained
///     discrepancy" — flavor only (admin log text), no dependency on <c>SeasonLedgerSystem</c> or the
///     Season 1 "Ledger Wakes" game rule; those are a different lane's files and aren't touched here.
/// </summary>
public sealed partial class NinjitsuSystem : SharedNinjitsuGearSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SpaceNinjaSystem _ninja = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpaceNinjaComponent, ComponentStartup>(OnNinjaStartup);

        InitializeVanish();
        InitializeSilentStep();
        InitializeTakedown();
    }

    /// <summary>Grants the innate ninjitsu skills (Silent Step + takedown chain state) the moment someone becomes a Space Ninja.</summary>
    private void OnNinjaStartup(Entity<SpaceNinjaComponent> ent, ref ComponentStartup args)
    {
        var training = EnsureComp<NinjitsuTrainingComponent>(ent.Owner);
        _actions.AddAction(ent.Owner, ref training.SilentStepActionEntity, training.SilentStepAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // Smoke Vanish windows politely excuse themselves (same idiom as SolreignMoonTouchedComponent).
        var vanished = EntityQueryEnumerator<NinjitsuVanishComponent>();
        while (vanished.MoveNext(out var uid, out var vanish))
        {
            if (NinjitsuRules.VanishActive(now, vanish.ExpiresAt))
                continue;

            EndVanish((uid, vanish));
        }
    }

    /// <summary>
    ///     Admin-log helper for the Solreign twist: ninjitsu actions are filed as "unexplained
    ///     discrepancies" rather than getting their own persistent Ledger entry — flavor text only,
    ///     the same footprint as any other admin log line.
    /// </summary>
    private void LedgerLog(LogType type, string message)
    {
        _adminLogger.Add(type, LogImpact.Low, $"[Ledger: unexplained discrepancy] {message}");
    }
}
