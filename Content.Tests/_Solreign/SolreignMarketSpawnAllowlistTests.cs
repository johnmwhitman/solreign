using System.Collections.Generic;
using Content.Server.Administration.Systems;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pins <see cref="SolreignOracleSystem.SolreignMarketSpawnAllowlist"/> — the game-side
///     backstop that gates which prototype ids a Director-daemon-issued <c>spawn_entity</c> event
///     (Requisitions Anonymous black-market delivery) may materialize on a buyer — to the EXACT
///     17-id set mirrored from the Director daemon's own vetted catalog
///     (<c>solreign-director/orchestrator/market_catalog.py</c>'s <c>spawn_entity_id</c> column,
///     verified by hand against this list when it was written). A silent drift here (someone adds
///     an id game-side without updating the daemon catalog, or vice versa) either soft-locks a
///     purchase (daemon sells something the game won't spawn) or — far worse — lets an
///     off-catalog id through if the set were ever accidentally widened. Uses
///     <c>InternalsVisibleTo("Content.Tests")</c> on Content.Server (the same grant
///     <see cref="Content.Server._Solreign.Director.DirectorChannel"/>'s canonicalization helpers
///     use) to reach the internal set directly.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignOracleSystem))]
public sealed class SolreignMarketSpawnAllowlistTests
{
    /// <summary>
    ///     The exact 17 ids from the daemon's <c>market_catalog.CATALOG</c>. Any drift between
    ///     this list and <see cref="SolreignOracleSystem.SolreignMarketSpawnAllowlist"/> is a bug
    ///     in one side or the other — this test does not care which, it just pins both never to
    ///     silently move.
    /// </summary>
    private static readonly HashSet<string> ExpectedDaemonCatalogSpawnIds = new()
    {
        "ClothingHeadHatTophat",
        "ClothingHeadHatFedoraBrown",
        "ClothingHeadHatWizard",
        "ClothingNeckCloakMoth",
        "ClothingEyesGlassesSunglasses",
        "ClothingHeadHatPaper",
        "BikeHorn",
        "PlushieBee",
        "PlushieNuke",
        "BalloonCorgi",
        "ToyRubberDuck",
        "FoodCakePlain",
        "FoodDonkpocket",
        "DrinkGoldenCup",
        "FlashlightLantern",
        "ToolboxMechanicalFilled",
        "HandheldGPSBasic",
    };

    [Test]
    public void SolreignMarketSpawnAllowlist_HasExactlySeventeenEntries()
    {
        Assert.That(SolreignOracleSystem.SolreignMarketSpawnAllowlist, Has.Count.EqualTo(17));
    }

    [Test]
    public void SolreignMarketSpawnAllowlist_MatchesDaemonCatalogExactly()
    {
        Assert.That(SolreignOracleSystem.SolreignMarketSpawnAllowlist,
            Is.EquivalentTo(ExpectedDaemonCatalogSpawnIds));
    }

    [Test]
    public void SolreignMarketSpawnAllowlist_ContainsNoBlankOrNullEntries()
    {
        Assert.That(SolreignOracleSystem.SolreignMarketSpawnAllowlist, Has.None.Null.Or.Empty);
    }
}
