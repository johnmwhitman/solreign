#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared.HUD;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Themes;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     HUD-THEME-TEST: pins the player-selectable HUD theme file sets so that a regression in any
///     theme directory (deleted file, typo'd filename, orphan art, broken dropdown wiring) can
///     never ship silently again.
///
///     HOW THEME RESOLUTION ACTUALLY WORKS (tribal knowledge, previously undocumented — see
///     docs/receipts/HUD-THEME-TEST-2026-07-17.md for the long form):
///
///     1. Two prototype families pair by ID. <c>hudTheme</c> (<see cref="HudThemePrototype"/>,
///        Resources/Prototypes/hud.yml) only populates the options-menu dropdown (MiscTab), whose
///        selection writes the prototype ID into the engine CVar <see cref="CVars.InterfaceTheme"/>.
///        The engine's UserInterfaceManager watches that CVar and looks the value up among
///        <c>uiTheme</c> prototypes (<see cref="UITheme"/>, Resources/Prototypes/themes.yml) via
///        SetThemeOrPrevious. If no uiTheme has that exact ID the selection is a SILENT NO-OP —
///        hence the wiring assertions below.
///
///     2. Texture lookup (<see cref="UITheme.TryResolveTexture"/>): a relative name (".png"
///        appended if absent) is tried at <c>{uiTheme.path}/{name}.png</c> first, then falls back
///        to the hardcoded <see cref="UITheme.DefaultThemePath"/> (/Textures/Interface/Default),
///        then as an absolute path. So per-theme directories are OVERLAYS over Default: themes
///        legitimately ship fewer files (e.g. Minimalist) and fall back per-file BY DESIGN, but a
///        misnamed or deleted override also falls back — invisibly. Default is the resolver of
///        last resort: any code-requested name missing there renders the engine's error texture.
///
///     3. Consumers requesting relative (theme-overridable) names: SlotControl and subclasses
///        (slot_highlight/blocked/Slots/*), HandButton (Slots/hand_*), inventory template
///        prototypes (Slots/{slotTexture} + fullTextureName — data-driven, harvested live below),
///        ActionButton (SlotBackground), ItemStatusPanel (item_status_*), StorageWindow +
///        ItemGridPiece (Storage/*), and TexturePath= XAML attributes with relative values
///        (arrow textures in ActionPageButtons/LobbyGui).
///
///     THE SNAPSHOT: each theme directory carries a meta.json whose <c>states</c> list is the
///     recorded manifest of exactly the .png set that directory ships (it doubles as the CC0
///     license receipt for the procedurally regenerated art). This test asserts directory contents
///     equal the recorded manifest EXACTLY, so any future deletion/typo/addition fails loudly and
///     must be acknowledged by deliberately editing the manifest in the same commit.
/// </summary>
[TestFixture]
public sealed class HudThemeResolutionTest : GameTest
{
    /// <summary>
    ///     Relative texture names requested from C# constants / XAML attributes (call sites in
    ///     each comment). Data-driven names (inventory templates) are harvested from the live
    ///     prototype manager instead — do NOT add slot textures here.
    /// </summary>
    private static readonly string[] CodeRequestedTextureNames =
    {
        // Content.Client/UserInterface/Controls/SlotControl.cs (ctor)
        "slot_highlight",
        "blocked",
        // Content.Client/UserInterface/Controls/SlotButton.cs (StorageTexturePath)
        "Slots/back",
        // Content.Client/UserInterface/Systems/Inventory/Widgets/InventoryGui.xaml
        "Slots/toggle",
        // Content.Client/UserInterface/Systems/Hands/Controls/HandButton.cs
        "Slots/hand_l",
        "Slots/hand_m",
        "Slots/hand_r",
        // Content.Client/UserInterface/Systems/Actions/Controls/ActionButton.cs
        "SlotBackground",
        // Content.Client/UserInterface/Systems/Inventory/Controls/ItemStatusPanel.xaml.cs
        "item_status_left",
        "item_status_left_highlight",
        "item_status_right",
        "item_status_right_highlight",
        // Content.Client/UserInterface/Systems/Storage/Controls/StorageWindow.cs
        "Storage/tile_empty",
        "Storage/tile_blocked",
        "Storage/tile_empty_opaque",
        "Storage/tile_blocked_opaque",
        "Storage/exit",
        "Storage/back",
        "Storage/sidebar_top",
        "Storage/sidebar_mid",
        "Storage/sidebar_bottom",
        "Storage/sidebar_fat",
        // Content.Client/UserInterface/Systems/Storage/Controls/ItemGridPiece.cs
        "Storage/piece_center",
        "Storage/piece_top",
        "Storage/piece_bottom",
        "Storage/piece_left",
        "Storage/piece_right",
        "Storage/piece_topLeft",
        "Storage/piece_topRight",
        "Storage/piece_bottomLeft",
        "Storage/piece_bottomRight",
        "Storage/marked_first",
        "Storage/marked_second",
        // Content.Client/UserInterface/Systems/Actions/Controls/ActionPageButtons.xaml
        "left_arrow.svg.192dpi",
        "right_arrow.svg.192dpi",
        // Content.Client/Lobby/UI/LobbyGui.xaml
        "filled_left_arrow.svg.192dpi",
        "filled_right_arrow.svg.192dpi",
        // RobustToolbox/Robust.Client/UserInterface/Stylesheets/DefaultStylesheet.cs (engine)
        "cross",
    };

