using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pure lifecycle seam used by the round-end handler. Constructing the envelope takes defensive
///     copies, so every player contribution and every contract occurrence is frozen before SQLite I/O.
/// </summary>
internal static class SeasonLedgerRoundEndCapture
{
    internal static RoundEndEnvelope Capture(
        int roundId,
        string gamemode,
        IReadOnlyList<RoundEndPlayerRecord> players,
        IReadOnlyList<ContractLogRecord> contracts)
    {
        var roster = new Dictionary<Guid, RoundContribution>();
        foreach (var player in players)
        {
            if (!roster.TryGetValue(player.User, out var existing))
            {
                roster.Add(player.User, player.Contribution);
                continue;
            }

            var contribution = player.Contribution;
            roster[player.User] = existing with
            {
                WasCaptainClean = existing.WasCaptainClean || contribution.WasCaptainClean,
                AntagWin = existing.AntagWin || contribution.AntagWin,
                EarlyDeath = existing.EarlyDeath || contribution.EarlyDeath,
                Standing = Math.Max(existing.Standing, contribution.Standing),
                ContractsCompleted = Math.Max(existing.ContractsCompleted, contribution.ContractsCompleted),
                ContractScore = Math.Max(existing.ContractScore, contribution.ContractScore),
                HrPointsEarned = Math.Max(existing.HrPointsEarned, contribution.HrPointsEarned),
            };
        }

        var accountSnapshot = roster
            .Select(pair => new RoundEndPlayerRecord(pair.Key, pair.Value))
            .ToArray();
        return new RoundEndEnvelope(roundId, gamemode, accountSnapshot, contracts);
    }
}

/// <summary>
///     Main-thread-owned cadence for durable spool recovery. The scheduler never attaches a continuation:
///     completion is observed only by the next <see cref="Tick"/> call from EntitySystem.Update.
/// </summary>
internal sealed class SeasonLedgerRecoveryScheduler
{
    internal const int MaximumEntries = 8;
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private Task<LedgerRecoverySweepResult>? _inFlight;
    private TimeSpan _sinceCompletion;
    private bool _initialSweepStarted;

    internal LedgerRecoverySweepResult? Tick(
        TimeSpan elapsed,
        Func<int, Task<LedgerRecoverySweepResult>> startRecovery)
    {
        ArgumentNullException.ThrowIfNull(startRecovery);
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));

        if (_inFlight is not null)
        {
            if (!_inFlight.IsCompleted)
                return null;

            var completed = _inFlight;
            _inFlight = null;
            _sinceCompletion = TimeSpan.Zero;
            return completed.GetAwaiter().GetResult();
        }

        _sinceCompletion += elapsed;
        if (_initialSweepStarted && _sinceCompletion < Interval)
            return null;

        _initialSweepStarted = true;
        _sinceCompletion = TimeSpan.Zero;
        // Invoking an async delegate can still run synchronously until its first incomplete await.
        // Offload the invocation itself so filesystem enumeration and lease-fast-path work never
        // execute on EntitySystem.Update's main thread.
        _inFlight = Task.Run(() => startRecovery(MaximumEntries) ??
            throw new InvalidOperationException("Recovery scheduler received no task."));
        return null;
    }
}

/// <summary>Sanitized, bounded operator rendering and argument policy.</summary>
internal static class SeasonLedgerRecoveryConsole
{
    internal const int MaximumStatusItems = 8;
    internal static bool IsOpaqueToken(string token) =>
        token is { Length: 32 } && token.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsArchiveReason(string reason) => SeasonLedgerSpool.IsArchiveReasonCode(reason);

    internal static IReadOnlyList<string> FormatStatus(IReadOnlyList<LedgerRecoverySnapshotItem> items)
    {
        var shown = items.Take(MaximumStatusItems).ToArray();
        var lines = new List<string>(shown.Length + 1)
        {
            $"Season Ledger recovery: unresolved={items.Count} shown={shown.Length}.",
        };
        foreach (var item in shown)
        {
            lines.Add($"round={item.RoundId?.ToString() ?? "none"} token={item.Token} attempts={item.Attempts} " +
                      $"state={item.State} category={item.ErrorCategory}");
        }

        return lines;
    }

    internal static async Task<IReadOnlyList<string>> FormatStatusSafelyAsync(
        Func<Task<IReadOnlyList<LedgerRecoverySnapshotItem>>> getSnapshot)
    {
        ArgumentNullException.ThrowIfNull(getSnapshot);
        try
        {
            return FormatStatus(await getSnapshot());
        }
        catch
        {
            // Snapshot failures may carry filesystem or envelope details. Keep the console fail-closed.
            return new[] { "Season Ledger recovery status unavailable." };
        }
    }

    internal static string FormatResult(RoundEndPersistenceResult result) =>
        $"round-recovery token={result.Token ?? "none"} attempts={result.Attempts} " +
        $"status={result.Status} category={result.ErrorCategory}";
}

[AdminCommand(AdminFlags.Host)]
internal sealed partial class SeasonLedgerRecoveryStatusCommand : LocalizedEntityCommands
{
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    public override string Command => "solreign_ledger_recovery_status";
    public override string Description => "Shows bounded, sanitized Season Ledger recovery state.";
    public override string Help => Command;

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        foreach (var line in await SeasonLedgerRecoveryConsole.FormatStatusSafelyAsync(_ledger.GetRecoverySnapshotAsync))
            shell.WriteLine(line);
    }
}

[AdminCommand(AdminFlags.Host)]
internal sealed partial class SeasonLedgerRecoveryRetryCommand : LocalizedEntityCommands
{
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    public override string Command => "solreign_ledger_recovery_retry";
    public override string Description => "Retries one pending Season Ledger envelope by opaque token.";
    public override string Help => $"{Command} <token>";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !SeasonLedgerRecoveryConsole.IsOpaqueToken(args[0]))
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var result = await _ledger.RetryRecoveryAsync(args[0]);
            shell.WriteLine(SeasonLedgerRecoveryConsole.FormatResult(result));
        }
        catch
        {
            // Never echo exception details: they can contain paths, SQL, or envelope content.
            shell.WriteError("Season Ledger recovery retry failed; inspect sanitized recovery status.");
        }
    }
}

[AdminCommand(AdminFlags.Host)]
internal sealed partial class SeasonLedgerRecoveryArchiveCommand : LocalizedEntityCommands
{
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    public override string Command => "solreign_ledger_recovery_archive";
    public override string Description => "Archives one recovery envelope with an allowlisted reason and audit receipt.";
    public override string Help => $"{Command} <token> <operator-review|legacy-reconciled|superseded-evidence>";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !SeasonLedgerRecoveryConsole.IsOpaqueToken(args[0]) ||
            !SeasonLedgerRecoveryConsole.IsArchiveReason(args[1]))
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            await _ledger.ArchiveRecoveryAsync(args[0], args[1]);
            shell.WriteLine($"Season Ledger recovery archived: token={args[0]} reason={args[1]} receipt=preserved.");
        }
        catch
        {
            // Never echo exception details: they can contain paths or envelope content.
            shell.WriteError("Season Ledger recovery archive failed; evidence was not reported as archived.");
        }
    }
}
