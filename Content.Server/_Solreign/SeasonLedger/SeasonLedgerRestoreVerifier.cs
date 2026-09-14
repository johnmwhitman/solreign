using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

namespace Content.Server._Solreign.SeasonLedger;

internal sealed record LedgerRestoreManifest(
    int ManifestSchema,
    string DatabaseSha256,
    int LedgerSchemaVersion,
    string SeasonFingerprint,
    long RoundEnvelopes,
    long PlayerStats,
    long OutboxRows);

internal readonly record struct LedgerRestoreProof(
    int LedgerSchemaVersion,
    long RoundEnvelopes,
    long PlayerStats,
    long OutboxRows);

internal enum LedgerRestoreFailureCode
{
    InvalidManifest,
    SnapshotUnavailable,
    DigestMismatch,
    IntegrityFailure,
    UnsupportedSchema,
    MissingStructure,
    ProgressionMismatch,
}

internal sealed class LedgerRestoreVerificationException : Exception
{
    public LedgerRestoreFailureCode Code { get; }

    internal LedgerRestoreVerificationException(LedgerRestoreFailureCode code)
        : base(code.ToString())
    {
        Code = code;
    }
}

internal enum LedgerRestoreStagingStage
{
    SourceOpened,
    CopyCompleted,
    CleanupStarted,
}

internal interface ILedgerRestoreStagingFaultInjector
{
    void Hit(LedgerRestoreStagingStage stage, string stagedPath);
}

internal sealed class NoopLedgerRestoreStagingFaultInjector : ILedgerRestoreStagingFaultInjector
{
    internal static readonly NoopLedgerRestoreStagingFaultInjector Instance = new();

    private NoopLedgerRestoreStagingFaultInjector()
    {
    }

    public void Hit(LedgerRestoreStagingStage stage, string stagedPath)
    {
    }
}

internal static class SeasonLedgerRestoreVerifier
{
    private const int ManifestSchemaVersion = 1;
    private const int HashBufferSize = 81_920;

    private static readonly string[] RequiredTables =
    {
        "meta",
        "player_stats",
        "round_envelopes",
        "ledger_outbox",
    };