    /// <summary>
    ///     Textures the ENGINE ships into the /Textures/Interface/Default VFS overlay from
    ///     RobustToolbox/Resources (the VFS unions engine and content roots at the same virtual
    ///     paths). These are engine art, deliberately NOT listed in the content directory's
    ///     meta.json manifest/license record. If the engine ever ships more, this pin fails
    ///     loudly and must be updated deliberately.
    /// </summary>
    private static readonly string[] EngineShippedDefaultTextures =
    {
        "cross", // engine DefaultStylesheet's window close button
    };

    /// <summary>
    ///     The engine ships exactly one uiTheme of its own ("Default", pointing at the same
    ///     /Textures/Interface/Default directory) which UserInterfaceManager indexes at boot
    ///     before content swaps the default to SS14DefaultTheme (Content.Client EntryPoint).
    /// </summary>
    private const string EngineDefaultThemeId = "Default";

    private static readonly ResPath InterfaceRoot = new("/Textures/Interface");

    [Test]
    public async Task DropdownWiring_EveryHudThemePairsWithAUiThemeAndARealDirectory()
    {
        await Client.WaitAssertion(() =>
        {
            var protoMan = Client.ResolveDependency<IPrototypeManager>();
            var resMan = Client.ResolveDependency<IResourceManager>();

            var hudThemes = protoMan.EnumeratePrototypes<HudThemePrototype>().ToDictionary(p => p.ID);
            var uiThemes = protoMan.EnumeratePrototypes<UITheme>().ToDictionary(p => p.ID);

            Assert.That(hudThemes, Is.Not.Empty, "no hudTheme prototypes loaded — hud.yml missing?");

            // The dropdown writes hudTheme.ID into CVars.InterfaceTheme; the engine then indexes
            // uiThemes by that string. Any hudTheme without an identically-ID'd uiTheme is a
            // dropdown entry that silently does nothing when selected.
            var expectedUiThemeIds = hudThemes.Keys.Append(EngineDefaultThemeId).ToHashSet();
            Assert.That(uiThemes.Keys, Is.EquivalentTo(expectedUiThemeIds),
                "hudTheme (hud.yml) and uiTheme (themes.yml) prototypes must pair 1:1 by ID "
                + $"(plus the engine's own '{EngineDefaultThemeId}') — a hudTheme without a matching "
                + "uiTheme is a dropdown entry whose selection is a silent no-op, and an unpaired "
                + "uiTheme is player-unreachable.");

            Assert.Multiple(() =>
            {
                foreach (var (id, hud) in hudThemes)
                {
                    // hudTheme.path documents the directory; it must agree with where the paired
                    // uiTheme actually resolves textures from.
                    var expectedDir = InterfaceRoot / hud.Path;
                    var actualDir = uiThemes[id].Path;
                    Assert.That(actualDir.CanonPath.TrimEnd('/'), Is.EqualTo(expectedDir.CanonPath.TrimEnd('/')),
                        $"hudTheme '{id}' declares path '{hud.Path}' but its uiTheme resolves from '{actualDir}'");

                    Assert.That(resMan.ContentFindFiles(expectedDir).Any(), Is.True,
                        $"theme directory '{expectedDir}' for '{id}' is missing or empty");
                }
            });
        });
    }

