using Content.Client.Resources;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client.Stylesheets.Fonts;

/// <summary>
///     This class should have a base type. The whole font system is currently kind of bad and completely temporary.
///     This class is just here because it does sort of work.
///     TODO: fix (once engine support is added for font properties?)
/// </summary>
/// <param name="resCache"></param>
/// <param name="variant"></param>
[PublicAPI]
public sealed class NotoFontFamilyStack
{
    private IResourceCache resCache;

    /// <param name="availableKinds">
    ///     Which weights this family actually ships on disk. Defaults to all four.
    ///     THIS MUST BE HONEST. GetFontPaths only falls back to a substitute weight when the kind is
    ///     absent from this set, so a family that claims a weight it does not have will ask the
    ///     resource cache for a file that is not there. RobotoMono ships Regular/Bold/Italic and no
    ///     BoldItalic, and claiming otherwise made the client throw
    ///     "Content file does not exist for font" at startup and took 380 of 385 integration tests
    ///     down with it.
    /// </param>
    public NotoFontFamilyStack(
        IResourceCache resCache,
        string basePath = "/Fonts/NotoSans/NotoSans",
        string symbolsPath = "/Fonts/NotoSans/NotoSansSymbols",
        HashSet<FontKind>? availableKinds = null)
    {
        this.resCache = resCache;
        _fontPrimary = $"{basePath}-{{0}}.ttf";
        _fontSymbols = $"{symbolsPath}-{{2}}.ttf";
        if (availableKinds is not null)
            AvailableKinds = availableKinds;
    }


    /// <summary>
    ///     The primary font path, with string substitution markers.
    /// </summary>
    /// <remarks>
    ///     If using the default GetFontPaths function, the substitutions are as follows:
    ///     0 is the font kind.
    ///     1 is the font kind with BoldItalic replaced with Bold when it occurs.
    /// </remarks>
    private string _fontPrimary;

    /// <summary>
    ///     The symbols font path, with string substitution markers.
    /// </summary>
    /// <remarks>
    ///     If using the default GetFontPaths function, the substitutions are as follows:
    ///     0 is the font kind.
    ///     1 is the font kind with BoldItalic replaced with Bold when it occurs.
    /// </remarks>
    private string _fontSymbols;

    /// <summary>
    ///     The fallback font path, exactly. (no string substitutions.)
    /// </summary>
    private string[] _extras = new[] { "/Fonts/NotoSans/NotoSansSymbols2-Regular.ttf" };

    public HashSet<FontKind> AvailableKinds = [FontKind.Regular, FontKind.Bold, FontKind.Italic, FontKind.BoldItalic];

    /// <summary>
    ///     This should return the paths of every font in this stack given the abstract members.
    /// </summary>
    /// <param name="kind">Which font kind to use.</param>
    /// <returns>An array of </returns>
    private string[] GetFontPaths(FontKind kind)
    {
        if (!AvailableKinds.Contains(kind))
        {
            if (kind == FontKind.BoldItalic && AvailableKinds.Contains(FontKind.Bold))
            {
                kind = FontKind.Bold;
            }
            else
            {
                kind = FontKind.Regular;
            }
        }

        var simpleKindStr = kind.SimplifyCompound().AsFileName();
        var boldOrRegularStr = kind.RegularOr(FontKind.Bold).AsFileName();

        var kindStr = kind.AsFileName();
        var fontList = new List<string>()
        {
            string.Format(_fontPrimary, kindStr, simpleKindStr, boldOrRegularStr),
            string.Format(_fontSymbols, kindStr, simpleKindStr, boldOrRegularStr),
        };
        fontList.AddRange(_extras);
        return fontList.ToArray();
    }

    /// <summary>
    ///     Retrieves an in-style font, of the provided size and kind.
    /// </summary>
    /// <param name="size">Size of the font to provide.</param>
    /// <param name="kind">Optional font kind. Defaults to Regular.</param>
    /// <returns>A Font resource.</returns>
    public Font GetFont(int size, FontKind kind = FontKind.Regular)
    {
        //ALDebugTools.AssertContains(AvailableKinds, kind);
        var paths = GetFontPaths(kind);

        return resCache.GetFont(paths, size);
    }
}
