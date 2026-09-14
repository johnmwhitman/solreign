using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server._Solreign.Showrunner;

/// <summary>
///     Pure, fail-closed Showrunner planning kernel.
///
///     It deliberately has no ECS, prototype, command, CVar, I/O, or execution
///     dependency. A caller may use the result for a future operator preview,
///     but this class cannot start a rule or mutate a round.
/// </summary>
public static class ShowrunnerPlanner
{
    public const int MaximumDescriptors = 128;
    public const int MaximumContextEntries = 128;
    public const int MaximumMapsPerDescriptor = 32;
    public const int MaximumIdentifierLength = 128;

    public static ShowrunnerPlanningResult BuildPlan(
        ShowrunnerPlanningContext? context,
        IEnumerable<ShowrunnerBeatDescriptor?>? descriptors)
    {
        if (context?.EmergencyStop == true)
            return Refuse(ShowrunnerReasonCode.EmergencyStop);

        if (!TryNormalizeContext(context, out var normalizedContext))
            return Refuse(ShowrunnerReasonCode.InvalidContext);

        if (descriptors == null)
            return Refuse(ShowrunnerReasonCode.InvalidDescriptor);

        var catalog = MaterializeBounded(descriptors, out var tooManyDescriptors);
        if (tooManyDescriptors)
            return Refuse(ShowrunnerReasonCode.TooManyDescriptors);

        var snapshotCatalog = new List<ShowrunnerBeatDescriptor>(catalog.Count);
        foreach (var descriptor in catalog)
        {
            if (!TrySnapshotDescriptor(descriptor, out var snapshot))
                return Refuse(ShowrunnerReasonCode.InvalidDescriptor);

            snapshotCatalog.Add(snapshot);
        }

        var validCatalog = snapshotCatalog
            .OrderBy(descriptor => descriptor.BeatId, StringComparer.Ordinal)
            .ToArray();

        if (HasDuplicates(validCatalog.Select(descriptor => descriptor.BeatId)))
            return Refuse(ShowrunnerReasonCode.DuplicateBeatId);

        if (HasDuplicates(validCatalog.Select(descriptor => descriptor.RuleId)))
            return Refuse(ShowrunnerReasonCode.DuplicateRuleId);

        var cooldowns = new HashSet<string>(normalizedContext.CoolingDownBeatIds, StringComparer.Ordinal);
        var activeRules = new HashSet<string>(normalizedContext.ActiveRuleIds, StringComparer.Ordinal);
        var rejectionReasons = new SortedSet<ShowrunnerReasonCode>();
        var eligible = new List<ShowrunnerBeatDescriptor>();

        foreach (var descriptor in validCatalog)
        {
            if (descriptor.CapabilityState != ShowrunnerCapabilityState.Eligible)
            {
                rejectionReasons.Add(ShowrunnerReasonCode.CapabilityNotEligible);
                continue;
            }

            if (!descriptor.AllowedMaps.Contains(normalizedContext.MapId, StringComparer.Ordinal))
            {
                rejectionReasons.Add(ShowrunnerReasonCode.MapUnsupported);
                continue;
            }

            if (normalizedContext.Population < descriptor.MinPlayers ||
                normalizedContext.Population > descriptor.MaxPlayers)
            {
                rejectionReasons.Add(ShowrunnerReasonCode.PopulationOutOfRange);
                continue;
            }

            if (cooldowns.Contains(descriptor.BeatId))
            {
                rejectionReasons.Add(ShowrunnerReasonCode.CooldownActive);
                continue;
            }

            if (activeRules.Contains(descriptor.RuleId))
            {
                rejectionReasons.Add(ShowrunnerReasonCode.ActiveRuleConflict);
                continue;
            }

            eligible.Add(descriptor);
        }

        var openings = ForRole(eligible, ShowrunnerBeatRole.Opening);
        var escalations = ForRole(eligible, ShowrunnerBeatRole.Escalation);
        var finales = ForRole(eligible, ShowrunnerBeatRole.Finale);

        if (openings.Length == 0 || escalations.Length == 0 || finales.Length == 0)
        {
            rejectionReasons.Add(ShowrunnerReasonCode.MissingRoleCandidate);
            rejectionReasons.Add(ShowrunnerReasonCode.NoCompletePlan);
            return Refuse(rejectionReasons);
        }

        ShowrunnerBeatDescriptor[]? winner = null;
        string? winnerRank = null;
        var rejectedForHeadlinerLimit = false;

        foreach (var opening in openings)
        {
            foreach (var escalation in escalations)
            {
                foreach (var finale in finales)
                {
                    var combination = new[] { opening, escalation, finale };
                    if (combination.Count(beat => beat.IsHeadliner) > 1)
                    {
                        rejectedForHeadlinerLimit = true;
                        continue;
                    }

                    var rank = Hash(CanonicalCombination(normalizedContext, combination));
                    if (winnerRank != null && string.CompareOrdinal(rank, winnerRank) >= 0)
                        continue;

                    winner = combination;
                    winnerRank = rank;
                }
            }
        }

        if (winner == null)
        {
            if (rejectedForHeadlinerLimit)
                rejectionReasons.Add(ShowrunnerReasonCode.HeadlinerLimit);

            rejectionReasons.Add(ShowrunnerReasonCode.NoCompletePlan);
            return Refuse(rejectionReasons);
        }

        var planFingerprint = Hash(CanonicalPreview(normalizedContext, validCatalog, winner));
        return new ShowrunnerPlanningResult(
            new ShowrunnerPlan(winner[0], winner[1], winner[2], planFingerprint),
            Array.Empty<ShowrunnerReasonCode>());
    }

