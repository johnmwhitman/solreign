using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     WRIST ORGANIZER server logic (roadmap: health-analyzer-style self readout on use, see
///     <see cref="SolreignWristOrganizerComponent"/>). Use-in-hand triggers a one-shot self-scan
///     popup — no DoAfter, no target, no BoundUserInterface: this is a wrist gadget you glance at,
///     not a clinical scanning tool.
///
///     Reuses the same data sources <c>Content.Server.Medical.HealthAnalyzerSystem</c> reads
///     (<see cref="DamageableComponent"/>, <see cref="MobThresholdSystem"/>) rather than
///     reimplementing its machinery. Cooldown/formatting math is pure and lives in
///     <see cref="SolreignWristOrganizerRules"/>
///     (Content.Tests/_Solreign/SolreignWristOrganizerRulesTests.cs) — see that class's doc comment
///     for why a radiation-dose line was scoped out (ANALYZER LAW: no owning-system read API).
///
///     W18 verifier fix: <c>DamageableComponent.TotalDamage</c> is analyzer-locked
///     (<c>[Access]</c> to <c>DamageableSystem</c> only, rwxrwx---) — RA0002 on a direct read.
///     Reads it through <see cref="DamageableSystem"/>'s own public <c>GetTotalDamage</c> instead,
///     the same owning-system API every other caller in the codebase uses for this field
///     (e.g. Content.Server/NPC/Systems/NPCUtilitySystem.cs, Content.Server/Spreader/KudzuSystem.cs).
///
///     Delight-eggs batch (feat/delight-eggs): the readout now drifts across a unit's own use count
///     this round (<see cref="SolreignWristOrganizerRules.DriftTierForPriorUses"/>) — the plain readout
///     first, a wry corporate-surveillance aside on the second use, then one cold personal observation
///     from the third use onward. Gated by <see cref="CCVars.SolreignDelightWristDriftEnabled"/>; off
///     leaves the original single-phrasing readout untouched regardless of use count.
/// </summary>
public sealed partial class SolreignWristOrganizerSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Tier-0 (second use) drift lines — picked at random, same "vary independently of the base readout" idiom as Providence's commiseration lines.</summary>
    private static readonly string[] DriftTierZeroKeys =
    {
        "solreign-wrist-organizer-drift-0-1",
        "solreign-wrist-organizer-drift-0-2",
    };

    /// <summary>Tier-1 (third-plus use) drift lines.</summary>
    private static readonly string[] DriftTierOneKeys =
    {
        "solreign-wrist-organizer-drift-1-1",
        "solreign-wrist-organizer-drift-1-2",
    };

    private bool _driftEnabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignDelightWristDriftEnabled, v => _driftEnabled = v, invokeImmediately: true);

        SubscribeLocalEvent<SolreignWristOrganizerComponent, UseInHandEvent>(OnUse);
    }

    private void OnUse(Entity<SolreignWristOrganizerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var comp = ent.Comp;
        var curTime = _timing.CurTime;

        if (!SolreignWristOrganizerRules.ReadoutReady(curTime, comp.NextReadoutTime))
        {
            _popup.PopupEntity(Loc.GetString("solreign-wrist-organizer-recalibrating"), args.User, args.User);
            return;
        }

        args.Handled = true;
        comp.NextReadoutTime = SolreignWristOrganizerRules.NextReadoutTime(curTime, comp.ReadoutCooldown);

        var vitals = "NOMINAL";
        var doomPercent = 0;

        if (TryComp<DamageableComponent>(args.User, out var damageable))
        {
            var totalDamage = _damageable.GetTotalDamage((args.User, damageable));

            float? critThreshold = null;
            float? deadThreshold = null;

            if (_thresholds.TryGetIncapThreshold(args.User, out var crit))
                critThreshold = (float) crit.Value;

            if (_thresholds.TryGetDeadThreshold(args.User, out var dead))
                deadThreshold = (float) dead.Value;

            vitals = SolreignWristOrganizerRules.VitalsStatus((float) totalDamage, deadThreshold, critThreshold);

            float? incapPercent = null;
            if (_thresholds.TryGetIncapPercentage(args.User, totalDamage, out var pct))
                incapPercent = (float) pct.Value;

            doomPercent = SolreignWristOrganizerRules.DoomPercent(incapPercent);
        }

        _popup.PopupEntity(
            Loc.GetString("solreign-wrist-organizer-readout",
                ("vitals", vitals),
                ("doom", doomPercent)),
            args.User,
            args.User,
            PopupType.Medium);

        _audio.PlayPvs(comp.ReadoutSound, args.User);

        if (_driftEnabled)
        {
            var driftTier = SolreignWristOrganizerRules.DriftTierForPriorUses(comp.TimesUsedThisRound);
            var driftKeys = driftTier switch
            {
                0 => DriftTierZeroKeys,
                1 => DriftTierOneKeys,
                _ => null,
            };

            if (driftKeys != null)
            {
                _popup.PopupEntity(Loc.GetString(driftKeys[_random.Next(driftKeys.Length)]), args.User, args.User);
            }
        }

        comp.TimesUsedThisRound++;
    }
}
