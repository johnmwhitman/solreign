using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Guidebook;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     UX-SIMPLE FIX 3: the repo-wide sweep in
///     <c>Content.IntegrationTests/Tests/Guidebook/GuideEntryPrototypeTests.cs</c> already
///     parameterizes over EVERY <c>GuideEntryPrototype</c> and asserts
///     <c>DocumentParsingManager.TryAddMarkup</c> returns true — including the 7 new Solreign
///     entries this fix adds (<c>Resources/Prototypes/_Solreign/Guidebook/SolreignHandbook.yml</c>).
///     That assertion alone does NOT catch a malformed bracket tag ([bold]/[italic]/
///     [color=...]/[keybind=...]/[textlink=... link=...]): a bad tag is silently substituted with a
///     <see cref="GuidebookError"/> control rather than failing the parse (see
///     <c>DocumentParsingManager.TextControlParser</c>). This test walks the parsed control tree for
///     each new Solreign page and asserts no <see cref="GuidebookError"/> descendant exists, closing
///     that gap specifically for the pages this fix adds (which use several bracket tags, including
///     a cross-reference [textlink] into the existing FirstShift entry).
/// </summary>
[TestFixture]
[TestOf(typeof(DocumentParsingManager))]
public sealed class SolreignGuidebookMarkupIntegrationTest : GameTest
{
    private static readonly string[] SolreignGuideEntryIds =
    {
        "SolreignHandbook",
        "SolreignDirectiveTerminalGuide",
        "SolreignRequisitionsAnonymousGuide",
        "SolreignLiabilityBoardGuide",
        "SolreignWingmatesGuide",
        "SolreignSalaryStandingGuide",
        "SolreignSeasonLedgerGuide",
    };

    [Test]
    [TestCaseSource(nameof(SolreignGuideEntryIds))]
    public async Task Page_ParsesWithoutMarkupErrors(string protoId)
    {
        var client = Pair.Client;
        await client.WaitIdleAsync();
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var resMan = client.ResolveDependency<IResourceManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();
        var proto = protoMan.Index<GuideEntryPrototype>(protoId);

        await client.WaitAssertion(() =>
        {
            using var reader = resMan.ContentFileReadText(proto.Text);
            var text = reader.ReadToEnd();
            var document = new Document();

            Assert.That(parser.TryAddMarkup(document, text), Is.True,
                $"{protoId}'s page failed to parse at all.");

            var errors = FindGuidebookErrors(document).ToList();
            Assert.That(errors, Is.Empty,
                $"{protoId}'s page rendered {errors.Count} GuidebookError control(s) — a bracket tag " +
                "([bold]/[italic]/[color=]/[keybind=]/[textlink=]) is malformed.");
        });
    }

    private static IEnumerable<Control> FindGuidebookErrors(Control root)
    {
        if (root is GuidebookError)
            yield return root;

        foreach (var child in root.Children)
        foreach (var found in FindGuidebookErrors(child))
            yield return found;
    }
}
