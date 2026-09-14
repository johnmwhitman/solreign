#nullable enable
using Content.Server._Solreign.Library;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure input-shaping tests for <see cref="LibraryRules"/> — the STATION-LIBRARY
///     title/body sanitize+clamp rules (this lane's design brief §5). No ECS, no I/O: exercises
///     the pure static class directly, the <c>NoticeboardRulesTests</c> precedent.
/// </summary>
[TestFixture]
[TestOf(typeof(LibraryRules))]
public sealed class LibraryRulesTests
{
    // --- TrySanitizeTitle ---

    [Test]
    public void TrySanitizeTitle_Blank_Rejected()
    {
        var ok = LibraryRules.TrySanitizeTitle(string.Empty, out var sanitized);

        Assert.That(ok, Is.False);
        Assert.That(sanitized, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TrySanitizeTitle_Null_TreatedAsBlank()
    {
        var ok = LibraryRules.TrySanitizeTitle(null, out var sanitized);

        Assert.That(ok, Is.False);
        Assert.That(sanitized, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TrySanitizeTitle_WhitespaceOnly_Rejected()
    {
        var ok = LibraryRules.TrySanitizeTitle("   \t  \n  ", out _);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void TrySanitizeTitle_TrimsSurroundingWhitespace()
    {
        var ok = LibraryRules.TrySanitizeTitle("  My Book  ", out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized, Is.EqualTo("My Book"));
    }

    [Test]
    public void TrySanitizeTitle_ExactlyAtLimit_NotTruncated()
    {
        var text = new string('x', LibraryRules.MaxTitleLength);

        var ok = LibraryRules.TrySanitizeTitle(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(LibraryRules.MaxTitleLength));
    }

    [Test]
    public void TrySanitizeTitle_OverLimit_TruncatesRatherThanRejects()
    {
        var text = new string('x', LibraryRules.MaxTitleLength + 50);

        var ok = LibraryRules.TrySanitizeTitle(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(LibraryRules.MaxTitleLength));
    }

    // --- TrySanitizeBody ---

    [Test]
    public void TrySanitizeBody_Blank_Rejected()
    {
        var ok = LibraryRules.TrySanitizeBody(string.Empty, out var sanitized);

        Assert.That(ok, Is.False);
        Assert.That(sanitized, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TrySanitizeBody_Null_TreatedAsBlank()
    {
        var ok = LibraryRules.TrySanitizeBody(null, out var sanitized);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void TrySanitizeBody_TrimsSurroundingWhitespace()
    {
        var ok = LibraryRules.TrySanitizeBody("  Once upon a time.  ", out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized, Is.EqualTo("Once upon a time."));
    }

    [Test]
    public void TrySanitizeBody_ExactlyAtLimit_NotTruncated()
    {
        var text = new string('x', LibraryRules.MaxBodyLength);

        var ok = LibraryRules.TrySanitizeBody(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(LibraryRules.MaxBodyLength));
    }

    [Test]
    public void TrySanitizeBody_OverLimit_TruncatesRatherThanRejects()
    {
        var text = new string('x', LibraryRules.MaxBodyLength + 500);

        var ok = LibraryRules.TrySanitizeBody(text, out var sanitized);

        Assert.That(ok, Is.True);
        Assert.That(sanitized.Length, Is.EqualTo(LibraryRules.MaxBodyLength));
    }

    // --- the closed length numbers themselves (regression pin) ---

    [Test]
    public void LengthCaps_AreSaneAndBelowThePaperCeilings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryRules.MaxTitleLength, Is.EqualTo(100));
            Assert.That(LibraryRules.MaxBodyLength, Is.EqualTo(6000));
            // Generous, but always fits comfortably inside the Paper reading UI it materializes
            // into — BookBase's own explicit contentSize is 12000 (Resources/Prototypes/Entities/
            // Objects/Misc/books.yml), PaperComponent's default ContentSize is 10000.
            Assert.That(LibraryRules.MaxBodyLength, Is.LessThan(12000));
        });
    }
}