    internal static async Task<LedgerRestoreManifest> CreateManifestAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        return await CreateManifestAsync(
            snapshotPath,
            cancellationToken,
            NoopLedgerRestoreStagingFaultInjector.Instance);
    }

    internal static async Task<LedgerRestoreManifest> CreateManifestAsync(
        string snapshotPath,
        CancellationToken cancellationToken,
        ILedgerRestoreStagingFaultInjector stagingFaultInjector)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stagingFaultInjector);
        var staged = await StageCandidateAsync(
            snapshotPath,
            cancellationToken,
            stagingFaultInjector);
        var cancellationObserved = false;
        try
        {
            var digest = await HashCandidateAsync(staged.Path, cancellationToken);
            var facts = await InspectCandidateAsync(staged.Path, cancellationToken);
            await RequireDigestUnchangedAsync(staged.Path, digest, cancellationToken);
            return new LedgerRestoreManifest(
                ManifestSchemaVersion,
                ToLowerHex(digest),
                facts.Proof.LedgerSchemaVersion,
                facts.SeasonFingerprint,
                facts.Proof.RoundEnvelopes,
                facts.Proof.PlayerStats,
                facts.Proof.OutboxRows);
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
            await DisposePreservingCancellationAsync(staged);
            throw;
        }
        finally
        {
            if (!cancellationObserved)
                await staged.DisposeAsync();
        }
    }

    internal static async Task<LedgerRestoreProof> VerifyAsync(
        string restoredPath,
        LedgerRestoreManifest manifest,
        CancellationToken cancellationToken)
    {
        return await VerifyAsync(
            restoredPath,
            manifest,
            cancellationToken,
            NoopLedgerRestoreStagingFaultInjector.Instance);
    }

    internal static async Task<LedgerRestoreProof> VerifyAsync(
        string restoredPath,
        LedgerRestoreManifest manifest,
        CancellationToken cancellationToken,
        ILedgerRestoreStagingFaultInjector stagingFaultInjector)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateManifest(manifest);
        ArgumentNullException.ThrowIfNull(stagingFaultInjector);
        var staged = await StageCandidateAsync(
            restoredPath,
            cancellationToken,
            stagingFaultInjector);
        var cancellationObserved = false;
        try
        {
            var actualDigest = await HashCandidateAsync(staged.Path, cancellationToken);
            var expectedDigest = Convert.FromHexString(manifest.DatabaseSha256);
            if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
                throw Failure(LedgerRestoreFailureCode.DigestMismatch);

            var facts = await InspectCandidateAsync(staged.Path, cancellationToken);
            await RequireDigestUnchangedAsync(staged.Path, actualDigest, cancellationToken);
            if (facts.Proof.LedgerSchemaVersion != manifest.LedgerSchemaVersion ||
                facts.Proof.RoundEnvelopes != manifest.RoundEnvelopes ||
                facts.Proof.PlayerStats != manifest.PlayerStats ||
                facts.Proof.OutboxRows != manifest.OutboxRows ||
                !string.Equals(facts.SeasonFingerprint, manifest.SeasonFingerprint, StringComparison.Ordinal))
            {
                throw Failure(LedgerRestoreFailureCode.ProgressionMismatch);
            }

            return facts.Proof;
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
            await DisposePreservingCancellationAsync(staged);
            throw;
        }
        finally
        {
            if (!cancellationObserved)
                await staged.DisposeAsync();
        }
    }

    private static async Task<StagedCandidate> StageCandidateAsync(
        string sourcePath,
        CancellationToken cancellationToken,
        ILedgerRestoreStagingFaultInjector stagingFaultInjector)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedSourcePath = RequireAliasFreePath(sourcePath);
        StagedCandidate? staged = null;

        try
        {
            var privateDirectory = CreatePrivateStagingDirectory();
            var directory = privateDirectory.Directory;
            staged = new StagedCandidate(
                directory.FullName,
                Path.Combine(directory.FullName, "snapshot.db"),
                stagingFaultInjector);

            await using var source = new FileStream(
                normalizedSourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                HashBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var sourceIdentity = GetFileIdentity(source.SafeFileHandle);
            RequireUniqueRegularFile(sourceIdentity);
            RequireSameIdentity(normalizedSourcePath, sourceIdentity);
            RequireNoSidecars(normalizedSourcePath);

            stagingFaultInjector.Hit(LedgerRestoreStagingStage.SourceOpened, staged.Path);
            RequireSourceStillBound(normalizedSourcePath, source, sourceIdentity);

            await using (var destination = new FileStream(
                             staged.Path,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             HashBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, HashBufferSize, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            RequireSourceStillBound(normalizedSourcePath, source, sourceIdentity);
            RestrictFile(staged.Path, privateDirectory.WindowsSid);
            stagingFaultInjector.Hit(LedgerRestoreStagingStage.CopyCompleted, staged.Path);
            cancellationToken.ThrowIfCancellationRequested();
            return staged;
        }
        catch (OperationCanceledException)
        {
            if (staged is not null)
                await DisposePreservingCancellationAsync(staged);
            throw;
        }
        catch (LedgerRestoreVerificationException)
        {
            if (staged is not null)
                await staged.DisposeAsync();
            throw;
        }
        catch
        {
            if (staged is not null)
                await staged.DisposeAsync();
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static async ValueTask DisposePreservingCancellationAsync(StagedCandidate staged)
    {
        try
        {
            await staged.DisposeAsync();
        }
        catch (LedgerRestoreVerificationException)
        {
            // Cancellation is the authoritative result. A failed owner-private
            // cleanup can leave a restricted temp artifact for operator/OS cleanup.
        }
    }

    private static PrivateStagingDirectory CreatePrivateStagingDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            var directory = Directory.CreateTempSubdirectory("solreign-ledger-restore-");
            File.SetUnixFileMode(
                directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return new PrivateStagingDirectory(directory, null);
        }

        return CreatePrivateWindowsStagingDirectory();
    }

    [SupportedOSPlatform("windows")]
    private static PrivateStagingDirectory CreatePrivateWindowsStagingDirectory()
    {
        DirectoryInfo? windowsDirectory = null;
        try
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var sid = identity.User ?? throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            var security = new DirectorySecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            windowsDirectory = Directory.CreateTempSubdirectory("solreign-ledger-restore-");
            windowsDirectory.SetAccessControl(security);
            VerifyWindowsDirectoryAcl(windowsDirectory.FullName, sid);
            return new PrivateStagingDirectory(windowsDirectory, sid);
        }
        catch (LedgerRestoreVerificationException)
        {
            DeleteDirectoryBestEffort(windowsDirectory?.FullName);
            throw;
        }
        catch
        {
            DeleteDirectoryBestEffort(windowsDirectory?.FullName);
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static void RestrictFile(string path, SecurityIdentifier? windowsSid)
    {
        if (OperatingSystem.IsWindows())
        {
            if (windowsSid is null)
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            VerifyWindowsFileAcl(path, windowsSid);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead);
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindowsDirectoryAcl(string path, SecurityIdentifier currentSid)
    {
        try
        {
            var security = new DirectoryInfo(path)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var rules = security
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            if (!security.AreAccessRulesProtected ||
                !Equals(security.GetOwner(typeof(SecurityIdentifier)), currentSid) ||
                rules.Count != 1 ||
                rules[0] is not FileSystemAccessRule rule ||
                rule.IsInherited ||
                !Equals(rule.IdentityReference, currentSid) ||
                rule.AccessControlType != AccessControlType.Allow ||
                (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl ||
                rule.InheritanceFlags != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))
            {
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            }
        }
        catch (LedgerRestoreVerificationException)
        {
            throw;
        }
        catch
        {
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindowsFileAcl(string path, SecurityIdentifier currentSid)
    {
        try
        {
            var security = new FileInfo(path)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var rules = security
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            if (!Equals(security.GetOwner(typeof(SecurityIdentifier)), currentSid) ||
                rules.Count != 1 ||
                rules[0] is not FileSystemAccessRule rule ||
                !rule.IsInherited ||
                !Equals(rule.IdentityReference, currentSid) ||
                rule.AccessControlType != AccessControlType.Allow ||
                (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl)
            {
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            }
        }
        catch (LedgerRestoreVerificationException)
        {
            throw;
        }
        catch
        {
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static void DeleteDirectoryBestEffort(string? path)
    {
        if (path is null)
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // The directory is randomized and contains no staged file yet.
        }
    }

    private static async Task RequireDigestUnchangedAsync(
        string path,
        byte[] expectedDigest,
        CancellationToken cancellationToken)
    {
        var digestAfterInspection = await HashCandidateAsync(path, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, digestAfterInspection))
            throw Failure(LedgerRestoreFailureCode.IntegrityFailure);
    }

    private static void ValidateManifest(LedgerRestoreManifest? manifest)
    {
        if (manifest is null ||
            manifest.ManifestSchema != ManifestSchemaVersion ||
            !IsLowerHexSha256(manifest.DatabaseSha256) ||
            !IsLowerHexSha256(manifest.SeasonFingerprint) ||
            manifest.LedgerSchemaVersion < 0 ||
            manifest.RoundEnvelopes < 0 ||
            manifest.PlayerStats < 0 ||
            manifest.OutboxRows < 0)
        {
            throw Failure(LedgerRestoreFailureCode.InvalidManifest);
        }
    }

    private static bool IsLowerHexSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    private static void RequireNoSidecars(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            PathEntryExists(path + "-wal") ||
            PathEntryExists(path + "-shm") ||
            PathEntryExists(path + "-journal"))
        {
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static string RequireAliasFreePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);

            var current = fullPath;
            var candidate = true;
            while (!string.Equals(current, root, StringComparison.Ordinal))
            {
                FileSystemInfo info = candidate ? new FileInfo(current) : new DirectoryInfo(current);
                info.Refresh();
                if (!info.Exists || info.LinkTarget is not null ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    candidate && (info.Attributes & FileAttributes.Directory) != 0)
                {
                    throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
                }

                candidate = false;
                current = Path.GetDirectoryName(current) ??
                          throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            }

            return fullPath;
        }
        catch (LedgerRestoreVerificationException)
        {
            throw;
        }
        catch
        {
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.Exists)
                return true;

            // LinkTarget still exposes a dangling symbolic link, while Attributes
            // throws for an ordinary missing path on some Unix runtimes.
            if (file.LinkTarget is not null)
                return true;

            var directory = new DirectoryInfo(path);
            directory.Refresh();
            return directory.Exists || directory.LinkTarget is not null;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void RequireSourceStillBound(
        string path,
        FileStream source,
        FileIdentity expected)
    {
        RequireAliasFreePath(path);
        var handleIdentity = GetFileIdentity(source.SafeFileHandle);
        RequireUniqueRegularFile(handleIdentity);
        if (!handleIdentity.SameFile(expected))
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        RequireSameIdentity(path, expected);
        RequireNoSidecars(path);
    }

    private static void RequireSameIdentity(string path, FileIdentity expected)
    {
        try
        {
            using var current = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                1,
                FileOptions.None);
            var currentIdentity = GetFileIdentity(current.SafeFileHandle);
            RequireUniqueRegularFile(currentIdentity);
            if (!currentIdentity.SameFile(expected))
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
        catch (LedgerRestoreVerificationException)
        {
            throw;
        }
        catch
        {
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static void RequireUniqueRegularFile(FileIdentity identity)
    {
        if (!identity.IsRegular || identity.LinkCount != 1)
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    private static FileIdentity GetFileIdentity(SafeFileHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed)
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);

        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(handle, out var info))
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            var fileIndex = ((ulong) info.FileIndexHigh << 32) | info.FileIndexLow;
            var isRegular = (info.FileAttributes &
                             (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
            return new FileIdentity(info.VolumeSerialNumber, fileIndex, info.NumberOfLinks, isRegular);
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);

        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            if (FStat(handle.DangerousGetHandle().ToInt32(), buffer) != 0)
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);

            ulong device;
            ulong inode;
            ulong linkCount;
            uint mode;
            if (OperatingSystem.IsMacOS())
            {
                device = unchecked((uint) Marshal.ReadInt32(buffer, 0));
                mode = unchecked((ushort) Marshal.ReadInt16(buffer, 4));
                linkCount = unchecked((ushort) Marshal.ReadInt16(buffer, 6));
                inode = unchecked((ulong) Marshal.ReadInt64(buffer, 8));
            }
            else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                device = unchecked((ulong) Marshal.ReadInt64(buffer, 0));
                inode = unchecked((ulong) Marshal.ReadInt64(buffer, 8));
                linkCount = unchecked((ulong) Marshal.ReadInt64(buffer, 16));
                mode = unchecked((uint) Marshal.ReadInt32(buffer, 24));
            }
            else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                device = unchecked((ulong) Marshal.ReadInt64(buffer, 0));
                inode = unchecked((ulong) Marshal.ReadInt64(buffer, 8));
                mode = unchecked((uint) Marshal.ReadInt32(buffer, 16));
                linkCount = unchecked((uint) Marshal.ReadInt32(buffer, 20));
            }
            else
            {
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            }

            const uint fileTypeMask = 0xF000;
            const uint regularFile = 0x8000;
            return new FileIdentity(device, inode, linkCount, (mode & fileTypeMask) == regularFile);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static async Task<byte[]> HashCandidateAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                HashBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[HashBufferSize];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                hash.AppendData(buffer, 0, read);
            return hash.GetHashAndReset();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
        }
    }

    private static async Task<CandidateFacts> InspectCandidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var queryOnly = connection.CreateCommand())
            {
                queryOnly.CommandText = "PRAGMA query_only=ON;";
                await queryOnly.ExecuteNonQueryAsync(cancellationToken);
            }

            await RequireIntegrityAsync(connection, cancellationToken);
            await RequireStructureAsync(connection, cancellationToken);

            var schemaVersion = await ReadInt32Async(connection, "PRAGMA user_version;", cancellationToken);
            if (schemaVersion != SeasonLedgerStore.CurrentSchemaVersion)
                throw Failure(LedgerRestoreFailureCode.UnsupportedSchema);

            var seasonId = await ReadCurrentSeasonAsync(connection, cancellationToken);
            var proof = new LedgerRestoreProof(
                schemaVersion,
                await ReadCountAsync(connection, "round_envelopes", cancellationToken),
                await ReadCountAsync(connection, "player_stats", cancellationToken),
                await ReadCountAsync(connection, "ledger_outbox", cancellationToken));
            return new CandidateFacts(proof, FingerprintSeason(seasonId));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LedgerRestoreVerificationException)
        {
            throw;
        }
        catch
        {
            throw Failure(LedgerRestoreFailureCode.IntegrityFailure);
        }
    }

    private static async Task RequireIntegrityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal) ||
            await reader.ReadAsync(cancellationToken))
        {
            throw Failure(LedgerRestoreFailureCode.IntegrityFailure);
        }
    }

    private static async Task RequireStructureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table'
              AND name IN ('meta', 'player_stats', 'round_envelopes', 'ledger_outbox');
            """;
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            found.Add(reader.GetString(0));

        foreach (var required in RequiredTables)
        {
            if (!found.Contains(required))
                throw Failure(LedgerRestoreFailureCode.MissingStructure);
        }
    }

    private static async Task<int> ReadInt32Async(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> ReadCountAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<string> ReadCurrentSeasonAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'current_season';";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not string seasonId || string.IsNullOrWhiteSpace(seasonId))
            throw Failure(LedgerRestoreFailureCode.MissingStructure);
        return seasonId;
    }

    private static string FingerprintSeason(string seasonId)
    {
        return ToLowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(seasonId)));
    }

    private static string ToLowerHex(byte[] value)
    {
        return Convert.ToHexString(value).ToLowerInvariant();
    }

    private static LedgerRestoreVerificationException Failure(LedgerRestoreFailureCode code)
    {
        return new LedgerRestoreVerificationException(code);
    }

    private readonly record struct CandidateFacts(
        LedgerRestoreProof Proof,
        string SeasonFingerprint);

    private readonly record struct PrivateStagingDirectory(
        DirectoryInfo Directory,
        SecurityIdentifier? WindowsSid);

    private readonly record struct FileIdentity(
        ulong Device,
        ulong File,
        ulong LinkCount,
        bool IsRegular)
    {
        internal bool SameFile(FileIdentity other) => Device == other.Device && File == other.File;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal FileAttributes FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(int fileDescriptor, IntPtr buffer);

    private sealed class StagedCandidate(
        string directoryPath,
        string path,
        ILedgerRestoreStagingFaultInjector stagingFaultInjector) : IAsyncDisposable
    {
        internal string Path { get; } = path;

        public ValueTask DisposeAsync()
        {
            try
            {
                stagingFaultInjector.Hit(LedgerRestoreStagingStage.CleanupStarted, Path);
                if (OperatingSystem.IsWindows() && File.Exists(Path))
                    File.SetAttributes(Path, File.GetAttributes(Path) & ~FileAttributes.ReadOnly);
                if (Directory.Exists(directoryPath))
                    Directory.Delete(directoryPath, recursive: true);
                return ValueTask.CompletedTask;
            }
            catch
            {
                throw Failure(LedgerRestoreFailureCode.SnapshotUnavailable);
            }
        }
    }
}
