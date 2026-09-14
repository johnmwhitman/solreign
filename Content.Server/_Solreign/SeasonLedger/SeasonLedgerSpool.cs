using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Content.Server._Solreign.SeasonLedger;

public enum RoundEndPersistenceStatus
{
    Committed,
    AlreadyCommitted,
    Pending,
}

public enum LedgerRecoveryState
{
    Pending,
    Conflict,
    Quarantined,
}

public enum LedgerRecoveryErrorCategory
{
    None,
    DatabaseBusy,
    ReplayConflict,
    InvalidContent,
    StorageFailure,
    // Finding 2 (2026-07-14): a non-retryable, non-conflict failure caught by the recovery sweep loop
    // (SQLITE_FULL/CORRUPT/READONLY, or any unanticipated exception). Distinct from StorageFailure,
    // which already denotes missing archived evidence (always State=Quarantined) — this entry stays
    // State=Pending/retryable, so operators must be able to tell the two apart in recovery status.
    StorageFaulted,
}

public sealed record RoundEndPersistenceResult(
    RoundEndPersistenceStatus Status,
    string? Token,
    int Attempts,
    LedgerRecoveryErrorCategory ErrorCategory);

public sealed record LedgerRecoverySnapshotItem(
    string Token,
    int? RoundId,
    DateOnly CapturedDate,
    int Attempts,
    LedgerRecoveryState State,
    LedgerRecoveryErrorCategory ErrorCategory);

public sealed record LedgerRecoverySweepResult(
    int Examined,
    int Acknowledged,
    int Pending,
    LedgerRecoverySnapshotItem? FirstPending = null,
    int Faulted = 0);

internal enum DirectoryDurabilityResult
{
    Synced,
    Unsupported,
}

internal interface IDirectoryDurabilityRuntime
{
    DirectoryDurabilityResult Flush(string path);
}

internal sealed class NativeDirectoryDurabilityRuntime : IDirectoryDurabilityRuntime
{
    internal static readonly NativeDirectoryDurabilityRuntime Instance = new();

    public DirectoryDurabilityResult Flush(string path) => SeasonLedgerSpool.TryFlushDirectoryDurably(path);
}

public sealed class LedgerRecoveryBlockedException : InvalidOperationException
{
    public LedgerRecoveryBlockedException()
        : base("Season change is blocked by unresolved Season Ledger recovery work.")
    {
    }
}

