using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Catches the Release-only analyzer errors that block <c>Content.Packaging</c>, at
///     development time instead of thirty minutes into a deploy build.
/// </summary>
/// <remarks>
///     <para>
///     WHY. On 2026-07-25 the Shift Archive merged to master with a green 2445-test battery and
///     then could not be packaged: <c>ShiftArchiveSystem</c> declared <c>[Dependency]</c> fields
///     without being <c>partial</c>. That is analyzer RA0049, a WARNING in Debug and an ERROR in
///     the Release configuration Content.Packaging builds. The full test suite is a Debug build,
///     so it had nothing to say.
///     </para>
///     <para>
///     This is the same shape as two other failures the same day: a server boot-test cannot prove
///     a client can load the content, and a Debug test run cannot prove the Release artifact
///     compiles. <b>A gate cannot vouch for a configuration it never builds.</b> The compiler is
///     still the authority here — this test exists purely to move the feedback from a ~30 minute
///     package step to about two seconds.
///     </para>
///     <para>
///     SCOPE. Only RA0049 (<c>[Dependency]</c> requires <c>partial</c>), because that is the one
///     that has actually bitten and the one with a reliable source-level signature. It does NOT
///     replace building Release. Other analyzer rules that fire in Release only (RA0026 static
///     Regex re-parse, RA0042 redundant explicit prototype names, RA0051) are deliberately not
///     reimplemented — approximating a compiler badly is worse than not approximating it, and
///     build_verify still builds Release before anything ships.
///     </para>
/// </remarks>
[TestFixture]
public sealed class SolreignReleaseAnalyzerGuardTest
{
    /// <summary>Projects whose Release build gates the deploy artifact.</summary>
    private static readonly string[] PackagedProjects =
    {
        "Content.Server",
        "Content.Client",
        "Content.Shared",
    };

    /// <summary>Self-test floor: a regressed sweep must not fake green on an empty set.</summary>
    private const int MinSourceFiles = 200;

    /// <summary>Start of a class declaration, capturing its modifier run.</summary>
    private static readonly Regex ClassDeclaration = new(
        @"\A\s*((?:public|internal|private|protected|sealed|abstract|static|partial|\s)*)\bclass\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Splits a file at each top-level class declaration so members attribute correctly.</summary>
    private static readonly Regex ClassSplit = new(
        @"(?m)^(?=\s*(?:public|internal|private|protected|sealed|abstract|static|partial|\s)*\bclass\s)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public void ClassesWithDependencyFields_ArePartial_SoReleasePackagingCompiles()
    {
        var repoRoot = LocateRepoRoot();
        var violations = new List<string>();
        var scanned = 0;

        foreach (var project in PackagedProjects)
        {
            var dir = Path.Combine(repoRoot, project);
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal))
                    continue;

                scanned++;
                var text = File.ReadAllText(file);
                if (!text.Contains("[Dependency]", StringComparison.Ordinal))
                    continue;

                // Attribute [Dependency] to the class that DECLARES it. An earlier version of this
                // check asked only whether the FILE contained the attribute, which flagged every
                // event and DTO class that happened to share a file with a system — noise that
                // would have got the whole test switched off.
                foreach (var block in ClassSplit.Split(text))
                {
                    var decl = ClassDeclaration.Match(block);
                    if (!decl.Success)
                        continue;

                    if (!block.Contains("[Dependency]", StringComparison.Ordinal))
                        continue;

                    if (decl.Groups[1].Value.Contains("partial", StringComparison.Ordinal))
                        continue;

                    violations.Add($"{rel}: class {decl.Groups[2].Value}");
                }
            }
        }

        Assert.That(scanned, Is.GreaterThan(MinSourceFiles),
            $"Swept only {scanned} source files across [{string.Join(", ", PackagedProjects)}] from " +
            $"'{repoRoot}'. Far too few — the sweep regressed and a green run would mean nothing.");

        Assert.That(violations, Is.Empty,
            "These classes declare [Dependency] fields without being `partial`. That is analyzer " +
            "RA0049: a warning in Debug, an ERROR in the Release configuration Content.Packaging " +
            "builds — so the full Debug suite can be green while the deploy artifact cannot be " +
            "built at all. Add `partial` to each." + Environment.NewLine +
            "  " + string.Join(Environment.NewLine + "  ", violations.OrderBy(v => v, StringComparer.Ordinal)));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Content.Server"))
                && Directory.Exists(Path.Combine(dir.FullName, "Content.Packaging")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail($"Could not find the repo root walking up from '{AppContext.BaseDirectory}'.");
        return string.Empty;
    }
}
