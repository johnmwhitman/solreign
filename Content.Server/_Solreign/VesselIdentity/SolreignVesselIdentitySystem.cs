using System;
using Content.Shared._Solreign.VesselIdentity;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.VesselIdentity;

/// <summary>
///     Server entity system managing player-named vessel identity, moderation checks,
///     admin name resets, cosmetic registry mark progression, and cross-round persistence (SR-W-043).
/// </summary>
public sealed partial class SolreignVesselIdentitySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _metaData = default!;

    public SolreignVesselRegistryStore RegistryStore { get; } = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignVesselIdentityComponent, ComponentInit>(OnComponentInit);
    }

    private void OnComponentInit(EntityUid uid, SolreignVesselIdentityComponent component, ComponentInit args)
    {
        if (string.IsNullOrWhiteSpace(component.VesselId))
        {
            component.VesselId = $"VESSEL-{uid.Id}";
        }

        // Try rehydrating persistent vessel state from registry store
        RehydrateVesselState(uid, component);
    }

    /// <summary>
    ///     Attempts to set or update a vessel's custom name with moderation clearance checks.
    /// </summary>
    public bool TryRenameVessel(EntityUid vesselUid, string candidateName, EntityUid? requesterUid = null, SolreignVesselIdentityComponent? component = null)
    {
        if (!Resolve(vesselUid, ref component))
            return false;

        var currentTime = _timing.CurTime;

        // Enforce rename cooldown
        if (currentTime - component.LastRenamedAt < TimeSpan.FromSeconds(component.RenameCooldownSeconds))
            return false;

        // Disallow renaming if vessel is flagged or reset by admin without clearance
        if (component.ModerationState == SolreignVesselModerationState.ResetByAdmin)
            return false;

        var sanitized = SolreignVesselIdentityMath.SanitizeName(candidateName);
        var moderationResult = SolreignVesselIdentityMath.EvaluateNameModeration(sanitized);

        if (moderationResult == SolreignVesselModerationState.Flagged)
        {
            component.ModerationState = SolreignVesselModerationState.Flagged;
            Dirty(vesselUid, component);
            return false;
        }

        var oldName = component.VesselName;
        component.VesselName = sanitized;
        component.ModerationState = moderationResult;
        component.LastRenamedAt = currentTime;

        // Update entity metadata name if applicable
        _metaData.SetEntityName(vesselUid, sanitized);

        Dirty(vesselUid, component);

        // Sync record to persistent store
        PersistVesselState(vesselUid, component);

        RaiseLocalEvent(vesselUid, new SolreignVesselRenamedEvent(
            GetNetEntity(vesselUid),
            oldName,
            sanitized,
            moderationResult));

        return true;
    }

    /// <summary>
    ///     Administrative override to reset an inappropriate or profane vessel name back to default.
    /// </summary>
    public bool TryResetVesselName(EntityUid vesselUid, string reason = "Admin Reset", SolreignVesselIdentityComponent? component = null)
    {
        if (!Resolve(vesselUid, ref component))
            return false;

        var offendingName = component.VesselName;
        component.VesselName = component.DefaultName;
        component.ModerationState = SolreignVesselModerationState.ResetByAdmin;
        component.RegistryMark = SolreignVesselRegistryMark.Unmarked;

        // Revert entity metadata name to default
        _metaData.SetEntityName(vesselUid, component.DefaultName);

        Dirty(vesselUid, component);

        // Update persistent store
        PersistVesselState(vesselUid, component);

        RaiseLocalEvent(vesselUid, new SolreignVesselNameResetEvent(
            GetNetEntity(vesselUid),
            offendingName,
            component.DefaultName,
            reason));

        return true;
    }

    /// <summary>
    ///     Records a completed sortie/mission for the vessel, updates total salvage earnings,
    ///     and evaluates cosmetic registry mark tier progression.
    /// </summary>
    public bool RecordMissionCompletion(EntityUid vesselUid, float salvageEarned, SolreignVesselIdentityComponent? component = null)
    {
        if (!Resolve(vesselUid, ref component))
            return false;

        component.MissionsCompleted++;
        if (salvageEarned > 0f)
        {
            component.TotalSalvageValue += salvageEarned;
        }

        // Evaluate cosmetic registry mark tier if not reset by admin
        if (component.ModerationState != SolreignVesselModerationState.ResetByAdmin)
        {
            var newMark = SolreignVesselIdentityMath.CalculateRegistryMark(
                component.MissionsCompleted,
                component.TotalSalvageValue);

            if (newMark > component.RegistryMark)
            {
                component.RegistryMark = newMark;
                RaiseLocalEvent(vesselUid, new SolreignVesselMarkEarnedEvent(
                    GetNetEntity(vesselUid),
                    component.VesselName,
                    newMark,
                    component.MissionsCompleted,
                    component.TotalSalvageValue));
            }
        }

        Dirty(vesselUid, component);

        // Update store
        PersistVesselState(vesselUid, component);
        return true;
    }

    /// <summary>
    ///     Serializes active component state into the persistent registry store.
    /// </summary>
    public void PersistVesselState(EntityUid vesselUid, SolreignVesselIdentityComponent? component = null)
    {
        if (!Resolve(vesselUid, ref component))
            return;

        var record = new SolreignVesselRegistryRecord
        {
            VesselId = component.VesselId,
            VesselName = component.VesselName,
            DefaultName = component.DefaultName,
            RegistryMark = component.RegistryMark,
            ModerationState = component.ModerationState,
            MissionsCompleted = component.MissionsCompleted,
            TotalSalvageValue = component.TotalSalvageValue
        };

        RegistryStore.SaveRecord(record);
    }

    /// <summary>
    ///     Rehydrates component state from persistent registry store if available.
    /// </summary>
    public bool RehydrateVesselState(EntityUid vesselUid, SolreignVesselIdentityComponent? component = null)
    {
        if (!Resolve(vesselUid, ref component))
            return false;

        if (!RegistryStore.TryGetRecord(component.VesselId, out var record) || record == null)
            return false;

        component.VesselName = record.VesselName;
        component.DefaultName = record.DefaultName;
        component.RegistryMark = record.RegistryMark;
        component.ModerationState = record.ModerationState;
        component.MissionsCompleted = record.MissionsCompleted;
        component.TotalSalvageValue = record.TotalSalvageValue;

        _metaData.SetEntityName(vesselUid, record.VesselName);

        Dirty(vesselUid, component);
        return true;
    }
}
