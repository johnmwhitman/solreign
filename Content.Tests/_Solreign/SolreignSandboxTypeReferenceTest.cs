using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Fails the build when sandboxed content uses a source generator whose output the
///     RobustToolbox client sandbox rejects.
/// </summary>
/// <remarks>
///     <para>
///     WHY THIS EXISTS. On 2026-07-25 SOLREIGN shipped a total client outage across two
///     consecutive releases. One <c>[GeneratedRegex]</c> attribute in Content.Shared — added to
///     satisfy analyzer RA0026 — made the regex source generator emit a
///     <c>RegexRunnerFactory</c> subclass annotated with <c>GeneratedCodeAttribute</c>. Neither
///     type is on the sandbox allowlist, so every connecting client threw
///     <c>TypeCheckFailedException: Assembly Content.Shared failed type checks</c> and aborted
///     before reaching the lobby.
///     </para>
///     <para>
///     NOTHING WE RAN COULD SEE IT. The sandbox check runs in the CLIENT against the shipped
///     assembly. The test battery, the YAML linter and the deploy pipeline's server boot-test
///     were green the whole time, because a server never sandbox-checks its own content. The
///     server was reporting healthy and was unplayable simultaneously.
///     </para>
///     <para>
///     SCOPE — READ THIS BEFORE TRUSTING A GREEN RUN. This is a NARROW, source-level test. It
///     checks one thing: that sandboxed projects do not use generators known to emit forbidden
///     types. It does NOT verify the sandbox is satisfied.
///     </para>
///     <para>
///     It is narrow deliberately. The faithful check is
///     <c>Robust.Shared.ContentPack.AssemblyTypeChecker</c> run against the RELEASE assemblies
///     that Content.Packaging actually ships. An approximation of that was attempted here first
///     and rejected: reading TypeRefs and diffing them against Sandbox.yml produced false
///     positives on nested BCL types (<c>Dictionary.Enumerator</c>,
///     <c>DebuggableAttribute.DebuggingModes</c>, <c>AppendInterpolatedStringHandler</c>), and it
///     inspected the DEBUG assemblies sitting next to the test rather than the Release ones that
///     ship — so it was both noisy and aimed at the wrong artifact. A gate people learn to
///     silence is worse than no gate.
///     </para>
///     <para>
///     FOLLOW-UP, NOT DONE HERE: wire AssemblyTypeChecker over the packaged Release output as a
///     deploy gate. That is the real fix; this test only closes the specific door that caused
///     the outage.
///     </para>
/// </remarks>
[TestFixture]
public sealed class SolreignSandboxTypeReferenceTest
{
    /// <summary>Projects whose assemblies the client sandbox-checks on load.</summary>
    private static readonly string[] SandboxedProjects =
    {
        "Content.Shared",
        "Content.Client",
        "Content.Shared.Database",
    };

    /// <summary>
    ///     Generators whose emitted types are absent from
    ///     RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml, with the sanctioned alternative.
    /// </summary>
    private static readonly (string Attribute, string Emits, string Instead)[] ForbiddenGenerators =
    {
        ("GeneratedRegex",
            "RegexRunnerFactory + GeneratedCodeAttribute",
            "a static readonly Regex with RegexOptions.Compiled — it compiles the pattern once, "
            + "which is all RA0026 asks for, without emitting a forbidden type"),
    };

    /// <summary>Self-test floor: a regressed file sweep must not fake green on an empty set.</summary>
    private const int MinSourceFiles = 200;

    /// <summary>Compiled once per attribute, not once per (file × attribute).</summary>
    /// <remarks>
    ///     Analyzer RA0026 flagged the previous <c>Regex.IsMatch(text, pattern, …)</c> here,
    ///     which re-parsed the pattern on every one of ~2,000 file/attribute combinations.
    ///     The warning was right; the FIX is the point. Reaching for <c>[GeneratedRegex]</c>
    ///     to silence RA0026 is precisely what caused the 2026-07-25 client outage this test
    ///     exists to prevent, so it is deliberately not used — a cached compiled Regex gives
    ///     the same parse-once behaviour with no generated types at all. That this file, of
    ///     all files, tripped the same analyzer is a fair illustration of how easy the
    ///     original mistake was to make.
    /// </remarks>
    private static readonly Dictionary<string, Regex> PatternCache = new(StringComparer.Ordinal);

    private static Regex AttributeUsePattern(string attribute)
    {
        if (PatternCache.TryGetValue(attribute, out var cached))
            return cached;

        var built = new Regex(
            @"^\s*\[\s*(?:\w+\s*:\s*)?(?:[\w.]+\.)?" + Regex.Escape(attribute) + @"(?:Attribute)?\s*[\(\]]",
            RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        PatternCache[attribute] = built;
        return built;
    }

    [Test]
    public void SandboxedProjects_DoNotUseForbiddenSourceGenerators()
    {
        var repoRoot = LocateRepoRoot();
        var violations = new List<string>();
        var scanned = 0;

        foreach (var project in SandboxedProjects)
        {
            var dir = Path.Combine(repoRoot, project);
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                // Skip build output; only first-party source is ours to fix.
                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal))
                    continue;

                scanned++;
                var text = File.ReadAllText(file);

                foreach (var (attribute, emits, instead) in ForbiddenGenerators)
                {
                    // Match the ATTRIBUTE USE ([GeneratedRegex(...)]), not prose that names it —
                    // this very file documents the attribute and must not trip its own gate.
                    //
                    // Accepts every spelling the compiler does, because a gate that only catches
                    // the tidiest one is a gate you can walk around by accident:
                    //   [GeneratedRegex(...)]              bare
                    //   [GeneratedRegexAttribute(...)]     explicit Attribute suffix
                    //   [System.Text.RegularExpressions.GeneratedRegex(...)]   fully qualified
                    //   [Foo.GeneratedRegex(...)]          alias/using-qualified
                    //   [field: GeneratedRegex(...)]       with a target specifier
                    if (!AttributeUsePattern(attribute).IsMatch(text))
                        continue;

                    violations.Add($"{rel}: [{attribute}] emits {emits} — use {instead}.");
                }
            }
        }

        Assert.That(scanned, Is.GreaterThan(MinSourceFiles),
            $"Swept only {scanned} source files across [{string.Join(", ", SandboxedProjects)}] from " +
            $"'{repoRoot}'. That is far too few — the sweep regressed and a green run would be " +
            "meaningless. Refusing to pass.");

        Assert.That(violations, Is.Empty,
            "Sandboxed content uses a source generator the client sandbox rejects. Every connecting " +
            "client will fail type checks and abort BEFORE the lobby, while the server keeps " +
            "reporting healthy — a total outage no server-side test can observe." + Environment.NewLine +
            "  " + string.Join(Environment.NewLine + "  ", violations.OrderBy(v => v, StringComparer.Ordinal)));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RobustToolbox", "Robust.Shared", "ContentPack", "Sandbox.yml")))
                return dir.FullName;

            dir = dir.Parent;
        }

        Assert.Fail($"Could not find the repo root (via RobustToolbox/.../Sandbox.yml) walking up from '{AppContext.BaseDirectory}'.");
        return string.Empty;
    }
}
