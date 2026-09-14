#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Providence;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Phase-2 Track B2 (docs/plans/2026-07-11-ROADMAP-PHASE2.md, Track B, Wave B2): "Providence
///     category fallback (missing collection -&gt; no crash)". <see cref="ProvidenceVoiceMap"/>'s pure
///     switch was already exhaustively unit-tested (<c>ProvidenceVoiceMapTests</c>), but nothing
///     exercised the live call path from a category through <see cref="ProvidenceVoiceSystem.PlayLine"/>
///     into a real <see cref="IPrototypeManager"/> lookup — which is exactly the shape of the
///     Werewolf "built but never instantiated" defect class (retro L9,
///     <c>SolreignPrototypeIdIntegrityTest</c>'s own doc comment), just for audio collections instead
///     of entity/polymorph prototypes.
///
///     Investigated before writing these tests (see <c>SharedAudioSystem.ResolveSound</c>): a
///     <see cref="SoundCollectionSpecifier"/> built from a bare string (as
///     <see cref="ProvidenceVoiceMap.CollectionFor"/> returns) is resolved via
///     <c>IPrototypeManager.Index&lt;SoundCollectionPrototype&gt;</c> — <c>Index</c>, not
///     <c>TryIndex</c> — so there is genuinely NO soft fallback in the engine for a missing
///     collection; it throws <see cref="UnknownPrototypeException"/>. That means "no crash" in
///     practice is only true today because every category currently has a matching YAML entry
///     (verified below, all 12) — the real safety net for a FUTURE category is the CI-reachable
///     regression test in this file, not a runtime fallback. Writing the tests to assert a fallback
///     that does not exist would be dishonest; instead this file (a) proves every category that
///     exists today resolves cleanly, and (b) documents the true throw-on-missing behavior so nobody
///     is surprised by it later. Production code (<c>ProvidenceVoiceSystem</c>) is not touched here —
///     out of scope for a test-only pass.
/// </summary>
[TestFixture]
public sealed class ProvidenceVoiceSystemIntegrationTest : GameTest
{
    // Dirty: Disabled_PlayLine_IsANoOp... flips CCVars.SolreignProvidenceEnabled via server.CfgMan
    // directly (the precedented NukeOpsTest idiom, not the unused-in-this-codebase OverrideCVar
    // auto-restore helper) — the server this mutates must never be handed back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private static IEnumerable<ProvidenceLineCategory> AllCategories() => Enum.GetValues<ProvidenceLineCategory>();

    [TestCaseSource(nameof(AllCategories))]
    public async Task PlayLine_ForEveryLiveCategory_DoesNotThrow(ProvidenceLineCategory category)
    {
        var server = Server;
        var providence = server.System<ProvidenceVoiceSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => providence.PlayLine(category),
                $"{category} has no matching soundCollection in providence_sounds.yml — the Providence " +
                "analogue of the werewolf 'built but never instantiated' defect. This test is the CI " +
                "gate that would catch a category added to the enum without its YAML collection.");
        });
    }

    [Test]
    public async Task Disabled_PlayLine_IsANoOp_EvenForACategoryThatWouldOtherwiseResolve()
    {
        var server = Server;
        var providence = server.System<ProvidenceVoiceSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceEnabled, false);
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(providence.Enabled, Is.False,
                    "The cached _enabled mirror should reflect the CVar flip.");
                Assert.DoesNotThrow(() => providence.PlayLine(ProvidenceLineCategory.ShiftStart),
                    "PlayLine must no-op (never touch the prototype manager) while disabled.");
            });
        });
    }

    [Test]
    public async Task MissingCollection_ResolveSound_ThrowsUnknownPrototypeException()
    {
        var server = Server;
        var audio = server.System<SharedAudioSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Throws<UnknownPrototypeException>(() =>
                audio.ResolveSound(new SoundCollectionSpecifier("SolreignProvidenceTotallyMadeUpCategory")),
                "Documents the real fallback behavior for a Providence category whose YAML collection " +
                "was never authored: ResolveSound's SoundCollectionSpecifier branch calls " +
                "IPrototypeManager.Index (not TryIndex) and throws — there is no soft fallback in the " +
                "engine. PlayLine_ForEveryLiveCategory_DoesNotThrow above is the actual safety net.");
        });
    }
}
