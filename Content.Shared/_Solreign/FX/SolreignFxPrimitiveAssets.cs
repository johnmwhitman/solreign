using Robust.Shared.Audio;
using Robust.Shared.Maths;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     The concrete "what to actually draw/play" resource table for each
///     <see cref="SolreignFxCategory"/> — plain data (RSI/shader paths, palette colors, sound
///     specifiers), engine-free so it stays directly unit-testable
///     (<c>Content.Tests._Solreign.FX.SolreignFxPrimitiveAssetsTests</c>) like every other Solreign
///     lookup table in this directory.
///
///     Every asset referenced below is an EXISTING, already-licensed repo resource — no new texture
///     or audio binary ships with this worktree (spec's own "compose existing mechanisms" framing;
///     mission text: "expect most primitives to compose existing shader/sprite assets"). Where no
///     dedicated Solreign asset exists, this table follows the exact precedent
///     <c>Resources/Prototypes/_Solreign/Entities/StationIdentity/spores.yml</c> already set for the
///     same problem ("upstream ships no dedicated spore/pollen asset... reused
///     <c>Effects/chempuff.rsi</c> at a small tinted scale rather than adding new art") — reusing
///     stock <c>Resources/Textures/Effects/*.rsi</c> art (CC0/CC-BY-SA-3.0, already attributed in
///     each RSI's own <c>meta.json</c>) with a Solreign-chosen tint/scale per primitive.
/// </summary>
public static class SolreignFxPrimitiveAssets
{
    /// <summary>One category's sprite/light/sound asset configuration.</summary>
    public readonly record struct Assets(
        string? RsiPath,
        string? SpriteState,
        Color[] Palette,
        Color LightColor,
        string? SoundPath);

    private static readonly Color[] SparkPalette =
    {
        Color.FromHex("#FFFFFF"), // white — generic kinetic
        Color.FromHex("#FF8C3B"), // orange — thermal/incendiary
        Color.FromHex("#3BB4FF"), // blue — cold/ion
        Color.FromHex("#FF3B3B"), // red — brute/critical
    };

    private static readonly Color[] ElectricalPalette =
    {
        Color.FromHex("#FFE23B"), // yellow — mains/standard arc
        Color.FromHex("#3BD1FF"), // cyan — malfunctioning device
        Color.FromHex("#B23BFF"), // violet — werewolf claw crackle
    };

    private static readonly Color[] DustPalette =
    {
        Color.FromHex("#9C8A6E"), // dust/plaster grey-tan
        Color.FromHex("#6E5A4A"), // debris brown
        Color.FromHex("#7A7A7A"), // ash grey
    };

    private static readonly Color[] SmokePalette =
    {
        Color.FromHex("#D8D8D8"), // white/steam
        Color.FromHex("#3A3A3A"), // black/soot
        Color.FromHex("#59FF40"), // toxic/gas-leak green (matches SolreignAcidTint's own tint)
    };

    private static readonly Color[] CastRingPalette =
    {
        Color.FromHex("#39FF14"), // acid-green — Solreign signature accent, default
        Color.FromHex("#3BD1FF"), // engineering/charge blue
        Color.FromHex("#B23BFF"), // changeling/arcane violet
        Color.FromHex("#FF8C3B"), // fire/heat orange
        Color.FromHex("#FFE23B"), // caution yellow
        Color.FromHex("#FF3B6E"), // vampire/blood magenta-red
    };

    private static readonly Color[] TransformationPalette =
    {
        Color.FromHex("#B23BFF"), // changeling violet
        Color.FromHex("#8B0000"), // vampire deep red
        Color.FromHex("#C9C9C9"), // werewolf moon-grey
        Color.FromHex("#FFFFFF"), // generic/unthemed
    };

    private static readonly Color[] StaminaBreakPalette =
    {
        Color.FromHex("#FFE23B"), // convulsion — matches electrical's mains-arc yellow
        Color.FromHex("#3BD1FF"),
    };

