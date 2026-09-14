#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     LIVE-INCIDENT GATE (2026-07-26): players reported spawning **naked and immediately gasping
///     for air**, on two consecutive releases. Every existing gate was green while it happened:
///     <list type="bullet">
///       <item><see cref="SolreignRoundStartJobSpreadGateTest"/> drives a REAL round start on all
///             seven Solreign maps and passed — but it only asserts that a player got an entity and
///             a job. A naked, asphyxiating body satisfies both.</item>
///       <item>The map-health scorecard checks job SPAWN POINTS exist, not what the spawned body is
///             wearing or breathing.</item>
///       <item>The box has <c>[log] enabled = false</c>, so the round-start diagnostics that WOULD
///             have named the cause were written to a 0-byte file.</item>
///     </list>
///     This fixture converts the two reported symptoms directly into per-map assertions. It is the
///     answer to "ask what your gate does NOT run": job spread was covered, embodiment was not.
///
///     WHAT IT ASSERTS, per Solreign rotation map, on a real round start:
///     <list type="number">
///       <item><b>CLOTHED</b> — every spawned crew member has something in the <c>jumpsuit</c> slot.
///             Starting gear failing to apply is the "no clothes" symptom, and it is also the
///             fingerprint of a profile/loadout fallback (a <c>Random()</c> profile or an exception
///             thrown mid-equip leaves a bare body).</item>
///       <item><b>BREATHING</b> — no spawned crew member accumulates Asphyxiation damage over a
///             short soak. This catches a species/internals mismatch (e.g. a Vox seated without its
///             nitrogen internals), an unpressurised spawn tile, and a station that starts without
///             breathable atmosphere.</item>
///     </list>
///
///     Deliberately NOT asserting zero damage of every type — crew can legitimately take other
///     damage at round start. Asphyxiation specifically is never normal for a freshly-spawned
///     crewmember standing in their own station.
/// </summary>
[TestFixture]
public sealed class SolreignSpawnedCrewClothedAndBreathingTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private static readonly ProtoId<JobPrototype>[] DiverseJobs =
    {
        "StationEngineer",
        "AtmosphericTechnician",
        "MedicalDoctor",
        "Botanist",
        "CargoTechnician",
    };

    private const int DummyCount = 4;

    /// <summary>
    ///     Ticks to soak after the round enters play. Asphyxiation accrues per respirator update, so
    ///     a body that cannot breathe shows non-zero damage well inside this window, while a healthy
    ///     one stays at exactly zero.
    /// </summary>
    private const int SoakTicks = 60;

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    public async Task SpawnedCrew_AreClothed_AndNotAsphyxiating(string mapProtoId)
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var invSys = pair.Server.System<InventorySystem>();
        var damageSys = pair.Server.System<DamageableSystem>();
        var entMan = pair.Server.EntMan;

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, mapProtoId);
        pair.Server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "SolreignAntagsOnSpawn");

        await pair.Server.AddDummySessions(DummyCount);
        await pair.RunTicksSync(5);

        var users = pair.Server.PlayerMan.Sessions.Select(x => x.UserId).ToList();
        for (var i = 0; i < users.Count; i++)
        {
            await pair.SetJobPriorities(
                users[i],
                (Passenger, JobPriority.Medium),
                (DiverseJobs[i % DiverseJobs.Length], JobPriority.High));
        }

        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound),
            $"[{mapProtoId}] round did not enter InRound");

        // Let respiration actually run — a body that cannot breathe reveals itself here.
        await pair.RunTicksSync(SoakTicks);

        var naked = new List<string>();
        var suffocating = new List<string>();
        var bodies = 0;

        await pair.Server.WaitPost(() =>
        {
            foreach (var session in pair.Server.PlayerMan.Sessions)
            {
                var uid = session.AttachedEntity;
                if (!entMan.EntityExists(uid))
                    continue;

                bodies++;
                var body = uid!.Value;

                if (!invSys.TryGetSlotEntity(body, "jumpsuit", out _))
                    naked.Add($"{session.Name} ({entMan.ToPrettyString(body)})");

                // RA0002 forbids reading DamageableComponent.Damage from outside, so this uses the
                // sanctioned system reader. That means TOTAL damage rather than Asphyxiation-only —
                // slightly broader than the symptom, but a crewmember who just spawned in their own
                // station should be at exactly zero, so any accrual is worth failing on.
                if (entMan.HasComponent<DamageableComponent>(body))
                {
                    var total = damageSys.GetTotalDamage(body);
                    if (total > 0)
                        suffocating.Add($"{session.Name} ({entMan.ToPrettyString(body)}) totalDamage={total}");
                }
            }
        });

        TestContext.Out.WriteLine(
            $"[SOLREIGN-EMBODIMENT] map={mapProtoId} bodies={bodies} naked={naked.Count} suffocating={suffocating.Count}");

        Assert.Multiple(() =>
        {
            Assert.That(bodies, Is.GreaterThan(0),
                $"[{mapProtoId}] no player got a body — this fixture proved nothing; fix the harness.");

            Assert.That(naked, Is.Empty,
                $"[{mapProtoId}] {naked.Count} spawned crew member(s) have NOTHING in the jumpsuit slot — " +
                "the reported \"drop in with no clothes\" symptom. Starting gear did not apply." +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", naked));

            Assert.That(suffocating, Is.Empty,
                $"[{mapProtoId}] {suffocating.Count} spawned crew member(s) are taking Asphyxiation damage — " +
                "the reported \"gasping for air\" symptom (total damage accruing). Either the spawn tile is unpressurised, the " +
                "station has no breathable atmosphere, or the seated species' internals were not applied." +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", suffocating));
        });
    }
}
