using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Moq;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;

namespace Content.Tests._Solreign;

/// <summary>
///     Runs the ENGINE'S OWN sandbox type checker over our content assemblies — the same
///     check the client performs on download, which is the only authority on whether a
///     player can load this build.
/// </summary>
/// <remarks>
///     <para>
///     On 2026-07-25 SOLREIGN shipped THREE builds that no client could load. Twice the
///     cause was a type outside the sandbox allowlist (a <c>[GeneratedRegex]</c> emitting
///     <c>RegexRunnerFactory</c>, then <c>System.Text.Json</c> in Content.Shared), and both
///     times every gate we owned was green: the integration battery, the YAML linter, and
///     the deploy pipeline's own server boot-test. A server never sandbox-checks its own
///     content, so "the server is up" and "players can play" were different facts and
///     nothing measured the second one.
///     </para>
///     <para>
///     The stopgap was <c>deploy/sandbox_scan.py</c>, a DENYLIST of types the client had
///     already named. It works, and it is fundamentally reactive — it learns each forbidden
///     type by being burned by it, which is how one outage became two. This test replaces
///     guessing with the real allowlist: <c>AssemblyTypeChecker</c> reads Sandbox.yml
///     (embedded in Robust.Shared) and knows every forbidden type without being told.
///     </para>
///     <para>
///     REFLECTION, AND WHY IT FAILS LOUDLY. <c>AssemblyTypeChecker</c> is
///     <c>internal sealed</c> with no InternalsVisibleTo for Content, so it is constructed
///     reflectively. Every lookup below asserts rather than skipping: if an engine update
///     moves or renames it, this test must go RED and get fixed, never quietly stop
///     checking. A gate that silently disables itself is worse than no gate, because the
///     green tick keeps being reported.
///     </para>
///     <para>
///     SCOPE. This checks the assemblies built next to the test runner (Debug). The
///     artifact-level scan in the deploy pipeline still covers what actually ships,
///     including the copies nested inside Content.Client.zip. The two are complementary:
///     this one catches a violation the moment it is written, that one catches anything
///     that reaches the zip.
///     </para>
/// </remarks>
[TestFixture]
public sealed class SolreignRealSandboxCheckTest
{
    /// <summary>Assemblies the client sandbox-checks when it loads downloaded content.</summary>
    private static readonly string[] SandboxedAssemblies =
    {
        "Content.Shared.dll",
        "Content.Client.dll",
    };

    [Test]
    public void ContentAssemblies_PassTheEngineSandboxTypeCheck()
    {
        // Reached via IResourceManager because it is PUBLIC and lives in the same assembly.
        // ModLoader looks like the natural handle and is itself internal, so it does not compile.
        var checkerType = typeof(IResourceManager).Assembly
            .GetType("Robust.Shared.ContentPack.AssemblyTypeChecker");

        Assert.That(checkerType, Is.Not.Null,
            "Could not find Robust.Shared.ContentPack.AssemblyTypeChecker. The engine moved or " +
            "renamed it. FIX THIS TEST — do not delete it: without it nothing verifies that a " +
            "client can load our content, which is exactly how 2026-07-25 shipped three " +
            "unloadable builds in a row.");

        var ctor = checkerType!.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            new[] { typeof(IResourceManager), typeof(ISawmill) },
            modifiers: null);

        Assert.That(ctor, Is.Not.Null,
            $"{checkerType.Name} no longer has an (IResourceManager, ISawmill) constructor. " +
            "Re-point this test at the current shape rather than removing it.");

        var check = checkerType.GetMethod("CheckAssembly", new[] { typeof(string) });
        Assert.That(check, Is.Not.Null,
            $"{checkerType.Name}.CheckAssembly(string) is gone. Re-point this test.");

        // Capture what the checker rejects, so a failure reads like the client's own log
        // instead of a bare 'returned false'.
        var errors = new List<string>();
        var sawmill = new Mock<ISawmill>();
        sawmill.Setup(s => s.Error(It.IsAny<string>()))
            .Callback<string>(m => errors.Add(m));

        // The resolver prefers its DISK load paths (the dotnet directory and Robust.Shared's
        // own directory), and every content reference sits beside this test assembly, so the
        // IResourceManager branch is not expected to be reached. A mock keeps that assumption
        // honest: if the engine ever does reach for it, this fails visibly rather than
        // silently resolving nothing.
        var res = Mock.Of<IResourceManager>();

        var failures = new List<string>();
        var checkedAny = false;

        foreach (var name in SandboxedAssemblies)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path))
                continue;

            checkedAny = true;
            errors.Clear();

            var checker = ctor!.Invoke(new object[] { res, sawmill.Object });
            var ok = (bool) check!.Invoke(checker, new object[] { path })!;

            if (!ok)
            {
                var detail = errors.Count > 0
                    ? Environment.NewLine + "      " + string.Join(Environment.NewLine + "      ",
                        errors.Distinct().Take(25))
                    : " (checker reported no detail)";
                failures.Add($"{name} FAILED the sandbox type check:{detail}");
            }
        }

        Assert.That(checkedAny, Is.True,
            $"Found none of [{string.Join(", ", SandboxedAssemblies)}] beside the test assembly " +
            $"('{AppContext.BaseDirectory}'). This test must never pass without checking a build.");

        Assert.That(failures, Is.Empty,
            "Content failed the ENGINE'S OWN sandbox check. Every connecting client will abort " +
            "before the lobby while the server keeps reporting healthy — a total outage that no " +
            "other test here can see." + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }
}