internal interface ILedgerRetryRuntime
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class ImmediateRetryRuntime : ILedgerRetryRuntime
{
    internal static readonly ImmediateRetryRuntime Instance = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

internal static class LedgerRetryPolicy
{
    internal static IReadOnlyList<TimeSpan> Delays { get; } = new[]
    {
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
    };

    internal static bool IsRetryable(SqliteException exception) =>
        exception.SqliteErrorCode is 5 or 6;
}

internal enum RoundEnvelopeSpoolStage
{
    AfterDurableStage,
    BeforeAcknowledge,
    AfterArchiveReceiptStaged,
    AfterArchiveEvidenceMoved,
    AfterWriterProbeBeforeManagedReservation,
}

internal interface IRoundEnvelopeSpoolFaultInjector
{
    void Hit(RoundEnvelopeSpoolStage stage);
}

internal sealed class NoopRoundEnvelopeSpoolFaultInjector : IRoundEnvelopeSpoolFaultInjector
{
    internal static readonly NoopRoundEnvelopeSpoolFaultInjector Instance = new();

    public void Hit(RoundEnvelopeSpoolStage stage)
    {
    }
}

internal sealed class SeasonLedgerSpool
{
    internal const long MaxEntryBytes = 1024 * 1024;
    private const int FileSchema = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private static readonly HashSet<string> ArchiveReasonCodes = new(StringComparer.Ordinal)
    {
        "operator-review",
        "legacy-reconciled",
        "superseded-evidence",
    };

    internal static bool IsArchiveReasonCode(string reasonCode) => ArchiveReasonCodes.Contains(reasonCode);

    private readonly string _directory;
    private readonly string _quarantineDirectory;
    private readonly string _archiveDirectory;
    private readonly string _leasePath;
    private readonly IDirectoryDurabilityRuntime _directoryDurabilityRuntime;

    internal SeasonLedgerSpool(
        string dbPath,
        IDirectoryDurabilityRuntime? directoryDurabilityRuntime = null)
    {
        _directory = dbPath + ".pending";
        _quarantineDirectory = Path.Combine(_directory, "quarantine");
        _archiveDirectory = Path.Combine(_directory, "archive");
        _leasePath = Path.Combine(_directory, ".lease");
        _directoryDurabilityRuntime = directoryDurabilityRuntime ?? NativeDirectoryDurabilityRuntime.Instance;
    }

    internal async Task<FileStream> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        EnsureSpoolRootBeforeLease();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _leasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.None);
            }
            catch (IOException)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    internal FileStream AcquireLease()
    {
        EnsureSpoolRootBeforeLease();
        while (true)
        {
            try
            {
                return new FileStream(_leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(5);
            }
        }
    }

    /// <summary>
    ///     Durably stages the captured envelope, optionally carrying fold intents so a crash after
    ///     stage but before envelope DB persist cannot permanently lose streak folds (Finding 4).
    ///     Matching reuse reconciles pending folds (ROUND-6): enrich foldless entries, fail closed
    ///     on nonempty conflicts, otherwise keep the durable existing entry.
    /// </summary>
    internal SpoolEntry StageOrReuse(
        CapturedRoundEndEnvelope captured,
        IReadOnlyList<ContractStreakFoldRequest>? pendingStreakFolds = null)
    {
        QuarantineAbandonedTemps();
        // Materialize for durable JSON (null when empty) so recovery can re-supply folds.
        IReadOnlyList<ContractStreakFoldRequest>? foldsToStage = null;
        if (pendingStreakFolds is { Count: > 0 })
            foldsToStage = pendingStreakFolds.ToArray();

        foreach (var path in Directory.EnumerateFiles(_directory, "*.pending"))
        {
            if (!TryRead(path, out var entry, quarantineInvalid: true))
                continue;
            if (entry!.RoundId == captured.RoundId &&
                string.Equals(entry.CaptureHash, captured.CaptureHash, StringComparison.Ordinal))
                return ReconcilePendingFoldsOnReuse(entry, foldsToStage);
        }

        var token = NewToken();
        var entryToWrite = new SpoolEntry(
            FileSchema,
            token,
            DateTimeOffset.UtcNow,
            0,
            LedgerRecoveryState.Pending,
            LedgerRecoveryErrorCategory.None,
            captured.RoundId,
            captured.CaptureHash,
            captured.CanonicalJson,
            foldsToStage);
        WriteNew(entryToWrite);
        return entryToWrite;
    }

    internal IReadOnlyList<SpoolEntry> ReadPendingEntries(int maxEntries)
    {
        if (maxEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        EnsurePrivateDirectoryDurably(_directory);
        QuarantineAbandonedTemps();

        // Finding 2 (2026-07-14): candidates are read in full (not capped mid-enumeration) so the bounded
        // window can be selected by (Attempts ascending, Token) rather than filename order. That keeps a
        // repeatedly-faulting entry from permanently occupying a slot ahead of never-attempted entries.
        // Conflict entries are excluded here rather than skipped by the caller — they are never retried by
        // this path, so they must not consume a slot in the bound either.
        var candidates = new List<SpoolEntry>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.pending"))
        {
            if (TryRead(path, out var entry, quarantineInvalid: true) && entry!.State == LedgerRecoveryState.Pending)
                candidates.Add(entry);
        }

        return candidates
            .OrderBy(entry => entry.Attempts)
            .ThenBy(entry => entry.Token, StringComparer.Ordinal)
            .Take(maxEntries)
            .ToArray();
    }

    internal SpoolEntry? ReadPendingEntry(string token)
    {
        ValidateToken(token);
        EnsurePrivateDirectoryDurably(_directory);
        QuarantineAbandonedTemps();
        var path = PendingPath(token);
        if (!File.Exists(path))
            return null;
        return TryRead(path, out var entry, quarantineInvalid: true) ? entry : null;
    }

    internal IReadOnlyList<LedgerRecoverySnapshotItem> Snapshot()
    {
        EnsurePrivateDirectoryDurably(_directory);
        QuarantineAbandonedTemps();
        var result = new List<LedgerRecoverySnapshotItem>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.pending"))
        {
            if (TryRead(path, out var entry, quarantineInvalid: true))
                result.Add(ToSnapshot(entry!));
        }

        if (Directory.Exists(_quarantineDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(_quarantineDirectory, "*.quarantine"))
            {
                var token = Path.GetFileNameWithoutExtension(path);
                var date = DateOnly.FromDateTime(File.GetLastWriteTimeUtc(path));
                result.Add(new LedgerRecoverySnapshotItem(
                    IsToken(token) ? token : NewToken(),
                    null,
                    date,
                    0,
                    LedgerRecoveryState.Quarantined,
                    LedgerRecoveryErrorCategory.InvalidContent));
            }
        }

        if (Directory.Exists(_archiveDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(_archiveDirectory, "*.receipt.staged"))
            {
                var fileName = Path.GetFileName(path);
                var token = fileName[..^".receipt.staged".Length];
                if (!IsToken(token) || result.Any(item => string.Equals(item.Token, token, StringComparison.Ordinal)))
                    continue;
                result.Add(new LedgerRecoverySnapshotItem(
                    token,
                    null,
                    DateOnly.FromDateTime(File.GetLastWriteTimeUtc(path)),
                    0,
                    LedgerRecoveryState.Quarantined,
                    LedgerRecoveryErrorCategory.StorageFailure));
            }
        }

        return result.OrderBy(item => item.CapturedDate).ThenBy(item => item.Token, StringComparer.Ordinal).ToArray();
    }

    internal bool HasUnresolved() => Snapshot().Count > 0;

    internal void Update(SpoolEntry entry) => ReplaceAtomically(entry);

    internal void Acknowledge(string token)
    {
        ValidateToken(token);
        File.Delete(PendingPath(token));
        _directoryDurabilityRuntime.Flush(_directory);
    }

    internal void Archive(
        string token,
        string reasonCode,
        IRoundEnvelopeSpoolFaultInjector faultInjector)
    {
        ValidateToken(token);
        if (!IsArchiveReasonCode(reasonCode))
            throw new ArgumentException("Archive reason is not allowlisted.", nameof(reasonCode));
        ArgumentNullException.ThrowIfNull(faultInjector);
        EnsurePrivateDirectoryDurably(_archiveDirectory);
        var finalReceiptPath = ArchiveReceiptPath(token);
        var stagedReceiptPath = ArchiveStagedReceiptPath(token);

        if (File.Exists(finalReceiptPath))
        {
            var completed = ReadArchiveReceipt(finalReceiptPath, token, reasonCode);
            if (!File.Exists(ArchiveEvidencePath(completed)) || File.Exists(ArchiveSourcePath(completed)))
                throw new InvalidDataException("Archived evidence is missing for its completed receipt.");
            return;
        }

        ArchiveReceipt receipt;
        if (File.Exists(stagedReceiptPath))
        {
            receipt = ReadArchiveReceipt(stagedReceiptPath, token, reasonCode);
        }
        else
        {
            receipt = CreateArchiveReceipt(token, reasonCode);
            WriteBytesDurably(
                stagedReceiptPath,
                JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions),
                createNew: true);
            _directoryDurabilityRuntime.Flush(_archiveDirectory);
            faultInjector.Hit(RoundEnvelopeSpoolStage.AfterArchiveReceiptStaged);
        }

        var source = ArchiveSourcePath(receipt);
        var destination = ArchiveEvidencePath(receipt);
        var sourceExists = File.Exists(source);
        var destinationExists = File.Exists(destination);
        if (sourceExists && destinationExists)
            throw new IOException("Archive evidence exists in both source and destination locations.");
        if (!sourceExists && !destinationExists)
            throw new FileNotFoundException("Recovery evidence was not found in either durable location.");
        if (sourceExists)
        {
            File.Move(source, destination, overwrite: false);
            TrySetPrivateFile(destination);
            _directoryDurabilityRuntime.Flush(Path.GetDirectoryName(source)!);
            _directoryDurabilityRuntime.Flush(_archiveDirectory);
        }

        faultInjector.Hit(RoundEnvelopeSpoolStage.AfterArchiveEvidenceMoved);
        File.Move(stagedReceiptPath, finalReceiptPath, overwrite: false);
        TrySetPrivateFile(finalReceiptPath);
        _directoryDurabilityRuntime.Flush(_archiveDirectory);
    }

    /// <summary>
    ///     Reuse-path fold reconciliation (Finding 4 / ROUND-6):
    ///     foldless existing + caller folds = durable enrich;
    ///     both nonempty and differing = fail closed;
    ///     identical or caller-empty = keep existing.
    /// </summary>
    private SpoolEntry ReconcilePendingFoldsOnReuse(
        SpoolEntry existing,
        IReadOnlyList<ContractStreakFoldRequest>? callerFolds)
    {
        var existingHas = existing.PendingStreakFolds is { Count: > 0 };
        var callerHas = callerFolds is { Count: > 0 };

        if (!existingHas && callerHas)
        {
            var enriched = existing with { PendingStreakFolds = callerFolds };
            Update(enriched);
            return enriched;
        }

        if (existingHas && callerHas && !PendingFoldsEqual(existing.PendingStreakFolds, callerFolds))
        {
            throw new InvalidOperationException(
                "Conflicting pending streak fold intents for a matching spool entry.");
        }

        // Identical folds, or caller supplied none: keep the durable existing entry.
        return existing;
    }

    private static bool PendingFoldsEqual(
        IReadOnlyList<ContractStreakFoldRequest>? left,
        IReadOnlyList<ContractStreakFoldRequest>? right)
    {
        var a = NormalizePendingFolds(left);
        var b = NormalizePendingFolds(right);
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static List<ContractStreakFoldRequest> NormalizePendingFolds(
        IReadOnlyList<ContractStreakFoldRequest>? folds)
    {
        if (folds is null || folds.Count == 0)
            return new List<ContractStreakFoldRequest>();
        return folds
            .OrderBy(f => f.User)
            .ThenBy(f => f.CompletedThisRound)
            .ToList();
    }

    internal static CapturedRoundEndEnvelope ValidateAndCapture(SpoolEntry entry)
    {
        if (entry.FileSchema != FileSchema || string.IsNullOrEmpty(entry.Token) || !IsToken(entry.Token) ||
            entry.RoundId <= 0 || entry.Attempts is < 0 or > 1_000_000 ||
            entry.CapturedUtc == default || !Enum.IsDefined(entry.State) ||
            entry.State is not (LedgerRecoveryState.Pending or LedgerRecoveryState.Conflict) ||
            !Enum.IsDefined(entry.ErrorCategory) ||
            string.IsNullOrEmpty(entry.CanonicalJson) || string.IsNullOrEmpty(entry.CaptureHash) ||
            entry.CaptureHash.Length != 64)
            throw new InvalidDataException("Invalid round-envelope spool metadata.");
        var captured = CapturedRoundEndEnvelope.ParseCanonical(entry.CanonicalJson);
        if (captured.RoundId != entry.RoundId ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(captured.CaptureHash),
                Convert.FromHexString(entry.CaptureHash)))
            throw new InvalidDataException("Round-envelope spool content failed verification.");
        return captured;
    }

    private bool TryRead(string path, out SpoolEntry? entry, bool quarantineInvalid)
    {
        entry = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxEntryBytes)
                throw new InvalidDataException("Invalid spool entry size.");
            var token = Path.GetFileNameWithoutExtension(path);
            ValidateToken(token);
            var bytes = File.ReadAllBytes(path);
            entry = JsonSerializer.Deserialize<SpoolEntry>(bytes, JsonOptions) ?? throw new InvalidDataException();
            if (!string.Equals(token, entry.Token, StringComparison.Ordinal))
                throw new InvalidDataException("Spool token mismatch.");
            ValidateAndCapture(entry);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          JsonException or InvalidDataException or FormatException or
                                          ArgumentException or OverflowException or KeyNotFoundException or
                                          InvalidOperationException or NullReferenceException)
        {
            if (quarantineInvalid)
                Quarantine(path);
            return false;
        }
    }

    private void WriteNew(SpoolEntry entry)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        if (bytes.Length > MaxEntryBytes)
            throw new InvalidDataException("Round-envelope spool entry exceeds the size limit.");
        var temp = Path.Combine(_directory, entry.Token + ".tmp");
        WriteBytesDurably(temp, bytes, createNew: true);
        File.Move(temp, PendingPath(entry.Token), overwrite: false);
        _directoryDurabilityRuntime.Flush(_directory);
    }

    private void ReplaceAtomically(SpoolEntry entry)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        if (bytes.Length > MaxEntryBytes)
            throw new InvalidDataException("Round-envelope spool entry exceeds the size limit.");
        var temp = Path.Combine(_directory, NewToken() + ".tmp");
        WriteBytesDurably(temp, bytes, createNew: true);
        File.Move(temp, PendingPath(entry.Token), overwrite: true);
        _directoryDurabilityRuntime.Flush(_directory);
    }

    private static void WriteBytesDurably(string path, byte[] bytes, bool createNew)
    {
        using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        TrySetPrivateFile(path);
    }

    private void QuarantineAbandonedTemps()
    {
        // cdx round-4: the lazy enumeration itself can throw under total disk failure. Contained
        // here (and, belt-and-suspenders, by the recovery scheduler's Tick try/catch in
        // SeasonLedgerSystem.Update) — abandoned temps are inert files; a skipped pass costs
        // nothing and the next snapshot retries.
        string[] temps;
        try
        {
            temps = Directory.GetFiles(_directory, "*.tmp");
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException
                                      and not AccessViolationException)
        {
            return;
        }

        foreach (var path in temps)
        {
            // cdx round-3 residual (2026-07-14): the quarantine move can throw under the SAME
            // ENOSPC/read-only condition that abandoned the temp in the first place — and this
            // runs inside Snapshot(), whose callers include the END of the recovery sweep. A
            // failed quarantine must degrade to "skip this temp for now" (it stays for a future
            // snapshot), never abort the caller after the sweep's real work already happened.
            try
            {
                Quarantine(path);
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException
                                          and not AccessViolationException)
            {
                // Best-effort, sanitized-silent (no paths/exception content) — consistent with
                // every other recovery log discipline.
            }
        }
    }

    private void Quarantine(string path)
    {
        if (!File.Exists(path))
            return;
        EnsurePrivateDirectoryDurably(_quarantineDirectory);
        var destination = Path.Combine(_quarantineDirectory, NewToken() + ".quarantine");
        File.Move(path, destination, overwrite: false);
        TrySetPrivateFile(destination);
        _directoryDurabilityRuntime.Flush(_directory);
        _directoryDurabilityRuntime.Flush(_quarantineDirectory);
    }

    internal static DirectoryDurabilityResult TryFlushDirectoryDurably(string path)
    {
        if (OperatingSystem.IsWindows())
            return DirectoryDurabilityResult.Unsupported;

        // .NET FileStream does not support opening directories on every Unix runtime. Use the
        // platform fsync primitive directly. EINVAL/ENOTSUP is an explicit fallback for filesystems
        // which cannot persist directory entries; all other failures remain visible to the caller.
        var descriptor = NativeMethods.Open(path, NativeMethods.OpenReadOnly);
        if (descriptor < 0)
            throw new IOException("Could not open a directory for a durability sync.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (NativeMethods.Fsync(descriptor) == 0)
                return DirectoryDurabilityResult.Synced;
            var error = Marshal.GetLastPInvokeError();
            if (error is NativeMethods.InvalidArgument or NativeMethods.NotSupportedMac or
                NativeMethods.NotSupportedLinux)
                return DirectoryDurabilityResult.Unsupported;
            throw new IOException("Could not durably sync a directory.",
                new System.ComponentModel.Win32Exception(error));
        }
        finally
        {
            NativeMethods.Close(descriptor);
        }
    }

    private string PendingPath(string token) => Path.Combine(_directory, token + ".pending");

    private string QuarantinePath(string token) => Path.Combine(_quarantineDirectory, token + ".quarantine");

    private string ArchiveReceiptPath(string token) => Path.Combine(_archiveDirectory, token + ".receipt.json");

    private string ArchiveStagedReceiptPath(string token) => Path.Combine(_archiveDirectory, token + ".receipt.staged");

    private string ArchiveEvidencePath(ArchiveReceipt receipt) =>
        Path.Combine(_archiveDirectory, receipt.Token + "." + receipt.SourceKind);

    private string ArchiveSourcePath(ArchiveReceipt receipt) => receipt.SourceKind switch
    {
        "pending" => PendingPath(receipt.Token),
        "quarantine" => QuarantinePath(receipt.Token),
        _ => throw new InvalidDataException("Archive receipt has an invalid source kind."),
    };

    private ArchiveReceipt CreateArchiveReceipt(string token, string reasonCode)
    {
        var pending = PendingPath(token);
        var quarantine = QuarantinePath(token);
        var pendingExists = File.Exists(pending);
        var quarantineExists = File.Exists(quarantine);
        if (pendingExists == quarantineExists)
            throw new FileNotFoundException("Exactly one token-addressed recovery evidence file is required.");

        var sourceKind = pendingExists ? "pending" : "quarantine";
        var sourceState = "quarantined";
        if (pendingExists)
        {
            sourceState = TryRead(pending, out var entry, quarantineInvalid: false)
                ? entry!.State.ToString().ToLowerInvariant()
                : "pending-unvalidated";
        }

        return new ArchiveReceipt(FileSchema, token, reasonCode, sourceKind, sourceState, DateTimeOffset.UtcNow);
    }

    private ArchiveReceipt ReadArchiveReceipt(string path, string token, string reasonCode)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > 4096)
            throw new InvalidDataException("Archive receipt has an invalid size.");
        var receipt = JsonSerializer.Deserialize<ArchiveReceipt>(File.ReadAllBytes(path), JsonOptions) ??
                      throw new InvalidDataException("Archive receipt is empty.");
        if (receipt.FileSchema != FileSchema || !string.Equals(receipt.Token, token, StringComparison.Ordinal) ||
            !string.Equals(receipt.ReasonCode, reasonCode, StringComparison.Ordinal) ||
            receipt.SourceKind is not ("pending" or "quarantine") ||
            receipt.SourceState is not ("pending" or "conflict" or "quarantined" or "pending-unvalidated") ||
            receipt.ArchivedUtc == default)
            throw new InvalidDataException("Archive receipt failed validation.");
        return receipt;
    }

    private static LedgerRecoverySnapshotItem ToSnapshot(SpoolEntry entry) => new(
        entry.Token,
        entry.RoundId,
        DateOnly.FromDateTime(entry.CapturedUtc.UtcDateTime),
        entry.Attempts,
        entry.State,
        entry.ErrorCategory);

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static void ValidateToken(string token)
    {
        if (!IsToken(token))
            throw new ArgumentException("Invalid opaque recovery token.", nameof(token));
    }

    private static bool IsToken(string token) => token.Length == 32 && token.All(ch =>
        ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private void EnsurePrivateDirectoryDurably(string path)
    {
        var created = !Directory.Exists(path);
        EnsurePrivateDirectory(path);
        if (!created)
            return;

        var parent = Directory.GetParent(Path.GetFullPath(path))?.FullName ??
                     throw new IOException("A durable directory must have a parent directory.");
        _directoryDurabilityRuntime.Flush(parent);
    }

    private void EnsureSpoolRootBeforeLease()
    {
        EnsurePrivateDirectory(_directory);
        var parent = Directory.GetParent(Path.GetFullPath(_directory))?.FullName ??
                     throw new IOException("The recovery spool must have a parent directory.");

        // Another process may have created the root without making its directory entry durable.
        // Every lease contender therefore syncs the database parent before it can use the lease.
        _directoryDurabilityRuntime.Flush(parent);
    }

    private static void TrySetPrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed record ArchiveReceipt(
        [property: JsonPropertyName("file_schema")] int FileSchema,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("reason_code")] string ReasonCode,
        [property: JsonPropertyName("source_kind")] string SourceKind,
        [property: JsonPropertyName("source_state")] string SourceState,
        [property: JsonPropertyName("archived_utc")] DateTimeOffset ArchivedUtc);

    private static class NativeMethods
    {
        internal const int OpenReadOnly = 0;
        internal const int InvalidArgument = 22;
        internal const int NotSupportedMac = 45;
        internal const int NotSupportedLinux = 95;

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        internal static extern int Fsync(int fileDescriptor);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static extern int Close(int fileDescriptor);
    }
}

internal sealed record SpoolEntry(
    int FileSchema,
    string Token,
    DateTimeOffset CapturedUtc,
    int Attempts,
    LedgerRecoveryState State,
    LedgerRecoveryErrorCategory ErrorCategory,
    int RoundId,
    string CaptureHash,
    string CanonicalJson,
    // Finding 4: fold intents staged with the envelope so crash-after-stage recovery cannot lose them.
    IReadOnlyList<ContractStreakFoldRequest>? PendingStreakFolds = null);