    /// <summary>
    ///     Single neutral entry ONLY (grk W3 round-1 review finding #6): <c>body_shock_generic</c>
    ///     is the redacted cover for THREE secret-role-reachable primitives
    ///     (electrical/cast_ring/stamina_break) — a larger confidentiality surface than
    ///     <c>transformation_generic</c>'s one, so it gets the SAME "never varies" treatment rather
    ///     than reusing <see cref="StaminaBreakPalette"/>'s two-color table (which is fine for
    ///     stamina_break's own non-redacted broadcast cue, but wrong for its generic cover — this
    ///     array's length must match <c>body_shock_generic</c>'s own <c>effects.yml</c>
    ///     <c>paletteCount: 1</c>).
    /// </summary>
    private static readonly Color[] BodyShockGenericPalette =
    {
        Color.FromHex("#FFE23B"),
    };

    /// <summary>Per-category asset configuration. Never throws for a category outside the switch (falls back to a null-asset, light-only default) — same total-function discipline as <see cref="SolreignFxCategoryTable.GetDefaults"/>.</summary>
    public static Assets GetAssets(SolreignFxCategory category) => category switch
    {
        SolreignFxCategory.ImpactLight => new Assets(
            "/Textures/Effects/sparks.rsi", "sparks", SparkPalette, Color.FromHex("#FFFFFF"),
            "/Audio/Weapons/genhit1.ogg"),

        SolreignFxCategory.ImpactHeavy => new Assets(
            "/Textures/Effects/sparks.rsi", "sparks", SparkPalette, Color.FromHex("#FF8C3B"),
            "/Audio/Weapons/genhit3.ogg"),

        SolreignFxCategory.Electrical => new Assets(
            "/Textures/Effects/electricity.rsi", "electrified", ElectricalPalette, Color.FromHex("#FFE23B"),
            "/Audio/Effects/sparks2.ogg"),

        SolreignFxCategory.Dust => new Assets(
            "/Textures/Effects/chempuff.rsi", "chempuff", DustPalette, Color.FromHex("#9C8A6E"),
            null),

        SolreignFxCategory.Smoke => new Assets(
            "/Textures/Effects/chemsmoke.rsi", "chemsmoke", SmokePalette, Color.FromHex("#D8D8D8"),
            null),

        SolreignFxCategory.CastRing => new Assets(
            null, null, CastRingPalette, Color.FromHex("#39FF14"),
            null),

        SolreignFxCategory.Transformation => new Assets(
            "/Textures/Effects/electricity.rsi", "electrified", TransformationPalette, Color.FromHex("#B23BFF"),
            "/Audio/Effects/Lightning/lightningshock.ogg"),

        SolreignFxCategory.StaminaBreak => new Assets(
            "/Textures/Effects/sparks.rsi", "sparks", StaminaBreakPalette, Color.FromHex("#FFE23B"),
            "/Audio/Weapons/Guns/Hits/taser_hit.ogg"),

        SolreignFxCategory.BodyShockGeneric => new Assets(
            "/Textures/Effects/sparks.rsi", "sparks", BodyShockGenericPalette, Color.FromHex("#FFE23B"),
            "/Audio/Weapons/Guns/Hits/taser_hit.ogg"),

        _ => new Assets(null, null, System.Array.Empty<Color>(), Color.White, null),
    };

    /// <summary>Bounds-safe palette color lookup — a <paramref name="paletteIndex"/> outside the category's array clamps to entry 0 rather than throwing (the actual wire-level fallback-to-declared-default already happened upstream in <see cref="SolreignFxCueV1.TryValidateReceived"/>; this is a defensive second floor, never trusting an asset table itself to be perfectly in sync with a prototype's authored <c>PaletteCount</c>).</summary>
    public static Color GetPaletteColor(SolreignFxCategory category, byte paletteIndex)
    {
        var assets = GetAssets(category);
        if (assets.Palette.Length == 0)
            return Color.White;

        var index = paletteIndex < assets.Palette.Length ? paletteIndex : 0;
        return assets.Palette[index];
    }

    /// <summary>Resolves a category's configured sound as a <see cref="SoundSpecifier"/>, or null if the category has none (spec's composing column: dust/smoke/cast_ring carry no dedicated FX-language audio — their audio, if any, belongs to the caller's own ability/ambience, a non-FX co-channel).</summary>
    public static SoundSpecifier? GetSound(SolreignFxCategory category)
    {
        var assets = GetAssets(category);
        return assets.SoundPath is null ? null : new SoundPathSpecifier(assets.SoundPath);
    }
}
