using System;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(TitleGrantRules))]
public sealed class TitleGrantRulesTests
{
    [Test]
    public void PlainTitle_IsAcceptedVerbatim()
    {
        Assert.That(TitleGrantRules.TryNormalize("Employee of the Quarter", out var title, out _), Is.True);
        Assert.That(title, Is.EqualTo("Employee of the Quarter"));
    }

    [Test]
    public void SurroundingAndRepeatedWhitespace_IsCollapsed()
    {
        Assert.That(TitleGrantRules.TryNormalize("  Employee   of \t the  Quarter  ", out var title, out _), Is.True);
        Assert.That(title, Is.EqualTo("Employee of the Quarter"));
    }

    [Test]
    public void Null_IsRejected()
    {
        Assert.That(TitleGrantRules.TryNormalize(null, out _, out var problem), Is.False);
        Assert.That(problem, Is.Not.Empty);
    }

    [Test]
    public void WhitespaceOnly_IsRejected()
    {
        Assert.That(TitleGrantRules.TryNormalize("   \t ", out _, out var problem), Is.False);
        Assert.That(problem, Is.Not.Empty);
    }

    [Test]
    public void ControlCharacters_AreRejected()
    {
        // \a (bell) is a control character but NOT whitespace, so it survives whitespace collapse and
        // must be caught by the character policy.
        Assert.That(TitleGrantRules.TryNormalize("Employee\aof the Quarter", out _, out var problem), Is.False);
        Assert.That(problem, Does.Contain("printable ASCII"));
    }

    [Test]
    public void EmbeddedNewlinesAndTabs_CollapseToSpacesInsteadOfRejecting()
    {
        // Whitespace-class control characters are normalized away, not rejected — admins pasting from
        // the website form shouldn't be punished for a line wrap.
        Assert.That(TitleGrantRules.TryNormalize("Employee\nof\tthe Quarter", out var title, out _), Is.True);
        Assert.That(title, Is.EqualTo("Employee of the Quarter"));
    }

    [Test]
    public void MarkupBrackets_AreRejected()
    {
        // '[' / ']' open RobustToolbox rich-text tags — the title renders in examine markup for everyone.
        Assert.That(TitleGrantRules.TryNormalize("[color=red]Admin[/color]", out _, out var problem), Is.False);
        Assert.That(problem, Does.Contain("markup"));
    }

    [Test]
    public void MarkupEscapeBackslash_IsRejected()
    {
        Assert.That(TitleGrantRules.TryNormalize(@"Totally \[Legit\] Title", out _, out var problem), Is.False);
        Assert.That(problem, Does.Contain("markup"));
    }

    [Test]
    public void NonAsciiText_IsRejected()
    {
        Assert.That(TitleGrantRules.TryNormalize("Employé of the Quarter", out _, out var problem), Is.False);
        Assert.That(problem, Does.Contain("printable ASCII"));
    }

    [Test]
    public void TitleAtLengthCap_IsAccepted()
    {
        var atCap = new string('x', TitleGrantRules.MaxLength);
        Assert.That(TitleGrantRules.TryNormalize(atCap, out var title, out _), Is.True);
        Assert.That(title, Is.EqualTo(atCap));
    }

    [Test]
    public void TitleOverLengthCap_IsRejected()
    {
        var overCap = new string('x', TitleGrantRules.MaxLength + 1);
        Assert.That(TitleGrantRules.TryNormalize(overCap, out _, out var problem), Is.False);
        Assert.That(problem, Does.Contain($"{TitleGrantRules.MaxLength}"));
    }

    [Test]
    public void LengthCap_IsMeasuredAfterWhitespaceCollapse()
    {
        // 48 payload characters padded far past the cap with collapsible whitespace must still pass.
        var atCap = new string('x', TitleGrantRules.MaxLength);
        Assert.That(TitleGrantRules.TryNormalize($"   {atCap}   ", out var title, out _), Is.True);
        Assert.That(title, Is.EqualTo(atCap));
    }

    // --- Display fold -----------------------------------------------------------------------------

    [Test]
    public void NoGrant_ShowsEarnedTitle()
    {
        Assert.That(TitleGrantRules.ResolveDisplayTitle("Chain Closer", null), Is.EqualTo("Chain Closer"));
        Assert.That(TitleGrantRules.ResolveDisplayTitle("Chain Closer", ""), Is.EqualTo("Chain Closer"));
    }

    [Test]
    public void ActiveGrant_MasksEarnedTitle()
    {
        Assert.That(TitleGrantRules.ResolveDisplayTitle("Chain Closer", "Community Hero"),
            Is.EqualTo("Community Hero"));
    }

    // --- Paper trail ------------------------------------------------------------------------------

    [Test]
    public void GrantAudit_NamesGranterTitleAndTarget()
    {
        var target = Guid.NewGuid();
        var line = TitleGrantRules.FormatGrantAudit("adminA", "playerB", target, "Community Hero");
        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("adminA"));
            Assert.That(line, Does.Contain("playerB"));
            Assert.That(line, Does.Contain("Community Hero"));
            Assert.That(line, Does.Contain(target.ToString()));
        });
    }

    [Test]
    public void RevokeAudit_NamesRevokerAndTarget()
    {
        var target = Guid.NewGuid();
        var line = TitleGrantRules.FormatRevokeAudit("adminA", "playerB", target);
        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("adminA"));
            Assert.That(line, Does.Contain("playerB"));
            Assert.That(line, Does.Contain(target.ToString()));
        });
    }
}
