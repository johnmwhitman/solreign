namespace Content.Client.Stylesheets.Palette;

/// <summary>
///     Stores all style palettes in one accessible location
/// </summary>
/// <remarks>
///     Technically not limited to only colors, can store like, standard padding amounts, and font sizes, maybe?
/// </remarks>
public static class Palettes
{
    // muted tones
    public static readonly ColorPalette Navy = ColorPalette.FromHexBase("#111417", lightnessShift: 0.05f, chromaShift: 0.0045f); // Solreign asphalt base (matches solreign.net)
    public static readonly ColorPalette Cyan = ColorPalette.FromHexBase("#1A1D24", lightnessShift: 0.05f, chromaShift: 0.0045f);
    public static readonly ColorPalette Slate = ColorPalette.FromHexBase("#2A2D34");
    public static readonly ColorPalette Neutral = ColorPalette.FromHexBase("#555555");
    public static readonly ColorPalette TerminalDark = ColorPalette.FromHexBase("#050a05"); // Very dark background
    public static readonly ColorPalette TerminalGrey = ColorPalette.FromHexBase("#1a221a"); // Dark secondary

    // status tones
    public static readonly ColorPalette Red = ColorPalette.FromHexBase("#b62124", chromaShift: 0.02f);
    public static readonly ColorPalette Amber = ColorPalette.FromHexBase("#FFB000"); // Solreign amber
    public static readonly ColorPalette Green = ColorPalette.FromHexBase("#5BD94A"); // Solreign green, pulled back from the branch's #39FF14 neon
    public static readonly StatusPalette Status = new([Red.Base, Amber.Base, Green.Base]);

    // highlight tones
    public static readonly ColorPalette Gold = ColorPalette.FromHexBase("#FFB000"); // Solreign amber
    public static readonly ColorPalette Maroon = ColorPalette.FromHexBase("#9b2236");
    public static readonly ColorPalette AcidGreen = ColorPalette.FromHexBase("#39ff14"); // Bright neon green

    // Intended to be used with `ModulateSelf` to darken / lighten something
    public static readonly ColorPalette AlphaModulate = ColorPalette.FromHexBase("#ffffff");

}
