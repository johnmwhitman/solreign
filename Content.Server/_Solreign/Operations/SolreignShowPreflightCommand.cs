using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using System.Globalization;
using System.Linq;

namespace Content.Server._Solreign.Operations;

/// <summary>
///     Reports a deliberately coarse, read-only snapshot of the current show environment.
///     This command is observational only: it does not evaluate readiness or expose game-rule
///     identities, player identities, planner output, or confidential configuration.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
internal sealed partial class SolreignShowPreflightCommand : LocalizedEntityCommands
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameMapManager _maps = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _config = default!;

    internal static IReadOnlyList<CVarDef<bool>> SignatureCVars { get; } =
        Array.AsReadOnly(new[]
        {
            CCVars.SolreignContractsQuestBoardEnabled,
            CCVars.SolreignDirectivesFaxEnabled,
            CCVars.SolreignFirstShiftAssignmentsEnabled,
            CCVars.SolreignFxCueV1Enabled,
            CCVars.SolreignFxWorldFeedbackV1Enabled,
            CCVars.SolreignFxWorldFeedbackObserveEnabled,
            CCVars.SolreignKartRepeatableHeatsEnabled,
            CCVars.SolreignProvidenceReactiveEnabled,
            CCVars.SolreignProvidenceEnabled,
            CCVars.SolreignRecordsTerminalEnabled,
            CCVars.SolreignShiftArchiveEnabled,
            CCVars.SolreignStationAuditEnabled,
            CCVars.SolreignStationAuditInspectionEnabled,
            CCVars.SolreignWingmatesEnabled,
        });

    public override string Command => "solreignshowpreflight";
    public override string Description => "Reports read-only SOLREIGN show state.";
    public override string Help => Command;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // Reject malformed invocations before consulting any injected dependency.
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var selectedMap = _maps.GetSelectedMap();
        var flags = SignatureCVars
            .Select(definition => new SolreignShowPreflightFlag(
                definition.Name,
                _config.GetCVar(definition)))
            .ToArray();
        var snapshot = new SolreignShowPreflightSnapshot(
            selectedMap?.ID,
            selectedMap?.MapName,
            _players.PlayerCount,
            _ticker.GetActiveGameRules().Count(),
            flags);

        foreach (var line in SolreignShowPreflightFormatter.Format(snapshot))
        {
            shell.WriteLine(line);
        }
    }
}

internal readonly record struct SolreignShowPreflightFlag(string Name, bool Enabled);

internal readonly record struct SolreignShowPreflightSnapshot(
    string? MapId,
    string? MapName,
    int ConnectedPlayers,
    int ActiveRuleCount,
    IReadOnlyList<SolreignShowPreflightFlag> FeatureFlags);

internal static class SolreignShowPreflightFormatter
{
    internal const int MaximumLines = 32;
    internal const int MaximumLineLength = 160;

    private const int FixedLineCountWithFlags = 6;

    internal static IReadOnlyList<string> Format(SolreignShowPreflightSnapshot snapshot)
    {
        var lines = new List<string>(MaximumLines)
        {
            "SOLREIGN SHOW PREFLIGHT",
            "OBSERVATION ONLY — NOT GO/READINESS AUTHORITY",
            FormatMap(snapshot.MapId, snapshot.MapName),
            $"Population.connected: {Math.Max(0, snapshot.ConnectedPlayers)}",
            $"ActiveRules.count: {Math.Max(0, snapshot.ActiveRuleCount)} (identities withheld)",
        };

        if (snapshot.FeatureFlags.Count == 0)
        {
            lines.Add("FeatureFlags: none");
            return lines;
        }

        lines.Add("FeatureFlags:");
        foreach (var flag in snapshot.FeatureFlags
                     .OrderBy(flag => flag.Name, StringComparer.Ordinal)
                     .Take(MaximumLines - FixedLineCountWithFlags))
        {
            var name = Sanitize(flag.Name);
            if (name.Length == 0)
                continue;

            lines.Add(Bound($"- {name}={(flag.Enabled ? "ON" : "OFF")}"));
        }

        if (lines.Count == FixedLineCountWithFlags)
            lines[^1] = "FeatureFlags: none";

        return lines;
    }

    private static string FormatMap(string? rawId, string? rawName)
    {
        var id = Sanitize(rawId);
        if (id.Length == 0)
            return "Map: NONE";

        var name = Sanitize(rawName);
        return name.Length == 0
            ? Bound($"Map: {id}")
            : Bound($"Map: {id} ({name})");
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = new char[Math.Min(value.Length, MaximumLineLength)];
        var length = 0;
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.GetUnicodeCategory(character) == UnicodeCategory.Format)
                continue;

            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = length > 0;
                continue;
            }

            if (pendingSpace && length < sanitized.Length)
                sanitized[length++] = ' ';

            pendingSpace = false;
            if (length >= sanitized.Length)
                break;

            sanitized[length++] = character;
        }

        return new string(sanitized, 0, length);
    }

    private static string Bound(string line)
    {
        return line.Length <= MaximumLineLength
            ? line
            : line[..MaximumLineLength];
    }
}