    [Test]
    public async Task SelectingEveryTheme_ViaTheCVar_ActuallySwitchesTheClientTheme()
    {
        await Pair.RunTicksSync(5);

        var cfg = Client.ResolveDependency<IConfigurationManager>();
        var uiMan = Client.ResolveDependency<IUserInterfaceManager>();
        var protoMan = Client.ResolveDependency<IPrototypeManager>();
        var originalCVar = cfg.GetCVar(CVars.InterfaceTheme);

        try
        {
            // Baseline: content's EntryPoint promotes SS14DefaultTheme to the active default.
            await Client.WaitAssertion(() =>
                Assert.That(uiMan.CurrentTheme.ID, Is.EqualTo("SS14DefaultTheme"),
                    "client should boot on SS14DefaultTheme (EntryPoint.SetDefaultTheme)"));

            foreach (var hud in protoMan.EnumeratePrototypes<HudThemePrototype>().OrderBy(p => p.Order))
            {
                await Client.WaitPost(() => cfg.SetCVar(CVars.InterfaceTheme, hud.ID));
                await Client.WaitAssertion(() =>
                    Assert.That(uiMan.CurrentTheme.ID, Is.EqualTo(hud.ID),
                        $"selecting '{hud.ID}' in the options dropdown must switch the live theme — "
                        + "a mismatch means SetThemeOrPrevious silently kept the previous theme"));
            }
        }
        finally
        {
            await Client.WaitPost(() => cfg.SetCVar(CVars.InterfaceTheme, originalCVar));
        }
    }

    [Test]
    public async Task EveryThemeFile_IsReachableByResolutionAndShadowsDefault()
    {
        await Client.WaitAssertion(() =>
        {
            var protoMan = Client.ResolveDependency<IPrototypeManager>();
            var resMan = Client.ResolveDependency<IResourceManager>();

            var defaultPngs = ThemePngs(resMan, UITheme.DefaultThemePath);
            Assert.That(defaultPngs, Is.Not.Empty, "Default theme directory has no textures?!");

            Assert.Multiple(() =>
            {
                foreach (var theme in protoMan.EnumeratePrototypes<UITheme>())
                {
                    var root = CanonRoot(theme);
                    if (root == UITheme.DefaultThemePath)
                        continue;

                    // Resolution tries {theme}/{rel} then Default/{rel} with the SAME relative
                    // path, so a theme file is reachable only if Default ships that relative path
                    // too (every code-requested name must exist in Default — asserted separately).
                    // A theme png with no Default counterpart is either a typo'd override that is
                    // silently falling back, or orphan art shipping dead weight in the ACZ.
                    var orphans = ThemePngs(resMan, root).Except(defaultPngs).ToList();
                    Assert.That(orphans, Is.Empty,
                        $"theme '{theme.ID}' ships files unreachable by UITheme.TryResolveTexture "
                        + $"(no {UITheme.DefaultThemePath} counterpart — typo'd override or dead weight): "
                        + string.Join(", ", orphans));
                }
            });

            // Nothing but textures and their recorded metadata may ship from theme directories:
            // *.png, *.png.yml sidecars (texture load parameters, base png must exist), *.svg
            // sources, and the meta.json manifest/license record.
            Assert.Multiple(() =>
            {
                foreach (var theme in protoMan.EnumeratePrototypes<UITheme>())
                {
                    var root = CanonRoot(theme);
                    var pngs = ThemePngs(resMan, root);
                    foreach (var file in resMan.ContentFindFiles(root))
                    {
                        var rel = file.RelativeTo(root).ToString();
                        if (rel.EndsWith(".png") || rel.EndsWith(".svg") || rel == "meta.json")
                            continue;

                        if (rel.EndsWith(".png.yml"))
                        {
                            Assert.That(pngs, Does.Contain(rel[..^4]),
                                $"theme '{theme.ID}': sidecar '{rel}' has no matching .png");
                            continue;
                        }

                        Assert.Fail($"theme '{theme.ID}': unexpected file '{rel}' in theme directory");
                    }
                }
            });
        });
    }

