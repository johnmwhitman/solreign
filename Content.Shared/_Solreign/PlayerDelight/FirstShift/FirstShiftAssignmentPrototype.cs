using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Guidebook;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.PlayerDelight.FirstShift;

[Serializable, NetSerializable]
public enum FirstShiftDepartment : byte
{
    Engineering,
    Medical,
    Science,
    Cargo,
    Service,
    Universal,
}

[Serializable, NetSerializable]
public enum FirstShiftAssignmentStage : byte
{
    Assigned,
    Orient,
    Try,
    Debrief,

    /// <summary>
    ///     The kishōtenketsu-restructure's *ketsu* stage (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md
    ///     §3.6, MG-W3): appended AFTER <see cref="Debrief"/> so existing serialized values never
    ///     shift. Reachable only when <c>solreign.mark.enabled</c> is true — see
    ///     <c>FirstShiftSystem.OnComplete</c>'s dormant-ship shim, which walks the reducer through
    ///     this stage in one adapter-level call rather than ever publishing it while the feature is
    ///     off.
    /// </summary>
    Mark,
}

[Serializable, NetSerializable]
public enum FirstShiftSafetyClassification : byte
{
    SafeOrientation,
    Restricted,
}

[Serializable, NetSerializable]
public enum FirstShiftTaskCategory : byte
{
    Orientation,
    Observation,
    GuideReference,
    BenignAppraisal,
}

/// <summary>A finite, reviewed First Shift card. Runtime systems never generate card text.</summary>
[Prototype]
public sealed partial class FirstShiftAssignmentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public FirstShiftDepartment Department;

    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> EligibleSafeJobs = new();

    [DataField(required: true)] public LocId Title = string.Empty;
    [DataField(required: true)] public LocId Why = string.Empty;
    [DataField(required: true)] public LocId Orient = string.Empty;
    [DataField(required: true)] public LocId Try = string.Empty;
    [DataField(required: true)] public LocId IfStuck = string.Empty;
    [DataField(required: true)] public LocId SafetyStop = string.Empty;

    /// <summary>Optional, curated corporate omens. Literal instructions are stored separately above.</summary>
    [DataField]
    public List<LocId> Flavor = new();

    /// <summary>Ordered station anchors. The final entry must be the Arrivals fallback.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Anchors = new();

    [DataField(required: true)]
    public ProtoId<GuideEntryPrototype> GuideEntry;

    [DataField(required: true)]
    public FirstShiftSafetyClassification SafetyClassification;

    [DataField(required: true)]
    public FirstShiftTaskCategory TaskCategory;

    [DataField]
    public bool Enabled = true;
}

/// <summary>Small immutable catalog projection consumed by the pure round reducer.</summary>
public readonly record struct FirstShiftCard
{
    public string Id { get; }
    public FirstShiftDepartment Department { get; }
    public FirstShiftSafetyClassification SafetyClassification { get; }
    public bool Enabled { get; }

    // Construction is deliberately restricted to FirstShiftCatalogBuilder.
    internal FirstShiftCard(
        string id,
        FirstShiftDepartment department,
        FirstShiftSafetyClassification safetyClassification,
        bool enabled)
    {
        Id = id;
        Department = department;
        SafetyClassification = safetyClassification;
        Enabled = enabled;
    }
}

/// <summary>
/// IoC-free projection used to reject structurally unsafe cards before runtime adapters consume them.
/// Prototype-manager existence and localization resolution are validated by the content layer.
/// </summary>
public readonly record struct FirstShiftAssignmentSchema(
    FirstShiftDepartment Department,
    IReadOnlyList<ProtoId<JobPrototype>> EligibleSafeJobs,
    LocId Title,
    LocId Why,
    LocId Orient,
    LocId Try,
    LocId IfStuck,
    LocId SafetyStop,
    IReadOnlyList<EntProtoId> Anchors,
    ProtoId<GuideEntryPrototype> GuideEntry,
    FirstShiftTaskCategory TaskCategory,
    FirstShiftSafetyClassification SafetyClassification);

public static class FirstShiftAssignmentValidator
{
    public const string ArrivalsFallback = "DefaultStationBeaconArrivals";

    public static IReadOnlyList<string> Validate(FirstShiftAssignmentSchema schema)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(schema.Department)) errors.Add("invalid-department");
        if (schema.SafetyClassification != FirstShiftSafetyClassification.SafeOrientation)
            errors.Add("unsafe-classification");
        if (schema.TaskCategory is not (FirstShiftTaskCategory.Orientation or
            FirstShiftTaskCategory.Observation or FirstShiftTaskCategory.GuideReference or
            FirstShiftTaskCategory.BenignAppraisal))
            errors.Add("forbidden-task-category");
        if (schema.Department != FirstShiftDepartment.Universal && schema.EligibleSafeJobs.Count == 0)
            errors.Add("missing-safe-jobs");
        if (string.IsNullOrWhiteSpace(schema.Title.Id)) errors.Add("missing-title");
        if (string.IsNullOrWhiteSpace(schema.Why.Id)) errors.Add("missing-why");
        if (string.IsNullOrWhiteSpace(schema.Orient.Id)) errors.Add("missing-orient");
        if (string.IsNullOrWhiteSpace(schema.Try.Id)) errors.Add("missing-try");
        if (string.IsNullOrWhiteSpace(schema.IfStuck.Id)) errors.Add("missing-if-stuck");
        if (string.IsNullOrWhiteSpace(schema.SafetyStop.Id)) errors.Add("missing-safety-stop");
        if (schema.Anchors.Count == 0 || schema.Anchors[^1].Id != ArrivalsFallback)
            errors.Add("missing-arrivals-fallback");
        if (string.IsNullOrWhiteSpace(schema.GuideEntry.Id)) errors.Add("missing-guide-entry");
        return errors;
    }
}

public readonly record struct FirstShiftCatalogBuildResult(
    IReadOnlyList<FirstShiftCard> Cards,
    IReadOnlyList<string> Errors);

/// <summary>
/// The only supported prototype-to-reducer projection. It fails the entire catalog closed so an invalid
/// or duplicate prototype can never become a selectable runtime card by partial success.
/// </summary>
public static class FirstShiftCatalogBuilder
{
    public static FirstShiftCatalogBuildResult Build(IEnumerable<FirstShiftAssignmentPrototype> prototypes)
    {
        var source = prototypes.OrderBy(prototype => prototype.ID, StringComparer.Ordinal).ToArray();
        var errors = new List<string>();

        foreach (var group in source.GroupBy(prototype => prototype.ID, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                errors.Add("missing-id");
            else if (group.Count() > 1)
                errors.Add($"duplicate-id:{group.Key}");
        }

        foreach (var prototype in source)
        {
            var schema = new FirstShiftAssignmentSchema(
                prototype.Department,
                prototype.EligibleSafeJobs,
                prototype.Title,
                prototype.Why,
                prototype.Orient,
                prototype.Try,
                prototype.IfStuck,
                prototype.SafetyStop,
                prototype.Anchors,
                prototype.GuideEntry,
                prototype.TaskCategory,
                prototype.SafetyClassification);
            foreach (var error in FirstShiftAssignmentValidator.Validate(schema))
                errors.Add($"{prototype.ID}:{error}");
        }

        if (errors.Count > 0)
            return new(Array.Empty<FirstShiftCard>(), errors);

        var cards = source
            .Select(prototype => new FirstShiftCard(
                prototype.ID,
                prototype.Department,
                prototype.SafetyClassification,
                prototype.Enabled))
            .ToArray();
        return new(cards, Array.Empty<string>());
    }
}
