using System.Linq;
using Content.Server._Solreign.DirectivesFax;
using Content.Server._Solreign.StationDirective;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Structural integrity checks for the Directives Fax clause catalog: every real Station Directive
///     id must map to 1-3 clauses (the "closed clause vocabulary" rail), and the catalog must never
///     silently drift from the real <c>StationDirectiveCatalog</c> it derives clauses for. Pure data —
///     no ECS, no Loc.
/// </summary>
[TestFixture]
[TestOf(typeof(DirectivesFaxClauseCatalog))]
public sealed class DirectivesFaxClauseCatalogTests
{
    [Test]
    public void EveryRealStationDirectiveId_HasAClauseMapping()
    {
        foreach (var directive in StationDirectiveCatalog.Directives)
        {
            Assert.That(DirectivesFaxClauseCatalog.ClausesByDirective.ContainsKey(directive.Id), Is.True,
                $"Station Directive '{directive.Id}' has no Directives Fax clause mapping.");
        }
    }

    [Test]
    public void NoClauseMapping_ReferencesAnUnknownDirectiveId()
    {
        var realIds = StationDirectiveCatalog.Directives.Select(d => d.Id).ToHashSet();

        foreach (var directiveId in DirectivesFaxClauseCatalog.ClausesByDirective.Keys)
        {
            Assert.That(realIds, Does.Contain(directiveId),
                $"Directives Fax maps clauses for '{directiveId}', which is not a real Station Directive id.");
        }
    }

    [Test]
    public void EveryDirective_HasBetweenOneAndThreeClauses()
    {
        foreach (var (directiveId, clauses) in DirectivesFaxClauseCatalog.ClausesByDirective)
        {
            Assert.That(clauses.Count, Is.InRange(1, 3), $"'{directiveId}' must have 1-3 clauses.");
        }
    }

    [Test]
    public void EveryClause_HasANonEmptyId()
    {
        foreach (var (directiveId, clauses) in DirectivesFaxClauseCatalog.ClausesByDirective)
        {
            foreach (var clause in clauses)
            {
                Assert.That(clause.Id, Is.Not.Null.And.Not.Empty, $"'{directiveId}' has a clause with no id.");
            }
        }
    }

    [Test]
    public void ThresholdClauses_HaveAPositiveThreshold()
    {
        foreach (var (directiveId, clauses) in DirectivesFaxClauseCatalog.ClausesByDirective)
        {
            foreach (var clause in clauses)
            {
                if (clause.Kind == DirectivesFaxClauseKind.ZeroCasualties)
                    continue;

                Assert.That(clause.Threshold, Is.GreaterThan(0),
                    $"'{directiveId}' clause '{clause.Id}' ({clause.Kind}) must have a positive threshold.");
            }
        }
    }

    [Test]
    public void GetClauses_UnknownDirectiveId_FailsClosed_ReturnsEmpty()
    {
        Assert.That(DirectivesFaxClauseCatalog.GetClauses("not-a-real-directive"), Is.Empty);
    }

    [Test]
    public void GetClauses_KnownDirectiveId_MatchesTheDictionary()
    {
        Assert.That(DirectivesFaxClauseCatalog.GetClauses("safety-inspection"),
            Is.EqualTo(DirectivesFaxClauseCatalog.ClausesByDirective["safety-inspection"]));
    }

    [Test]
    public void GetDisplayName_KnownDirectiveId_IsNonEmptyAndNotTheRawId()
    {
        foreach (var directive in StationDirectiveCatalog.Directives)
        {
            var name = DirectivesFaxClauseCatalog.GetDisplayName(directive.Id);
            Assert.That(name, Is.Not.Null.And.Not.Empty);
            Assert.That(name, Is.Not.EqualTo(directive.Id), $"'{directive.Id}' has no human-readable display name.");
        }
    }

    [Test]
    public void GetDisplayName_UnknownDirectiveId_FailsClosed_ReturnsTheRawId()
    {
        Assert.That(DirectivesFaxClauseCatalog.GetDisplayName("not-a-real-directive"), Is.EqualTo("not-a-real-directive"));
    }
}
