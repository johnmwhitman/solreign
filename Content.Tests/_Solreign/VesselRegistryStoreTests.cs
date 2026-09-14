using System.IO;
using Content.Server._Solreign.VesselIdentity;
using Content.Shared._Solreign.VesselIdentity;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignVesselRegistryStore))]
public sealed class SolreignVesselRegistryStoreTests
{
    private SolreignVesselRegistryStore _store = default!;
    private string _tempFilePath = default!;

    [SetUp]
    public void SetUp()
    {
        _store = new SolreignVesselRegistryStore();
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"vessel_registry_test_{System.Guid.NewGuid():N}.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempFilePath))
        {
            try { File.Delete(_tempFilePath); } catch { }
        }
    }

    [Test]
    public void SaveRecord_And_TryGetRecord_Works()
    {
        var record = new SolreignVesselRegistryRecord
        {
            VesselId = "VESSEL-001",
            VesselName = "Star Wanderer",
            DefaultName = "SV-Expedition-001",
            RegistryMark = SolreignVesselRegistryMark.BronzeStripe,
            ModerationState = SolreignVesselModerationState.Approved,
            MissionsCompleted = 4,
            TotalSalvageValue = 15000f
        };

        _store.SaveRecord(record);

        Assert.That(_store.TryGetRecord("VESSEL-001", out var retrieved), Is.True);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.VesselName, Is.EqualTo("Star Wanderer"));
        Assert.That(retrieved.RegistryMark, Is.EqualTo(SolreignVesselRegistryMark.BronzeStripe));
        Assert.That(retrieved.MissionsCompleted, Is.EqualTo(4));
    }

    [Test]
    public void RemoveRecord_ClearsRecord()
    {
        var record = new SolreignVesselRegistryRecord { VesselId = "VESSEL-002", VesselName = "Void Seeker" };
        _store.SaveRecord(record);

        Assert.That(_store.RemoveRecord("VESSEL-002"), Is.True);
        Assert.That(_store.TryGetRecord("VESSEL-002", out _), Is.False);
    }

    [Test]
    public void SerializeAndLoadJson_PreservesAllRecordData()
    {
        var record1 = new SolreignVesselRegistryRecord
        {
            VesselId = "VESSEL-101",
            VesselName = "Solar Eclipse",
            DefaultName = "SV-101",
            RegistryMark = SolreignVesselRegistryMark.SilverInsignia,
            ModerationState = SolreignVesselModerationState.Approved,
            MissionsCompleted = 12,
            TotalSalvageValue = 65000f
        };

        var record2 = new SolreignVesselRegistryRecord
        {
            VesselId = "VESSEL-102",
            VesselName = "SV-102",
            DefaultName = "SV-102",
            RegistryMark = SolreignVesselRegistryMark.Unmarked,
            ModerationState = SolreignVesselModerationState.ResetByAdmin,
            MissionsCompleted = 1,
            TotalSalvageValue = 2000f
        };

        _store.SaveRecord(record1);
        _store.SaveRecord(record2);

        var json = _store.SerializeToJson();
        Assert.That(json, Is.Not.Empty);

        var newStore = new SolreignVesselRegistryStore();
        newStore.LoadFromJson(json);

        Assert.That(newStore.Records.Count, Is.EqualTo(2));
        Assert.That(newStore.TryGetRecord("VESSEL-101", out var rec1), Is.True);
        Assert.That(rec1!.VesselName, Is.EqualTo("Solar Eclipse"));
        Assert.That(rec1.RegistryMark, Is.EqualTo(SolreignVesselRegistryMark.SilverInsignia));

        Assert.That(newStore.TryGetRecord("VESSEL-102", out var rec2), Is.True);
        Assert.That(rec2!.ModerationState, Is.EqualTo(SolreignVesselModerationState.ResetByAdmin));
    }

    [Test]
    public void SaveToFile_And_LoadFromFile_PersistsDiskEnvelope()
    {
        var record = new SolreignVesselRegistryRecord
        {
            VesselId = "VESSEL-DISK-01",
            VesselName = "Chrono Comet",
            RegistryMark = SolreignVesselRegistryMark.GoldEmblem,
            MissionsCompleted = 28,
            TotalSalvageValue = 180000f
        };

        _store.SaveRecord(record);
        _store.SaveToFile(_tempFilePath);

        Assert.That(File.Exists(_tempFilePath), Is.True);

        var loadedStore = new SolreignVesselRegistryStore();
        loadedStore.LoadFromFile(_tempFilePath);

        Assert.That(loadedStore.TryGetRecord("VESSEL-DISK-01", out var diskRec), Is.True);
        Assert.That(diskRec!.VesselName, Is.EqualTo("Chrono Comet"));
        Assert.That(diskRec.RegistryMark, Is.EqualTo(SolreignVesselRegistryMark.GoldEmblem));
    }
}