    private static bool TryNormalizeContext(
        ShowrunnerPlanningContext? context,
        out ShowrunnerPlanningContext normalized)
    {
        normalized = null!;
        if (context == null ||
            context.Population < 0 ||
            !IsBoundedNonBlank(context.MapId) ||
            !TryNormalizeSet(
                context.CoolingDownBeatIds,
                MaximumContextEntries,
                out var cooldowns) ||
            !TryNormalizeSet(
                context.ActiveRuleIds,
                MaximumContextEntries,
                out var activeRules))
        {
            return false;
        }

        normalized = context with
        {
            CoolingDownBeatIds = cooldowns,
            ActiveRuleIds = activeRules,
        };
        return true;
    }

    private static bool TrySnapshotDescriptor(
        ShowrunnerBeatDescriptor? descriptor,
        out ShowrunnerBeatDescriptor snapshot)
    {
        snapshot = null!;
        if (descriptor == null ||
            !IsBoundedNonBlank(descriptor.BeatId) ||
            !IsBoundedNonBlank(descriptor.RuleId) ||
            !Enum.IsDefined(descriptor.Role) ||
            !Enum.IsDefined(descriptor.CapabilityState) ||
            descriptor.MinPlayers < 0 ||
            descriptor.MaxPlayers < descriptor.MinPlayers ||
            !IsLowerHexSha256(descriptor.CapabilityDigest) ||
            !TryNormalizeSet(
                descriptor.AllowedMaps,
                MaximumMapsPerDescriptor,
                out var allowedMaps) ||
            allowedMaps.Count == 0)
        {
            return false;
        }

        snapshot = descriptor with
        {
            AllowedMaps = allowedMaps,
        };
        return true;
    }

    private static bool IsLowerHexSha256(string? digest)
    {
        return digest != null &&
               digest.Length == 64 &&
               digest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsBoundedNonBlank(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length <= MaximumIdentifierLength;
    }

    private static bool HasDuplicates(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return values.Any(value => !seen.Add(value));
    }

    private static bool TryNormalizeSet(
        IEnumerable<string>? values,
        int maximumEntries,
        out IReadOnlyCollection<string> normalized)
    {
        normalized = Array.Empty<string>();
        if (values == null)
            return false;

        var set = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var value in values)
        {
            count++;
            if (count > maximumEntries || !IsBoundedNonBlank(value))
                return false;

            set.Add(value);
        }

        normalized = Array.AsReadOnly(set.Order(StringComparer.Ordinal).ToArray());
        return true;
    }

