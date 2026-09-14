using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class SeasonLedgerRestoreVerifierTests
{
    private static readonly Guid Player = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly List<string> _paths = new();
    private string _root = default!;

    [SetUp]
    public void SetUp()
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal))
            temp = "/private" + temp;
        _root = Path.Combine(temp, $"solreign_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _paths)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try
                {
                    File.Delete(path + suffix);
                }
                catch
                {
                    // Synthetic temp files only; best-effort cleanup.
                }
            }
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Synthetic temp tree only; best-effort cleanup.
        }
    }

    [Test]
    public async Task ClosedSnapshot_VerifiesProgressionAndRestoredReplayRemainsIdempotent()
    {
        var (snapshot, captured) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        var restored = TempPath("restore");
        File.Copy(snapshot, restored);

        var proof = await SeasonLedgerRestoreVerifier.VerifyAsync(restored, manifest, CancellationToken.None);
        var unchangedManifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(restored, CancellationToken.None);
        var beforePlayers = await ReadCountAsync(restored, "player_stats");
        var beforeOutbox = await ReadCountAsync(restored, "ledger_outbox");
        var restoredStore = new SeasonLedgerStore(restored);
        var currentSeason = await restoredStore.GetCurrentSeasonAsync();
        var replay = await restoredStore.PersistRoundEndOnceAsync(captured, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.ManifestSchema, Is.EqualTo(1));
            Assert.That(manifest.DatabaseSha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(manifest.SeasonFingerprint, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(unchangedManifest, Is.EqualTo(manifest),
                "read-only verification must leave the candidate byte-for-byte unchanged");
            Assert.That(proof, Is.EqualTo(new LedgerRestoreProof(
                SeasonLedgerStore.CurrentSchemaVersion,
                RoundEnvelopes: 1,
                PlayerStats: 1,
                OutboxRows: 1)));
            Assert.That(currentSeason, Is.EqualTo("S2"));
            Assert.That(replay, Is.EqualTo(RoundEnvelopePersistResult.AlreadyCommitted));
            Assert.That(beforePlayers, Is.EqualTo(1));
            Assert.That(beforeOutbox, Is.EqualTo(1));
        });
        Assert.That(await ReadCountAsync(restored, "player_stats"), Is.EqualTo(beforePlayers));
        Assert.That(await ReadCountAsync(restored, "ledger_outbox"), Is.EqualTo(beforeOutbox));
        Assert.That(await ReadCountAsync(restored, "round_envelopes"), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task ManifestSchemaOtherThanOneIsInvalid(int manifestSchema)
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { ManifestSchema = manifestSchema },
                CancellationToken.None),
            LedgerRestoreFailureCode.InvalidManifest);
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task MalformedDigestIsInvalid(string digest)
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { DatabaseSha256 = digest },
                CancellationToken.None),
            LedgerRestoreFailureCode.InvalidManifest);
    }

    [Test]
    public async Task UppercaseDigestAndFingerprintAreInvalid()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { DatabaseSha256 = manifest.DatabaseSha256.ToUpperInvariant() },
                CancellationToken.None),
            LedgerRestoreFailureCode.InvalidManifest);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { SeasonFingerprint = manifest.SeasonFingerprint.ToUpperInvariant() },
                CancellationToken.None),
            LedgerRestoreFailureCode.InvalidManifest);
    }

    [Test]
    public async Task IncorrectDigestFailsBeforeCandidateInspection()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { DatabaseSha256 = new string('0', 64) },
                CancellationToken.None),
            LedgerRestoreFailureCode.DigestMismatch);
    }

    [Test]
    public async Task TruncatedTransferredFileFailsDigestVerification()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        var restored = TempPath("truncated");
        File.Copy(snapshot, restored);
        await using (var stream = new FileStream(restored, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(128);
        }

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(restored, manifest, CancellationToken.None),
            LedgerRestoreFailureCode.DigestMismatch);
    }

    [Test]
    public async Task NonDatabaseSnapshotFailsIntegrityWithoutLeakingDetails()
    {
        var path = TempPath("corrupt");
        await File.WriteAllTextAsync(path, "not a sqlite database");

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(path, CancellationToken.None),
            LedgerRestoreFailureCode.IntegrityFailure);
    }

    [TestCase(0)]
    [TestCase(99)]
    public async Task OlderAndFutureSchemaVersionsFailClosed(int version)
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        await ExecuteAsync(snapshot, $"PRAGMA user_version = {version};");

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None),
            LedgerRestoreFailureCode.UnsupportedSchema);
    }

    [Test]
    public async Task MissingRequiredTableFailsClosed()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        await ExecuteAsync(snapshot, "DROP TABLE ledger_outbox;");

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None),
            LedgerRestoreFailureCode.MissingStructure);
    }

    [Test]
    public async Task ChangedAggregateOrSeasonFingerprintFailsProgressionProof()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { PlayerStats = manifest.PlayerStats + 1 },
                CancellationToken.None),
            LedgerRestoreFailureCode.ProgressionMismatch);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest with { SeasonFingerprint = new string('0', 64) },
                CancellationToken.None),
            LedgerRestoreFailureCode.ProgressionMismatch);
    }

    [Test]
    public async Task WalResidentPrivateMutationIsRejectedEvenWhenPublicProofIsUnchanged()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var baseline = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);

        await using var connection = new SqliteConnection(ConnectionString(snapshot, SqliteOpenMode.ReadWrite));
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(connection, "PRAGMA wal_autocheckpoint=0;");
        var digestBeforeMutation = await HashFileAsync(snapshot);

        await ExecuteAsync(connection, "UPDATE player_stats SET standing_total = standing_total + 1000;");

        var digestAfterMutation = await HashFileAsync(snapshot);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(snapshot + "-wal"), Is.True, "the private mutation must remain outside the main database file");
            Assert.That(digestAfterMutation, Is.EqualTo(digestBeforeMutation), "the adversarial setup must leave the main-file digest unchanged");
        });

        var mainFileManifest = baseline with { DatabaseSha256 = digestAfterMutation };
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(snapshot, mainFileManifest, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public async Task AdjacentRollbackJournalIsRejectedAsUnboundState()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(snapshot + "-journal", "synthetic unbound rollback state");

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(snapshot, manifest, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public async Task SidecarDirectoryIsRejectedAsUnboundState()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        Directory.CreateDirectory(snapshot + "-wal");

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public async Task DanglingSidecarSymlinkIsRejectedAsUnboundState()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        CreateFileSymlinkOrIgnore(snapshot + "-shm", TempPath("missing_sidecar_target"));

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public async Task WalMutationReachedThroughFileSymlinkIsRejected()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        var alias = TempPath("file_symlink");
        CreateFileSymlinkOrIgnore(alias, snapshot);
        await using var connection = await BeginWalMutationAsync(snapshot);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(alias, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(alias, manifest, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public async Task WalMutationReachedThroughParentDirectorySymlinkIsRejected()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var realDirectory = Path.Combine(_root, "real_parent");
        Directory.CreateDirectory(realDirectory);
        var realSnapshot = Path.Combine(realDirectory, "snapshot.db");
        File.Move(snapshot, realSnapshot);
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(realSnapshot, CancellationToken.None);
        var aliasDirectory = Path.Combine(_root, "alias_parent");
        CreateDirectorySymlinkOrIgnore(aliasDirectory, realDirectory);
        var alias = Path.Combine(aliasDirectory, "snapshot.db");
        await using var connection = await BeginWalMutationAsync(realSnapshot);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(alias, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(alias, manifest, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public async Task WalMutationReachedThroughHardLinkIsRejected()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        var alias = TempPath("hardlink");
        CreateHardLinkOrIgnore(alias, snapshot);
        await using var connection = await BeginWalMutationAsync(snapshot);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(alias, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(alias, manifest, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public void AliasPolicy_SourceRequiresPathAndHandleIdentityWithOneLink()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerRestoreVerifier.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("LinkTarget"));
            Assert.That(source, Does.Contain("FileAttributes.ReparsePoint"));
            Assert.That(source, Does.Contain("LinkCount != 1"));
            Assert.That(source, Does.Contain("GetFileIdentity"));
            Assert.That(source, Does.Contain("RequireSameIdentity"));
        });
    }

    [Test]
    public void WindowsStagingAcl_SourceContractIsFailClosedAndIdentityBound()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerRestoreVerifier.cs"));
        var testSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Content.Tests/_Solreign/SeasonLedgerRestoreVerifierTests.cs"));
        var directDelete = "Directory.Delete(Path.GetDirectoryName" +
                           "(injector.StagedPath)!, recursive: true)";

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("WindowsIdentity.GetCurrent"));
            Assert.That(source, Does.Contain("SetAccessRuleProtection(isProtected: true, preserveInheritance: false)"));
            Assert.That(source, Does.Contain("InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit"));
            Assert.That(source, Does.Contain("FileSystemRights.FullControl"));
            Assert.That(source, Does.Contain("VerifyWindowsDirectoryAcl"));
            Assert.That(source, Does.Contain("VerifyWindowsFileAcl"));
            Assert.That(testSource, Does.Contain("CleanupStagingResidueBestEffort"));
            Assert.That(testSource, Does.Contain("FileAttributes.ReadOnly"));
            Assert.That(testSource, Does.Not.Contain(directDelete));
            Assert.That(testSource.Split(
                    "finally\n        {\n            CleanupStagingResidueBestEffort(injector.StagedPath);",
                    StringSplitOptions.None),
                Has.Length.EqualTo(4));
        });
    }

    [Test]
    public async Task WindowsStageAclAllowsOnlyCurrentServiceIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows DACL runtime proof runs only on Windows.");
            return;
        }

        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var injector = new CaptureWindowsAclAtCopy();
        await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None, injector);

        AssertWindowsStagingAcl(injector.DirectoryAcl, injector.FileAcl, injector.CurrentSid);
    }

    [Test]
    public void WindowsCleanupFailureResidueRetainsRestrictedAcl()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows DACL runtime proof runs only on Windows.");
            return;
        }

        var (snapshot, _) = CreateClosedSnapshotAsync().GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        var injector = new CancelAndFailCleanup(cancellation);

        try
        {
            Assert.That(
                async () => await SeasonLedgerRestoreVerifier.CreateManifestAsync(
                    snapshot,
                    cancellation.Token,
                    injector),
                Throws.InstanceOf<OperationCanceledException>());

            var directoryAcl = new DirectoryInfo(Path.GetDirectoryName(injector.StagedPath)!)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var fileAcl = new FileInfo(injector.StagedPath)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            AssertWindowsStagingAcl(directoryAcl, fileAcl, identity.User!);
        }
        finally
        {
            CleanupStagingResidueBestEffort(injector.StagedPath);
        }
    }

    [Test]
    public async Task AtomicPathReplacementDuringManifestStagingCannotSplitDigestAndProof()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        var replacement = TempPath("manifest_replacement");
        File.Copy(snapshot, replacement);
        await ExecuteAsync(replacement, "UPDATE meta SET value = 'S99' WHERE key = 'current_season';");
        var injector = new ReplacePathWhenSourceIsOpen(snapshot, replacement);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(
                snapshot,
                CancellationToken.None,
                injector),
            LedgerRestoreFailureCode.SnapshotUnavailable);

        Assert.Multiple(() =>
        {
            Assert.That(injector.HitCount, Is.EqualTo(1));
            Assert.That(File.Exists(injector.StagedPath), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(injector.StagedPath)!), Is.False);
        });
    }

    [Test]
    public async Task AtomicPathReplacementDuringVerifyStagingCannotChangeInspectedBytes()
    {
        var (snapshot, _) = await CreateClosedSnapshotAsync();
        var manifest = await SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None);
        var replacement = TempPath("verify_replacement");
        File.Copy(snapshot, replacement);
        await ExecuteAsync(replacement, "UPDATE meta SET value = 'S99' WHERE key = 'current_season';");
        var injector = new ReplacePathWhenSourceIsOpen(snapshot, replacement);

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.VerifyAsync(
                snapshot,
                manifest,
                CancellationToken.None,
                injector),
            LedgerRestoreFailureCode.SnapshotUnavailable);

        Assert.Multiple(() =>
        {
            Assert.That(injector.HitCount, Is.EqualTo(1));
            Assert.That(File.Exists(injector.StagedPath), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(injector.StagedPath)!), Is.False);
        });
    }

    [Test]
    public void CancellationAfterStagingCleansOwnerPrivateArtifact()
    {
        var (snapshot, _) = CreateClosedSnapshotAsync().GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        var injector = new CancelAfterCopy(cancellation);

        Assert.That(
            async () => await SeasonLedgerRestoreVerifier.CreateManifestAsync(
                snapshot,
                cancellation.Token,
                injector),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.Multiple(() =>
        {
            Assert.That(injector.StagedPath, Is.Not.Empty);
            Assert.That(File.Exists(injector.StagedPath), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(injector.StagedPath)!), Is.False);
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(injector.DirectoryMode, Is.EqualTo(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
                Assert.That(injector.FileMode, Is.EqualTo(UnixFileMode.UserRead));
            }
        });
    }

    [Test]
    public void CancellationRemainsAuthoritativeWhenPrivateCleanupFails()
    {
        var (snapshot, _) = CreateClosedSnapshotAsync().GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        var injector = new CancelAndFailCleanup(cancellation);

        try
        {
            Assert.That(
                async () => await SeasonLedgerRestoreVerifier.CreateManifestAsync(
                    snapshot,
                    cancellation.Token,
                    injector),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.Multiple(() =>
            {
                Assert.That(injector.CleanupHitCount, Is.EqualTo(1));
                Assert.That(File.Exists(injector.StagedPath), Is.True,
                    "failed cleanup may leave only the owner-private staging artifact");
            });
        }
        finally
        {
            CleanupStagingResidueBestEffort(injector.StagedPath);
        }
    }

    [Test]
    public void VerifyCancellationRemainsAuthoritativeWhenPrivateCleanupFails()
    {
        var (snapshot, _) = CreateClosedSnapshotAsync().GetAwaiter().GetResult();
        var manifest = SeasonLedgerRestoreVerifier.CreateManifestAsync(snapshot, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        using var cancellation = new CancellationTokenSource();
        var injector = new CancelAndFailCleanup(cancellation);

        try
        {
            Assert.That(
                async () => await SeasonLedgerRestoreVerifier.VerifyAsync(
                    snapshot,
                    manifest,
                    cancellation.Token,
                    injector),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(injector.CleanupHitCount, Is.EqualTo(1));
        }
        finally
        {
            CleanupStagingResidueBestEffort(injector.StagedPath);
        }
    }

    [Test]
    public async Task MissingSnapshotHasFixedUnavailableFailure()
    {
        var missing = TempPath("missing");

        await AssertFailureAsync(
            () => SeasonLedgerRestoreVerifier.CreateManifestAsync(missing, CancellationToken.None),
            LedgerRestoreFailureCode.SnapshotUnavailable);
    }

    [Test]
    public void CancellationIsPreservedForManifestAndVerify()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var manifest = new LedgerRestoreManifest(
            1,
            new string('0', 64),
            SeasonLedgerStore.CurrentSchemaVersion,
            new string('0', 64),
            0,
            0,
            0);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await SeasonLedgerRestoreVerifier.CreateManifestAsync("unused", cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(
                async () => await SeasonLedgerRestoreVerifier.VerifyAsync("unused", manifest, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        });
    }

    [Test]
    public void VerificationExceptionDeclaresOnlyBoundedFailureCode()
    {
        var properties = typeof(LedgerRestoreVerificationException)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.That(properties.Select(property => property.Name), Is.EqualTo(new[] { "Code" }));
        Assert.That(properties.Single().PropertyType, Is.EqualTo(typeof(LedgerRestoreFailureCode)));
    }

    private async Task<(string Snapshot, CapturedRoundEndEnvelope Captured)> CreateClosedSnapshotAsync()
    {
        var source = TempPath("source");
        var snapshot = TempPath("snapshot");
        var store = new SeasonLedgerStore(source);
        var captured = new RoundEndEnvelope(
            41,
            "Secret",
            new[]
            {
                new RoundEndPlayerRecord(
                    Player,
                    new RoundContribution(
                        WasCaptainClean: true,
                        AntagWin: false,
                        EarlyDeath: false,
                        RoundId: 41,
                        Gamemode: "Secret",
                        Standing: 5)),
            },
            Array.Empty<ContractLogRecord>())
            .Canonicalize();

        Assert.That(
            await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None),
            Is.EqualTo(RoundEnvelopePersistResult.Committed));
        Assert.That(await store.BumpSeasonAsync(), Is.EqualTo("S2"));

        await using var sourceConnection = new SqliteConnection(ConnectionString(source, SqliteOpenMode.ReadOnly));
        await using var snapshotConnection = new SqliteConnection(ConnectionString(snapshot, SqliteOpenMode.ReadWriteCreate));
        await sourceConnection.OpenAsync();
        await snapshotConnection.OpenAsync();
        sourceConnection.BackupDatabase(snapshotConnection);
        await snapshotConnection.CloseAsync();
        await sourceConnection.CloseAsync();
        return (snapshot, captured);
    }

    private static Task AssertFailureAsync(
        Func<Task> action,
        LedgerRestoreFailureCode expected)
    {
        var exception = Assert.ThrowsAsync<LedgerRestoreVerificationException>(async () => await action());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(expected));
            Assert.That(exception.Message, Is.EqualTo(expected.ToString()));
            Assert.That(exception.InnerException, Is.Null);
            Assert.That(exception.Message, Does.Not.Contain(Path.GetTempPath()));
            Assert.That(exception.Message, Does.Not.Contain(Player.ToString("D")));
        });
        return Task.CompletedTask;
    }

    private async Task<long> ReadCountAsync(string path, string table)
    {
        await using var connection = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadOnly));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadWrite));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private string TempPath(string purpose)
    {
        var path = Path.Combine(_root, $"{purpose}_{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return path;
    }

    private static async Task<SqliteConnection> BeginWalMutationAsync(string path)
    {
        var connection = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadWrite));
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(connection, "PRAGMA wal_autocheckpoint=0;");
        var digestBefore = await HashFileAsync(path);
        await ExecuteAsync(connection, "UPDATE player_stats SET standing_total = standing_total + 1000;");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path + "-wal"), Is.True);
            Assert.That(HashFileAsync(path).GetAwaiter().GetResult(), Is.EqualTo(digestBefore));
        });
        return connection;
    }

    private static void CreateFileSymlinkOrIgnore(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            Assert.Ignore($"File symlink primitive unavailable: {exception.GetType().Name}");
        }
    }

    private static void CreateDirectorySymlinkOrIgnore(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            Assert.Ignore($"Directory symlink primitive unavailable: {exception.GetType().Name}");
        }
    }

    private static void CreateHardLinkOrIgnore(string linkPath, string targetPath)
    {
        var result = OperatingSystem.IsWindows()
            ? CreateHardLinkWindows(linkPath, targetPath, IntPtr.Zero) ? 0 : Marshal.GetLastPInvokeError()
            : CreateHardLinkUnix(targetPath, linkPath) == 0 ? 0 : Marshal.GetLastPInvokeError();
        if (result != 0)
            Assert.Ignore($"Hard-link primitive unavailable: {new Win32Exception(result).NativeErrorCode}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Content.Server/_Solreign/SeasonLedger/SeasonLedgerRestoreVerifier.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void CleanupStagingResidueBestEffort(string stagedPath)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(stagedPath);
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Name.StartsWith("solreign-ledger-restore-", StringComparison.Ordinal))
                return;

            CleanupEntryNoFollow(directory);
        }
        catch
        {
            // Test-owned randomized residue only; cleanup must never mask the assertion result.
        }
    }

    private static void CleanupEntryNoFollow(FileSystemInfo entry)
    {
        entry.Refresh();
        var attributes = entry.Attributes;
        var isLink = entry.LinkTarget is not null ||
                     (attributes & FileAttributes.ReparsePoint) != 0;
        if (isLink)
        {
            if (entry is DirectoryInfo)
                Directory.Delete(entry.FullName);
            else
                File.Delete(entry.FullName);
            return;
        }

        if (entry is DirectoryInfo directory)
        {
            foreach (var child in directory.EnumerateFileSystemInfos())
                CleanupEntryNoFollow(child);
        }

        File.SetAttributes(entry.FullName, attributes & ~FileAttributes.ReadOnly);
        if (entry is DirectoryInfo)
            Directory.Delete(entry.FullName);
        else
            File.Delete(entry.FullName);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsStagingAcl(
        DirectorySecurity directoryAcl,
        FileSecurity fileAcl,
        SecurityIdentifier currentSid)
    {
        var directoryRules = directoryAcl
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var fileRules = fileAcl
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(directoryAcl.AreAccessRulesProtected, Is.True);
            Assert.That(directoryAcl.GetOwner(typeof(SecurityIdentifier)), Is.EqualTo(currentSid));
            Assert.That(fileAcl.GetOwner(typeof(SecurityIdentifier)), Is.EqualTo(currentSid));
            Assert.That(directoryRules, Has.Length.EqualTo(1));
            Assert.That(fileRules, Has.Length.EqualTo(1));
            Assert.That(directoryRules.All(rule =>
                rule.IdentityReference.Equals(currentSid) &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl &&
                !rule.IsInherited &&
                rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)),
                Is.True);
            Assert.That(fileRules.All(rule =>
                rule.IdentityReference.Equals(currentSid) &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl),
                Is.True);
        });
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(string existingPath, string newPath);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string newPath, string existingPath, IntPtr securityAttributes);

    private static string ConnectionString(string path, SqliteOpenMode mode)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
    }

    private sealed class ReplacePathWhenSourceIsOpen(string source, string replacement)
        : ILedgerRestoreStagingFaultInjector
    {
        internal int HitCount { get; private set; }
        internal string StagedPath { get; private set; } = string.Empty;

        public void Hit(LedgerRestoreStagingStage stage, string stagedPath)
        {
            if (stage != LedgerRestoreStagingStage.SourceOpened)
                return;

            StagedPath = stagedPath;
            File.Move(replacement, source, overwrite: true);
            HitCount++;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class CaptureWindowsAclAtCopy : ILedgerRestoreStagingFaultInjector
    {
        internal DirectorySecurity DirectoryAcl { get; private set; } = null!;
        internal FileSecurity FileAcl { get; private set; } = null!;
        internal SecurityIdentifier CurrentSid { get; private set; } = null!;

        public void Hit(LedgerRestoreStagingStage stage, string stagedPath)
        {
            if (stage != LedgerRestoreStagingStage.CopyCompleted)
                return;

            DirectoryAcl = new DirectoryInfo(Path.GetDirectoryName(stagedPath)!)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            FileAcl = new FileInfo(stagedPath)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            CurrentSid = identity.User;
        }
    }

    private sealed class CancelAfterCopy(CancellationTokenSource cancellation)
        : ILedgerRestoreStagingFaultInjector
    {
        internal string StagedPath { get; private set; } = string.Empty;
        internal UnixFileMode? DirectoryMode { get; private set; }
        internal UnixFileMode? FileMode { get; private set; }

        public void Hit(LedgerRestoreStagingStage stage, string stagedPath)
        {
            if (stage != LedgerRestoreStagingStage.CopyCompleted)
                return;

            StagedPath = stagedPath;
            if (!OperatingSystem.IsWindows())
            {
                DirectoryMode = File.GetUnixFileMode(Path.GetDirectoryName(stagedPath)!);
                FileMode = File.GetUnixFileMode(stagedPath);
            }

            cancellation.Cancel();
        }
    }

    private sealed class CancelAndFailCleanup(CancellationTokenSource cancellation)
        : ILedgerRestoreStagingFaultInjector
    {
        internal string StagedPath { get; private set; } = string.Empty;
        internal int CleanupHitCount { get; private set; }

        public void Hit(LedgerRestoreStagingStage stage, string stagedPath)
        {
            if (stage == LedgerRestoreStagingStage.CopyCompleted)
            {
                StagedPath = stagedPath;
                cancellation.Cancel();
                return;
            }

            if (stage != LedgerRestoreStagingStage.CleanupStarted)
                return;

            CleanupHitCount++;
            throw new IOException("deterministic private cleanup failure");
        }
    }
}
