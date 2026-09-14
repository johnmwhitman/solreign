#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Antags.Vampire;
using Content.Server._Solreign.Antags.Werewolf;
using Content.Server.Spawners.Components;
using Content.Shared._Solreign.HotPotato;
using Content.Shared._Solreign.Sprint;
using Content.Shared.Damage.Components;
using Content.Shared.Movement.Components;
using Prometheus;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     PERF HARNESS + BASELINE (the /health lane, docs/perf/2026-07-11-baseline.md).
///
///     Loads the real SolreignOasis round, spawns a synthetic "15 dummy players' worth of mobs"
///     population plus a representative sample of every Solreign system that carries its own
///     per-tick <c>Update</c> loop, then reads two honest numbers straight out of the engine's own
///     instrumentation:
///
///     <list type="bullet">
///     <item>
///         Per-<see cref="EntitySystem"/> mean update time, from the engine's own
///         <c>robust_entity_systems_update_usage</c> Prometheus histogram (a
///         <see cref="Stopwatch"/> running on the SERVER thread inside
///         <c>EntitySystemManager.TickUpdate</c> — see RobustToolbox/Robust.Shared/GameObjects/
///         EntitySystemManager.cs). This is the trustworthy number: it is measured where the work
///         actually happens and is immune to IntegrationTest harness overhead. We snapshot the
///         histogram before and after the measured tick window and diff, so results are not
///         contaminated by any other test that happened to run earlier in the same process.
///     </item>
///     <item>
///         Wall-clock time per <c>WaitRunTicks(1)</c> call, purely as a directional cross-check.
///         This DOES include IntegrationTest harness thread-hop/channel overhead (the test thread
///         and the server thread are different threads talking over a channel) and is NOT a
///         faithful stand-in for a real dedicated server's tick pacing. Use it only to eyeball
///         gross regressions between runs of this same harness, never as an absolute ms budget.
///     </item>
///     </list>
///
///     Everything this test spawns either uses an existing prototype as-is, or adds a component
///     via <c>EnsureComponent&lt;T&gt;()</c> and leaves its fields at their declared defaults — it
///     never assigns a field on an [Access]-locked component from outside its owning system. Where
///     a system's owning API is the only legitimate way to drive state (Sprint), we call that API
///     instead of poking the component.
/// </summary>
[TestFixture]
public sealed class SolreignPerfBaselineTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
        Map = "SolreignOasis",
        Dirty = true, // heavy custom mutation + toggles engine metrics; never recycle this pair.
    };

    private const int PostRoundStartWarmupTicks = 60;
    private const int PostSpawnWarmupTicks = 30;
    private const int MeasuredTicks = 240;
    private const int DummyPlayerMobs = 15;

    [Test]
    public async Task Baseline()
    {
        var server = Server;
        var entMan = server.EntMan;
        var sysMan = entMan.EntitySysManager;

        // Let the round actually finish starting (station post-init, StationSolreignContractsComponent
        // fill-in, atmos seeding, etc.) before we touch anything.
        await server.WaitRunTicks(PostRoundStartWarmupTicks);

        EntityCoordinates? spawnCoords = null;
        await server.WaitPost(() =>
        {
            var query = entMan.EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            if (query.MoveNext(out _, out _, out var xform))
                spawnCoords = xform.Coordinates;
        });

        Assert.That(spawnCoords, Is.Not.Null,
            "Could not find a SpawnPointComponent entity on SolreignOasis to anchor benchmark spawns.");
        var coords = spawnCoords!.Value;

        var spawned = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            // --- "15 dummy players' worth of mobs" ---
            for (var i = 0; i < DummyPlayerMobs; i++)
                spawned.Add(entMan.SpawnEntity("MobHuman", coords));

            // --- Unicorn FX (SolreignPeriodicEffectSystem) ---
            for (var i = 0; i < 8; i++)
                entMan.SpawnEntity("SolreignMobUnicorn", coords);

            // --- Pets (SolreignTameableSystem); prototypes already carry the component ---
            foreach (var petProto in new[] { "MobSolreignTabby", "MobSolreignDuck", "MobSolreignPup" })
                for (var i = 0; i < 3; i++)
                    entMan.SpawnEntity(petProto, coords);

            // --- Compliance Retrieval Units (Terminator/ComplianceRetrievalUnitSystem);
            //     prototype already carries ComplianceFixationComponent ---
            for (var i = 0; i < 5; i++)
                entMan.SpawnEntity("SolreignMobComplianceRetrievalUnit", coords);

            // --- Dormant Ents (SolreignDormantEntSystem); prototype already carries the component ---
            for (var i = 0; i < 6; i++)
                entMan.SpawnEntity("SolreignDormantEnt", coords);

            // --- Hot Potato bombs (SolreignHotPotatoSystem). SolreignHotPotatoComponent carries no
            //     [Access] restriction, so we can legitimately arm these directly from the test to
            //     exercise the "armed" per-tick branch. Detonation is pushed an hour out so nothing
            //     actually goes off mid-benchmark - we only want the steady-state armed-loop cost. ---
            var now = server.Timing.CurTime;
            for (var i = 0; i < 4; i++)
            {
                var uid = entMan.SpawnEntity("SolreignHotPotatoBomb", coords);
                var potato = entMan.EnsureComponent<SolreignHotPotatoComponent>(uid);
                potato.Armed = true;
                potato.DetonateAt = now + TimeSpan.FromHours(1);
                potato.NextBeep = now + TimeSpan.FromHours(1);
            }

            // --- RC controllers + cars (RcControllerSystem), left unbound. RcControllerComponent
            //     and RcControllableComponent are both [Access]-locked to RcControllerSystem, and
            //     binding is only reachable through a private AfterInteract handler, so we don't
            //     force a bound state here - this exercises the (already well-gated) idle fast path
            //     only. See docs/perf/2026-07-11-baseline.md for the honest caveat. ---
            for (var i = 0; i < 3; i++)
            {
                entMan.SpawnEntity("SolreignMobRcCameraCar", coords);
                entMan.SpawnEntity("SolreignItemRcController", coords);
            }

            // --- Vampire / Werewolf antag components layered onto some of the dummy player mobs.
            //     These are normally granted at antag-select time; there's no "just spawn one"
            //     prototype, so we EnsureComponent<T>() (never touching a member of the [Access]-
            //     locked component type) and leave every field at its declared default. Defaults are
            //     already "long overdue" (TimeSpan.Zero) so the very first tick naturally exercises
            //     the real per-tick branch, then settles into the same steady-state gate every other
            //     player would see. ---
            for (var i = 0; i < 5 && i < spawned.Count; i++)
                entMan.EnsureComponent<SolreignVampireComponent>(spawned[i]);

            for (var i = 5; i < 10 && i < spawned.Count; i++)
                entMan.EnsureComponent<SolreignWerewolfComponent>(spawned[i]);
        });

        // --- Sprint (SolreignSprintSystem, Shared): SprintComponent is [Access]-locked to its own
        //     system, so the legitimate way to turn sprint on is the system's own public API, same
        //     as a real client's Sprint keybind would. ---
        var sprintSystem = server.System<SolreignSprintSystem>();
        await server.WaitPost(() =>
        {
            foreach (var uid in spawned)
            {
                entMan.EnsureComponent<StaminaComponent>(uid);
                entMan.EnsureComponent<MovementSpeedModifierComponent>(uid);
                sprintSystem.SetSprintKeyHeld(uid, true);
            }
        });

        // Let MapInit fire on everything we just spawned (schedules PeriodicEffect/DormantEnt next-
        // fire times, etc.) before we start measuring.
        await server.WaitRunTicks(PostSpawnWarmupTicks);

        // --- Measurement ---
        await server.WaitPost(() => sysMan.MetricsEnabled = true);

        var before = await CollectHistogramSnapshot();

        var tickTimesMs = new List<double>(MeasuredTicks);
        for (var i = 0; i < MeasuredTicks; i++)
        {
            var sw = Stopwatch.StartNew();
            await server.WaitRunTicks(1);
            sw.Stop();
            tickTimesMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        var after = await CollectHistogramSnapshot();

        await server.WaitPost(() => sysMan.MetricsEnabled = false);

        // --- Report ---
        tickTimesMs.Sort();
        double Percentile(double p)
        {
            var idx = (int)Math.Clamp(Math.Round(p * (tickTimesMs.Count - 1)), 0, tickTimesMs.Count - 1);
            return tickTimesMs[idx];
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== SOLREIGN PERF BASELINE ===");
        sb.AppendLine($"Measured ticks: {MeasuredTicks}");
        sb.AppendLine($"Dummy player mobs: {DummyPlayerMobs}, plus heavy-system population (see test source).");
        sb.AppendLine();
        sb.AppendLine("--- WaitRunTicks(1) wall-clock distribution (harness overhead included, directional only) ---");
        sb.AppendLine($"min={tickTimesMs[0]:F3}ms p50={Percentile(0.50):F3}ms p95={Percentile(0.95):F3}ms max={tickTimesMs[^1]:F3}ms mean={tickTimesMs.Average():F3}ms");
        sb.AppendLine();
        sb.AppendLine("--- Per-EntitySystem mean update time (server-thread Stopwatch, Δsum/Δcount over the measured window) ---");
        sb.AppendLine($"{"System",-45} {"MeanUs",10} {"Ticks",8} {"TotalMs",9}");

        double totalSumDelta = 0;
        var rows = new List<(string System, double MeanUs, double Count, double TotalMs)>();

        foreach (var (system, afterStat) in after)
        {
            before.TryGetValue(system, out var beforeStat);
            var dSum = afterStat.Sum - beforeStat.Sum;
            var dCount = afterStat.Count - beforeStat.Count;
            if (dCount <= 0)
                continue;

            var meanSeconds = dSum / dCount;
            // NOTE: dCount is this system's own TickUpdate call count over the window. Most systems
            // tick once per simulated tick, but a few (e.g. StatusEffectsSystem) opt into
            // UpdatesOutsidePrediction and so get invoked on both the noPredictions pass and the
            // normal pass -> dCount == 2x for those specific rows. That's an honest per-system fact,
            // not a bug - but it means dCount must NOT be used to infer "how many ticks happened
            // overall"; MeasuredTicks (the number of WaitRunTicks(1) calls we actually made) is the
            // only correct denominator for the headline ms/tick figure below.
            rows.Add((system, meanSeconds * 1_000_000, dCount, dSum * 1000));
            totalSumDelta += dSum;
        }

        foreach (var row in rows.OrderByDescending(r => r.TotalMs))
            sb.AppendLine($"{row.System,-45} {row.MeanUs,10:F2} {row.Count,8:F0} {row.TotalMs,9:F3}");

        sb.AppendLine();
        sb.AppendLine($"TOTAL entity-system time across all systems: {totalSumDelta * 1000:F3}ms over {MeasuredTicks} simulated ticks => {totalSumDelta * 1000 / MeasuredTicks:F4}ms/tick (server-thread measured, the honest headline number).");

        TestContext.Out.WriteLine(sb.ToString());
        Console.WriteLine(sb.ToString());
    }

    private async Task<Dictionary<string, (double Sum, double Count)>> CollectHistogramSnapshot()
    {
        await using var ms = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(ms, CancellationToken.None);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var result = new Dictionary<string, (double Sum, double Count)>();
        var sumRegex = new Regex("^robust_entity_systems_update_usage_sum\\{system=\"([^\"]+)\"\\}\\s+([0-9.eE+-]+)", RegexOptions.Multiline);
        var countRegex = new Regex("^robust_entity_systems_update_usage_count\\{system=\"([^\"]+)\"\\}\\s+([0-9.eE+-]+)", RegexOptions.Multiline);

        foreach (Match m in sumRegex.Matches(text))
        {
            var name = m.Groups[1].Value;
            var value = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            result[name] = (value, result.TryGetValue(name, out var existing) ? existing.Count : 0);
        }

        foreach (Match m in countRegex.Matches(text))
        {
            var name = m.Groups[1].Value;
            var value = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var existing = result.TryGetValue(name, out var e) ? e : (0, 0);
            result[name] = (existing.Sum, value);
        }

        return result;
    }
}
