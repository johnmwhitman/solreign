using Content.Shared._Solreign.Zoo;
using Content.Server.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Player;
using Content.Shared.Popups;

namespace Content.Server._Solreign.Zoo;

/// <summary>
///     Server system governing SR-W-049: Zoo Breach Systemic Playground.
///     Manages the 3 authored breach paths, power and forcefield containment interactions,
///     dynamic population scaling, and safe recovery logic without round destruction.
/// </summary>
public sealed partial class SolreignZooSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private PopupSystem _popup = default!;

    private static readonly ISawmill Sawmill = Logger.GetSawmill("solreign.zoo");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignZooContainmentComponent, ComponentInit>(OnInit);
    }

    private void OnInit(EntityUid uid, SolreignZooContainmentComponent comp, ComponentInit args)
    {
        UpdatePopulationScaling(comp);
    }

    /// <summary>
    ///     Calculates dynamic population scaling tier based on active connected players.
    /// </summary>
    public ZooPopTier UpdatePopulationScaling(SolreignZooContainmentComponent comp)
    {
        var playerCount = _playerManager.PlayerCount;

        if (playerCount <= 15)
        {
            comp.PopTier = ZooPopTier.LowPop;
            comp.MaxSpecimenCapacity = 2;
        }
        else if (playerCount <= 40)
        {
            comp.PopTier = ZooPopTier.MidPop;
            comp.MaxSpecimenCapacity = 4;
        }
        else
        {
            comp.PopTier = ZooPopTier.HighPop;
            comp.MaxSpecimenCapacity = 6;
        }

        return comp.PopTier;
    }

    /// <summary>
    ///     Authored Breach Path 1: Containment Grid Power Surge & Blackout.
    /// </summary>
    public bool TriggerPowerCascade(EntityUid uid, SolreignZooContainmentComponent comp)
    {
        if (comp.ActiveBreachPath != ZooBreachPath.None)
            return false;

        comp.ActiveBreachPath = ZooBreachPath.PowerCascade;
        comp.IsPowered = false;
        comp.Integrity = MathF.Max(0f, comp.Integrity - 30f);
        comp.IsRecovered = false;

        _popup.PopupEntity(Loc.GetString("solreign-zoo-power-cascade-warning", ("cell", comp.CellId)), uid, PopupType.LargeCaution);
        Sawmill.Info($"Zoo Containment {comp.CellId} breached via PowerCascade path.");
        return true;
    }

    /// <summary>
    ///     Authored Breach Path 2: Bio-Feeder Jam & Agitation Surge.
    /// </summary>
    public bool TriggerBioFeederSurge(EntityUid uid, SolreignZooContainmentComponent comp)
    {
        if (comp.ActiveBreachPath != ZooBreachPath.None)
            return false;

        UpdatePopulationScaling(comp);

        comp.ActiveBreachPath = ZooBreachPath.BioFeederSurge;
        comp.StressLevel = 85f;
        comp.Integrity = MathF.Max(10f, comp.Integrity - 20f);
        comp.IsRecovered = false;

        _popup.PopupEntity(Loc.GetString("solreign-zoo-feeder-surge-warning", ("cell", comp.CellId)), uid, PopupType.MediumCaution);
        Sawmill.Info($"Zoo Containment {comp.CellId} breached via BioFeederSurge path (PopTier: {comp.PopTier}).");
        return true;
    }

    /// <summary>
    ///     Authored Breach Path 3: Environmental Vent Pipe Pressure Leak.
    /// </summary>
    public bool TriggerVentLeak(EntityUid uid, SolreignZooContainmentComponent comp)
    {
        if (comp.ActiveBreachPath != ZooBreachPath.None)
            return false;

        comp.ActiveBreachPath = ZooBreachPath.VentLeak;
        comp.StressLevel = 50f;
        comp.IsRecovered = false;

        _popup.PopupEntity(Loc.GetString("solreign-zoo-vent-leak-warning", ("cell", comp.CellId)), uid, PopupType.Medium);
        Sawmill.Info($"Zoo Containment {comp.CellId} breached via VentLeak path.");
        return true;
    }

    /// <summary>
    ///     Executes safe recovery logic to restore containment and prevent permanent round destruction.
    /// </summary>
    public bool AttemptSafeRecovery(EntityUid uid, SolreignZooContainmentComponent comp)
    {
        comp.IsPowered = true;
        comp.Integrity = 100f;
        comp.StressLevel = 0f;
        comp.ActiveBreachPath = ZooBreachPath.None;
        comp.IsRecovered = true;

        _popup.PopupEntity(Loc.GetString("solreign-zoo-recovery-success", ("cell", comp.CellId)), uid, PopupType.Medium);
        Sawmill.Info($"Zoo Containment {comp.CellId} safely recovered and re-sealed.");
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Process active containment cell ticks
        var query = EntityQueryEnumerator<SolreignZooContainmentComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IsRecovered)
                continue;

            // Power Cascade recovery check
            if (comp.ActiveBreachPath == ZooBreachPath.PowerCascade && comp.IsPowered)
            {
                comp.Integrity = MathF.Min(100f, comp.Integrity + (10f * frameTime));
                if (comp.Integrity >= 100f)
                {
                    AttemptSafeRecovery(uid, comp);
                }
            }

            // BioFeederSurge stress decay upon feeding
            if (comp.ActiveBreachPath == ZooBreachPath.BioFeederSurge && comp.StressLevel > 0f)
            {
                comp.StressLevel = MathF.Max(0f, comp.StressLevel - (2f * frameTime));
                if (comp.StressLevel <= 0f && comp.Integrity >= 80f)
                {
                    AttemptSafeRecovery(uid, comp);
                }
            }

            // Safe auto-recall safeguard if breach is lingering to avoid permanent round disruption
            if (!comp.IsRecovered && comp.StressLevel <= 5f && comp.IsPowered)
            {
                AttemptSafeRecovery(uid, comp);
            }
        }
    }
}