    [Test]
    public async Task EveryCodeRequestedTextureName_ResolvesForEveryThemeWithoutTheErrorFallback()
    {
        await Client.WaitAssertion(() =>
        {
            var protoMan = Client.ResolveDependency<IPrototypeManager>();
            var resMan = Client.ResolveDependency<IResourceManager>();

            // The full request contract = the static call-site names + every name the inventory
            // template data can make SlotControl request at runtime.
            var requested = new SortedSet<string>(CodeRequestedTextureNames);
            foreach (var template in protoMan.EnumeratePrototypes<InventoryTemplatePrototype>())
            {
                foreach (var slot in template.Slots)
                {
                    requested.Add($"Slots/{slot.TextureName}"); // ClientInventorySystem.SlotData.TextureName
                    requested.Add(slot.FullTextureName);        // SlotControl.FullButtonTexturePath
                }
            }

            var defaultPngs = ThemePngs(resMan, UITheme.DefaultThemePath);

            // Default is the resolver of last resort: every requested name must exist there, or
            // some theme (by design or by regression) renders the engine's error texture.
            Assert.Multiple(() =>
            {
                foreach (var name in requested)
                {
                    Assert.That(defaultPngs, Does.Contain($"{name}.png"),
                        $"code-requested texture '{name}' is missing from {UITheme.DefaultThemePath} — "
                        + "the universal fallback would fail and the error texture would render");
                }
            });

            // And the real resolver must succeed for every (theme, name) pair — per-file fallback
            // to Default is the designed behavior for themes that override only a subset.
            Assert.Multiple(() =>
            {
                foreach (var theme in protoMan.EnumeratePrototypes<UITheme>())
                {
                    foreach (var name in requested)
                    {
                        Assert.That(theme.TryResolveTexture(name, out _), Is.True,
                            $"theme '{theme.ID}' fails to resolve code-requested texture '{name}'");
                    }
                }
            });
        });
    }

    [Test]
    public async Task ThemeDirectories_ExactlyMatchTheirRecordedManifests()
    {
        await Client.WaitAssertion(() =>
        {
            var protoMan = Client.ResolveDependency<IPrototypeManager>();
            var resMan = Client.ResolveDependency<IResourceManager>();

            Assert.Multiple(() =>
            {
                foreach (var theme in protoMan.EnumeratePrototypes<UITheme>())
                {
                    var root = CanonRoot(theme);
                    var metaPath = root / "meta.json";
                    Assert.That(resMan.ContentFileExists(metaPath), Is.True,
                        $"theme '{theme.ID}' has no meta.json manifest/license record at {metaPath}");
                    if (!resMan.ContentFileExists(metaPath))
                        continue;

                    using var stream = resMan.ContentFileRead(metaPath);
                    using var doc = JsonDocument.Parse(stream);
                    var recorded = doc.RootElement.GetProperty("states").EnumerateArray()
                        .Select(s => s.GetProperty("name").GetString()!)
                        .ToHashSet();

                    var actual = ThemePngs(resMan, root).Select(p => p[..^4]).ToHashSet();

                    // The Default directory is a VFS union of content and engine resources; the
                    // engine's own contributions are pinned separately and are not part of the
                    // content manifest (they are not content-licensed art).
                    if (root == UITheme.DefaultThemePath)
                    {
                        foreach (var engineName in EngineShippedDefaultTextures)
                        {
                            Assert.That(actual.Remove(engineName), Is.True,
                                $"engine-shipped texture '{engineName}' is pinned in "
                                + $"{nameof(EngineShippedDefaultTextures)} but absent from the VFS — "
                                + "did the engine stop shipping it?");
                            Assert.That(recorded, Does.Not.Contain(engineName),
                                $"'{engineName}' is engine art and must not be claimed by the "
                                + "content meta.json license record");
                        }
                    }

                    // Snapshot assertion: the manifest is the deliberate record of this theme's
                    // file set (and of which names fall back to Default by design — everything
                    // Default ships that is absent here). Any file add/delete/rename must be
                    // acknowledged by editing meta.json's states in the same commit, so no theme
                    // set change can ever ship silently.
                    var missingFiles = recorded.Except(actual).OrderBy(x => x).ToList();
                    var unrecordedFiles = actual.Except(recorded).OrderBy(x => x).ToList();
                    Assert.That(missingFiles, Is.Empty,
                        $"theme '{theme.ID}': meta.json records textures that no longer exist on disk "
                        + "(deleted or renamed without updating the manifest): "
                        + string.Join(", ", missingFiles));
                    Assert.That(unrecordedFiles, Is.Empty,
                        $"theme '{theme.ID}': directory ships textures absent from meta.json's states "
                        + "(add them to the manifest to record the change deliberately): "
                        + string.Join(", ", unrecordedFiles));
                }
            });
        });
    }

    private static ResPath CanonRoot(UITheme theme)
    {
        return new ResPath(theme.Path.CanonPath.TrimEnd('/'));
    }

    private static HashSet<string> ThemePngs(IResourceManager resMan, ResPath themeRoot)
    {
        return resMan.ContentFindFiles(themeRoot)
            .Select(f => f.RelativeTo(themeRoot).ToString())
            .Where(f => f.EndsWith(".png"))
            .ToHashSet();
    }
}
