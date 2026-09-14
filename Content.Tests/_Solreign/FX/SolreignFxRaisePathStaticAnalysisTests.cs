#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Spec §7's "Static — raise-path enforcement" row (grk #2A): no direct
///     <c>RaiseNetworkEvent</c> of <c>SolreignFxCueV1</c> may exist outside
///     <c>Content.Server._Solreign.FX.SolreignFxServerSystem</c> — the API-enforced §5.2 split
///     (<c>RaiseCue</c>/<c>RaiseSecretRoleCue</c>) is only actually enforced if nothing else in the
///     codebase can construct-and-raise a cue by hand. A grep-based source scan rather than a
///     reflection/IL scan: cheap, and precise enough for the shape this needs to catch (a stray
///     <c>RaiseNetworkEvent(someCue, ...)</c> call where <c>someCue</c> is visibly a
///     <c>SolreignFxCueV1</c>-typed local/variable name).
/// </summary>
[TestFixture]
public sealed class SolreignFxRaisePathStaticAnalysisTests
{
    private const string AllowedFileName = "SolreignFxServerSystem.cs";

    // Matches a RaiseNetworkEvent call whose first argument mentions a cue-shaped identifier
    // ("cue", "genericCue", "detailCue", "Cue" anywhere) — deliberately generous (name-based, not a
    // full type-resolving parse) so it also catches a differently-named local that still obviously
    // holds a SolreignFxCueV1.
    private static readonly Regex RaiseNetworkEventOfCue = new(
        @"RaiseNetworkEvent\s*\(\s*\w*[Cc]ue\w*\b",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpaceStation14.slnx")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not locate the repo root (SpaceStation14.slnx) from the test's base directory");
        return dir!.FullName;
    }

    [Test]
    public void NoFileOutsideSolreignFxServerSystem_RaisesASolreignFxCueDirectly()
    {
        var root = FindRepoRoot();
        var violations = new System.Collections.Generic.List<string>();

        foreach (var dir in new[] { "Content.Server", "Content.Client", "Content.Shared", "Content.IntegrationTests" })
        {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full))
                continue;

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == AllowedFileName)
                    continue;

                // Only files that even mention SolreignFxCueV1 are relevant — everything else in a
                // multi-thousand-file repo is irrelevant noise for this specific check.
                var text = File.ReadAllText(file);
                if (!text.Contains("SolreignFxCueV1"))
                    continue;

                foreach (Match match in RaiseNetworkEventOfCue.Matches(text))
                {
                    violations.Add($"{Path.GetRelativePath(root, file)}: '{match.Value}'");
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "Only Content.Server._Solreign.FX.SolreignFxServerSystem may RaiseNetworkEvent a SolreignFxCueV1 " +
            $"(spec §5.2's API-enforced raise split). Violations:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void SolreignFxServerSystem_ActuallyRaisesTheCueType_SoThisCheckIsNotVacuous()
    {
        var root = FindRepoRoot();
        var file = Directory.EnumerateFiles(Path.Combine(root, "Content.Server"), AllowedFileName, SearchOption.AllDirectories).SingleOrDefault();

        Assert.That(file, Is.Not.Null, $"{AllowedFileName} should exist under Content.Server");
        var text = File.ReadAllText(file!);
        Assert.That(RaiseNetworkEventOfCue.Matches(text).Count, Is.GreaterThanOrEqualTo(2),
            "expected both the RaiseCue and RaiseSecretRoleCue (generic+detail) raise call sites — if this drops to 0, the regex above may be silently matching nothing");
    }
}
