using Content.Server._Solreign.Operations;
using Content.Shared.Administration;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using System;
using System.Linq;

namespace Content.Tests._Solreign.Operations;

[TestFixture]
[TestOf(typeof(SolreignShowPreflightCommand))]
public sealed class SolreignShowPreflightCommandTests
{
    private static readonly string[] ExpectedCVarNames =
    [
        "solreign.contracts_lowpop.enabled",
        "solreign.directives_fax.enabled",
        "solreign.first_shift_assignments_enabled",
        "solreign.fx.cue_v1",
        "solreign.fx.world_feedback_v1",
        "solreign.fx.world_feedback_observe",
        "solreign.kart_repeatable_heats_enabled",
        "solreign.providence.reactive_enabled",
        "solreign.providence_enabled",
        "solreign.records_terminal.enabled",
        "solreign.shift_archive.enabled",
        "solreign.station_audit.enabled",
        "solreign.station_audit.inspection_enabled",
        "solreign.wingmates_enabled",
    ];

    [Test]
    public void CommandContractIsAdminOnlyReadOnlyAndZeroArgument()
    {
        var type = typeof(SolreignShowPreflightCommand);
        var attribute = type.GetCustomAttributesData()
            .Single(candidate => candidate.AttributeType.Name == "AdminCommandAttribute");

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToInt64(attribute.ConstructorArguments.Single().Value),
                Is.EqualTo(Convert.ToInt64(AdminFlags.Admin)));
            Assert.That(type.IsSubclassOf(typeof(LocalizedEntityCommands)), Is.True);

            var command = new SolreignShowPreflightCommand();
            Assert.That(command.Command, Is.EqualTo("solreignshowpreflight"));
            Assert.That(command.Help, Is.EqualTo("solreignshowpreflight"));
            Assert.That(command.Description, Does.Contain("read-only").IgnoreCase);
        });
    }

    [Test]
    public void InvalidArgumentsAreRejectedBeforeDependenciesAreRead()
    {
        var shell = new Mock<IConsoleShell>(MockBehavior.Strict);
        shell.Setup(candidate => candidate.WriteError("solreignshowpreflight"));

        var command = new SolreignShowPreflightCommand();
        command.Execute(shell.Object, "unexpected", ["unexpected"]);

        shell.Verify(candidate => candidate.WriteError("solreignshowpreflight"), Times.Once);
        shell.VerifyNoOtherCalls();
    }

    [Test]
    public void SignatureCVarAllowlistIsExactServerOwnedAndContainsNoClientOrConfidentialConfiguration()
    {
        var definitions = SolreignShowPreflightCommand.SignatureCVars;
        var names = definitions.Select(definition => definition.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EqualTo(ExpectedCVarNames));
            Assert.That(names, Is.Unique);
            Assert.That(definitions.All(definition =>
                    definition.Flags.HasFlag(CVar.SERVERONLY) ||
                    definition.Flags.HasFlag(CVar.SERVER)),
                Is.True);
            Assert.That(definitions.All(definition => !definition.Flags.HasFlag(CVar.CLIENTONLY)), Is.True);
            Assert.That(definitions.All(definition => !definition.Flags.HasFlag(CVar.CONFIDENTIAL)), Is.True);
            Assert.That(names, Has.None.Contains("token").And.None.Contains("url").And.None.Contains("path"));
            Assert.That(names, Has.None.EqualTo("solreign.director.token"));
            Assert.That(names, Has.None.EqualTo("solreign.season_ledger_db_path"));
        });
    }

    [Test]
    public void FormatterIsDeterministicBoundedAndWithholdsRuleIdentity()
    {
        var snapshot = new SolreignShowPreflightSnapshot(
            "Amber",
            "Amber Station\n\u202Eforged\u200B-line",
            4,
            2,
            [
                new("solreign.wingmates_enabled", false),
                new("solreign.first_shift_assignments_enabled", true),
            ]);

        var lines = SolreignShowPreflightFormatter.Format(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Is.EqualTo(new[]
            {
                "SOLREIGN SHOW PREFLIGHT",
                "OBSERVATION ONLY — NOT GO/READINESS AUTHORITY",
                "Map: Amber (Amber Station forged-line)",
                "Population.connected: 4",
                "ActiveRules.count: 2 (identities withheld)",
                "FeatureFlags:",
                "- solreign.first_shift_assignments_enabled=ON",
                "- solreign.wingmates_enabled=OFF",
            }));
            Assert.That(lines.Count, Is.LessThanOrEqualTo(SolreignShowPreflightFormatter.MaximumLines));
            Assert.That(lines.All(line => line.Length <= SolreignShowPreflightFormatter.MaximumLineLength), Is.True);
            Assert.That(string.Join('\n', lines), Does.Not.Contain("Traitor").And.Not.Contain("Nukeops"));
            Assert.That(SolreignShowPreflightFormatter.Format(snapshot), Is.EqualTo(lines));
        });
    }

    [Test]
    public void FormatterMissingMapAndInvalidCountsFailClosed()
    {
        var snapshot = new SolreignShowPreflightSnapshot(
            null,
            null,
            -4,
            -2,
            []);

        Assert.That(SolreignShowPreflightFormatter.Format(snapshot), Is.EqualTo(new[]
        {
            "SOLREIGN SHOW PREFLIGHT",
            "OBSERVATION ONLY — NOT GO/READINESS AUTHORITY",
            "Map: NONE",
            "Population.connected: 0",
            "ActiveRules.count: 0 (identities withheld)",
            "FeatureFlags: none",
        }));
    }

    [Test]
    public void FormatterEnforcesMaximumLineAndLengthBounds()
    {
        var longValue = new string('x', SolreignShowPreflightFormatter.MaximumLineLength * 2);
        var flags = Enumerable.Range(0, SolreignShowPreflightFormatter.MaximumLines * 2)
            .Select(index => new SolreignShowPreflightFlag($"{index:D3}.{longValue}", index % 2 == 0))
            .ToArray();
        var snapshot = new SolreignShowPreflightSnapshot(
            longValue,
            longValue,
            1,
            1,
            flags);

        var lines = SolreignShowPreflightFormatter.Format(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(SolreignShowPreflightFormatter.MaximumLines));
            Assert.That(lines.All(line =>
                    line.Length <= SolreignShowPreflightFormatter.MaximumLineLength),
                Is.True);
            Assert.That(lines[2], Has.Length.EqualTo(SolreignShowPreflightFormatter.MaximumLineLength));
            Assert.That(lines[^1], Has.Length.EqualTo(SolreignShowPreflightFormatter.MaximumLineLength));
        });
    }
}
