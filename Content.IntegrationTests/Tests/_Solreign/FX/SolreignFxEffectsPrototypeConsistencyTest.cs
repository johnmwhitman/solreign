#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign.FX;

/// <summary>
///     W3's "effects.yml-class prototypes" integration gate (spec §7's "Integration —
///     prototype/orphan/YAML" row, mission text's "the YAML linter" requirement made concrete for
///     THIS file): boots a real connected server/client pair, loads
///     <c>Resources/Prototypes/_Solreign/FX/effects.yml</c> through the real
///     <see cref="IPrototypeManager"/> on both sides, and asserts:
///
///     1. Exactly the 10 ids in <see cref="SolreignFxWireAllowlist.V1"/> exist, on BOTH server and
///        client (cdx #19/grk #5's "ids used by §5 but absent from §2 would otherwise be
///        spec-contradictory," now closed for real — W1's own receipt deferred this cross-check
///        "past W1 (the YAML file does not exist yet)").
///     2. Every prototype's <see cref="SolreignFxCuePrototype.AudienceClassification"/> matches the
///        sealed manifest's <see cref="SolreignFxWireAllowlist.AudienceClassificationV1"/> exactly.
///     3. Every prototype passes W1's load-time validation rules
///        (<see cref="SolreignFxCuePrototypeValidation"/>) — inverted/non-finite bounds, wire
///        ceilings, non-empty palette/phase, in-range declared defaults.
///     4. `transformation` is the only <see cref="SolreignFxAudienceClassification.DetailOnly"/>
///        member and the only one with <c>RequiresRedactedBroadcastVariant</c> set, with
///        <c>GenericVariant == "transformation_generic"</c> (spec §5.2).
///     5. Every prototype that a secret-role caller might plausibly reach for
///        (<c>electrical</c>/<c>cast_ring</c>/<c>stamina_break</c>) declares a <c>GenericVariant</c>
///        that itself resolves — closing grk review finding M-G from the W2 receipt ("the gap is
///        that W3's effects.yml must configure a GenericVariant for every such primitive").
///
///     Also doubles as the interim budget-regression check (spec §7's "Budget regression (interim,
///     pre-profiler)" row): under the shipped default-on CVar and across an explicit off-to-on
///     transition, the client's real
///     <c>EntityQuery&lt;SolreignFxPooledSpriteComponent&gt;</c> count must equal EXACTLY the sum of
///     every category's spec §3 concurrent cap (Transformation's is 0 by design) — never more,
///     proving <see cref="Content.Client._Solreign.FX.SolreignFxRenderPool"/> sizes to the spec
///     table, not silently over/under.
/// </summary>
[TestFixture]
public sealed class SolreignFxEffectsPrototypeConsistencyTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task EffectsYaml_MatchesTheSealedWireAllowlistExactly_OnServerAndClient()
    {
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() => AssertPrototypesConsistent(Server.ResolveDependency<IPrototypeManager>(), "SERVER"));
        await Client.WaitAssertion(() => AssertPrototypesConsistent(Client.ResolveDependency<IPrototypeManager>(), "CLIENT"));
    }

    private static void AssertPrototypesConsistent(IPrototypeManager protoMan, string side)
    {
        var loaded = protoMan.EnumeratePrototypes<SolreignFxCuePrototype>().ToDictionary(p => p.ID);

        Assert.That(loaded.Keys, Is.EquivalentTo(SolreignFxWireAllowlist.V1),
            $"[{side}] effects.yml's loaded ids must equal SolreignFxWireAllowlist.V1 exactly (cdx #19/grk #5)");

        Assert.Multiple(() =>
        {
            foreach (var (id, prototype) in loaded)
            {
                Assert.That(SolreignFxWireAllowlist.TryGetAudienceClassification(id, out var expected), Is.True, $"[{side}] '{id}' has no manifest classification");
                Assert.That(prototype.AudienceClassification, Is.EqualTo(expected), $"[{side}] '{id}' YAML audienceClassification disagrees with the sealed manifest");

                Assert.That(SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons), Is.True,
                    $"[{side}] '{id}' fails load-time validation: {string.Join("; ", reasons)}");
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(loaded["transformation"].AudienceClassification, Is.EqualTo(SolreignFxAudienceClassification.DetailOnly));
            Assert.That(loaded["transformation"].RequiresRedactedBroadcastVariant, Is.True);
            Assert.That(loaded["transformation"].GenericVariant?.Id, Is.EqualTo("transformation_generic"));

            foreach (var id in loaded.Keys.Where(id => id != "transformation"))
            {
                Assert.That(loaded[id].RequiresRedactedBroadcastVariant, Is.False,
                    $"[{side}] only 'transformation' may require the redacted-broadcast split in v1 (spec §5.2)");
            }
        });

        Assert.Multiple(() =>
        {
            foreach (var id in new[] { "electrical", "cast_ring", "stamina_break" })
            {
                var variant = loaded[id].GenericVariant;
                Assert.That(variant, Is.Not.Null, $"[{side}] '{id}' must declare a GenericVariant (W2 grk review finding M-G) for RaiseSecretRoleCue to route a secret-role caller through");
                Assert.That(loaded.ContainsKey(variant!.Value.Id), Is.True, $"[{side}] '{id}'.GenericVariant ('{variant}') must itself resolve as a loaded prototype");
            }
        });
    }

    [Test]
    public async Task RenderPool_ShipsSizedToTheSpecBudgetTable_WhileTheMasterSwitchIsOnByDefault()
    {
        // The activation pass deliberately changed solreign.fx.cue_v1's shipped default to true.
        // The default-on client must therefore build the same bounded pool that the explicit
        // enable-path test below verifies; a future accidental default-off regression should be
        // visible here rather than silently presenting an inert feature.
        await Pair.RunTicksSync(10);

        var expectedTotal = 0;
        foreach (SolreignFxCategory category in System.Enum.GetValues<SolreignFxCategory>())
            expectedTotal += SolreignFxCategoryTable.GetDefaults(category).ConcurrentCap;

        await Client.WaitAssertion(() =>
        {
            var entMan = Client.ResolveDependency<Robust.Shared.GameObjects.IEntityManager>();
            var count = entMan.EntityQuery<SolreignFxPooledSpriteComponent>(true).Count();

            Assert.That(count, Is.EqualTo(expectedTotal),
                "the default-on render pool must still honor the exact bounded spec budget");
        });
    }

    [Test]
    public async Task RenderPool_TearsDownWhenDisabled_AndRebuildsToTheSpecBudgetWhenReenabled()
    {
        await Pair.RunTicksSync(5);

        var original = Server.CfgMan.GetCVar(Content.Shared.CCVar.CCVars.SolreignFxCueV1Enabled);
        try
        {
            Server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SolreignFxCueV1Enabled, false);
            await Pair.RunTicksSync(5);
            await Client.WaitAssertion(() =>
            {
                var entMan = Client.ResolveDependency<Robust.Shared.GameObjects.IEntityManager>();
                Assert.That(entMan.EntityQuery<SolreignFxPooledSpriteComponent>(true), Is.Empty,
                    "the master kill switch must tear down the entire cosmetic render pool");
            });

            Server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SolreignFxCueV1Enabled, true);
            await Pair.RunTicksSync(10);

            var expectedTotal = 0;
            foreach (SolreignFxCategory category in System.Enum.GetValues<SolreignFxCategory>())
                expectedTotal += SolreignFxCategoryTable.GetDefaults(category).ConcurrentCap;

            await Client.WaitAssertion(() =>
            {
                var entMan = Client.ResolveDependency<Robust.Shared.GameObjects.IEntityManager>();
                var count = entMan.EntityQuery<SolreignFxPooledSpriteComponent>(true).Count();

                Assert.That(count, Is.EqualTo(expectedTotal),
                    "the re-enabled pool must equal the exact sum of every category's bounded spec cap");
            });
        }
        finally
        {
            Server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SolreignFxCueV1Enabled, original);
            await Pair.RunTicksSync(2);
        }
    }
}