    private static List<ShowrunnerBeatDescriptor?> MaterializeBounded(
        IEnumerable<ShowrunnerBeatDescriptor?> descriptors,
        out bool tooMany)
    {
        var result = new List<ShowrunnerBeatDescriptor?>(MaximumDescriptors);
        tooMany = false;

        foreach (var descriptor in descriptors)
        {
            if (result.Count == MaximumDescriptors)
            {
                tooMany = true;
                break;
            }

            result.Add(descriptor);
        }

        return result;
    }

    private static ShowrunnerBeatDescriptor[] ForRole(
        IEnumerable<ShowrunnerBeatDescriptor> descriptors,
        ShowrunnerBeatRole role)
    {
        return descriptors
            .Where(descriptor => descriptor.Role == role)
            .OrderBy(descriptor => descriptor.BeatId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CanonicalCombination(
        ShowrunnerPlanningContext context,
        IReadOnlyList<ShowrunnerBeatDescriptor> beats)
    {
        var builder = new StringBuilder("solreign-showrunner-rank-v0");
        Append(builder, context.Seed.ToString(CultureInfo.InvariantCulture));
        Append(builder, context.Population.ToString(CultureInfo.InvariantCulture));
        Append(builder, context.MapId);

        foreach (var beat in beats)
        {
            Append(builder, beat.Role.ToString());
            Append(builder, beat.BeatId);
            Append(builder, beat.RuleId);
            Append(builder, beat.CapabilityDigest);
        }

        return builder.ToString();
    }

    private static string CanonicalPreview(
        ShowrunnerPlanningContext context,
        IReadOnlyList<ShowrunnerBeatDescriptor> catalog,
        IReadOnlyList<ShowrunnerBeatDescriptor> winner)
    {
        var builder = new StringBuilder("solreign-showrunner-fingerprint-v0");
        Append(builder, context.Seed.ToString(CultureInfo.InvariantCulture));
        Append(builder, context.Population.ToString(CultureInfo.InvariantCulture));
        Append(builder, context.MapId);

        foreach (var beatId in context.CoolingDownBeatIds.Order(StringComparer.Ordinal))
            Append(builder, $"cooldown:{beatId}");

        foreach (var ruleId in context.ActiveRuleIds.Order(StringComparer.Ordinal))
            Append(builder, $"active:{ruleId}");

        foreach (var beat in catalog)
        {
            Append(builder, beat.BeatId);
            Append(builder, beat.RuleId);
            Append(builder, beat.Role.ToString());
            Append(builder, beat.MinPlayers.ToString(CultureInfo.InvariantCulture));
            Append(builder, beat.MaxPlayers.ToString(CultureInfo.InvariantCulture));
            Append(builder, beat.IsHeadliner ? "1" : "0");
            Append(builder, beat.CapabilityState.ToString());
            Append(builder, beat.CapabilityDigest);
            foreach (var map in beat.AllowedMaps.Order(StringComparer.Ordinal))
                Append(builder, $"map:{map}");
        }

        foreach (var beat in winner)
            Append(builder, $"selected:{beat.BeatId}");

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder
            .Append('\n')
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static string Hash(string canonicalValue)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue)));
    }

    private static ShowrunnerPlanningResult Refuse(params ShowrunnerReasonCode[] reasons)
    {
        return Refuse((IEnumerable<ShowrunnerReasonCode>) reasons);
    }

    private static ShowrunnerPlanningResult Refuse(IEnumerable<ShowrunnerReasonCode> reasons)
    {
        var snapshot = reasons.Distinct().Order().ToArray();
        return new ShowrunnerPlanningResult(
            null,
            Array.AsReadOnly(snapshot));
    }
}
